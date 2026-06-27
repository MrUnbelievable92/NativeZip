using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using DevTools;
using MaxMath;

using static MaxMath.math;

namespace NativeZip
{
	unsafe internal static class Encoder
	{
		internal const uint kNumOpts = 1 << 12;
		internal const uint kNumLenSpecSymbols = Base.kNumLowLenSymbols + Base.kNumMidLenSymbols;
		internal const uint kIfinityPrice = 0x0FFF_FFFF;

		public static long count0;
		public static long count1;
		public static long count2;
		internal struct EncodingData
		{
			internal BinTree _matchFinder;
			internal RangeEncoder _rangeEncoder;
			internal LiteralEncoder _literalEncoder;
			internal LenPriceTableEncoder _lenEncoder;
			internal LenPriceTableEncoder _repMatchLenEncoder;
			
			[NativeDisableUnsafePtrRestriction] internal void* _data;
		
			internal uint4 reps;
			internal uint4 repLens;
			internal uint4 _repDistances;

			internal long nowPos64;
			internal Base.State _state;

			internal uint _longestMatchLength;
			internal uint _numDistancePairs;
			internal uint _additionalOffset;
			internal uint _matchPriceCount;
			internal ushort _optimumCurrentIndex;
			internal ushort _optimumEndIndex;
			internal byte _alignPriceCount;

			internal byte _previousByte;
			internal ushort _numFastBytes;

			internal uint _dictionarySize;
			internal byte _distTableSize;
			internal byte _posStateBits;
			internal byte _posStateMask;
			internal byte _numLiteralPosStateBits;
			internal byte _numLiteralContextBits;
			internal bool _longestMatchWasFound;

			internal MatchFinder _matchFinderType;
			
			internal Optimal* _optimum => (Optimal*)_data;
			internal uint* _optimumPrice { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (uint*)(_optimum + kNumOpts); }

			internal uint* _matchDistances			{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _optimumPrice + kNumOpts; }
			internal uint* _posSlotPrices			{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _matchDistances + (Base.kMatchMaxLen * 2 + 2); }
			internal uint* _distancesPrices			{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _posSlotPrices + (1 << (Base.kNumPosSlotBits + Base.kNumLenToPosStatesBits)); }
			internal uint* _alignPrices				{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _distancesPrices + (Base.kNumFullDistances << Base.kNumLenToPosStatesBits); }
			internal uint* tempPrices				{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _alignPrices + Base.kAlignTableSize; }

			internal BitEncoder* _isMatch			{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (BitEncoder*)(tempPrices + (Base.kNumFullDistances - Base.kStartPosModelIndex)); }
			internal BitEncoder* _isRep				{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _isMatch + (Base.kNumStates << Base.kNumPosStatesBitsMax); }
			internal BitEncoder* _isRepG0			{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _isRep + Base.kNumStates; }
			internal BitEncoder* _isRepG1			{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _isRepG0 + Base.kNumStates; }
			internal BitEncoder* _isRepG2			{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _isRepG1 + Base.kNumStates; }
			internal BitEncoder* _isRep0Long		{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _isRepG2 + Base.kNumStates; }
			internal BitEncoder* _posEncoders		{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _isRep0Long + (Base.kNumStates << Base.kNumPosStatesBitsMax); }

			internal BitEncoder* _posAlignEncoder	{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _posEncoders + (Base.kNumFullDistances - Base.kEndPosModelIndex); }
			internal BitEncoder* _posSlotEncoder	{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _posAlignEncoder + (1 << Base.kNumAlignBits); }
			
			internal void* _matchFinderData			{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _posSlotEncoder + (Base.kNumLenToPosStates * (1 << Base.kNumPosSlotBits)); }
		}

		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        internal struct MallocJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] internal EncodingData* __Data;
			internal ZipCompressed JobData;
			
			private void ApplySettings()
			{
				if (JobData._settings.Equals(default(CompressionSettings)))
				{
					JobData._settings = CompressionSettings.Default;
				}

				__Data->_dictionarySize = 1u << JobData._settings.DictionarySize;
				__Data->_distTableSize = (byte)(2u * (uint)JobData._settings.DictionarySize);
				__Data->_posStateBits = (byte)JobData._settings.PositionStateBits;	
				__Data->_posStateMask = bitmask8((uint)JobData._settings.PositionStateBits);
				__Data->_numLiteralContextBits = (byte)JobData._settings.LiteralContextBits;
				__Data->_numLiteralPosStateBits = (byte)JobData._settings.LiteralPositionBits;
				__Data->_numFastBytes = (ushort)JobData._settings.FastBytes;
				__Data->_matchFinderType = JobData._settings.MatchFinder;
			}

