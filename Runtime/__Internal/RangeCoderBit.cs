using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;
using DevTools;

using static MaxMath.math;
using MaxMath.Intrinsics;

namespace NativeZip
{
	internal struct BitEncoder
	{
		public const int kNumBitModelTotalBits = 11;
		public const uint kBitModelTotal = (1 << kNumBitModelTotalBits);
		public const int kNumMoveBits = 5;
		public const int kNumMoveReducingBits = 2;
		public const int kNumBitPriceShiftBits = 6;

		public ushort Prob;
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Init() { Prob = (ushort)(kBitModelTotal >> 1); }
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Encode(ref RangeEncoder encoder, bool symbol)
		{
			uint newBound = (encoder.Range >> kNumBitModelTotalBits) * Prob;
			if (constexpr.IS_CONST(symbol))
			{
				if (symbol)
				{
					encoder.Low += newBound;
					encoder.Range -= newBound;
					Prob -= (ushort)(Prob >> kNumMoveBits);
				}
				else
				{
					encoder.Range = newBound;
					Prob += (ushort)((kBitModelTotal - Prob) >> kNumMoveBits);
				}
			}
			else
			{
				encoder.Low += symbol ? newBound : 0;
				encoder.Range = (symbol ? encoder.Range : (newBound * 2)) - newBound;
				Prob += symbol ? (ushort)(0 - (Prob >> kNumMoveBits)) : (ushort)((kBitModelTotal - Prob) >> kNumMoveBits);
			}

			if (encoder.Range < RangeEncoder.kTopValue)
			{
				encoder.Range <<= 8;
				encoder.ShiftLow();
			}
		}

