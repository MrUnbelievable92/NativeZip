using System.Runtime.CompilerServices;

using static MaxMath.math;

namespace NativeZip
{
	unsafe internal struct RangeEncoder
	{
		public const uint kTopValue = 1 << 24;

		private UnsafeLongByteList* Stream;

		public ulong Low;
		public uint Range;
		private uint _cacheSize;
		private byte _cache;

		public void SetStream(UnsafeLongByteList* stream)
		{
			Stream = stream;
		}

		public void Init()
		{
			Low = 0;
			Range = (0xFFFF_FFFFu >> BitEncoder.kNumBitModelTotalBits) * (BitEncoder.kBitModelTotal >> 1);
			_cacheSize = 1;
			_cache = 0;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void FlushData()
		{
			for (int i = 0; i < 5; i++)
			{
				ShiftLow();
			}
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ShiftLow()
		{
			if ((tobyte((uint)Low < 0xFF00_0000u) | (int)(Low >> 32) & 1) != 0)
			{
				byte temp = _cache;
				long idx = Stream->Length;
				Stream->Grow(Stream->Capacity + _cacheSize);
				Stream->Length += _cacheSize;
				Stream->Ptr[idx++] = (byte)(_cache + (byte)(Low >> 32));
				while (--_cacheSize != 0)
				{
					Stream->Ptr[idx++] = (byte)((byte)(Low >> 32) - 1);
				}
				_cache = (byte)(Low >> 24);
			}
			_cacheSize++;
			Low = (uint)Low << 8;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void EncodeDirectBits(uint v, int numTotalBits)
		{
			for (int i = numTotalBits - 1; i >= 0; i--)
			{
				Range >>= 1;
				Low += Range & (ulong)-(long)((v >> i) & 1);
				if (Range < kTopValue)
				{
					Range <<= 8;
					ShiftLow();
				}
			}
		}
	}

	unsafe internal struct RangeDecoder
	{
		public const uint kTopValue = 1 << 24;
		public UnsafeLongByteList* Stream;
		public long StreamPos;

		public uint Range;
		public uint Code;
		
		public void SetStream(UnsafeLongByteList* stream)
		{
			Stream = stream;
		}

		public void Init()
		{
			Code = 0;
			Range = (0xFFFFFFFF >> BitDecoder.kNumBitModelTotalBits) * (BitDecoder.kBitModelTotal >> 1);
			StreamPos = 12;
			Code = reversebytes(*(uint*)(Stream->Ptr + 8));
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public byte ReadByte()
		{
			return Stream->Ptr[StreamPos++];
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint DecodeDirectBits(int numTotalBits)
		{
			uint result = 0;
			while (numTotalBits-- > 0)
			{
				Range >>= 1;
				bool borrow = Range > Code;
				Code -= borrow ? 0 : Range;
				result = (result << 1) | (1 - touint(borrow));

				if (Range < kTopValue)
				{
					Code = (Code << 8) | ReadByte();
					Range <<= 8;
				}
			}
			return result;
		}
	}
}