            public void Execute()
            {
				ApplySettings();

				long bytes = ((long)sizeof(Optimal) * kNumOpts)
						   + ((long)sizeof(uint) * kNumOpts)
						   + ((long)sizeof(uint) * (Base.kMatchMaxLen * 2 + 2))
						   + ((long)sizeof(uint) * (1 << (Base.kNumPosSlotBits + Base.kNumLenToPosStatesBits)))
						   + ((long)sizeof(uint) * (Base.kNumFullDistances << Base.kNumLenToPosStatesBits))
						   + ((long)sizeof(uint) * Base.kAlignTableSize)
						   + ((long)sizeof(uint) * (Base.kNumFullDistances - Base.kStartPosModelIndex))
						   + ((long)sizeof(BitEncoder) * (Base.kNumStates << Base.kNumPosStatesBitsMax))
						   + ((long)sizeof(BitEncoder) * Base.kNumStates)
						   + ((long)sizeof(BitEncoder) * Base.kNumStates)
						   + ((long)sizeof(BitEncoder) * Base.kNumStates)
						   + ((long)sizeof(BitEncoder) * Base.kNumStates)
						   + ((long)sizeof(BitEncoder) * (Base.kNumStates << Base.kNumPosStatesBitsMax))
						   + ((long)sizeof(BitEncoder) * (Base.kNumFullDistances - Base.kEndPosModelIndex))
						   + ((long)sizeof(BitEncoder) * (1 << Base.kNumAlignBits))
						   + ((long)sizeof(BitEncoder) * (Base.kNumLenToPosStates * (1 << Base.kNumPosSlotBits)));
				

				long BinTreeBytes = BinTree.RequiredBytes(__Data->_dictionarySize, __Data->_numFastBytes, __Data->_matchFinderType);
				long LiteralEncoderBytes = LiteralEncoder.RequiredBytes(__Data->_numLiteralPosStateBits, __Data->_numLiteralContextBits);
				long LenPriceTableEncoderBytes = LenPriceTableEncoder.RequiredBytes(__Data->_posStateBits);
				bytes += BinTreeBytes;
				bytes += LiteralEncoderBytes;
				bytes += 2 * LenPriceTableEncoderBytes;

				__Data->_data = UnsafeUtility.Malloc(bytes, 64, Allocator.Persistent);
				
#if ENABLE_UNITY_COLLECTIONS_CHECKS
AtomicSafetyHandle.CheckReadAndThrow(JobData.m_Safety);
AtomicSafetyHandle.CheckWriteAndThrow(JobData.m_Safety);
#endif
				__Data->_matchFinder = new BinTree(__Data->_dictionarySize, __Data->_numFastBytes, __Data->_matchFinderType, (byte*)JobData._srcData->_ptr, JobData._srcData->_numBytes, __Data->_matchFinderData);
				__Data->_literalEncoder = new LiteralEncoder(__Data->_numLiteralPosStateBits, (byte*)__Data->_matchFinderData + BinTreeBytes);
				__Data->_lenEncoder = new LenPriceTableEncoder(__Data->_posStateBits, __Data->_numFastBytes, (byte*)__Data->_matchFinderData + BinTreeBytes + LiteralEncoderBytes);
				__Data->_repMatchLenEncoder = new LenPriceTableEncoder(__Data->_posStateBits, __Data->_numFastBytes, (byte*)__Data->_matchFinderData + BinTreeBytes + LiteralEncoderBytes + LenPriceTableEncoderBytes);
            }
        }

		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        internal struct InitMiscJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] internal EncodingData* __Data;
			internal ZipCompressed JobData;
			
			private void WriteCoderProperties(UnsafeLongByteList* outStream, long inSize)
			{
				*(long*)outStream->Ptr = inSize << 8; // topmost bytes get overwritten during encoding
				*outStream->Ptr = (byte)((__Data->_posStateBits * 5 + __Data->_numLiteralPosStateBits) * 9 + __Data->_numLiteralContextBits);
				
				outStream->Length += 7;
			}
			
            public void Execute()
            {
				for (int i = 0; i < kNumOpts; i++)
				{
					__Data->_optimum[i] = new Optimal();
					__Data->_optimumPrice[i] = 0;
				}

				WriteCoderProperties(JobData._data, JobData._srcData->_numBytes);

				__Data->_rangeEncoder.SetStream(JobData._data);
				__Data->_state.Init();
				__Data->_previousByte = 0;
				__Data->reps = 0;
				__Data->repLens = 0;
				__Data->_repDistances = 0;

				__Data->_rangeEncoder.Init();

				for (uint i = 0; i < Base.kNumStates; i++)
				{
					for (uint j = 0; j <= __Data->_posStateMask; j++)
					{
						uint complexState = (i << Base.kNumPosStatesBitsMax) + j;
						__Data->_isMatch[complexState].Init();
						__Data->_isRep0Long[complexState].Init();
					}
					__Data->_isRep[i].Init();
					__Data->_isRepG0[i].Init();
					__Data->_isRepG1[i].Init();
					__Data->_isRepG2[i].Init();
				}
				__Data->_literalEncoder.Init(__Data->_numLiteralPosStateBits, __Data->_numLiteralContextBits);
				for (uint i = 0; i < Base.kNumLenToPosStates; i++)
					BitTreeEncoder.Init(__Data->_posSlotEncoder + (i << Base.kNumPosSlotBits), Base.kNumPosSlotBits);
				for (uint i = 0; i < Base.kNumFullDistances - Base.kEndPosModelIndex; i++)
					__Data->_posEncoders[i].Init();

				__Data->_lenEncoder.Init();
				__Data->_repMatchLenEncoder.Init();

				BitTreeEncoder.Init(__Data->_posAlignEncoder, Base.kNumAlignBits);

				__Data->_longestMatchWasFound = false;
				__Data->_optimumEndIndex = 0;
				__Data->_optimumCurrentIndex = 0;
				__Data->_additionalOffset = 0;

				FillDistancesPrices(__Data);
				FillAlignPrices(__Data);
				
				__Data->nowPos64 = 0;
            }
        }
		
		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        internal struct InitMatchFinderJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] internal EncodingData* __Data;
			
            public void Execute()
            {
				__Data->_matchFinder.Init();
            }
        }
		
		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        internal struct InitMatchFinderHashJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] internal EncodingData* __Data;
			internal int slice;
			internal int workers;

            public void Execute()
            {
				__Data->_matchFinder.InitHashParallel(slice, workers);
            }
        }

		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        internal struct EncodeJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] internal EncodingData* __Data;

			
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void ReadMatchDistances(out uint lenRes, out uint numDistancePairs)
			{
				lenRes = 0;
				numDistancePairs = __Data->_matchFinder.GetMatches(__Data->_matchDistances);
				if (numDistancePairs > 0)
				{
					lenRes = __Data->_matchDistances[numDistancePairs - 2];
					if (lenRes == __Data->_numFastBytes)
					{
						lenRes += __Data->_matchFinder.GetMatchLen((int)lenRes - 1, __Data->_matchDistances[numDistancePairs - 1], Base.kMatchMaxLen - lenRes);
					}
				}
				__Data->_additionalOffset++;
			}

			
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void MovePos(uint num)
			{
				if (num > 0)
				{
					__Data->_matchFinder.Skip(num);
					__Data->_additionalOffset += num;
				}
			}
			
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private uint GetRepLen1Price(Base.State state, uint posState)
			{
				return __Data->_isRepG0[state.Index].GetPrice0() + __Data->_isRep0Long[((uint)state.Index << Base.kNumPosStatesBitsMax) + posState].GetPrice0();
			}
			
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private uint GetPureRepPrice(uint repIndex, Base.State state, uint posState)
			{
				uint price;
				if (repIndex == 0)
				{
					price = __Data->_isRepG0[state.Index].GetPrice0();
					price += __Data->_isRep0Long[((uint)state.Index << Base.kNumPosStatesBitsMax) + posState].GetPrice1();
				}
				else
				{
					price = __Data->_isRepG0[state.Index].GetPrice1();
					if (repIndex == 1)
					{
						price += __Data->_isRepG1[state.Index].GetPrice0();
					}
					else
					{
						price += __Data->_isRepG1[state.Index].GetPrice1();
						price += __Data->_isRepG2[state.Index].GetPrice(tobool(repIndex - 2));
					}
				}
				return price;
			}
			
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private uint GetRepPrice(uint repIndex, uint len, Base.State state, uint posState)
			{
				uint price = __Data->_repMatchLenEncoder.GetPrice(len - Base.kMatchMinLen, posState);
				return price + GetPureRepPrice(repIndex, state, posState);
			}
	
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private uint GetPosLenPrice(uint pos, uint len, uint posState)
			{
				uint price;
				uint lenToPosState = Base.GetLenToPosState(len);
				if (pos < Base.kNumFullDistances)
				{
					price = __Data->_distancesPrices[(lenToPosState * Base.kNumFullDistances) + pos];
				}
				else
				{
					price = __Data->_posSlotPrices[(lenToPosState << Base.kNumPosSlotBits) + GetPosSlot2(pos)] + __Data->_alignPrices[pos & Base.kAlignMask];
				}
				return price + __Data->_lenEncoder.GetPrice(len - Base.kMatchMinLen, posState);
			}
			
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private uint Backward(out uint backRes, uint cur)
			{
				__Data->_optimumEndIndex = (ushort)cur;
				uint posMem = __Data->_optimum[cur].PosPrev;
				uint backMem = __Data->_optimum[cur].BackPrev;
				do
				{
					if (__Data->_optimum[cur].Prev1IsChar)
					{
						__Data->_optimum[posMem].MakeAsChar();
						__Data->_optimum[posMem].PosPrev = posMem - 1;
						if (__Data->_optimum[cur].Prev2)
						{
							__Data->_optimum[posMem - 1].Prev1IsChar = false;
							__Data->_optimum[posMem - 1].PosPrev = __Data->_optimum[cur].PosPrev2;
							__Data->_optimum[posMem - 1].BackPrev = __Data->_optimum[cur].BackPrev2;
						}
					}
					uint posPrev = posMem;
					uint backCur = backMem;

					backMem = __Data->_optimum[posPrev].BackPrev;
					posMem = __Data->_optimum[posPrev].PosPrev;

					__Data->_optimum[posPrev].BackPrev = backCur;
					__Data->_optimum[posPrev].PosPrev = cur;
					cur = posPrev;
				}
				while (cur > 0);
				backRes = __Data->_optimum[0].BackPrev;
				__Data->_optimumCurrentIndex = (ushort)__Data->_optimum[0].PosPrev;
				return __Data->_optimumCurrentIndex;
			}
			
			
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void MemSetInfinityPrice_INLINE(uint start, long count, bool unrolled = false)
			{
				uint* ptr = __Data->_optimumPrice + start;

				if (unrolled)
				{
					while (count >= 32)
					{
						*((uint8*)ptr + 0) = kIfinityPrice;
						*((uint8*)ptr + 1) = kIfinityPrice;
						*((uint8*)ptr + 2) = kIfinityPrice;
						*((uint8*)ptr + 3) = kIfinityPrice;

						count -= 32;
						ptr += 32;
					}
					
					if (count >= 16)
					{
						*((uint8*)ptr + 0) = kIfinityPrice;
						*((uint8*)ptr + 1) = kIfinityPrice;

						count -= 16;
						ptr += 16;
					}
					
					if (count >= 8)
					{
						*((uint8*)ptr + 0) = kIfinityPrice;

						count -= 8;
						ptr += 8;
					}
				}
				else
				{
					while (count >= 8)
					{
						*((uint8*)ptr + 0) = kIfinityPrice;

						count -= 8;
						ptr += 8;
					}
				}
				
				if (count >= 4)
				{
					*((uint4*)ptr + 0) = kIfinityPrice;

					count -= 4;
					ptr += 4;
				}
				
				if (count >= 2)
				{
					*((uint2*)ptr + 0) = kIfinityPrice;

					count -= 2;
					ptr += 2;
				}
				
				if (count != 0)
				{
					*ptr = kIfinityPrice;
				}
			}
			
			[MethodImpl(MethodImplOptions.NoInlining)]
			private void MemSetInfinityPrice_NO_INLINE(uint start, long count)
			{
				MemSetInfinityPrice_INLINE(start, count, false);
			}
			
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void MemSetInfinityPrice(uint start, long count, bool unrolled = false)
			{
				if (COMPILATION_OPTIONS.OPTIMIZE_FOR == OptimizeFor.Performance)
				{
					MemSetInfinityPrice_INLINE(start, count, unrolled);
				}
				else
				{
					MemSetInfinityPrice_NO_INLINE(start, count);
				}
			}
			
			[SkipLocalsInit]
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private uint GetOptimum(uint position, out uint backRes)
			{
				if (__Data->_optimumEndIndex != __Data->_optimumCurrentIndex)
				{
					uint lenRes = __Data->_optimum[__Data->_optimumCurrentIndex].PosPrev - __Data->_optimumCurrentIndex;
					backRes = __Data->_optimum[__Data->_optimumCurrentIndex].BackPrev;
					__Data->_optimumCurrentIndex = (ushort)__Data->_optimum[__Data->_optimumCurrentIndex].PosPrev;
					return lenRes;
				}
				__Data->_optimumCurrentIndex = 0;
				__Data->_optimumEndIndex = 0;

				uint lenMain, numDistancePairs;
				if (Hint.Unlikely(__Data->_longestMatchWasFound))
				{
					lenMain = __Data->_longestMatchLength;
					numDistancePairs = __Data->_numDistancePairs;
					__Data->_longestMatchWasFound = false;
				}
				else
				{
					ReadMatchDistances(out lenMain, out numDistancePairs);
				}

				uint numAvailableBytes = __Data->_matchFinder.NumAvailableBytes + 1;
				if (Hint.Unlikely(numAvailableBytes < 2))
				{
					backRes = 0xFFFF_FFFF;
					return 1;
				}
				numAvailableBytes = min(numAvailableBytes, Base.kMatchMaxLen);

				uint repMaxIndex = (uint)indexof(__Data->repLens, cmax(__Data->repLens));
				__Data->reps = __Data->_repDistances;
				__Data->repLens = __Data->_matchFinder.GetMatchLen4(uint.MaxValue, __Data->reps, Base.kMatchMaxLen);

				if (Hint.Unlikely(__Data->repLens[(int)repMaxIndex] >= __Data->_numFastBytes))
				{
					backRes = repMaxIndex;
					uint lenRes = __Data->repLens[(int)repMaxIndex];
					MovePos(lenRes - 1);
					return lenRes;
				}

				if (Hint.Unlikely(lenMain >= __Data->_numFastBytes))
				{
					backRes = __Data->_matchDistances[numDistancePairs - 1] + Base.kNumRepDistances;
					MovePos(lenMain - 1);
					return lenMain;
				}
				
				Byte currentByte = __Data->_matchFinder.GetIndexByte(0 - 1);
				Byte matchByte = __Data->_matchFinder.GetIndexByte((int)(0 - __Data->_repDistances[0] - 1 - 1));

				if (lenMain < 2 & currentByte != matchByte && __Data->repLens[(int)repMaxIndex] < 2)
				{
					backRes = 0xFFFF_FFFFu;
					return 1;
				}

				__Data->_optimum[0].State = __Data->_state;

				uint posState = (position & __Data->_posStateMask);
				
				__Data->_optimumPrice[1] = __Data->_isMatch[((uint)__Data->_state.Index << Base.kNumPosStatesBitsMax) + posState].GetPrice0() +
							__Data->_literalEncoder.GetPrice(__Data->_literalEncoder.GetSubCoder(__Data->_numLiteralContextBits, position, __Data->_previousByte), !__Data->_state.IsCharState, matchByte, currentByte);
				__Data->_optimum[1].MakeAsChar();

				uint matchPrice = __Data->_isMatch[((uint)__Data->_state.Index << Base.kNumPosStatesBitsMax) + posState].GetPrice1();
				uint repMatchPrice = matchPrice + __Data->_isRep[__Data->_state.Index].GetPrice1();

				if (Hint.Unlikely(matchByte == currentByte))
				{
					uint shortRepPrice = repMatchPrice + GetRepLen1Price(__Data->_state, posState);
					if (Hint.Likely(shortRepPrice < __Data->_optimumPrice[1]))
					{
						__Data->_optimumPrice[1] = shortRepPrice;
						__Data->_optimum[1].MakeAsShortRep();
					}
				}

				uint lenEnd = ((lenMain >= __Data->repLens[(int)repMaxIndex]) ? lenMain : __Data->repLens[(int)repMaxIndex]);

				if (Hint.Unlikely(lenEnd < 2))
				{
					backRes = __Data->_optimum[1].BackPrev;
					return 1;
				}
				
				__Data->_optimum[1].PosPrev = 0;
				__Data->_optimum[0].Backs = __Data->reps;

				MemSetInfinityPrice(2, lenEnd - 1L, unrolled: true);

				for (uint i = (uint)first(__Data->repLens >= 2); i < Base.kNumRepDistances; i++)
				{
					uint repLen = __Data->repLens[(int)i];
					if (repLen < 2)
					{
						continue;
					}
					uint price = repMatchPrice + GetPureRepPrice(i, __Data->_state, posState);
					do
					{
						uint curAndLenPrice = price + __Data->_repMatchLenEncoder.GetPrice(repLen - 2, posState);
						if (curAndLenPrice < __Data->_optimumPrice[repLen])
						{
							__Data->_optimumPrice[repLen] = curAndLenPrice;
							__Data->_optimum[repLen].PosPrev = 0;
							__Data->_optimum[repLen].BackPrev = i;
							__Data->_optimum[repLen].Prev1IsChar = false;
						}
					}
					while (--repLen >= 2);
				}

				uint normalMatchPrice = matchPrice + __Data->_isRep[__Data->_state.Index].GetPrice0();
				
				uint len = ((__Data->repLens[0] >= 2) ? __Data->repLens[0] + 1 : 2);
				if (Hint.Likely(len <= lenMain))
				{
					uint offs = 0;
					while (len > __Data->_matchDistances[offs])
					{
						offs += 2;
					}
					for (; ; len++)
					{
						uint distance = __Data->_matchDistances[offs + 1];
						uint curAndLenPrice = normalMatchPrice + GetPosLenPrice(distance, len, posState);
						if (Hint.Likely(curAndLenPrice < __Data->_optimumPrice[len]))
						{
							__Data->_optimumPrice[len] = curAndLenPrice;
							__Data->_optimum[len].PosPrev = 0;
							__Data->_optimum[len].BackPrev = distance + Base.kNumRepDistances;
							__Data->_optimum[len].Prev1IsChar = false;
						}
						if (Hint.Likely(len == __Data->_matchDistances[offs]))
						{
							offs += 2;
							if (offs == numDistancePairs)
							{
								break;
							}
						}
					}
				}

				uint cur = 0;

				while (true)
				{
					cur++;
					if (Hint.Unlikely(cur == lenEnd))
					{
						return Backward(out backRes, cur);
					}
					ReadMatchDistances(out uint newLen, out numDistancePairs);
					if (Hint.Unlikely(newLen >= __Data->_numFastBytes))
					{
						__Data->_numDistancePairs = numDistancePairs;
						__Data->_longestMatchLength = newLen;
						__Data->_longestMatchWasFound = true;
						return Backward(out backRes, cur);
					}
					position++;
					uint posPrev = __Data->_optimum[cur].PosPrev;
					Base.State state;
					if (Hint.Unlikely(__Data->_optimum[cur].Prev1IsChar))
					{
						posPrev--;
						if (Hint.Likely(__Data->_optimum[cur].Prev2))
						{
							state = __Data->_optimum[__Data->_optimum[cur].PosPrev2].State;
							byte sub = (byte)(state.IsCharState ? 3 : 6);
							state.UpdateMatch();
							state.Index -= sub;
							state.Index += tobyte(__Data->_optimum[cur].BackPrev2 < Base.kNumRepDistances);
						}
						else
						{
							state = __Data->_optimum[posPrev].State;
							state.UpdateChar();
						}
					}
					else
					{
						state = __Data->_optimum[posPrev].State;
					}

					if (posPrev == cur - 1)
					{
						if (__Data->_optimum[cur].IsShortRep)
						{
							state.UpdateShortRep();
						}
						else
						{
							state.UpdateChar();
						}
					}
					else
					{
						bool b = __Data->_optimum[cur].Prev1IsChar_AND_Prev2;
						posPrev = b ? __Data->_optimum[cur].PosPrev2 : posPrev;
						uint pos = b ? __Data->_optimum[cur].BackPrev2 : __Data->_optimum[cur].BackPrev;
						state.UpdateMatch();
						state.Index += tobyte(b | andnot(pos < Base.kNumRepDistances, b));

						uint4* reps = stackalloc uint4[5]
						{
							__Data->_optimum[posPrev].Backs,
							__Data->_optimum[posPrev].Backs.yxzw,
							__Data->_optimum[posPrev].Backs.zxyw,
							__Data->_optimum[posPrev].Backs.wxyz,
							__Data->_optimum[posPrev].Backs.xxyz
						};
						*((uint*)reps + 16) = pos - Base.kNumRepDistances;
						__Data->reps = reps[min(pos, Base.kNumRepDistances)];
					}
					__Data->_optimum[cur].State = state;
					__Data->_optimum[cur].Backs = __Data->reps;
					uint curPrice = __Data->_optimumPrice[cur];

					currentByte = __Data->_matchFinder.GetIndexByte(0 - 1);
					matchByte = __Data->_matchFinder.GetIndexByte((int)(0 - __Data->reps[0] - 1 - 1));

					posState = (position & __Data->_posStateMask);

					uint curAnd1Price = curPrice 
								      + __Data->_isMatch[((uint)state.Index << Base.kNumPosStatesBitsMax) + posState].GetPrice0() 
									  + __Data->_literalEncoder.GetPrice(__Data->_literalEncoder.GetSubCoder(__Data->_numLiteralContextBits, position, __Data->_matchFinder.GetIndexByte(0 - 2)), !state.IsCharState, matchByte, currentByte);

					bool nextIsChar;
					if (Hint.Unlikely(nextIsChar = curAnd1Price < __Data->_optimumPrice[cur + 1]))
					{
						__Data->_optimumPrice[cur + 1] = curAnd1Price;
						__Data->_optimum[cur + 1].PosPrev = cur;
						__Data->_optimum[cur + 1].MakeAsChar();
					}

					matchPrice = curPrice + __Data->_isMatch[((uint)state.Index << Base.kNumPosStatesBitsMax) + posState].GetPrice1();
					repMatchPrice = matchPrice + __Data->_isRep[state.Index].GetPrice1();

					if (Hint.Unlikely(matchByte == currentByte 
								   && !(__Data->_optimum[cur + 1].PosPrev < cur & __Data->_optimum[cur + 1].BackPrev == 0)))
					{
						uint shortRepPrice = repMatchPrice + GetRepLen1Price(state, posState);
						if (Hint.Unlikely(shortRepPrice <= __Data->_optimumPrice[cur + 1]))
						{
							__Data->_optimumPrice[cur + 1] = shortRepPrice;
							__Data->_optimum[cur + 1].PosPrev = cur;
							__Data->_optimum[cur + 1].MakeAsShortRep();
							nextIsChar = true;
						}
					}

					uint numAvailableBytesFull = __Data->_matchFinder.NumAvailableBytes + 1;
					numAvailableBytesFull = min(kNumOpts - 1 - cur, numAvailableBytesFull);
					numAvailableBytes = numAvailableBytesFull;

					if (numAvailableBytes < 2)
					{
						continue;
					}
					numAvailableBytes = min(numAvailableBytes, __Data->_numFastBytes);
					if (Hint.Likely(!nextIsChar & matchByte != currentByte))
					{
						uint t = min(numAvailableBytesFull - 1, __Data->_numFastBytes);
						uint lenTest2 = __Data->_matchFinder.GetMatchLen(0, __Data->reps[0], t);
						if (Hint.Unlikely(lenTest2 >= 2))
						{
							Base.State state2 = state;
							state2.UpdateChar();
							uint posStateNext = (position + 1) & __Data->_posStateMask;
							uint nextRepMatchPrice = curAnd1Price 
												   + __Data->_isMatch[((uint)state2.Index << Base.kNumPosStatesBitsMax) + posStateNext].GetPrice1() 
												   + __Data->_isRep[state2.Index].GetPrice1();
							
							uint offset = cur + 1 + lenTest2;
							if (lenEnd < offset)
							{
								MemSetInfinityPrice(lenEnd + 1, offset - lenEnd);
								lenEnd += offset - lenEnd;
							}
							uint curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
							if (curAndLenPrice < __Data->_optimumPrice[offset])
							{
								__Data->_optimumPrice[offset] = curAndLenPrice;
								__Data->_optimum[offset].PosPrev = cur + 1;
								__Data->_optimum[offset].BackPrev = 0;
								__Data->_optimum[offset].Prev1IsChar = true;
								__Data->_optimum[offset].Prev2 = false;
							}
						}
					}

					uint startLen = 2;

					for (uint repIndex = 0; repIndex < Base.kNumRepDistances; repIndex++)
					{
						uint lenTest = __Data->_matchFinder.GetMatchLen(0 - 1, __Data->reps[(int)repIndex], numAvailableBytes);
						if (lenTest < 2)
						{
							continue;
						}
						uint lenTestTemp = lenTest;
						do
						{
							if (lenEnd < cur + lenTest)
							{
								MemSetInfinityPrice(lenEnd + 1, (cur + lenTest) - lenEnd);
								lenEnd += (cur + lenTest) - lenEnd;
							}
							uint curAndLenPrice = repMatchPrice + GetRepPrice(repIndex, lenTest, state, posState);
							if (curAndLenPrice < __Data->_optimumPrice[cur + lenTest])
							{
								__Data->_optimumPrice[cur + lenTest] = curAndLenPrice;
								__Data->_optimum[cur + lenTest].PosPrev = cur;
								__Data->_optimum[cur + lenTest].BackPrev = repIndex;
								__Data->_optimum[cur + lenTest].Prev1IsChar = false;
							}
						}
						while(--lenTest >= 2);
						lenTest = lenTestTemp;

						startLen = repIndex == 0 ? lenTest + 1 : startLen;

						if (Hint.Likely(lenTest < numAvailableBytesFull))
						{
							uint t = min(numAvailableBytesFull - 1 - lenTest, __Data->_numFastBytes);
							uint lenTest2 = __Data->_matchFinder.GetMatchLen((int)lenTest, __Data->reps[(int)repIndex], t);
							if (Hint.Unlikely(lenTest2 >= 2))
							{
								Base.State state2 = state;
								state2.UpdateRep();
								uint posStateNext = (position + lenTest) & __Data->_posStateMask;
								uint curAndLenCharPrice = repMatchPrice 
														+ GetRepPrice(repIndex, lenTest, state, posState) 
														+ __Data->_isMatch[((uint)state2.Index << Base.kNumPosStatesBitsMax) + posStateNext].GetPrice0() 
														+ __Data->_literalEncoder.GetPrice(__Data->_literalEncoder.GetSubCoder(__Data->_numLiteralContextBits, position + lenTest, __Data->_matchFinder.GetIndexByte((int)lenTest - 1 - 1)), true, __Data->_matchFinder.GetIndexByte((int)((int)lenTest - 1 - (int)(__Data->reps[(int)repIndex] + 1))), __Data->_matchFinder.GetIndexByte((int)lenTest - 1));
								state2.UpdateChar();
								posStateNext = (position + lenTest + 1) & __Data->_posStateMask;
								uint nextMatchPrice = curAndLenCharPrice + __Data->_isMatch[((uint)state2.Index << Base.kNumPosStatesBitsMax) + posStateNext].GetPrice1();
								uint nextRepMatchPrice = nextMatchPrice + __Data->_isRep[state2.Index].GetPrice1();
								
								uint offset = lenTest + 1 + lenTest2;
								if (lenEnd < cur + offset)
								{
									MemSetInfinityPrice(lenEnd + 1, (cur + offset) - lenEnd);
									lenEnd += (cur + offset) - lenEnd;
								}
								uint curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
								if (Hint.Likely(curAndLenPrice < __Data->_optimumPrice[cur + offset]))
								{
									__Data->_optimumPrice[cur + offset] = curAndLenPrice;
									__Data->_optimum[cur + offset].PosPrev = cur + lenTest + 1;
									__Data->_optimum[cur + offset].BackPrev = 0;
									__Data->_optimum[cur + offset].Prev1IsChar = true;
									__Data->_optimum[cur + offset].Prev2 = true;
									__Data->_optimum[cur + offset].PosPrev2 = cur;
									__Data->_optimum[cur + offset].BackPrev2 = repIndex;
								}
							}
						}
					}

					if (Hint.Unlikely(newLen > numAvailableBytes))
					{
						newLen = numAvailableBytes;
						for (numDistancePairs = 0; newLen > __Data->_matchDistances[numDistancePairs]; numDistancePairs += 2) 
						{
							;
						}
						__Data->_matchDistances[numDistancePairs] = newLen;
						numDistancePairs += 2;
					}
					if (Hint.Likely(newLen >= startLen))
					{
						normalMatchPrice = matchPrice + __Data->_isRep[state.Index].GetPrice0();
						if (lenEnd < cur + newLen)
						{
							MemSetInfinityPrice(lenEnd + 1, (cur + newLen) - lenEnd);
							lenEnd += (cur + newLen) - lenEnd;
						}
						uint offs = 0;
						while (startLen > __Data->_matchDistances[offs])
						{
							offs += 2;
						}

						for (uint lenTest = startLen; ; lenTest++)
						{
							uint curBack = __Data->_matchDistances[offs + 1];
							uint curAndLenPrice = normalMatchPrice + GetPosLenPrice(curBack, lenTest, posState);
							if (Hint.Likely(curAndLenPrice < __Data->_optimumPrice[cur + lenTest]))
							{
								__Data->_optimumPrice[cur + lenTest] = curAndLenPrice;
								__Data->_optimum[cur + lenTest].PosPrev = cur;
								__Data->_optimum[cur + lenTest].BackPrev = curBack + Base.kNumRepDistances;
								__Data->_optimum[cur + lenTest].Prev1IsChar = false;
							}

							if (Hint.Likely(lenTest == __Data->_matchDistances[offs]))
							{
								if (Hint.Likely(lenTest < numAvailableBytesFull))
								{
									uint t = min(numAvailableBytesFull - 1 - lenTest, __Data->_numFastBytes);
									uint lenTest2 = __Data->_matchFinder.GetMatchLen((int)lenTest, curBack, t);
									if (Hint.Unlikely(lenTest2 >= 2))
									{
										Base.State state2 = state;
										state2.UpdateMatch();
										uint posStateNext = (position + lenTest) & __Data->_posStateMask;
										uint curAndLenCharPrice = curAndLenPrice +
												__Data->_isMatch[((uint)state2.Index << Base.kNumPosStatesBitsMax) + posStateNext].GetPrice0() +
												__Data->_literalEncoder.GetPrice(__Data->_literalEncoder.GetSubCoder(__Data->_numLiteralContextBits, position + lenTest,
												__Data->_matchFinder.GetIndexByte((int)lenTest - 1 - 1)), 
												true,
												__Data->_matchFinder.GetIndexByte((int)lenTest - (int)(curBack + 1) - 1),
												__Data->_matchFinder.GetIndexByte((int)lenTest - 1));
										state2.UpdateChar();
										posStateNext = (position + lenTest + 1) & __Data->_posStateMask;
										uint nextMatchPrice = curAndLenCharPrice + __Data->_isMatch[((uint)state2.Index << Base.kNumPosStatesBitsMax) + posStateNext].GetPrice1();
										uint nextRepMatchPrice = nextMatchPrice + __Data->_isRep[state2.Index].GetPrice1();

										uint offset = lenTest + 1 + lenTest2;
										if (lenEnd < cur + offset)
										{
											MemSetInfinityPrice(lenEnd + 1, (cur + offset) - lenEnd);
											lenEnd += (cur + offset) - lenEnd;
										}
										curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
										if (curAndLenPrice < __Data->_optimumPrice[cur + offset])
										{
											__Data->_optimumPrice[cur + offset] = curAndLenPrice;
											__Data->_optimum[cur + offset].PosPrev = cur + lenTest + 1;
											__Data->_optimum[cur + offset].BackPrev = 0;
											__Data->_optimum[cur + offset].Prev1IsChar = true;
											__Data->_optimum[cur + offset].Prev2 = true;
											__Data->_optimum[cur + offset].PosPrev2 = cur;
											__Data->_optimum[cur + offset].BackPrev2 = curBack + Base.kNumRepDistances;
										}
									}
								}
								offs += 2;
								if (offs == numDistancePairs)
								{
									break;
								}
							}
						}
					}
				}
			}

			public void Execute()
			{
				if (__Data->_matchFinder.NumAvailableBytes == 0)
				{
					__Data->_rangeEncoder.FlushData();
					return;
				}
				ReadMatchDistances(out _, out _);
				
				__Data->_isMatch[0].Prob = (ushort)((BitEncoder.kBitModelTotal >> 1) + ((BitEncoder.kBitModelTotal - (BitEncoder.kBitModelTotal >> 1)) >> BitEncoder.kNumMoveBits));

				byte curByte = __Data->_matchFinder.GetIndexByte(-1);
				__Data->_literalEncoder.Encode(__Data->_literalEncoder.GetSubCoder(__Data->_numLiteralContextBits, (uint)(__Data->nowPos64), __Data->_previousByte), ref __Data->_rangeEncoder, curByte);
				__Data->_previousByte = curByte;

				__Data->_additionalOffset--;
				__Data->nowPos64++;
				
			LOOP:
				long progressPosValuePrev = __Data->nowPos64;
				if (__Data->_matchFinder.NumAvailableBytes == 0)
				{
					__Data->_rangeEncoder.FlushData();
					return;
				}
				while (true)
				{
					uint len = GetOptimum((uint)__Data->nowPos64, out uint pos);
					
					uint posState = ((uint)__Data->nowPos64) & __Data->_posStateMask;
					uint complexState = ((uint)__Data->_state.Index << Base.kNumPosStatesBitsMax) + posState;
					if (Hint.Unlikely(len == 1 & pos == 0xFFFFFFFF))
					{
						__Data->_isMatch[complexState].Encode(ref __Data->_rangeEncoder, false);
						curByte = __Data->_matchFinder.GetIndexByte((int)(0 - __Data->_additionalOffset));
						BitEncoder* subCoder = __Data->_literalEncoder.GetSubCoder(__Data->_numLiteralContextBits, (uint)__Data->nowPos64, __Data->_previousByte);
						if (Hint.Unlikely(__Data->_state.IsCharState))
						{
							__Data->_literalEncoder.Encode(subCoder, ref __Data->_rangeEncoder, curByte);
						}
						else
						{
							byte matchByte = __Data->_matchFinder.GetIndexByte((int)(0 - __Data->_repDistances[0] - 1 - __Data->_additionalOffset));
							__Data->_literalEncoder.EncodeMatched(subCoder, ref __Data->_rangeEncoder, matchByte, curByte);
						}
						__Data->_previousByte = curByte;
						__Data->_state.UpdateChar();
					}
					else
					{
						__Data->_isMatch[complexState].Encode(ref __Data->_rangeEncoder, true);
						if (Hint.Unlikely(pos < Base.kNumRepDistances)) // very likely if many consequent values are the same
						{
							__Data->_isRep[__Data->_state.Index].Encode(ref __Data->_rangeEncoder, true);
							if (Hint.Unlikely(pos == 0)) // very likely if many consequent values are the same
							{
								__Data->_isRepG0[__Data->_state.Index].Encode(ref __Data->_rangeEncoder, false);
								__Data->_isRep0Long[complexState].Encode(ref __Data->_rangeEncoder, len != 1);
							}
							else
							{
								__Data->_isRepG0[__Data->_state.Index].Encode(ref __Data->_rangeEncoder, true);
								__Data->_isRepG1[__Data->_state.Index].Encode(ref __Data->_rangeEncoder, pos != 1);
								if (Hint.Likely(pos != 1))
								{
									__Data->_isRepG2[__Data->_state.Index].Encode(ref __Data->_rangeEncoder, pos - 2 != 0);
								}
							}
							__Data->_state.Index = (byte)(__Data->_state.IsCharState ? 9u - tobyte(len != 1) : 11u);
							if (len != 1)
							{
								__Data->_repMatchLenEncoder.Encode(ref __Data->_rangeEncoder, len - Base.kMatchMinLen, posState);
							}
							uint distance = __Data->_repDistances[(int)pos];
							if (pos != 0)
							{
								for (uint i = pos; i >= 1; i--)
								{
									__Data->_repDistances[(int)i] = __Data->_repDistances[(int)i - 1];
								}
								__Data->_repDistances[0] = distance;
							}
						}
						else
						{
							__Data->_isRep[__Data->_state.Index].Encode(ref __Data->_rangeEncoder, false);
							__Data->_state.UpdateMatch();
							__Data->_lenEncoder.Encode(ref __Data->_rangeEncoder, len - Base.kMatchMinLen, posState);
							pos -= Base.kNumRepDistances;
							uint posSlot = GetPosSlot(pos);
							uint lenToPosState = Base.GetLenToPosState(len);
							BitTreeEncoder.Encode(__Data->_posSlotEncoder + (lenToPosState << Base.kNumPosSlotBits), Base.kNumPosSlotBits, ref __Data->_rangeEncoder, posSlot);

							if (Hint.Likely(posSlot >= Base.kStartPosModelIndex))
							{
								int footerBits = (int)((posSlot >> 1) - 1);
								uint baseVal = ((2 | (posSlot & 1)) << footerBits);
								uint posReduced = pos - baseVal;

								if (Hint.Unlikely(posSlot < Base.kEndPosModelIndex))
								{
									BitTreeEncoder.ReverseEncode(__Data->_posEncoders, footerBits, baseVal - posSlot - 1, ref __Data->_rangeEncoder, posReduced);
								}
								else
								{
									__Data->_rangeEncoder.EncodeDirectBits(posReduced >> Base.kNumAlignBits, footerBits - Base.kNumAlignBits);
									BitTreeEncoder.ReverseEncode(__Data->_posAlignEncoder, Base.kNumAlignBits, 0, ref __Data->_rangeEncoder, posReduced & Base.kAlignMask);
									__Data->_alignPriceCount++;
								}
							}
							uint distance = pos;
							__Data->_repDistances = __Data->_repDistances.xxyz;
							__Data->_repDistances.x = distance;
							__Data->_matchPriceCount++;
						}
						__Data->_previousByte = __Data->_matchFinder.GetIndexByte((int)(len - 1 - __Data->_additionalOffset));
					}
					__Data->_additionalOffset -= len;
					__Data->nowPos64 += len;
					if (Hint.Unlikely(__Data->_additionalOffset == 0))
					{
						if (Hint.Unlikely(__Data->_matchPriceCount >= (1 << 7)))
						{
							FillDistancesPrices(__Data);
						}
						if (Hint.Unlikely(__Data->_alignPriceCount >= Base.kAlignTableSize))
						{
							FillAlignPrices(__Data);
						}
						if (Hint.Unlikely(__Data->_matchFinder.NumAvailableBytes == 0))
						{
							__Data->_rangeEncoder.FlushData();
							break;
						}

						if (Hint.Unlikely(__Data->nowPos64 - progressPosValuePrev >= (1 << 12)))
						{
							goto LOOP;
						}
					}
				}
			}
		}
		
		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        internal struct FreeJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] internal EncodingData* __Data;
			
			public void Execute()
			{
				UnsafeUtility.Free(__Data->_data, Allocator.Persistent);
				UnsafeUtility.Free(__Data, Allocator.Persistent);
			}
		}


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static byte FastPos([AssumeRange(0L, 2047L)] int idx)
		{
			switch (idx)
			{
				case 0: return 0;
				case 1: return 1;
				case 2: return 2;
				case 3: return 3;
				case 4: return 4;
				case 5: return 4;
				case 6: return 5;
				case 7: return 5;
				case 8: return 6;
				case 9: return 6;
				case 10: return 6;
				case 11: return 6;
				case 12: return 7;
				case 13: return 7;
				case 14: return 7;
				case 15: return 7;
				case 16: return 8;
				case 17: return 8;
				case 18: return 8;
				case 19: return 8;
				case 20: return 8;
				case 21: return 8;
				case 22: return 8;
				case 23: return 8;
				case 24: return 9;
				case 25: return 9;
				case 26: return 9;
				case 27: return 9;
				case 28: return 9;
				case 29: return 9;
				case 30: return 9;
				case 31: return 9;
				case 32: return 10;
				case 33: return 10;
				case 34: return 10;
				case 35: return 10;
				case 36: return 10;
				case 37: return 10;
				case 38: return 10;
				case 39: return 10;
				case 40: return 10;
				case 41: return 10;
				case 42: return 10;
				case 43: return 10;
				case 44: return 10;
				case 45: return 10;
				case 46: return 10;
				case 47: return 10;
				case 48: return 11;
				case 49: return 11;
				case 50: return 11;
				case 51: return 11;
				case 52: return 11;
				case 53: return 11;
				case 54: return 11;
				case 55: return 11;
				case 56: return 11;
				case 57: return 11;
				case 58: return 11;
				case 59: return 11;
				case 60: return 11;
				case 61: return 11;
				case 62: return 11;
				case 63: return 11;
				case 64: return 12;
				case 65: return 12;
				case 66: return 12;
				case 67: return 12;
				case 68: return 12;
				case 69: return 12;
				case 70: return 12;
				case 71: return 12;
				case 72: return 12;
				case 73: return 12;
				case 74: return 12;
				case 75: return 12;
				case 76: return 12;
				case 77: return 12;
				case 78: return 12;
				case 79: return 12;
				case 80: return 12;
				case 81: return 12;
				case 82: return 12;
				case 83: return 12;
				case 84: return 12;
				case 85: return 12;
				case 86: return 12;
				case 87: return 12;
				case 88: return 12;
				case 89: return 12;
				case 90: return 12;
				case 91: return 12;
				case 92: return 12;
				case 93: return 12;
				case 94: return 12;
				case 95: return 12;
				case 96: return 13;
				case 97: return 13;
				case 98: return 13;
				case 99: return 13;
				case 100: return 13;
				case 101: return 13;
				case 102: return 13;
				case 103: return 13;
				case 104: return 13;
				case 105: return 13;
				case 106: return 13;
				case 107: return 13;
				case 108: return 13;
				case 109: return 13;
				case 110: return 13;
				case 111: return 13;
				case 112: return 13;
				case 113: return 13;
				case 114: return 13;
				case 115: return 13;
				case 116: return 13;
				case 117: return 13;
				case 118: return 13;
				case 119: return 13;
				case 120: return 13;
				case 121: return 13;
				case 122: return 13;
				case 123: return 13;
				case 124: return 13;
				case 125: return 13;
				case 126: return 13;
				case 127: return 13;
				case 128: return 14;
				case 129: return 14;
				case 130: return 14;
				case 131: return 14;
				case 132: return 14;
				case 133: return 14;
				case 134: return 14;
				case 135: return 14;
				case 136: return 14;
				case 137: return 14;
				case 138: return 14;
				case 139: return 14;
				case 140: return 14;
				case 141: return 14;
				case 142: return 14;
				case 143: return 14;
				case 144: return 14;
				case 145: return 14;
				case 146: return 14;
				case 147: return 14;
				case 148: return 14;
				case 149: return 14;
				case 150: return 14;
				case 151: return 14;
				case 152: return 14;
				case 153: return 14;
				case 154: return 14;
				case 155: return 14;
				case 156: return 14;
				case 157: return 14;
				case 158: return 14;
				case 159: return 14;
				case 160: return 14;
				case 161: return 14;
				case 162: return 14;
				case 163: return 14;
				case 164: return 14;
				case 165: return 14;
				case 166: return 14;
				case 167: return 14;
				case 168: return 14;
				case 169: return 14;
				case 170: return 14;
				case 171: return 14;
				case 172: return 14;
				case 173: return 14;
				case 174: return 14;
				case 175: return 14;
				case 176: return 14;
				case 177: return 14;
				case 178: return 14;
				case 179: return 14;
				case 180: return 14;
				case 181: return 14;
				case 182: return 14;
				case 183: return 14;
				case 184: return 14;
				case 185: return 14;
				case 186: return 14;
				case 187: return 14;
				case 188: return 14;
				case 189: return 14;
				case 190: return 14;
				case 191: return 14;
				case 192: return 15;
				case 193: return 15;
				case 194: return 15;
				case 195: return 15;
				case 196: return 15;
				case 197: return 15;
				case 198: return 15;
				case 199: return 15;
				case 200: return 15;
				case 201: return 15;
				case 202: return 15;
				case 203: return 15;
				case 204: return 15;
				case 205: return 15;
				case 206: return 15;
				case 207: return 15;
				case 208: return 15;
				case 209: return 15;
				case 210: return 15;
				case 211: return 15;
				case 212: return 15;
				case 213: return 15;
				case 214: return 15;
				case 215: return 15;
				case 216: return 15;
				case 217: return 15;
				case 218: return 15;
				case 219: return 15;
				case 220: return 15;
				case 221: return 15;
				case 222: return 15;
				case 223: return 15;
				case 224: return 15;
				case 225: return 15;
				case 226: return 15;
				case 227: return 15;
				case 228: return 15;
				case 229: return 15;
				case 230: return 15;
				case 231: return 15;
				case 232: return 15;
				case 233: return 15;
				case 234: return 15;
				case 235: return 15;
				case 236: return 15;
				case 237: return 15;
				case 238: return 15;
				case 239: return 15;
				case 240: return 15;
				case 241: return 15;
				case 242: return 15;
				case 243: return 15;
				case 244: return 15;
				case 245: return 15;
				case 246: return 15;
				case 247: return 15;
				case 248: return 15;
				case 249: return 15;
				case 250: return 15;
				case 251: return 15;
				case 252: return 15;
				case 253: return 15;
				case 254: return 15;
				case 255: return 15;
				case 256: return 16;
				case 257: return 16;
				case 258: return 16;
				case 259: return 16;
				case 260: return 16;
				case 261: return 16;
				case 262: return 16;
				case 263: return 16;
				case 264: return 16;
				case 265: return 16;
				case 266: return 16;
				case 267: return 16;
				case 268: return 16;
				case 269: return 16;
				case 270: return 16;
				case 271: return 16;
				case 272: return 16;
				case 273: return 16;
				case 274: return 16;
				case 275: return 16;
				case 276: return 16;
				case 277: return 16;
				case 278: return 16;
				case 279: return 16;
				case 280: return 16;
				case 281: return 16;
				case 282: return 16;
				case 283: return 16;
				case 284: return 16;
				case 285: return 16;
				case 286: return 16;
				case 287: return 16;
				case 288: return 16;
				case 289: return 16;
				case 290: return 16;
				case 291: return 16;
				case 292: return 16;
				case 293: return 16;
				case 294: return 16;
				case 295: return 16;
				case 296: return 16;
				case 297: return 16;
				case 298: return 16;
				case 299: return 16;
				case 300: return 16;
				case 301: return 16;
				case 302: return 16;
				case 303: return 16;
				case 304: return 16;
				case 305: return 16;
				case 306: return 16;
				case 307: return 16;
				case 308: return 16;
				case 309: return 16;
				case 310: return 16;
				case 311: return 16;
				case 312: return 16;
				case 313: return 16;
				case 314: return 16;
				case 315: return 16;
				case 316: return 16;
				case 317: return 16;
				case 318: return 16;
				case 319: return 16;
				case 320: return 16;
				case 321: return 16;
				case 322: return 16;
				case 323: return 16;
				case 324: return 16;
				case 325: return 16;
				case 326: return 16;
				case 327: return 16;
				case 328: return 16;
				case 329: return 16;
				case 330: return 16;
				case 331: return 16;
				case 332: return 16;
				case 333: return 16;
				case 334: return 16;
				case 335: return 16;
				case 336: return 16;
				case 337: return 16;
				case 338: return 16;
				case 339: return 16;
				case 340: return 16;
				case 341: return 16;
				case 342: return 16;
				case 343: return 16;
				case 344: return 16;
				case 345: return 16;
				case 346: return 16;
				case 347: return 16;
				case 348: return 16;
				case 349: return 16;
				case 350: return 16;
				case 351: return 16;
				case 352: return 16;
				case 353: return 16;
				case 354: return 16;
				case 355: return 16;
				case 356: return 16;
				case 357: return 16;
				case 358: return 16;
				case 359: return 16;
				case 360: return 16;
				case 361: return 16;
				case 362: return 16;
				case 363: return 16;
				case 364: return 16;
				case 365: return 16;
				case 366: return 16;
				case 367: return 16;
				case 368: return 16;
				case 369: return 16;
				case 370: return 16;
				case 371: return 16;
				case 372: return 16;
				case 373: return 16;
				case 374: return 16;
				case 375: return 16;
				case 376: return 16;
				case 377: return 16;
				case 378: return 16;
				case 379: return 16;
				case 380: return 16;
				case 381: return 16;
				case 382: return 16;
				case 383: return 16;
				case 384: return 17;
				case 385: return 17;
				case 386: return 17;
				case 387: return 17;
				case 388: return 17;
				case 389: return 17;
				case 390: return 17;
				case 391: return 17;
				case 392: return 17;
				case 393: return 17;
				case 394: return 17;
				case 395: return 17;
				case 396: return 17;
				case 397: return 17;
				case 398: return 17;
				case 399: return 17;
				case 400: return 17;
				case 401: return 17;
				case 402: return 17;
				case 403: return 17;
				case 404: return 17;
				case 405: return 17;
				case 406: return 17;
				case 407: return 17;
				case 408: return 17;
				case 409: return 17;
				case 410: return 17;
				case 411: return 17;
				case 412: return 17;
				case 413: return 17;
				case 414: return 17;
				case 415: return 17;
				case 416: return 17;
				case 417: return 17;
				case 418: return 17;
				case 419: return 17;
				case 420: return 17;
				case 421: return 17;
				case 422: return 17;
				case 423: return 17;
				case 424: return 17;
				case 425: return 17;
				case 426: return 17;
				case 427: return 17;
				case 428: return 17;
				case 429: return 17;
				case 430: return 17;
				case 431: return 17;
				case 432: return 17;
				case 433: return 17;
				case 434: return 17;
				case 435: return 17;
				case 436: return 17;
				case 437: return 17;
				case 438: return 17;
				case 439: return 17;
				case 440: return 17;
				case 441: return 17;
				case 442: return 17;
				case 443: return 17;
				case 444: return 17;
				case 445: return 17;
				case 446: return 17;
				case 447: return 17;
				case 448: return 17;
				case 449: return 17;
				case 450: return 17;
				case 451: return 17;
				case 452: return 17;
				case 453: return 17;
				case 454: return 17;
				case 455: return 17;
				case 456: return 17;
				case 457: return 17;
				case 458: return 17;
				case 459: return 17;
				case 460: return 17;
				case 461: return 17;
				case 462: return 17;
				case 463: return 17;
				case 464: return 17;
				case 465: return 17;
				case 466: return 17;
				case 467: return 17;
				case 468: return 17;
				case 469: return 17;
				case 470: return 17;
				case 471: return 17;
				case 472: return 17;
				case 473: return 17;
				case 474: return 17;
				case 475: return 17;
				case 476: return 17;
				case 477: return 17;
				case 478: return 17;
				case 479: return 17;
				case 480: return 17;
				case 481: return 17;
				case 482: return 17;
				case 483: return 17;
				case 484: return 17;
				case 485: return 17;
				case 486: return 17;
				case 487: return 17;
				case 488: return 17;
				case 489: return 17;
				case 490: return 17;
				case 491: return 17;
				case 492: return 17;
				case 493: return 17;
				case 494: return 17;
				case 495: return 17;
				case 496: return 17;
				case 497: return 17;
				case 498: return 17;
				case 499: return 17;
				case 500: return 17;
				case 501: return 17;
				case 502: return 17;
				case 503: return 17;
				case 504: return 17;
				case 505: return 17;
				case 506: return 17;
				case 507: return 17;
				case 508: return 17;
				case 509: return 17;
				case 510: return 17;
				case 511: return 17;
				case 512: return 18;
				case 513: return 18;
				case 514: return 18;
				case 515: return 18;
				case 516: return 18;
				case 517: return 18;
				case 518: return 18;
				case 519: return 18;
				case 520: return 18;
				case 521: return 18;
				case 522: return 18;
				case 523: return 18;
				case 524: return 18;
				case 525: return 18;
				case 526: return 18;
				case 527: return 18;
				case 528: return 18;
				case 529: return 18;
				case 530: return 18;
				case 531: return 18;
				case 532: return 18;
				case 533: return 18;
				case 534: return 18;
				case 535: return 18;
				case 536: return 18;
				case 537: return 18;
				case 538: return 18;
				case 539: return 18;
				case 540: return 18;
				case 541: return 18;
				case 542: return 18;
				case 543: return 18;
				case 544: return 18;
				case 545: return 18;
				case 546: return 18;
				case 547: return 18;
				case 548: return 18;
				case 549: return 18;
				case 550: return 18;
				case 551: return 18;
				case 552: return 18;
				case 553: return 18;
				case 554: return 18;
				case 555: return 18;
				case 556: return 18;
				case 557: return 18;
				case 558: return 18;
				case 559: return 18;
				case 560: return 18;
				case 561: return 18;
				case 562: return 18;
				case 563: return 18;
				case 564: return 18;
				case 565: return 18;
				case 566: return 18;
				case 567: return 18;
				case 568: return 18;
				case 569: return 18;
				case 570: return 18;
				case 571: return 18;
				case 572: return 18;
				case 573: return 18;
				case 574: return 18;
				case 575: return 18;
				case 576: return 18;
				case 577: return 18;
				case 578: return 18;
				case 579: return 18;
				case 580: return 18;
				case 581: return 18;
				case 582: return 18;
				case 583: return 18;
				case 584: return 18;
				case 585: return 18;
				case 586: return 18;
				case 587: return 18;
				case 588: return 18;
				case 589: return 18;
				case 590: return 18;
				case 591: return 18;
				case 592: return 18;
				case 593: return 18;
				case 594: return 18;
				case 595: return 18;
				case 596: return 18;
				case 597: return 18;
				case 598: return 18;
				case 599: return 18;
				case 600: return 18;
				case 601: return 18;
				case 602: return 18;
				case 603: return 18;
				case 604: return 18;
				case 605: return 18;
				case 606: return 18;
				case 607: return 18;
				case 608: return 18;
				case 609: return 18;
				case 610: return 18;
				case 611: return 18;
				case 612: return 18;
				case 613: return 18;
				case 614: return 18;
				case 615: return 18;
				case 616: return 18;
				case 617: return 18;
				case 618: return 18;
				case 619: return 18;
				case 620: return 18;
				case 621: return 18;
				case 622: return 18;
				case 623: return 18;
				case 624: return 18;
				case 625: return 18;
				case 626: return 18;
				case 627: return 18;
				case 628: return 18;
				case 629: return 18;
				case 630: return 18;
				case 631: return 18;
				case 632: return 18;
				case 633: return 18;
				case 634: return 18;
				case 635: return 18;
				case 636: return 18;
				case 637: return 18;
				case 638: return 18;
				case 639: return 18;
				case 640: return 18;
				case 641: return 18;
				case 642: return 18;
				case 643: return 18;
				case 644: return 18;
				case 645: return 18;
				case 646: return 18;
				case 647: return 18;
				case 648: return 18;
				case 649: return 18;
				case 650: return 18;
				case 651: return 18;
				case 652: return 18;
				case 653: return 18;
				case 654: return 18;
				case 655: return 18;
				case 656: return 18;
				case 657: return 18;
				case 658: return 18;
				case 659: return 18;
				case 660: return 18;
				case 661: return 18;
				case 662: return 18;
				case 663: return 18;
				case 664: return 18;
				case 665: return 18;
				case 666: return 18;
				case 667: return 18;
				case 668: return 18;
				case 669: return 18;
				case 670: return 18;
				case 671: return 18;
				case 672: return 18;
				case 673: return 18;
				case 674: return 18;
				case 675: return 18;
				case 676: return 18;
				case 677: return 18;
				case 678: return 18;
				case 679: return 18;
				case 680: return 18;
				case 681: return 18;
				case 682: return 18;
				case 683: return 18;
				case 684: return 18;
				case 685: return 18;
				case 686: return 18;
				case 687: return 18;
				case 688: return 18;
				case 689: return 18;
				case 690: return 18;
				case 691: return 18;
				case 692: return 18;
				case 693: return 18;
				case 694: return 18;
				case 695: return 18;
				case 696: return 18;
				case 697: return 18;
				case 698: return 18;
				case 699: return 18;
				case 700: return 18;
				case 701: return 18;
				case 702: return 18;
				case 703: return 18;
				case 704: return 18;
				case 705: return 18;
				case 706: return 18;
				case 707: return 18;
				case 708: return 18;
				case 709: return 18;
				case 710: return 18;
				case 711: return 18;
				case 712: return 18;
				case 713: return 18;
				case 714: return 18;
				case 715: return 18;
				case 716: return 18;
				case 717: return 18;
				case 718: return 18;
				case 719: return 18;
				case 720: return 18;
				case 721: return 18;
				case 722: return 18;
				case 723: return 18;
				case 724: return 18;
				case 725: return 18;
				case 726: return 18;
				case 727: return 18;
				case 728: return 18;
				case 729: return 18;
				case 730: return 18;
				case 731: return 18;
				case 732: return 18;
				case 733: return 18;
				case 734: return 18;
				case 735: return 18;
				case 736: return 18;
				case 737: return 18;
				case 738: return 18;
				case 739: return 18;
				case 740: return 18;
				case 741: return 18;
				case 742: return 18;
				case 743: return 18;
				case 744: return 18;
				case 745: return 18;
				case 746: return 18;
				case 747: return 18;
				case 748: return 18;
				case 749: return 18;
				case 750: return 18;
				case 751: return 18;
				case 752: return 18;
				case 753: return 18;
				case 754: return 18;
				case 755: return 18;
				case 756: return 18;
				case 757: return 18;
				case 758: return 18;
				case 759: return 18;
				case 760: return 18;
				case 761: return 18;
				case 762: return 18;
				case 763: return 18;
				case 764: return 18;
				case 765: return 18;
				case 766: return 18;
				case 767: return 18;
				case 768: return 19;
				case 769: return 19;
				case 770: return 19;
				case 771: return 19;
				case 772: return 19;
				case 773: return 19;
				case 774: return 19;
				case 775: return 19;
				case 776: return 19;
				case 777: return 19;
				case 778: return 19;
				case 779: return 19;
				case 780: return 19;
				case 781: return 19;
				case 782: return 19;
				case 783: return 19;
				case 784: return 19;
				case 785: return 19;
				case 786: return 19;
				case 787: return 19;
				case 788: return 19;
				case 789: return 19;
				case 790: return 19;
				case 791: return 19;
				case 792: return 19;
				case 793: return 19;
				case 794: return 19;
				case 795: return 19;
				case 796: return 19;
				case 797: return 19;
				case 798: return 19;
				case 799: return 19;
				case 800: return 19;
				case 801: return 19;
				case 802: return 19;
				case 803: return 19;
				case 804: return 19;
				case 805: return 19;
				case 806: return 19;
				case 807: return 19;
				case 808: return 19;
				case 809: return 19;
				case 810: return 19;
				case 811: return 19;
				case 812: return 19;
				case 813: return 19;
				case 814: return 19;
				case 815: return 19;
				case 816: return 19;
				case 817: return 19;
				case 818: return 19;
				case 819: return 19;
				case 820: return 19;
				case 821: return 19;
				case 822: return 19;
				case 823: return 19;
				case 824: return 19;
				case 825: return 19;
				case 826: return 19;
				case 827: return 19;
				case 828: return 19;
				case 829: return 19;
				case 830: return 19;
				case 831: return 19;
				case 832: return 19;
				case 833: return 19;
				case 834: return 19;
				case 835: return 19;
				case 836: return 19;
				case 837: return 19;
				case 838: return 19;
				case 839: return 19;
				case 840: return 19;
				case 841: return 19;
				case 842: return 19;
				case 843: return 19;
				case 844: return 19;
				case 845: return 19;
				case 846: return 19;
				case 847: return 19;
				case 848: return 19;
				case 849: return 19;
				case 850: return 19;
				case 851: return 19;
				case 852: return 19;
				case 853: return 19;
				case 854: return 19;
				case 855: return 19;
				case 856: return 19;
				case 857: return 19;
				case 858: return 19;
				case 859: return 19;
				case 860: return 19;
				case 861: return 19;
				case 862: return 19;
				case 863: return 19;
				case 864: return 19;
				case 865: return 19;
				case 866: return 19;
				case 867: return 19;
				case 868: return 19;
				case 869: return 19;
				case 870: return 19;
				case 871: return 19;
				case 872: return 19;
				case 873: return 19;
				case 874: return 19;
				case 875: return 19;
				case 876: return 19;
				case 877: return 19;
				case 878: return 19;
				case 879: return 19;
				case 880: return 19;
				case 881: return 19;
				case 882: return 19;
				case 883: return 19;
				case 884: return 19;
				case 885: return 19;
				case 886: return 19;
				case 887: return 19;
				case 888: return 19;
				case 889: return 19;
				case 890: return 19;
				case 891: return 19;
				case 892: return 19;
				case 893: return 19;
				case 894: return 19;
				case 895: return 19;
				case 896: return 19;
				case 897: return 19;
				case 898: return 19;
				case 899: return 19;
				case 900: return 19;
				case 901: return 19;
				case 902: return 19;
				case 903: return 19;
				case 904: return 19;
				case 905: return 19;
				case 906: return 19;
				case 907: return 19;
				case 908: return 19;
				case 909: return 19;
				case 910: return 19;
				case 911: return 19;
				case 912: return 19;
				case 913: return 19;
				case 914: return 19;
				case 915: return 19;
				case 916: return 19;
				case 917: return 19;
				case 918: return 19;
				case 919: return 19;
				case 920: return 19;
				case 921: return 19;
				case 922: return 19;
				case 923: return 19;
				case 924: return 19;
				case 925: return 19;
				case 926: return 19;
				case 927: return 19;
				case 928: return 19;
				case 929: return 19;
				case 930: return 19;
				case 931: return 19;
				case 932: return 19;
				case 933: return 19;
				case 934: return 19;
				case 935: return 19;
				case 936: return 19;
				case 937: return 19;
				case 938: return 19;
				case 939: return 19;
				case 940: return 19;
				case 941: return 19;
				case 942: return 19;
				case 943: return 19;
				case 944: return 19;
				case 945: return 19;
				case 946: return 19;
				case 947: return 19;
				case 948: return 19;
				case 949: return 19;
				case 950: return 19;
				case 951: return 19;
				case 952: return 19;
				case 953: return 19;
				case 954: return 19;
				case 955: return 19;
				case 956: return 19;
				case 957: return 19;
				case 958: return 19;
				case 959: return 19;
				case 960: return 19;
				case 961: return 19;
				case 962: return 19;
				case 963: return 19;
				case 964: return 19;
				case 965: return 19;
				case 966: return 19;
				case 967: return 19;
				case 968: return 19;
				case 969: return 19;
				case 970: return 19;
				case 971: return 19;
				case 972: return 19;
				case 973: return 19;
				case 974: return 19;
				case 975: return 19;
				case 976: return 19;
				case 977: return 19;
				case 978: return 19;
				case 979: return 19;
				case 980: return 19;
				case 981: return 19;
				case 982: return 19;
				case 983: return 19;
				case 984: return 19;
				case 985: return 19;
				case 986: return 19;
				case 987: return 19;
				case 988: return 19;
				case 989: return 19;
				case 990: return 19;
				case 991: return 19;
				case 992: return 19;
				case 993: return 19;
				case 994: return 19;
				case 995: return 19;
				case 996: return 19;
				case 997: return 19;
				case 998: return 19;
				case 999: return 19;
				case 1000: return 19;
				case 1001: return 19;
				case 1002: return 19;
				case 1003: return 19;
				case 1004: return 19;
				case 1005: return 19;
				case 1006: return 19;
				case 1007: return 19;
				case 1008: return 19;
				case 1009: return 19;
				case 1010: return 19;
				case 1011: return 19;
				case 1012: return 19;
				case 1013: return 19;
				case 1014: return 19;
				case 1015: return 19;
				case 1016: return 19;
				case 1017: return 19;
				case 1018: return 19;
				case 1019: return 19;
				case 1020: return 19;
				case 1021: return 19;
				case 1022: return 19;
				case 1023: return 19;
				case 1024: return 20;
				case 1025: return 20;
				case 1026: return 20;
				case 1027: return 20;
				case 1028: return 20;
				case 1029: return 20;
				case 1030: return 20;
				case 1031: return 20;
				case 1032: return 20;
				case 1033: return 20;
				case 1034: return 20;
				case 1035: return 20;
				case 1036: return 20;
				case 1037: return 20;
				case 1038: return 20;
				case 1039: return 20;
				case 1040: return 20;
				case 1041: return 20;
				case 1042: return 20;
				case 1043: return 20;
				case 1044: return 20;
				case 1045: return 20;
				case 1046: return 20;
				case 1047: return 20;
				case 1048: return 20;
				case 1049: return 20;
				case 1050: return 20;
				case 1051: return 20;
				case 1052: return 20;
				case 1053: return 20;
				case 1054: return 20;
				case 1055: return 20;
				case 1056: return 20;
				case 1057: return 20;
				case 1058: return 20;
				case 1059: return 20;
				case 1060: return 20;
				case 1061: return 20;
				case 1062: return 20;
				case 1063: return 20;
				case 1064: return 20;
				case 1065: return 20;
				case 1066: return 20;
				case 1067: return 20;
				case 1068: return 20;
				case 1069: return 20;
				case 1070: return 20;
				case 1071: return 20;
				case 1072: return 20;
				case 1073: return 20;
				case 1074: return 20;
				case 1075: return 20;
				case 1076: return 20;
				case 1077: return 20;
				case 1078: return 20;
				case 1079: return 20;
				case 1080: return 20;
				case 1081: return 20;
				case 1082: return 20;
				case 1083: return 20;
				case 1084: return 20;
				case 1085: return 20;
				case 1086: return 20;
				case 1087: return 20;
				case 1088: return 20;
				case 1089: return 20;
				case 1090: return 20;
				case 1091: return 20;
				case 1092: return 20;
				case 1093: return 20;
				case 1094: return 20;
				case 1095: return 20;
				case 1096: return 20;
				case 1097: return 20;
				case 1098: return 20;
				case 1099: return 20;
				case 1100: return 20;
				case 1101: return 20;
				case 1102: return 20;
				case 1103: return 20;
				case 1104: return 20;
				case 1105: return 20;
				case 1106: return 20;
				case 1107: return 20;
				case 1108: return 20;
				case 1109: return 20;
				case 1110: return 20;
				case 1111: return 20;
				case 1112: return 20;
				case 1113: return 20;
				case 1114: return 20;
				case 1115: return 20;
				case 1116: return 20;
				case 1117: return 20;
				case 1118: return 20;
				case 1119: return 20;
				case 1120: return 20;
				case 1121: return 20;
				case 1122: return 20;
				case 1123: return 20;
				case 1124: return 20;
				case 1125: return 20;
				case 1126: return 20;
				case 1127: return 20;
				case 1128: return 20;
				case 1129: return 20;
				case 1130: return 20;
				case 1131: return 20;
				case 1132: return 20;
				case 1133: return 20;
				case 1134: return 20;
				case 1135: return 20;
				case 1136: return 20;
				case 1137: return 20;
				case 1138: return 20;
				case 1139: return 20;
				case 1140: return 20;
				case 1141: return 20;
				case 1142: return 20;
				case 1143: return 20;
				case 1144: return 20;
				case 1145: return 20;
				case 1146: return 20;
				case 1147: return 20;
				case 1148: return 20;
				case 1149: return 20;
				case 1150: return 20;
				case 1151: return 20;
				case 1152: return 20;
				case 1153: return 20;
				case 1154: return 20;
				case 1155: return 20;
				case 1156: return 20;
				case 1157: return 20;
				case 1158: return 20;
				case 1159: return 20;
				case 1160: return 20;
				case 1161: return 20;
				case 1162: return 20;
				case 1163: return 20;
				case 1164: return 20;
				case 1165: return 20;
				case 1166: return 20;
				case 1167: return 20;
				case 1168: return 20;
				case 1169: return 20;
				case 1170: return 20;
				case 1171: return 20;
				case 1172: return 20;
				case 1173: return 20;
				case 1174: return 20;
				case 1175: return 20;
				case 1176: return 20;
				case 1177: return 20;
				case 1178: return 20;
				case 1179: return 20;
				case 1180: return 20;
				case 1181: return 20;
				case 1182: return 20;
				case 1183: return 20;
				case 1184: return 20;
				case 1185: return 20;
				case 1186: return 20;
				case 1187: return 20;
				case 1188: return 20;
				case 1189: return 20;
				case 1190: return 20;
				case 1191: return 20;
				case 1192: return 20;
				case 1193: return 20;
				case 1194: return 20;
				case 1195: return 20;
				case 1196: return 20;
				case 1197: return 20;
				case 1198: return 20;
				case 1199: return 20;
				case 1200: return 20;
				case 1201: return 20;
				case 1202: return 20;
				case 1203: return 20;
				case 1204: return 20;
				case 1205: return 20;
				case 1206: return 20;
				case 1207: return 20;
				case 1208: return 20;
				case 1209: return 20;
				case 1210: return 20;
				case 1211: return 20;
				case 1212: return 20;
				case 1213: return 20;
				case 1214: return 20;
				case 1215: return 20;
				case 1216: return 20;
				case 1217: return 20;
				case 1218: return 20;
				case 1219: return 20;
				case 1220: return 20;
				case 1221: return 20;
				case 1222: return 20;
				case 1223: return 20;
				case 1224: return 20;
				case 1225: return 20;
				case 1226: return 20;
				case 1227: return 20;
				case 1228: return 20;
				case 1229: return 20;
				case 1230: return 20;
				case 1231: return 20;
				case 1232: return 20;
				case 1233: return 20;
				case 1234: return 20;
				case 1235: return 20;
				case 1236: return 20;
				case 1237: return 20;
				case 1238: return 20;
				case 1239: return 20;
				case 1240: return 20;
				case 1241: return 20;
				case 1242: return 20;
				case 1243: return 20;
				case 1244: return 20;
				case 1245: return 20;
				case 1246: return 20;
				case 1247: return 20;
				case 1248: return 20;
				case 1249: return 20;
				case 1250: return 20;
				case 1251: return 20;
				case 1252: return 20;
				case 1253: return 20;
				case 1254: return 20;
				case 1255: return 20;
				case 1256: return 20;
				case 1257: return 20;
				case 1258: return 20;
				case 1259: return 20;
				case 1260: return 20;
				case 1261: return 20;
				case 1262: return 20;
				case 1263: return 20;
				case 1264: return 20;
				case 1265: return 20;
				case 1266: return 20;
				case 1267: return 20;
				case 1268: return 20;
				case 1269: return 20;
				case 1270: return 20;
				case 1271: return 20;
				case 1272: return 20;
				case 1273: return 20;
				case 1274: return 20;
				case 1275: return 20;
				case 1276: return 20;
				case 1277: return 20;
				case 1278: return 20;
				case 1279: return 20;
				case 1280: return 20;
				case 1281: return 20;
				case 1282: return 20;
				case 1283: return 20;
				case 1284: return 20;
				case 1285: return 20;
				case 1286: return 20;
				case 1287: return 20;
				case 1288: return 20;
				case 1289: return 20;
				case 1290: return 20;
				case 1291: return 20;
				case 1292: return 20;
				case 1293: return 20;
				case 1294: return 20;
				case 1295: return 20;
				case 1296: return 20;
				case 1297: return 20;
				case 1298: return 20;
				case 1299: return 20;
				case 1300: return 20;
				case 1301: return 20;
				case 1302: return 20;
				case 1303: return 20;
				case 1304: return 20;
				case 1305: return 20;
				case 1306: return 20;
				case 1307: return 20;
				case 1308: return 20;
				case 1309: return 20;
				case 1310: return 20;
				case 1311: return 20;
				case 1312: return 20;
				case 1313: return 20;
				case 1314: return 20;
				case 1315: return 20;
				case 1316: return 20;
				case 1317: return 20;
				case 1318: return 20;
				case 1319: return 20;
				case 1320: return 20;
				case 1321: return 20;
				case 1322: return 20;
				case 1323: return 20;
				case 1324: return 20;
				case 1325: return 20;
				case 1326: return 20;
				case 1327: return 20;
				case 1328: return 20;
				case 1329: return 20;
				case 1330: return 20;
				case 1331: return 20;
				case 1332: return 20;
				case 1333: return 20;
				case 1334: return 20;
				case 1335: return 20;
				case 1336: return 20;
				case 1337: return 20;
				case 1338: return 20;
				case 1339: return 20;
				case 1340: return 20;
				case 1341: return 20;
				case 1342: return 20;
				case 1343: return 20;
				case 1344: return 20;
				case 1345: return 20;
				case 1346: return 20;
				case 1347: return 20;
				case 1348: return 20;
				case 1349: return 20;
				case 1350: return 20;
				case 1351: return 20;
				case 1352: return 20;
				case 1353: return 20;
				case 1354: return 20;
				case 1355: return 20;
				case 1356: return 20;
				case 1357: return 20;
				case 1358: return 20;
				case 1359: return 20;
				case 1360: return 20;
				case 1361: return 20;
				case 1362: return 20;
				case 1363: return 20;
				case 1364: return 20;
				case 1365: return 20;
				case 1366: return 20;
				case 1367: return 20;
				case 1368: return 20;
				case 1369: return 20;
				case 1370: return 20;
				case 1371: return 20;
				case 1372: return 20;
				case 1373: return 20;
				case 1374: return 20;
				case 1375: return 20;
				case 1376: return 20;
				case 1377: return 20;
				case 1378: return 20;
				case 1379: return 20;
				case 1380: return 20;
				case 1381: return 20;
				case 1382: return 20;
				case 1383: return 20;
				case 1384: return 20;
				case 1385: return 20;
				case 1386: return 20;
				case 1387: return 20;
				case 1388: return 20;
				case 1389: return 20;
				case 1390: return 20;
				case 1391: return 20;
				case 1392: return 20;
				case 1393: return 20;
				case 1394: return 20;
				case 1395: return 20;
				case 1396: return 20;
				case 1397: return 20;
				case 1398: return 20;
				case 1399: return 20;
				case 1400: return 20;
				case 1401: return 20;
				case 1402: return 20;
				case 1403: return 20;
				case 1404: return 20;
				case 1405: return 20;
				case 1406: return 20;
				case 1407: return 20;
				case 1408: return 20;
				case 1409: return 20;
				case 1410: return 20;
				case 1411: return 20;
				case 1412: return 20;
				case 1413: return 20;
				case 1414: return 20;
				case 1415: return 20;
				case 1416: return 20;
				case 1417: return 20;
				case 1418: return 20;
				case 1419: return 20;
				case 1420: return 20;
				case 1421: return 20;
				case 1422: return 20;
				case 1423: return 20;
				case 1424: return 20;
				case 1425: return 20;
				case 1426: return 20;
				case 1427: return 20;
				case 1428: return 20;
				case 1429: return 20;
				case 1430: return 20;
				case 1431: return 20;
				case 1432: return 20;
				case 1433: return 20;
				case 1434: return 20;
				case 1435: return 20;
				case 1436: return 20;
				case 1437: return 20;
				case 1438: return 20;
				case 1439: return 20;
				case 1440: return 20;
				case 1441: return 20;
				case 1442: return 20;
				case 1443: return 20;
				case 1444: return 20;
				case 1445: return 20;
				case 1446: return 20;
				case 1447: return 20;
				case 1448: return 20;
				case 1449: return 20;
				case 1450: return 20;
				case 1451: return 20;
				case 1452: return 20;
				case 1453: return 20;
				case 1454: return 20;
				case 1455: return 20;
				case 1456: return 20;
				case 1457: return 20;
				case 1458: return 20;
				case 1459: return 20;
				case 1460: return 20;
				case 1461: return 20;
				case 1462: return 20;
				case 1463: return 20;
				case 1464: return 20;
				case 1465: return 20;
				case 1466: return 20;
				case 1467: return 20;
				case 1468: return 20;
				case 1469: return 20;
				case 1470: return 20;
				case 1471: return 20;
				case 1472: return 20;
				case 1473: return 20;
				case 1474: return 20;
				case 1475: return 20;
				case 1476: return 20;
				case 1477: return 20;
				case 1478: return 20;
				case 1479: return 20;
				case 1480: return 20;
				case 1481: return 20;
				case 1482: return 20;
				case 1483: return 20;
				case 1484: return 20;
				case 1485: return 20;
				case 1486: return 20;
				case 1487: return 20;
				case 1488: return 20;
				case 1489: return 20;
				case 1490: return 20;
				case 1491: return 20;
				case 1492: return 20;
				case 1493: return 20;
				case 1494: return 20;
				case 1495: return 20;
				case 1496: return 20;
				case 1497: return 20;
				case 1498: return 20;
				case 1499: return 20;
				case 1500: return 20;
				case 1501: return 20;
				case 1502: return 20;
				case 1503: return 20;
				case 1504: return 20;
				case 1505: return 20;
				case 1506: return 20;
				case 1507: return 20;
				case 1508: return 20;
				case 1509: return 20;
				case 1510: return 20;
				case 1511: return 20;
				case 1512: return 20;
				case 1513: return 20;
				case 1514: return 20;
				case 1515: return 20;
				case 1516: return 20;
				case 1517: return 20;
				case 1518: return 20;
				case 1519: return 20;
				case 1520: return 20;
				case 1521: return 20;
				case 1522: return 20;
				case 1523: return 20;
				case 1524: return 20;
				case 1525: return 20;
				case 1526: return 20;
				case 1527: return 20;
				case 1528: return 20;
				case 1529: return 20;
				case 1530: return 20;
				case 1531: return 20;
				case 1532: return 20;
				case 1533: return 20;
				case 1534: return 20;
				case 1535: return 20;
				case 1536: return 21;
				case 1537: return 21;
				case 1538: return 21;
				case 1539: return 21;
				case 1540: return 21;
				case 1541: return 21;
				case 1542: return 21;
				case 1543: return 21;
				case 1544: return 21;
				case 1545: return 21;
				case 1546: return 21;
				case 1547: return 21;
				case 1548: return 21;
				case 1549: return 21;
				case 1550: return 21;
				case 1551: return 21;
				case 1552: return 21;
				case 1553: return 21;
				case 1554: return 21;
				case 1555: return 21;
				case 1556: return 21;
				case 1557: return 21;
				case 1558: return 21;
				case 1559: return 21;
				case 1560: return 21;
				case 1561: return 21;
				case 1562: return 21;
				case 1563: return 21;
				case 1564: return 21;
				case 1565: return 21;
				case 1566: return 21;
				case 1567: return 21;
				case 1568: return 21;
				case 1569: return 21;
				case 1570: return 21;
				case 1571: return 21;
				case 1572: return 21;
				case 1573: return 21;
				case 1574: return 21;
				case 1575: return 21;
				case 1576: return 21;
				case 1577: return 21;
				case 1578: return 21;
				case 1579: return 21;
				case 1580: return 21;
				case 1581: return 21;
				case 1582: return 21;
				case 1583: return 21;
				case 1584: return 21;
				case 1585: return 21;
				case 1586: return 21;
				case 1587: return 21;
				case 1588: return 21;
				case 1589: return 21;
				case 1590: return 21;
				case 1591: return 21;
				case 1592: return 21;
				case 1593: return 21;
				case 1594: return 21;
				case 1595: return 21;
				case 1596: return 21;
				case 1597: return 21;
				case 1598: return 21;
				case 1599: return 21;
				case 1600: return 21;
				case 1601: return 21;
				case 1602: return 21;
				case 1603: return 21;
				case 1604: return 21;
				case 1605: return 21;
				case 1606: return 21;
				case 1607: return 21;
				case 1608: return 21;
				case 1609: return 21;
				case 1610: return 21;
				case 1611: return 21;
				case 1612: return 21;
				case 1613: return 21;
				case 1614: return 21;
				case 1615: return 21;
				case 1616: return 21;
				case 1617: return 21;
				case 1618: return 21;
				case 1619: return 21;
				case 1620: return 21;
				case 1621: return 21;
				case 1622: return 21;
				case 1623: return 21;
				case 1624: return 21;
				case 1625: return 21;
				case 1626: return 21;
				case 1627: return 21;
				case 1628: return 21;
				case 1629: return 21;
				case 1630: return 21;
				case 1631: return 21;
				case 1632: return 21;
				case 1633: return 21;
				case 1634: return 21;
				case 1635: return 21;
				case 1636: return 21;
				case 1637: return 21;
				case 1638: return 21;
				case 1639: return 21;
				case 1640: return 21;
				case 1641: return 21;
				case 1642: return 21;
				case 1643: return 21;
				case 1644: return 21;
				case 1645: return 21;
				case 1646: return 21;
				case 1647: return 21;
				case 1648: return 21;
				case 1649: return 21;
				case 1650: return 21;
				case 1651: return 21;
				case 1652: return 21;
				case 1653: return 21;
				case 1654: return 21;
				case 1655: return 21;
				case 1656: return 21;
				case 1657: return 21;
				case 1658: return 21;
				case 1659: return 21;
				case 1660: return 21;
				case 1661: return 21;
				case 1662: return 21;
				case 1663: return 21;
				case 1664: return 21;
				case 1665: return 21;
				case 1666: return 21;
				case 1667: return 21;
				case 1668: return 21;
				case 1669: return 21;
				case 1670: return 21;
				case 1671: return 21;
				case 1672: return 21;
				case 1673: return 21;
				case 1674: return 21;
				case 1675: return 21;
				case 1676: return 21;
				case 1677: return 21;
				case 1678: return 21;
				case 1679: return 21;
				case 1680: return 21;
				case 1681: return 21;
				case 1682: return 21;
				case 1683: return 21;
				case 1684: return 21;
				case 1685: return 21;
				case 1686: return 21;
				case 1687: return 21;
				case 1688: return 21;
				case 1689: return 21;
				case 1690: return 21;
				case 1691: return 21;
				case 1692: return 21;
				case 1693: return 21;
				case 1694: return 21;
				case 1695: return 21;
				case 1696: return 21;
				case 1697: return 21;
				case 1698: return 21;
				case 1699: return 21;
				case 1700: return 21;
				case 1701: return 21;
				case 1702: return 21;
				case 1703: return 21;
				case 1704: return 21;
				case 1705: return 21;
				case 1706: return 21;
				case 1707: return 21;
				case 1708: return 21;
				case 1709: return 21;
				case 1710: return 21;
				case 1711: return 21;
				case 1712: return 21;
				case 1713: return 21;
				case 1714: return 21;
				case 1715: return 21;
				case 1716: return 21;
				case 1717: return 21;
				case 1718: return 21;
				case 1719: return 21;
				case 1720: return 21;
				case 1721: return 21;
				case 1722: return 21;
				case 1723: return 21;
				case 1724: return 21;
				case 1725: return 21;
				case 1726: return 21;
				case 1727: return 21;
				case 1728: return 21;
				case 1729: return 21;
				case 1730: return 21;
				case 1731: return 21;
				case 1732: return 21;
				case 1733: return 21;
				case 1734: return 21;
				case 1735: return 21;
				case 1736: return 21;
				case 1737: return 21;
				case 1738: return 21;
				case 1739: return 21;
				case 1740: return 21;
				case 1741: return 21;
				case 1742: return 21;
				case 1743: return 21;
				case 1744: return 21;
				case 1745: return 21;
				case 1746: return 21;
				case 1747: return 21;
				case 1748: return 21;
				case 1749: return 21;
				case 1750: return 21;
				case 1751: return 21;
				case 1752: return 21;
				case 1753: return 21;
				case 1754: return 21;
				case 1755: return 21;
				case 1756: return 21;
				case 1757: return 21;
				case 1758: return 21;
				case 1759: return 21;
				case 1760: return 21;
				case 1761: return 21;
				case 1762: return 21;
				case 1763: return 21;
				case 1764: return 21;
				case 1765: return 21;
				case 1766: return 21;
				case 1767: return 21;
				case 1768: return 21;
				case 1769: return 21;
				case 1770: return 21;
				case 1771: return 21;
				case 1772: return 21;
				case 1773: return 21;
				case 1774: return 21;
				case 1775: return 21;
				case 1776: return 21;
				case 1777: return 21;
				case 1778: return 21;
				case 1779: return 21;
				case 1780: return 21;
				case 1781: return 21;
				case 1782: return 21;
				case 1783: return 21;
				case 1784: return 21;
				case 1785: return 21;
				case 1786: return 21;
				case 1787: return 21;
				case 1788: return 21;
				case 1789: return 21;
				case 1790: return 21;
				case 1791: return 21;
				case 1792: return 21;
				case 1793: return 21;
				case 1794: return 21;
				case 1795: return 21;
				case 1796: return 21;
				case 1797: return 21;
				case 1798: return 21;
				case 1799: return 21;
				case 1800: return 21;
				case 1801: return 21;
				case 1802: return 21;
				case 1803: return 21;
				case 1804: return 21;
				case 1805: return 21;
				case 1806: return 21;
				case 1807: return 21;
				case 1808: return 21;
				case 1809: return 21;
				case 1810: return 21;
				case 1811: return 21;
				case 1812: return 21;
				case 1813: return 21;
				case 1814: return 21;
				case 1815: return 21;
				case 1816: return 21;
				case 1817: return 21;
				case 1818: return 21;
				case 1819: return 21;
				case 1820: return 21;
				case 1821: return 21;
				case 1822: return 21;
				case 1823: return 21;
				case 1824: return 21;
				case 1825: return 21;
				case 1826: return 21;
				case 1827: return 21;
				case 1828: return 21;
				case 1829: return 21;
				case 1830: return 21;
				case 1831: return 21;
				case 1832: return 21;
				case 1833: return 21;
				case 1834: return 21;
				case 1835: return 21;
				case 1836: return 21;
				case 1837: return 21;
				case 1838: return 21;
				case 1839: return 21;
				case 1840: return 21;
				case 1841: return 21;
				case 1842: return 21;
				case 1843: return 21;
				case 1844: return 21;
				case 1845: return 21;
				case 1846: return 21;
				case 1847: return 21;
				case 1848: return 21;
				case 1849: return 21;
				case 1850: return 21;
				case 1851: return 21;
				case 1852: return 21;
				case 1853: return 21;
				case 1854: return 21;
				case 1855: return 21;
				case 1856: return 21;
				case 1857: return 21;
				case 1858: return 21;
				case 1859: return 21;
				case 1860: return 21;
				case 1861: return 21;
				case 1862: return 21;
				case 1863: return 21;
				case 1864: return 21;
				case 1865: return 21;
				case 1866: return 21;
				case 1867: return 21;
				case 1868: return 21;
				case 1869: return 21;
				case 1870: return 21;
				case 1871: return 21;
				case 1872: return 21;
				case 1873: return 21;
				case 1874: return 21;
				case 1875: return 21;
				case 1876: return 21;
				case 1877: return 21;
				case 1878: return 21;
				case 1879: return 21;
				case 1880: return 21;
				case 1881: return 21;
				case 1882: return 21;
				case 1883: return 21;
				case 1884: return 21;
				case 1885: return 21;
				case 1886: return 21;
				case 1887: return 21;
				case 1888: return 21;
				case 1889: return 21;
				case 1890: return 21;
				case 1891: return 21;
				case 1892: return 21;
				case 1893: return 21;
				case 1894: return 21;
				case 1895: return 21;
				case 1896: return 21;
				case 1897: return 21;
				case 1898: return 21;
				case 1899: return 21;
				case 1900: return 21;
				case 1901: return 21;
				case 1902: return 21;
				case 1903: return 21;
				case 1904: return 21;
				case 1905: return 21;
				case 1906: return 21;
				case 1907: return 21;
				case 1908: return 21;
				case 1909: return 21;
				case 1910: return 21;
				case 1911: return 21;
				case 1912: return 21;
				case 1913: return 21;
				case 1914: return 21;
				case 1915: return 21;
				case 1916: return 21;
				case 1917: return 21;
				case 1918: return 21;
				case 1919: return 21;
				case 1920: return 21;
				case 1921: return 21;
				case 1922: return 21;
				case 1923: return 21;
				case 1924: return 21;
				case 1925: return 21;
				case 1926: return 21;
				case 1927: return 21;
				case 1928: return 21;
				case 1929: return 21;
				case 1930: return 21;
				case 1931: return 21;
				case 1932: return 21;
				case 1933: return 21;
				case 1934: return 21;
				case 1935: return 21;
				case 1936: return 21;
				case 1937: return 21;
				case 1938: return 21;
				case 1939: return 21;
				case 1940: return 21;
				case 1941: return 21;
				case 1942: return 21;
				case 1943: return 21;
				case 1944: return 21;
				case 1945: return 21;
				case 1946: return 21;
				case 1947: return 21;
				case 1948: return 21;
				case 1949: return 21;
				case 1950: return 21;
				case 1951: return 21;
				case 1952: return 21;
				case 1953: return 21;
				case 1954: return 21;
				case 1955: return 21;
				case 1956: return 21;
				case 1957: return 21;
				case 1958: return 21;
				case 1959: return 21;
				case 1960: return 21;
				case 1961: return 21;
				case 1962: return 21;
				case 1963: return 21;
				case 1964: return 21;
				case 1965: return 21;
				case 1966: return 21;
				case 1967: return 21;
				case 1968: return 21;
				case 1969: return 21;
				case 1970: return 21;
				case 1971: return 21;
				case 1972: return 21;
				case 1973: return 21;
				case 1974: return 21;
				case 1975: return 21;
				case 1976: return 21;
				case 1977: return 21;
				case 1978: return 21;
				case 1979: return 21;
				case 1980: return 21;
				case 1981: return 21;
				case 1982: return 21;
				case 1983: return 21;
				case 1984: return 21;
				case 1985: return 21;
				case 1986: return 21;
				case 1987: return 21;
				case 1988: return 21;
				case 1989: return 21;
				case 1990: return 21;
				case 1991: return 21;
				case 1992: return 21;
				case 1993: return 21;
				case 1994: return 21;
				case 1995: return 21;
				case 1996: return 21;
				case 1997: return 21;
				case 1998: return 21;
				case 1999: return 21;
				case 2000: return 21;
				case 2001: return 21;
				case 2002: return 21;
				case 2003: return 21;
				case 2004: return 21;
				case 2005: return 21;
				case 2006: return 21;
				case 2007: return 21;
				case 2008: return 21;
				case 2009: return 21;
				case 2010: return 21;
				case 2011: return 21;
				case 2012: return 21;
				case 2013: return 21;
				case 2014: return 21;
				case 2015: return 21;
				case 2016: return 21;
				case 2017: return 21;
				case 2018: return 21;
				case 2019: return 21;
				case 2020: return 21;
				case 2021: return 21;
				case 2022: return 21;
				case 2023: return 21;
				case 2024: return 21;
				case 2025: return 21;
				case 2026: return 21;
				case 2027: return 21;
				case 2028: return 21;
				case 2029: return 21;
				case 2030: return 21;
				case 2031: return 21;
				case 2032: return 21;
				case 2033: return 21;
				case 2034: return 21;
				case 2035: return 21;
				case 2036: return 21;
				case 2037: return 21;
				case 2038: return 21;
				case 2039: return 21;
				case 2040: return 21;
				case 2041: return 21;
				case 2042: return 21;
				case 2043: return 21;
				case 2044: return 21;
				case 2045: return 21;
				case 2046: return 21;
				case 2047: return 21;

				default: throw Assert.Unreachable();
			}
			//return (byte)(idx <= 8 ? idx - (tobyte(idx > 4) + tobyte(idx > 6)) : 6
			//            + (int)count(idx > new int8(11, 15, 23, 31, 47, 63, 95, 127))
			//            + (int)count(idx > new int8(191, 255, 383, 511, 767, 1023, 1535, 2047)));
		}
        
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte Shift1(uint pos)
        {
            switch (lzcnt(pos))
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                {
                    return 20;
                }
                case 11:
                case 12:
                case 13:
                case 14:
                case 15:
                case 16:
                case 17:
                case 18:
                case 19:
                case 20:
                {
                    return 10;
                }
                case 21:
                case 22:
                case 23:
                case 24:
                case 25:
                case 26:
                case 27:
                case 28:
                case 29:
                case 30:
                case 31:
                case 32:
                {
                    return 0;
                }

                default: throw DevTools.Assert.Unreachable();
            }
        }
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte Shift2(uint pos)
        {
            switch (lzcnt(pos))
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                {
                    return 20;
                }
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                case 11:
                case 12:
                case 13:
                case 14:
                {
                    return 10;
                }
                case 15:
                case 16:
                case 17:
                case 18:
                case 19:
                case 20:
                case 21:
                case 22:
                case 23:
                case 24:
                case 25:
                case 26:
                case 27:
                case 28:
                case 29:
                case 30:
                case 31:
                case 32:
                {
                    return 0;
                }

                default: throw DevTools.Assert.Unreachable();
            }
        }
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static uint GetPosSlot(uint pos)
		{
            byte __base = 0;
            __base += Shift1(pos);

            return FastPos((int)pos >> __base) + 2u * __base;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static uint GetPosSlot2(uint pos)
		{
            byte __base = 6;
            __base += Shift2(pos);

            return FastPos((int)pos >> __base) + 2u * __base;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void FillDistancesPrices(EncodingData* __Data)
		{
			for (uint i = Base.kStartPosModelIndex; i < Base.kNumFullDistances; i++)
			{ 
				uint posSlot = GetPosSlot(i);
				int footerBits = (int)((posSlot >> 1) - 1);
				uint baseVal = ((2 | (posSlot & 1)) << footerBits);
				__Data->tempPrices[i - Base.kStartPosModelIndex] = BitTreeEncoder.ReverseGetPrice(__Data->_posEncoders, footerBits, baseVal - posSlot - 1, i - baseVal);
			}

			for (uint lenToPosState = 0; lenToPosState < Base.kNumLenToPosStates; lenToPosState++)
			{
				uint posSlot;
				BitEncoder* encoder = __Data->_posSlotEncoder + (lenToPosState << Base.kNumPosSlotBits);
			
				uint st = lenToPosState << Base.kNumPosSlotBits;
				for (posSlot = 0; posSlot < __Data->_distTableSize; posSlot++)
				{
					__Data->_posSlotPrices[st + posSlot] = BitTreeEncoder.GetPrice(encoder, Base.kNumPosSlotBits, posSlot);
				}
				for (posSlot = Base.kEndPosModelIndex; posSlot < __Data->_distTableSize; posSlot++)
				{
					__Data->_posSlotPrices[st + posSlot] += (((posSlot >> 1) - 1) - Base.kNumAlignBits) << BitEncoder.kNumBitPriceShiftBits;
				}

				uint st2 = lenToPosState * Base.kNumFullDistances;
				for (uint i = 0; i < Base.kStartPosModelIndex; i++)
				{
					__Data->_distancesPrices[st2 + i] = __Data->_posSlotPrices[st + i];
				}
				for (uint i = Base.kStartPosModelIndex; i < Base.kNumFullDistances; i++)
				{
					__Data->_distancesPrices[st2 + i] = __Data->_posSlotPrices[st + GetPosSlot(i)] + __Data->tempPrices[i - Base.kStartPosModelIndex];
				}
			}
			__Data->_matchPriceCount = 0;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void FillAlignPrices(EncodingData* __Data)
		{
			for (uint i = 0; i < Base.kAlignTableSize; i++)
			{
				__Data->_alignPrices[i] = BitTreeEncoder.ReverseGetPrice(__Data->_posAlignEncoder, Base.kNumAlignBits, 0, i);
			}
			__Data->_alignPriceCount = 0;
		}

		internal static JobHandle Schedule(CompressionSettings settings, ZipCompressed jobData, JobHandle inputDeps)
		{
			EncodingData* data = (EncodingData*)UnsafeUtility.Malloc(sizeof(EncodingData), 64, Allocator.Persistent);
			inputDeps = new MallocJob { __Data = data, JobData = jobData }.Schedule(inputDeps);

			if (settings.MatchFinder == MatchFinder.BT4)
			{
				int workersUsed = (JobsUtility.JobWorkerCount * 7) / 8;
				JobHandle* initJobs = stackalloc JobHandle[2 + workersUsed];
				initJobs[0] = new InitMiscJob			 { __Data = data, JobData = jobData }.Schedule(inputDeps);
				initJobs[1] = new InitMatchFinderJob	 { __Data = data					}.Schedule(inputDeps);
				for (int i = 0; i < workersUsed; i++)
				{
					initJobs[2 + i] = new InitMatchFinderHashJob { __Data = data, slice = i, workers = workersUsed }.Schedule(inputDeps);
				}

				inputDeps = JobHandleUnsafeUtility.CombineDependencies(initJobs, 2 + workersUsed);
			}
			else
			{
				JobHandle init0 = new InitMiscJob			 { __Data = data, JobData = jobData		 }.Schedule(inputDeps);
				JobHandle init1 = new InitMatchFinderJob	 { __Data = data						 }.Schedule(inputDeps);
				JobHandle init2 = new InitMatchFinderHashJob { __Data = data, slice = 0, workers = 1 }.Schedule(inputDeps);

				inputDeps = JobHandle.CombineDependencies(init0, init1, init2);
			}

			inputDeps = new EncodeJob { __Data = data }.Schedule(inputDeps);

			new FreeJob { __Data = data }.Schedule(inputDeps);

			return inputDeps;
		}
	}
}