		// Switch table >>> stackalloc (reads SIMD vectors from memory either way, + MOV instructions)
		// Switch table == static readonly memory without a copy in C# land
		[return: AssumeRange(0ul, 576ul)]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static ushort ProbPrices([AssumeRange(0ul, 511ul)] uint index)
		{
			switch (index)
			{
				case 0:   return 0;
				case 1:   return 576;
				case 2:   return 512;
				case 3:   return 480;
				case 4:   return 448;
				case 5:   return 432;
				case 6:   return 416;
				case 7:   return 400;
				case 8:   return 384;
				case 9:   return 376;
				case 10:  return 368;
				case 11:  return 360;
				case 12:  return 352;
				case 13:  return 344;
				case 14:  return 336;
				case 15:  return 328;
				case 16:  return 320;
				case 17:  return 316;
				case 18:  return 312;
				case 19:  return 308;
				case 20:  return 304;
				case 21:  return 300;
				case 22:  return 296;
				case 23:  return 292;
				case 24:  return 288;
				case 25:  return 284;
				case 26:  return 280;
				case 27:  return 276;
				case 28:  return 272;
				case 29:  return 268;
				case 30:  return 264;
				case 31:  return 260;
				case 32:  return 256;
				case 33:  return 254;
				case 34:  return 252;
				case 35:  return 250;
				case 36:  return 248;
				case 37:  return 246;
				case 38:  return 244;
				case 39:  return 242;
				case 40:  return 240;
				case 41:  return 238;
				case 42:  return 236;
				case 43:  return 234;
				case 44:  return 232;
				case 45:  return 230;
				case 46:  return 228;
				case 47:  return 226;
				case 48:  return 224;
				case 49:  return 222;
				case 50:  return 220;
				case 51:  return 218;
				case 52:  return 216;
				case 53:  return 214;
				case 54:  return 212;
				case 55:  return 210;
				case 56:  return 208;
				case 57:  return 206;
				case 58:  return 204;
				case 59:  return 202;
				case 60:  return 200;
				case 61:  return 198;
				case 62:  return 196;
				case 63:  return 194;
				case 64:  return 192;
				case 65:  return 191;
				case 66:  return 190;
				case 67:  return 189;
				case 68:  return 188;
				case 69:  return 187;
				case 70:  return 186;
				case 71:  return 185;
				case 72:  return 184;
				case 73:  return 183;
				case 74:  return 182;
				case 75:  return 181;
				case 76:  return 180;
				case 77:  return 179;
				case 78:  return 178;
				case 79:  return 177;
				case 80:  return 176;
				case 81:  return 175;
				case 82:  return 174;
				case 83:  return 173;
				case 84:  return 172;
				case 85:  return 171;
				case 86:  return 170;
				case 87:  return 169;
				case 88:  return 168;
				case 89:  return 167;
				case 90:  return 166;
				case 91:  return 165;
				case 92:  return 164;
				case 93:  return 163;
				case 94:  return 162;
				case 95:  return 161;
				case 96:  return 160;
				case 97:  return 159;
				case 98:  return 158;
				case 99:  return 157;
				case 100: return 156;
				case 101: return 155;
				case 102: return 154;
				case 103: return 153;
				case 104: return 152;
				case 105: return 151;
				case 106: return 150;
				case 107: return 149;
				case 108: return 148;
				case 109: return 147;
				case 110: return 146;
				case 111: return 145;
				case 112: return 144;
				case 113: return 143;
				case 114: return 142;
				case 115: return 141;
				case 116: return 140;
				case 117: return 139;
				case 118: return 138;
				case 119: return 137;
				case 120: return 136;
				case 121: return 135;
				case 122: return 134;
				case 123: return 133;
				case 124: return 132;
				case 125: return 131;
				case 126: return 130;
				case 127: return 129;
				case 128: return 128;
				case 129: return 127;
				case 130: return 127;
				case 131: return 126;
				case 132: return 126;
				case 133: return 125;
				case 134: return 125;
				case 135: return 124;
				case 136: return 124;
				case 137: return 123;
				case 138: return 123;
				case 139: return 122;
				case 140: return 122;
				case 141: return 121;
				case 142: return 121;
				case 143: return 120;
				case 144: return 120;
				case 145: return 119;
				case 146: return 119;
				case 147: return 118;
				case 148: return 118;
				case 149: return 117;
				case 150: return 117;
				case 151: return 116;
				case 152: return 116;
				case 153: return 115;
				case 154: return 115;
				case 155: return 114;
				case 156: return 114;
				case 157: return 113;
				case 158: return 113;
				case 159: return 112;
				case 160: return 112;
				case 161: return 111;
				case 162: return 111;
				case 163: return 110;
				case 164: return 110;
				case 165: return 109;
				case 166: return 109;
				case 167: return 108;
				case 168: return 108;
				case 169: return 107;
				case 170: return 107;
				case 171: return 106;
				case 172: return 106;
				case 173: return 105;
				case 174: return 105;
				case 175: return 104;
				case 176: return 104;
				case 177: return 103;
				case 178: return 103;
				case 179: return 102;
				case 180: return 102;
				case 181: return 101;
				case 182: return 101;
				case 183: return 100;
				case 184: return 100;
				case 185: return 99;
				case 186: return 99;
				case 187: return 98;
				case 188: return 98;
				case 189: return 97;
				case 190: return 97;
				case 191: return 96;
				case 192: return 96;
				case 193: return 95;
				case 194: return 95;
				case 195: return 94;
				case 196: return 94;
				case 197: return 93;
				case 198: return 93;
				case 199: return 92;
				case 200: return 92;
				case 201: return 91;
				case 202: return 91;
				case 203: return 90;
				case 204: return 90;
				case 205: return 89;
				case 206: return 89;
				case 207: return 88;
				case 208: return 88;
				case 209: return 87;
				case 210: return 87;
				case 211: return 86;
				case 212: return 86;
				case 213: return 85;
				case 214: return 85;
				case 215: return 84;
				case 216: return 84;
				case 217: return 83;
				case 218: return 83;
				case 219: return 82;
				case 220: return 82;
				case 221: return 81;
				case 222: return 81;
				case 223: return 80;
				case 224: return 80;
				case 225: return 79;
				case 226: return 79;
				case 227: return 78;
				case 228: return 78;
				case 229: return 77;
				case 230: return 77;
				case 231: return 76;
				case 232: return 76;
				case 233: return 75;
				case 234: return 75;
				case 235: return 74;
				case 236: return 74;
				case 237: return 73;
				case 238: return 73;
				case 239: return 72;
				case 240: return 72;
				case 241: return 71;
				case 242: return 71;
				case 243: return 70;
				case 244: return 70;
				case 245: return 69;
				case 246: return 69;
				case 247: return 68;
				case 248: return 68;
				case 249: return 67;
				case 250: return 67;
				case 251: return 66;
				case 252: return 66;
				case 253: return 65;
				case 254: return 65;
				case 255: return 64;
				case 256: return 64;
				case 257: return 63;
				case 258: return 63;
				case 259: return 63;
				case 260: return 63;
				case 261: return 62;
				case 262: return 62;
				case 263: return 62;
				case 264: return 62;
				case 265: return 61;
				case 266: return 61;
				case 267: return 61;
				case 268: return 61;
				case 269: return 60;
				case 270: return 60;
				case 271: return 60;
				case 272: return 60;
				case 273: return 59;
				case 274: return 59;
				case 275: return 59;
				case 276: return 59;
				case 277: return 58;
				case 278: return 58;
				case 279: return 58;
				case 280: return 58;
				case 281: return 57;
				case 282: return 57;
				case 283: return 57;
				case 284: return 57;
				case 285: return 56;
				case 286: return 56;
				case 287: return 56;
				case 288: return 56;
				case 289: return 55;
				case 290: return 55;
				case 291: return 55;
				case 292: return 55;
				case 293: return 54;
				case 294: return 54;
				case 295: return 54;
				case 296: return 54;
				case 297: return 53;
				case 298: return 53;
				case 299: return 53;
				case 300: return 53;
				case 301: return 52;
				case 302: return 52;
				case 303: return 52;
				case 304: return 52;
				case 305: return 51;
				case 306: return 51;
				case 307: return 51;
				case 308: return 51;
				case 309: return 50;
				case 310: return 50;
				case 311: return 50;
				case 312: return 50;
				case 313: return 49;
				case 314: return 49;
				case 315: return 49;
				case 316: return 49;
				case 317: return 48;
				case 318: return 48;
				case 319: return 48;
				case 320: return 48;
				case 321: return 47;
				case 322: return 47;
				case 323: return 47;
				case 324: return 47;
				case 325: return 46;
				case 326: return 46;
				case 327: return 46;
				case 328: return 46;
				case 329: return 45;
				case 330: return 45;
				case 331: return 45;
				case 332: return 45;
				case 333: return 44;
				case 334: return 44;
				case 335: return 44;
				case 336: return 44;
				case 337: return 43;
				case 338: return 43;
				case 339: return 43;
				case 340: return 43;
				case 341: return 42;
				case 342: return 42;
				case 343: return 42;
				case 344: return 42;
				case 345: return 41;
				case 346: return 41;
				case 347: return 41;
				case 348: return 41;
				case 349: return 40;
				case 350: return 40;
				case 351: return 40;
				case 352: return 40;
				case 353: return 39;
				case 354: return 39;
				case 355: return 39;
				case 356: return 39;
				case 357: return 38;
				case 358: return 38;
				case 359: return 38;
				case 360: return 38;
				case 361: return 37;
				case 362: return 37;
				case 363: return 37;
				case 364: return 37;
				case 365: return 36;
				case 366: return 36;
				case 367: return 36;
				case 368: return 36;
				case 369: return 35;
				case 370: return 35;
				case 371: return 35;
				case 372: return 35;
				case 373: return 34;
				case 374: return 34;
				case 375: return 34;
				case 376: return 34;
				case 377: return 33;
				case 378: return 33;
				case 379: return 33;
				case 380: return 33;
				case 381: return 32;
				case 382: return 32;
				case 383: return 32;
				case 384: return 32;
				case 385: return 31;
				case 386: return 31;
				case 387: return 31;
				case 388: return 31;
				case 389: return 30;
				case 390: return 30;
				case 391: return 30;
				case 392: return 30;
				case 393: return 29;
				case 394: return 29;
				case 395: return 29;
				case 396: return 29;
				case 397: return 28;
				case 398: return 28;
				case 399: return 28;
				case 400: return 28;
				case 401: return 27;
				case 402: return 27;
				case 403: return 27;
				case 404: return 27;
				case 405: return 26;
				case 406: return 26;
				case 407: return 26;
				case 408: return 26;
				case 409: return 25;
				case 410: return 25;
				case 411: return 25;
				case 412: return 25;
				case 413: return 24;
				case 414: return 24;
				case 415: return 24;
				case 416: return 24;
				case 417: return 23;
				case 418: return 23;
				case 419: return 23;
				case 420: return 23;
				case 421: return 22;
				case 422: return 22;
				case 423: return 22;
				case 424: return 22;
				case 425: return 21;
				case 426: return 21;
				case 427: return 21;
				case 428: return 21;
				case 429: return 20;
				case 430: return 20;
				case 431: return 20;
				case 432: return 20;
				case 433: return 19;
				case 434: return 19;
				case 435: return 19;
				case 436: return 19;
				case 437: return 18;
				case 438: return 18;
				case 439: return 18;
				case 440: return 18;
				case 441: return 17;
				case 442: return 17;
				case 443: return 17;
				case 444: return 17;
				case 445: return 16;
				case 446: return 16;
				case 447: return 16;
				case 448: return 16;
				case 449: return 15;
				case 450: return 15;
				case 451: return 15;
				case 452: return 15;
				case 453: return 14;
				case 454: return 14;
				case 455: return 14;
				case 456: return 14;
				case 457: return 13;
				case 458: return 13;
				case 459: return 13;
				case 460: return 13;
				case 461: return 12;
				case 462: return 12;
				case 463: return 12;
				case 464: return 12;
				case 465: return 11;
				case 466: return 11;
				case 467: return 11;
				case 468: return 11;
				case 469: return 10;
				case 470: return 10;
				case 471: return 10;
				case 472: return 10;
				case 473: return 9;
				case 474: return 9;
				case 475: return 9;
				case 476: return 9;
				case 477: return 8;
				case 478: return 8;
				case 479: return 8;
				case 480: return 8;
				case 481: return 7;
				case 482: return 7;
				case 483: return 7;
				case 484: return 7;
				case 485: return 6;
				case 486: return 6;
				case 487: return 6;
				case 488: return 6;
				case 489: return 5;
				case 490: return 5;
				case 491: return 5;
				case 492: return 5;
				case 493: return 4;
				case 494: return 4;
				case 495: return 4;
				case 496: return 4;
				case 497: return 3;
				case 498: return 3;
				case 499: return 3;
				case 500: return 3;
				case 501: return 2;
				case 502: return 2;
				case 503: return 2;
				case 504: return 2;
				case 505: return 1;
				case 506: return 1;
				case 507: return 1;
				case 508: return 1;
				case 509: return 0;
				case 510: return 0;
				case 511: return 0;

				default: throw Assert.Unreachable();
			}
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint GetPrice(bool symbol)
		{
			return ProbPrices(((uint)(symbol ? (0 - Prob) : Prob) & (kBitModelTotal - 1)) >> kNumMoveReducingBits);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint GetPrice0() => ProbPrices((uint)Prob >> kNumMoveReducingBits);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint GetPrice1() => ProbPrices((kBitModelTotal - Prob) >> kNumMoveReducingBits);
	}

	internal struct BitDecoder
	{
		public const int kNumBitModelTotalBits = 11;
		public const uint kBitModelTotal = (1 << kNumBitModelTotalBits);
		public const int kNumMoveBits = 5;

		public ushort Prob;

		public void Init() { Prob = (ushort)(kBitModelTotal >> 1); }

		[return: AssumeRange(0ul, 1ul)]
		public uint Decode(ref RangeDecoder rangeDecoder)
		{
			ulong newBound = ((ulong)rangeDecoder.Range >> kNumBitModelTotalBits) * Prob;
			ulong newBound2 = newBound + newBound;
			ulong t = (ulong)rangeDecoder.Code - newBound;
			rangeDecoder.Code = (long)t < 0 ? rangeDecoder.Code : (uint)t;
			rangeDecoder.Range = (uint)(((long)t < 0 ? newBound2 : rangeDecoder.Range) - newBound);
			ushort p = (long)t < 0 ? (ushort)((kBitModelTotal - Prob) >> kNumMoveBits) : (ushort)-(Prob >> kNumMoveBits);
			Prob += p;
			
			if (rangeDecoder.Range < RangeDecoder.kTopValue)
			{
				rangeDecoder.Code = (rangeDecoder.Code << 8) | rangeDecoder.ReadByte();
				rangeDecoder.Range <<= 8;
			}

			return tobyte((long)t >= 0);
		}
	}
}
