using System.Runtime.CompilerServices;
using MaxMath;
using Unity.Burst.CompilerServices;

using static MaxMath.math;

namespace NativeZip
{
	unsafe internal struct BinTree
	{
		public byte* _bufferBase;
		public uint* _son;
		public uint* _hash;

		private byte* _stream;
		private long inSize;
		private long _streamPos2;
		private uint _posLimit;

		private uint _pointerToLastSafePosition;

		private uint _bufferOffset;

		private uint _blockSize;
		private uint _pos;
		private uint _keepSizeBefore;
		private uint _keepSizeAfter;
		private uint _streamPos;

		private uint _hashMask;
		private uint _cyclicBufferPos;
		private uint _cyclicBufferSize;
		private uint _matchMaxLen;

		private uint _cutValue;
		private uint _hashSizeSum;
	
		private bool HASH_ARRAY;
		private bool _streamEndWasReached;

		const uint kHash2Size = 1 << 10;
		const uint kHash3Size = 1 << 16;
		const uint kBT2HashSize = 1 << 16;
		const uint kStartMaxLen = 1;
		const uint kEmptyHashValue = 0;
		const uint kMaxValForNormalize = ((uint)1 << 31) - 1;

		public readonly uint NumAvailableBytes => _streamPos - _pos;
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public BinTree(uint historySize, uint matchMaxLen, MatchFinder matchFinder, byte* stream, long inSize, void* basePtr)
		{
			_stream = stream;
			this.inSize = inSize;
			_streamPos2 = 0;
			_posLimit = 0;
			
			_cutValue = 16 + (matchMaxLen >> 1);
				
			_keepSizeBefore = historySize + Encoder.kNumOpts;
			_keepSizeAfter = matchMaxLen + Base.kMatchMaxLen + 1;

			uint windowReservSize = (_keepSizeBefore + _keepSizeAfter) / 2 + 256;
			
			_blockSize = _keepSizeBefore + _keepSizeAfter + windowReservSize;
			_pointerToLastSafePosition = _blockSize - _keepSizeAfter;
			_matchMaxLen = matchMaxLen;
			_cyclicBufferSize = historySize + 1;
			
			HASH_ARRAY = matchFinder == MatchFinder.BT4;

			if (HASH_ARRAY)
			{
				_hashSizeSum = historySize - 1;
				_hashSizeSum |= (_hashSizeSum >> 1);
				_hashSizeSum |= (_hashSizeSum >> 2);
				_hashSizeSum |= (_hashSizeSum >> 4);
				_hashSizeSum |= (_hashSizeSum >> 8);
				_hashSizeSum >>= 1;
				_hashSizeSum |= 0xFFFF;
				if (_hashSizeSum > (1 << 24))
					_hashSizeSum >>= 1;
				_hashMask = _hashSizeSum;
				_hashSizeSum++;
				_hashSizeSum += kHash2Size + kHash3Size;
			}
			else
			{
				_hashSizeSum = kBT2HashSize;
				_hashMask = 0;
			}
			
			_bufferBase = (byte*)basePtr;
			_son = (uint*)((byte*)basePtr + _blockSize);
			_hash = _son + (_cyclicBufferSize * 2);
			
			_bufferOffset = 0;
			_pos = 0;
			_streamPos = 0;
			_streamEndWasReached = false;
			_cyclicBufferPos = 0;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static long RequiredBytes(uint historySize, uint matchMaxLen, MatchFinder matchFinder)
		{
			uint keepSizeBefore = historySize + Encoder.kNumOpts;
			uint keepSizeAfter = matchMaxLen + Base.kMatchMaxLen + 1;

			uint windowReservSize = (keepSizeBefore + keepSizeAfter) / 2 + 256;

			long blockSize = keepSizeBefore + keepSizeAfter + windowReservSize;
			long son = (long)sizeof(uint) * (historySize + 1) * 2;

			long hashSizeSum = kBT2HashSize;
			long kFixHashSize;
			
			bool HASH_ARRAY = matchFinder == MatchFinder.BT4;
			if (HASH_ARRAY)
			{
				kFixHashSize = kHash2Size + kHash3Size;
			}
			else
			{
				kFixHashSize = 0;
			}

			if (HASH_ARRAY)
			{
				hashSizeSum = historySize - 1;
				hashSizeSum |= (hashSizeSum >> 1);
				hashSizeSum |= (hashSizeSum >> 2);
				hashSizeSum |= (hashSizeSum >> 4);
				hashSizeSum |= (hashSizeSum >> 8);
				hashSizeSum >>= 1;
				hashSizeSum |= 0xFFFF;
				if (hashSizeSum > (1 << 24))
					hashSizeSum >>= 1;
				hashSizeSum++;
				hashSizeSum += kFixHashSize;
			}
			long hash = (long)sizeof(uint) * hashSizeSum;

			return blockSize + son + hash;
		}

		// Switch table >>> stackalloc (reads SIMD vectors from memory either way, + MOV instructions)
		// Switch table == static readonly memory without a copy in C# land
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static uint CRC(byte b)
		{
		    switch (b) 
		    {
		        case 0:   return 0x00000000;
		        case 1:   return 0x77073096;
		        case 2:   return 0xEE0E612C;
		        case 3:   return 0x990951BA;
		        case 4:   return 0x076DC419;
		        case 5:   return 0x706AF48F;
		        case 6:   return 0xE963A535;
		        case 7:   return 0x9E6495A3;
		        case 8:   return 0x0EDB8832;
		        case 9:   return 0x79DCB8A4;
		        case 10:  return 0xE0D5E91E;
		        case 11:  return 0x97D2D988;
		        case 12:  return 0x09B64C2B;
		        case 13:  return 0x7EB17CBD;
		        case 14:  return 0xE7B82D07;
		        case 15:  return 0x90BF1D91;
		        case 16:  return 0x1DB71064;
		        case 17:  return 0x6AB020F2;
		        case 18:  return 0xF3B97148;
		        case 19:  return 0x84BE41DE;
		        case 20:  return 0x1ADAD47D;
		        case 21:  return 0x6DDDE4EB;
		        case 22:  return 0xF4D4B551;
		        case 23:  return 0x83D385C7;
		        case 24:  return 0x136C9856;
		        case 25:  return 0x646BA8C0;
		        case 26:  return 0xFD62F97A;
		        case 27:  return 0x8A65C9EC;
		        case 28:  return 0x14015C4F;
		        case 29:  return 0x63066CD9;
		        case 30:  return 0xFA0F3D63;
		        case 31:  return 0x8D080DF5;
		        case 32:  return 0x3B6E20C8;
		        case 33:  return 0x4C69105E;
		        case 34:  return 0xD56041E4;
		        case 35:  return 0xA2677172;
		        case 36:  return 0x3C03E4D1;
		        case 37:  return 0x4B04D447;
		        case 38:  return 0xD20D85FD;
		        case 39:  return 0xA50AB56B;
		        case 40:  return 0x35B5A8FA;
		        case 41:  return 0x42B2986C;
		        case 42:  return 0xDBBBC9D6;
		        case 43:  return 0xACBCF940;
		        case 44:  return 0x32D86CE3;
		        case 45:  return 0x45DF5C75;
		        case 46:  return 0xDCD60DCF;
		        case 47:  return 0xABD13D59;
		        case 48:  return 0x26D930AC;
		        case 49:  return 0x51DE003A;
		        case 50:  return 0xC8D75180;
		        case 51:  return 0xBFD06116;
		        case 52:  return 0x21B4F4B5;
		        case 53:  return 0x56B3C423;
		        case 54:  return 0xCFBA9599;
		        case 55:  return 0xB8BDA50F;
		        case 56:  return 0x2802B89E;
		        case 57:  return 0x5F058808;
		        case 58:  return 0xC60CD9B2;
		        case 59:  return 0xB10BE924;
		        case 60:  return 0x2F6F7C87;
		        case 61:  return 0x58684C11;
		        case 62:  return 0xC1611DAB;
		        case 63:  return 0xB6662D3D;
		        case 64:  return 0x76DC4190;
		        case 65:  return 0x01DB7106;
		        case 66:  return 0x98D220BC;
		        case 67:  return 0xEFD5102A;
		        case 68:  return 0x71B18589;
		        case 69:  return 0x06B6B51F;
		        case 70:  return 0x9FBFE4A5;
		        case 71:  return 0xE8B8D433;
		        case 72:  return 0x7807C9A2;
		        case 73:  return 0x0F00F934;
		        case 74:  return 0x9609A88E;
		        case 75:  return 0xE10E9818;
		        case 76:  return 0x7F6A0DBB;
		        case 77:  return 0x086D3D2D;
		        case 78:  return 0x91646C97;
		        case 79:  return 0xE6635C01;
		        case 80:  return 0x6B6B51F4;
		        case 81:  return 0x1C6C6162;
		        case 82:  return 0x856530D8;
		        case 83:  return 0xF262004E;
		        case 84:  return 0x6C0695ED;
		        case 85:  return 0x1B01A57B;
		        case 86:  return 0x8208F4C1;
		        case 87:  return 0xF50FC457;
		        case 88:  return 0x65B0D9C6;
		        case 89:  return 0x12B7E950;
		        case 90:  return 0x8BBEB8EA;
		        case 91:  return 0xFCB9887C;
		        case 92:  return 0x62DD1DDF;
		        case 93:  return 0x15DA2D49;
		        case 94:  return 0x8CD37CF3;
		        case 95:  return 0xFBD44C65;
		        case 96:  return 0x4DB26158;
		        case 97:  return 0x3AB551CE;
		        case 98:  return 0xA3BC0074;
		        case 99:  return 0xD4BB30E2;
		        case 100: return 0x4ADFA541;
		        case 101: return 0x3DD895D7;
		        case 102: return 0xA4D1C46D;
		        case 103: return 0xD3D6F4FB;
		        case 104: return 0x4369E96A;
		        case 105: return 0x346ED9FC;
		        case 106: return 0xAD678846;
		        case 107: return 0xDA60B8D0;
		        case 108: return 0x44042D73;
		        case 109: return 0x33031DE5;
		        case 110: return 0xAA0A4C5F;
		        case 111: return 0xDD0D7CC9;
		        case 112: return 0x5005713C;
		        case 113: return 0x270241AA;
		        case 114: return 0xBE0B1010;
		        case 115: return 0xC90C2086;
		        case 116: return 0x5768B525;
		        case 117: return 0x206F85B3;
		        case 118: return 0xB966D409;
		        case 119: return 0xCE61E49F;
		        case 120: return 0x5EDEF90E;
		        case 121: return 0x29D9C998;
		        case 122: return 0xB0D09822;
		        case 123: return 0xC7D7A8B4;
		        case 124: return 0x59B33D17;
		        case 125: return 0x2EB40D81;
		        case 126: return 0xB7BD5C3B;
		        case 127: return 0xC0BA6CAD;
		        case 128: return 0xEDB88320;
		        case 129: return 0x9ABFB3B6;
		        case 130: return 0x03B6E20C;
		        case 131: return 0x74B1D29A;
		        case 132: return 0xEAD54739;
		        case 133: return 0x9DD277AF;
		        case 134: return 0x04DB2615;
		        case 135: return 0x73DC1683;
		        case 136: return 0xE3630B12;
		        case 137: return 0x94643B84;
		        case 138: return 0x0D6D6A3E;
		        case 139: return 0x7A6A5AA8;
		        case 140: return 0xE40ECF0B;
		        case 141: return 0x9309FF9D;
		        case 142: return 0x0A00AE27;
		        case 143: return 0x7D079EB1;
		        case 144: return 0xF00F9344;
		        case 145: return 0x8708A3D2;
		        case 146: return 0x1E01F268;
		        case 147: return 0x6906C2FE;
		        case 148: return 0xF762575D;
		        case 149: return 0x806567CB;
		        case 150: return 0x196C3671;
		        case 151: return 0x6E6B06E7;
		        case 152: return 0xFED41B76;
		        case 153: return 0x89D32BE0;
		        case 154: return 0x10DA7A5A;
		        case 155: return 0x67DD4ACC;
		        case 156: return 0xF9B9DF6F;
		        case 157: return 0x8EBEEFF9;
		        case 158: return 0x17B7BE43;
		        case 159: return 0x60B08ED5;
		        case 160: return 0xD6D6A3E8;
		        case 161: return 0xA1D1937E;
		        case 162: return 0x38D8C2C4;
		        case 163: return 0x4FDFF252;
		        case 164: return 0xD1BB67F1;
		        case 165: return 0xA6BC5767;
		        case 166: return 0x3FB506DD;
		        case 167: return 0x48B2364B;
		        case 168: return 0xD80D2BDA;
		        case 169: return 0xAF0A1B4C;
		        case 170: return 0x36034AF6;
		        case 171: return 0x41047A60;
		        case 172: return 0xDF60EFC3;
		        case 173: return 0xA867DF55;
		        case 174: return 0x316E8EEF;
		        case 175: return 0x4669BE79;
		        case 176: return 0xCB61B38C;
		        case 177: return 0xBC66831A;
		        case 178: return 0x256FD2A0;
		        case 179: return 0x5268E236;
		        case 180: return 0xCC0C7795;
		        case 181: return 0xBB0B4703;
		        case 182: return 0x220216B9;
		        case 183: return 0x5505262F;
		        case 184: return 0xC5BA3BBE;
		        case 185: return 0xB2BD0B28;
		        case 186: return 0x2BB45A92;
		        case 187: return 0x5CB36A04;
		        case 188: return 0xC2D7FFA7;
		        case 189: return 0xB5D0CF31;
		        case 190: return 0x2CD99E8B;
		        case 191: return 0x5BDEAE1D;
		        case 192: return 0x9B64C2B0;
		        case 193: return 0xEC63F226;
		        case 194: return 0x756AA39C;
		        case 195: return 0x026D930A;
		        case 196: return 0x9C0906A9;
		        case 197: return 0xEB0E363F;
		        case 198: return 0x72076785;
		        case 199: return 0x05005713;
		        case 200: return 0x95BF4A82;
		        case 201: return 0xE2B87A14;
		        case 202: return 0x7BB12BAE;
		        case 203: return 0x0CB61B38;
		        case 204: return 0x92D28E9B;
		        case 205: return 0xE5D5BE0D;
		        case 206: return 0x7CDCEFB7;
		        case 207: return 0x0BDBDF21;
		        case 208: return 0x86D3D2D4;
		        case 209: return 0xF1D4E242;
		        case 210: return 0x68DDB3F8;
		        case 211: return 0x1FDA836E;
		        case 212: return 0x81BE16CD;
		        case 213: return 0xF6B9265B;
		        case 214: return 0x6FB077E1;
		        case 215: return 0x18B74777;
		        case 216: return 0x88085AE6;
		        case 217: return 0xFF0F6A70;
		        case 218: return 0x66063BCA;
		        case 219: return 0x11010B5C;
		        case 220: return 0x8F659EFF;
		        case 221: return 0xF862AE69;
		        case 222: return 0x616BFFD3;
		        case 223: return 0x166CCF45;
		        case 224: return 0xA00AE278;
		        case 225: return 0xD70DD2EE;
		        case 226: return 0x4E048354;
		        case 227: return 0x3903B3C2;
		        case 228: return 0xA7672661;
		        case 229: return 0xD06016F7;
		        case 230: return 0x4969474D;
		        case 231: return 0x3E6E77DB;
		        case 232: return 0xAED16A4A;
		        case 233: return 0xD9D65ADC;
		        case 234: return 0x40DF0B66;
		        case 235: return 0x37D83BF0;
		        case 236: return 0xA9BCAE53;
		        case 237: return 0xDEBB9EC5;
		        case 238: return 0x47B2CF7F;
		        case 239: return 0x30B5FFE9;
		        case 240: return 0xBDBDF21C;
		        case 241: return 0xCABAC28A;
		        case 242: return 0x53B39330;
		        case 243: return 0x24B4A3A6;
		        case 244: return 0xBAD03605;
		        case 245: return 0xCDD70693;
		        case 246: return 0x54DE5729;
		        case 247: return 0x23D967BF;
		        case 248: return 0xB3667A2E;
		        case 249: return 0xC4614AB8;
		        case 250: return 0x5D681B02;
		        case 251: return 0x2A6F2B94;
		        case 252: return 0xB40BBE37;
		        case 253: return 0xC30C8EA1;
		        case 254: return 0x5A05DF1B;
		        case 255: return 0x2D02EF8D;
		    }
		}
		
		

		[MethodImpl(MethodImplOptions.NoInlining)]
		private readonly bool GetMatchLength_NO_INLINE(uint lenLimit, uint cur, uint pby1, ref uint len)
		{
			return GetMatchLength_INLINE(lenLimit, cur, pby1, ref len);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private readonly bool GetMatchLength_INLINE(uint lenLimit, uint cur, uint pby1, ref uint len)
		{
			uint prevLen = len;

		    if (lenLimit >= sizeof(ulong4))
			{
				uint __lenLimit = lenLimit - ((uint)sizeof(ulong4) - 1);

				while (len < __lenLimit)
				{
				    if (Hint.Unlikely((*(ulong4*)(_bufferBase + cur + len)).Equals(*(ulong4*)(_bufferBase + pby1 + len))))
				    {
				        len += (uint)sizeof(ulong4);
				    }
				    else
					{
						int cmp = bitmask(*(byte32*)(_bufferBase + cur + len) == (*(byte32*)(_bufferBase + pby1 + len)));

						bool result = tzcnt(cmp) == 0;
						len += result ? (uint)t1cnt(cmp) : 0;

						return result;
					}
				}
			}
			
		    if (lenLimit >= sizeof(ulong2)
			  & len < lenLimit - ((uint)sizeof(ulong2) - 1))
			{
				len += (*(ulong2*)(_bufferBase + cur + len)).Equals(*(ulong2*)(_bufferBase + pby1 + len)) ? (uint)sizeof(ulong2) : 0;
			}
			
		    if (lenLimit >= sizeof(ulong)
			  & len < lenLimit - ((uint)sizeof(ulong) - 1))
			{
				len += (*(ulong*)(_bufferBase + cur + len)).Equals(*(ulong*)(_bufferBase + pby1 + len)) ? (uint)sizeof(ulong) : 0;
			}
			
		    if (lenLimit >= sizeof(uint)
			  & len < lenLimit - ((uint)sizeof(uint) - 1))
			{
				len += (*(uint*)(_bufferBase + cur + len)).Equals(*(uint*)(_bufferBase + pby1 + len)) ? (uint)sizeof(uint) : 0;
			}
			
		    if (lenLimit >= sizeof(ushort)
			  & len < lenLimit - ((uint)sizeof(ushort) - 1))
			{
				len += (*(ushort*)(_bufferBase + cur + len)).Equals(*(ushort*)(_bufferBase + pby1 + len)) ? (uint)sizeof(ushort) : 0;
			}
			
		    if (lenLimit >= sizeof(byte)
			  & len < lenLimit - ((uint)sizeof(byte) - 1))
			{
				len += (*(byte*)(_bufferBase + cur + len)).Equals(*(byte*)(_bufferBase + pby1 + len)) ? (uint)sizeof(byte) : 0;
			}

		    return len != prevLen;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private readonly bool GetMatchLength(uint lenLimit, uint cur, uint pby1, ref uint len)
		{
			if (COMPILATION_OPTIONS.OPTIMIZE_FOR == Unity.Burst.OptimizeFor.Performance)
			{
				return GetMatchLength_INLINE(lenLimit, cur, pby1, ref len);
			}
			else
			{
				return GetMatchLength_NO_INLINE(lenLimit, cur, pby1, ref len);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void MoveBlock()
		{
			uint offset = _bufferOffset + _pos - _keepSizeBefore;
			offset -= tobyte(offset != 0);
			uint numBytes = (uint)(_bufferOffset) + _streamPos - offset;

			for (uint i = 0; i < numBytes; i++)
			{
				_bufferBase[i] = _bufferBase[offset + i];
			}
			_bufferOffset -= offset;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ReadBlock()
		{
			if (_streamEndWasReached)
				return;
			while (true)
			{
				int size = (int)((0 - _bufferOffset) + _blockSize - _streamPos);
				if (size == 0)
				{
					return;
				}
				long numReadBytes = min(size, inSize);
				if (numReadBytes == 0)
				{
					_posLimit = _streamPos;
					uint pointerToPostion = _bufferOffset + _posLimit;
					if (pointerToPostion > _pointerToLastSafePosition)
					{
						_posLimit = _pointerToLastSafePosition - _bufferOffset;
					}

					_streamEndWasReached = true;
					return;
				}
				else
				{
					inSize -= numReadBytes;
					int __pos = (int)(_bufferOffset + _streamPos);
					for (long i = 0; i < numReadBytes; i++)
					{
						_bufferBase[__pos + i] = _stream[_streamPos2++];
					}
				}
				_streamPos += (uint)numReadBytes;
				if (_streamPos >= _pos + _keepSizeAfter)
				{
					_posLimit = _streamPos - _keepSizeAfter;
				}
			}
		}

		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void MovePos()
		{
			if (++_cyclicBufferPos >= _cyclicBufferSize)
			{
				_cyclicBufferPos = 0;
			}
				
			_pos++;

			if (Hint.Unlikely(_pos > _posLimit))
			{
				uint pointerToPostion = _bufferOffset + _pos;
				if (Hint.Likely(pointerToPostion > _pointerToLastSafePosition))
				{
					MoveBlock();
				}
				ReadBlock();
			}
			if (Hint.Unlikely(_pos == kMaxValForNormalize))
			{
				Normalize();
			}
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public readonly byte GetIndexByte(int index) { return _bufferBase[_bufferOffset + _pos + index]; }
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public readonly uint GetMatchLen(int index, uint distance, uint limit)
		{
			if (_streamEndWasReached
			 & (_pos + index + limit > _streamPos))
			{
				limit = _streamPos - (uint)(_pos + index);
			}
			distance++;
			uint pby = _bufferOffset + _pos + (uint)index;
			
			uint len = 0;
			GetMatchLength(limit, pby, pby - distance, ref len);
			return len;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public readonly uint4 GetMatchLen4(uint index, uint4 distance, uint limit)
		{
			if (_streamEndWasReached
			 & (_pos + index + limit > _streamPos))
			{
				limit = _streamPos - (_pos + index);
			}

			distance++;
			uint4 pby = _bufferOffset + _pos + index;
			
			uint lenX = 0;
			uint lenY = 0;
			uint lenZ = 0;
			uint lenW = 0;
			GetMatchLength(limit, pby.x, (pby - distance).x, ref lenX);
			GetMatchLength(limit, pby.y, (pby - distance).y, ref lenY);
			GetMatchLength(limit, pby.z, (pby - distance).z, ref lenZ);
			GetMatchLength(limit, pby.w, (pby - distance).w, ref lenW);

			return new uint4(lenX, lenY, lenZ, lenW);
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ReduceOffsets(int subValue)
		{
			_bufferOffset += (uint)subValue;
			_posLimit -= (uint)subValue;
			_pos -= (uint)subValue;
			_streamPos -= (uint)subValue;
		}
		
		public void Init()
		{
			ReadBlock();
			ReduceOffsets(-1);
		}

		public void InitHashParallel(int slice, int workers)
		{
			uint start = (uint)slice * (_hashSizeSum / (uint)workers);
			uint end = slice == workers - 1 ? _hashSizeSum : (uint)(slice + 1) * (_hashSizeSum / (uint)workers);
			uint count = end - start;

			uint* ptr = _hash + start;
			while (Hint.Likely(count >= 32))
			{
				*((uint8*)ptr + 0) = 0;
				*((uint8*)ptr + 1) = 0;
				*((uint8*)ptr + 2) = 0;
				*((uint8*)ptr + 3) = 0;
				count -= 32;
				ptr += 32;
			}

			if (Hint.Likely(count >= 16))
			{
				*((uint8*)ptr + 0) = 0;
				*((uint8*)ptr + 1) = 0;
				count -= 16;
				ptr += 16;
			}

			if (Hint.Likely(count >= 8))
			{
				*((uint8*)ptr + 0) = 0;
				count -= 8;
				ptr += 8;
			}

			if (Hint.Likely(count >= 4))
			{
				*((uint4*)ptr + 0) = 0;
				count -= 4;
				ptr += 4;
			}

			if (Hint.Likely(count >= 2))
			{
				*((uint2*)ptr + 0) = 0;
				count -= 2;
				ptr += 2;
			}

			if (Hint.Likely(count != 0))
			{
				*ptr = 0;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint GetMatches(uint* distances)
		{
			byte kNumHashDirectBytes = HASH_ARRAY ? (byte)0 : (byte)2;
			uint kFixHashSize = HASH_ARRAY ? kHash2Size + kHash3Size : 0;

			uint lenLimit;
			if (Hint.Likely(_pos + _matchMaxLen <= _streamPos))
			{
				lenLimit = _matchMaxLen;
			}
			else
			{
				lenLimit = _streamPos - _pos;
				if (Hint.Unlikely(lenLimit < tobyte(HASH_ARRAY) + 3))
				{
					MovePos();
					return 0;
				}
			}

			uint offset = 0;
			uint matchMinPos = (_pos > _cyclicBufferSize) ? (_pos - _cyclicBufferSize) : 0;
			uint cur = _bufferOffset + _pos;
			uint maxLen = kStartMaxLen; // to avoid items for len < hashSize;
			uint hashValue, hash2Value = 0, hash3Value = 0;

			if (HASH_ARRAY)
			{
				uint temp = CRC(_bufferBase[cur]) ^ _bufferBase[cur + 1];
				hash2Value = temp & (kHash2Size - 1);
				temp ^= ((uint)(_bufferBase[cur + 2]) << 8);
				hash3Value = temp & (kHash3Size - 1);
				hashValue = (temp ^ (CRC(_bufferBase[cur + 3]) << 5)) & _hashMask;
			}
			else
			{
				hashValue = _bufferBase[cur] ^ ((uint)(_bufferBase[cur + 1]) << 8);
			}

			uint curMatch = _hash[kFixHashSize + hashValue];
			if (HASH_ARRAY)
			{
				uint curMatch2 = _hash[hash2Value];
				uint curMatch3 = _hash[kHash2Size + hash3Value];
				_hash[hash2Value] = _pos;
				_hash[kHash2Size + hash3Value] = _pos;
				if (Hint.Likely(curMatch2 > matchMinPos
							 && _bufferBase[_bufferOffset + curMatch2] == _bufferBase[cur]))
				{
					distances[offset++] = maxLen = 2;
					distances[offset++] = _pos - curMatch2 - 1;
				}
				if (Hint.Likely(curMatch3 > matchMinPos
							 && _bufferBase[_bufferOffset + curMatch3] == _bufferBase[cur]))
				{
					offset -= curMatch3 == curMatch2 ? 2u : 0u;
					distances[offset++] = maxLen = 3;
					distances[offset++] = _pos - curMatch3 - 1;
					curMatch2 = curMatch3;
				}
				
				offset -= (offset != 0 & curMatch2 == curMatch) ? 2u : 0u;
				maxLen = (offset != 0 & curMatch2 == curMatch) ? kStartMaxLen : maxLen;
			}

			_hash[kFixHashSize + hashValue] = _pos;

			uint ptr0 = (_cyclicBufferPos << 1) + 1;
			uint ptr1 = (_cyclicBufferPos << 1);

			uint len0, len1;
			len0 = len1 = kNumHashDirectBytes;
			
			if (kNumHashDirectBytes != 0 
			  & curMatch > matchMinPos
			 && _bufferBase[_bufferOffset + curMatch + kNumHashDirectBytes] != _bufferBase[cur + kNumHashDirectBytes])
			{
				distances[offset++] = maxLen = kNumHashDirectBytes;
				distances[offset++] = _pos - curMatch - 1;
			}
			
			uint count = _cutValue;
			
			while(true)
			{
				if (curMatch <= matchMinPos || count-- == 0)
				{
					_son[ptr0] = _son[ptr1] = kEmptyHashValue;
					break;
				}
				uint delta = _pos - curMatch;
				uint cyclicPos = ((delta <= _cyclicBufferPos) ? (_cyclicBufferPos - delta) :
																(_cyclicBufferPos - delta + _cyclicBufferSize)) << 1;
				uint pby1 = _bufferOffset + curMatch;
				uint len = min(len0, len1);
				if (GetMatchLength(lenLimit, cur, pby1, ref len))
				{
					if (maxLen < len)
					{
						distances[offset++] = maxLen = len;
						distances[offset++] = delta - 1;
						if (len == lenLimit)
						{
							_son[ptr1] = _son[cyclicPos];
							_son[ptr0] = _son[cyclicPos + 1];
							break;
						}
					}
				}
				bool b = _bufferBase[pby1 + len] < _bufferBase[cur + len];
				_son[b ? ptr1 : ptr0] = curMatch;
				ptr0 = b ? ptr0 : cyclicPos;
				ptr1 = b ? cyclicPos + 1 : ptr1;
				curMatch = _son[b ? ptr1 : ptr0];
				len0 = b ? len0 : len;
				len1 = b ? len : len1;
			}
			MovePos();
			return offset;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Skip(uint num)
		{
			byte kNumHashDirectBytes = HASH_ARRAY ? (byte)0 : (byte)2;
			uint kFixHashSize = HASH_ARRAY ? kHash2Size + kHash3Size : 0;

			do
			{
				uint lenLimit;
				if (Hint.Likely(_pos + _matchMaxLen <= _streamPos))
				{
					lenLimit = _matchMaxLen;
				}	
				else
				{
					lenLimit = _streamPos - _pos;
					if (lenLimit < tobyte(HASH_ARRAY) + 3)
					{
						MovePos();
						continue;
					}
				}

				uint matchMinPos = (_pos > _cyclicBufferSize) ? (_pos - _cyclicBufferSize) : 0;
				uint cur = _bufferOffset + _pos;

				uint hashValue;

				if (HASH_ARRAY)
				{
					uint temp = CRC(_bufferBase[cur]) ^ _bufferBase[cur + 1];
					uint hash2Value = temp & (kHash2Size - 1);
					_hash[hash2Value] = _pos;
					temp ^= ((uint)(_bufferBase[cur + 2]) << 8);
					uint hash3Value = temp & (kHash3Size - 1);
					_hash[kHash2Size + hash3Value] = _pos;
					hashValue = (temp ^ (CRC(_bufferBase[cur + 3]) << 5)) & _hashMask;
				}
				else
				{
					hashValue = _bufferBase[cur] ^ ((uint)(_bufferBase[cur + 1]) << 8);
				}

				uint curMatch = _hash[kFixHashSize + hashValue];
				_hash[kFixHashSize + hashValue] = _pos;

				uint ptr0 = (_cyclicBufferPos << 1) + 1;
				uint ptr1 = (_cyclicBufferPos << 1);

				uint len0, len1;
				len0 = len1 = kNumHashDirectBytes;

				uint count = _cutValue;
				while (true)
				{
					if (curMatch <= matchMinPos || count-- == 0)
					{
						_son[ptr0] = _son[ptr1] = kEmptyHashValue;
						break;
					}

					uint delta = _pos - curMatch;
					uint cyclicPos = ((delta <= _cyclicBufferPos) ? (_cyclicBufferPos - delta) 
																  : (_cyclicBufferPos - delta + _cyclicBufferSize)) << 1;
					uint pby1 = _bufferOffset + curMatch;
					uint len = min(len0, len1);
					if (GetMatchLength(lenLimit, cur, pby1, ref len))
					{
						if (len == lenLimit)
						{
							_son[ptr1] = _son[cyclicPos];
							_son[ptr0] = _son[cyclicPos + 1];
							break;
						}
					}

					bool b = _bufferBase[pby1 + len] < _bufferBase[cur + len];
					_son[b ? ptr1 : ptr0] = curMatch;
					ptr0 = b ? ptr0 : cyclicPos;
					ptr1 = b ? cyclicPos + 1 : ptr1;
					curMatch = _son[b ? ptr1 : ptr0];
					len0 = b ? len0 : len;
					len1 = b ? len : len1;
				}
				MovePos();
			}
			while (--num != 0);
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private readonly void NormalizeLinks(uint* items, uint numItems, uint subValue)
		{
			for (uint i = 0; i < numItems; i++)
			{
				items[i] = max(items[i], subValue) - subValue;
			}
		}
		
		[MethodImpl(MethodImplOptions.NoInlining)]
		void Normalize()
		{
			uint subValue = _pos - _cyclicBufferSize;
			NormalizeLinks(_son, _cyclicBufferSize * 2, subValue);
			NormalizeLinks(_hash, _hashSizeSum, subValue);
			ReduceOffsets((int)subValue);
		}
	}
}
