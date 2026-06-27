using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace NativeZip
{
	unsafe internal static class Decoder
	{
		private struct Data
		{
			[NativeDisableUnsafePtrRestriction] internal BitDecoder* _decoders;

			internal OutWindow m_OutWindow;
			internal RangeDecoder m_RangeDecoder;
			internal LenDecoder m_LenDecoder;
			internal LenDecoder m_RepLenDecoder;
			internal LiteralDecoder m_LiteralDecoder;
			internal uint lc;
			internal uint lp;
			internal uint pb;
			internal uint numPosStates;

			internal BitDecoder* m_IsMatchDecoders		{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>_decoders; }
			internal BitDecoder* m_IsRepDecoders		{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_IsMatchDecoders + (Base.kNumStates << Base.kNumPosStatesBitsMax); }
			internal BitDecoder* m_IsRepG0Decoders		{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_IsRepDecoders + Base.kNumStates; }
			internal BitDecoder* m_IsRepG1Decoders		{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_IsRepG0Decoders + Base.kNumStates; }
			internal BitDecoder* m_IsRepG2Decoders		{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_IsRepG1Decoders + Base.kNumStates; }
			internal BitDecoder* m_IsRep0LongDecoders	{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_IsRepG2Decoders + Base.kNumStates; }
			internal BitDecoder* m_PosDecoders			{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_IsRep0LongDecoders + (Base.kNumStates << Base.kNumPosStatesBitsMax); }

			internal BitDecoder* m_PosAlignDecoder	{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_PosDecoders + (Base.kNumFullDistances - Base.kEndPosModelIndex); }
			internal BitDecoder* m_PosSlotDecoder	{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_PosAlignDecoder + (1 << Base.kNumAlignBits); }
			internal BitDecoder* m_LenDecoders		{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_PosSlotDecoder + (Base.kNumLenToPosStates << Base.kNumPosSlotBits); }
			internal BitDecoder* m_RepLenDecoders	{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_LenDecoders + (1L << Base.kNumHighLenBits) + (numPosStates << Base.kNumMidLenBits) + (numPosStates << Base.kNumLowLenBits); }
			internal BitDecoder* m_LiteralDecoders	{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_RepLenDecoders + (1L << Base.kNumHighLenBits) + (numPosStates << Base.kNumMidLenBits) + (numPosStates << Base.kNumLowLenBits); }
		}

		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        private struct AllocateJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] public Data* __Data;
			public ZipCompressed JobData;
			
			private void SetDecoderProperties()
			{
				byte b = __Data->m_RangeDecoder.Stream->Ptr[0];
				__Data->lc = b % 9u;
				uint remainder = b / 9u;
				__Data->lp = remainder % 5u;
				__Data->pb = remainder / 5u;
				__Data->numPosStates = 1u << (int)__Data->pb;
			}

            public void Execute()
            {
				__Data->m_RangeDecoder.SetStream(JobData._data);
				__Data->m_OutWindow = new OutWindow((byte*)JobData._srcData->_ptr);

				SetDecoderProperties();

				const long DECODERS = (Base.kNumStates << Base.kNumPosStatesBitsMax)
								    + Base.kNumStates
								    + Base.kNumStates
								    + Base.kNumStates
								    + Base.kNumStates
								    + (Base.kNumStates << Base.kNumPosStatesBitsMax)
								    + (Base.kNumFullDistances - Base.kEndPosModelIndex)
								    + (1 << Base.kNumAlignBits)
								    + (Base.kNumLenToPosStates << Base.kNumPosSlotBits);
				
				uint numStates = 1u << (int)(__Data->lc + __Data->lp);
				long numDecoders = DECODERS + (numStates * LiteralDecoder.DECODERS);
				long LenDecoders = (1L << Base.kNumHighLenBits)
								 + (__Data->numPosStates << Base.kNumMidLenBits)
								 + (__Data->numPosStates << Base.kNumLowLenBits);
				numDecoders += 2 * LenDecoders;
				
#if ENABLE_UNITY_COLLECTIONS_CHECKS
AtomicSafetyHandle.CheckReadAndThrow(JobData.m_Safety);
#endif
				__Data->_decoders = (BitDecoder*)UnsafeUtility.Malloc(sizeof(BitDecoder) * numDecoders, UnsafeUtility.AlignOf<BitDecoder>(), Allocator.Persistent);
            }
        }
		
		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        private struct LenDecodersInitJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] public Data* __Data;

            public void Execute()
            {
				__Data->m_LenDecoder = new LenDecoder(__Data->numPosStates, __Data->m_LenDecoders);
				__Data->m_RepLenDecoder = new LenDecoder(__Data->numPosStates, __Data->m_RepLenDecoders);
				__Data->m_LenDecoder.Init();
				__Data->m_RepLenDecoder.Init();
            }
        }
		
		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        private struct LiteralDecoderInitJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] public Data* __Data;

            public void Execute()
            {
				__Data->m_LiteralDecoder = new LiteralDecoder(__Data->lp);
				__Data->m_LiteralDecoder.Init(__Data->m_LiteralDecoders, __Data->lp, __Data->lc);
            }
        }
		
		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        private struct MiscInitJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] public Data* __Data;

            public void Execute()
            {
				for (uint i = 0; i < Base.kNumStates; i++)
				{
					for (uint j = 0; j < __Data->numPosStates; j++)
					{
						uint index = (i << Base.kNumPosStatesBitsMax) + j;
						__Data->m_IsMatchDecoders[index].Init();
						__Data->m_IsRep0LongDecoders[index].Init();
					}
					__Data->m_IsRepDecoders[i].Init();
					__Data->m_IsRepG0Decoders[i].Init();
					__Data->m_IsRepG1Decoders[i].Init();
					__Data->m_IsRepG2Decoders[i].Init();
				}

				for (uint i = 0; i < Base.kNumLenToPosStates; i++)
				{
					BitTreeDecoder.Init(__Data->m_PosSlotDecoder + (i << Base.kNumPosSlotBits), Base.kNumPosSlotBits);
				}
				for (uint i = 0; i < Base.kNumFullDistances - Base.kEndPosModelIndex; i++)
				{
					__Data->m_PosDecoders[i].Init();
				}

				BitTreeDecoder.Init(__Data->m_PosAlignDecoder, Base.kNumAlignBits);
				__Data->m_RangeDecoder.Init();
            }
        }
		
		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        private struct DecodeJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] public Data* __Data;

            public void Execute()
            {
				Base.State state = new Base.State();
				state.Init();
				uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;

				ulong nowPos64 = 1;
				ulong outSize64 = *(ulong*)(__Data->m_RangeDecoder.Stream->Ptr + 1) & 0x0000_FFFF_FFFF_FFFF;

				__Data->m_IsMatchDecoders[0].Prob = (ushort)((BitDecoder.kBitModelTotal >> 1) + ((BitDecoder.kBitModelTotal - (BitDecoder.kBitModelTotal >> 1)) >> BitDecoder.kNumMoveBits));

				__Data->m_OutWindow.PutByte(__Data->m_LiteralDecoder.DecodeNormal(ref __Data->m_RangeDecoder, 0, 0, __Data->m_LiteralDecoders, __Data->lc));
				uint posStateMask = __Data->numPosStates - 1;

				while (nowPos64 < outSize64)
				{
					uint posState = (uint)nowPos64 & posStateMask;
					if (__Data->m_IsMatchDecoders[((uint)state.Index << Base.kNumPosStatesBitsMax) + posState].Decode(ref __Data->m_RangeDecoder) == 0)
					{
						byte b;
						byte prevByte = __Data->m_OutWindow.GetByte(0);
						if (state.IsCharState)
						{
							b = __Data->m_LiteralDecoder.DecodeNormal(ref __Data->m_RangeDecoder, (uint)nowPos64, prevByte, __Data->m_LiteralDecoders, __Data->lc);
						}
						else
						{
							b = __Data->m_LiteralDecoder.DecodeWithMatchByte(ref __Data->m_RangeDecoder, (uint)nowPos64, prevByte, __Data->m_OutWindow.GetByte(rep0), __Data->m_LiteralDecoders, __Data->lc);
						}
							
						__Data->m_OutWindow.PutByte(b);
						state.UpdateChar();
						nowPos64++;
					}
					else
					{
						uint len;
						if (__Data->m_IsRepDecoders[state.Index].Decode(ref __Data->m_RangeDecoder) == 1)
						{
							if (__Data->m_IsRepG0Decoders[state.Index].Decode(ref __Data->m_RangeDecoder) == 0)
							{
								if (__Data->m_IsRep0LongDecoders[((uint)state.Index << Base.kNumPosStatesBitsMax) + posState].Decode(ref __Data->m_RangeDecoder) == 0)
								{
									state.UpdateShortRep();
									__Data->m_OutWindow.PutByte(__Data->m_OutWindow.GetByte(rep0));
									nowPos64++;
									continue;
								}
							}
							else
							{
								uint distance;
								if (__Data->m_IsRepG1Decoders[state.Index].Decode(ref __Data->m_RangeDecoder) == 0)
								{
									distance = rep1;
								}
								else
								{
									if (__Data->m_IsRepG2Decoders[state.Index].Decode(ref __Data->m_RangeDecoder) == 0)
									{
										distance = rep2;
									}
									else
									{
										distance = rep3;
										rep3 = rep2;
									}
									rep2 = rep1;
								}
								rep1 = rep0;
								rep0 = distance;
							}
							len = __Data->m_RepLenDecoder.Decode(ref __Data->m_RangeDecoder, posState) + Base.kMatchMinLen;
							state.UpdateRep();
						}
						else
						{
							rep3 = rep2;
							rep2 = rep1;
							rep1 = rep0;
							len = Base.kMatchMinLen + __Data->m_LenDecoder.Decode(ref __Data->m_RangeDecoder, posState);
							state.UpdateMatch();
							uint posSlot = BitTreeDecoder.Decode(ref __Data->m_RangeDecoder, __Data->m_PosSlotDecoder + (Base.GetLenToPosState(len) << Base.kNumPosSlotBits), Base.kNumPosSlotBits);
							if (posSlot >= Base.kStartPosModelIndex)
							{
								int numDirectBits = (int)((posSlot >> 1) - 1);
								rep0 = (2 | (posSlot & 1)) << numDirectBits;
								if (posSlot < Base.kEndPosModelIndex)
								{
									rep0 += BitTreeDecoder.ReverseDecode(__Data->m_PosDecoders, rep0 - posSlot - 1, ref __Data->m_RangeDecoder, numDirectBits);
								}
								else
								{
									rep0 += (__Data->m_RangeDecoder.DecodeDirectBits(numDirectBits - Base.kNumAlignBits) << Base.kNumAlignBits);
									rep0 += BitTreeDecoder.ReverseDecode(__Data->m_PosAlignDecoder, 0, ref __Data->m_RangeDecoder, Base.kNumAlignBits);
								}
							}
							else
							{
								rep0 = posSlot;
							}
						}

						__Data->m_OutWindow.CopyBlock(rep0, len);
						nowPos64 += len;
					}
				}
            }
        }
		
		[BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        private struct FreeJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] public Data* __Data;

            public void Execute()
            {
				UnsafeUtility.Free(__Data->_decoders, Allocator.Persistent);
				UnsafeUtility.Free(__Data, Allocator.Persistent);
			}
		}

		internal static JobHandle Schedule(ZipCompressed jobData, JobHandle inputDeps)
		{
			Data* __Data = (Data*)UnsafeUtility.Malloc(sizeof(Data), UnsafeUtility.AlignOf<Data>(), Allocator.Persistent);

			inputDeps = new AllocateJob { __Data = __Data, JobData = jobData }.Schedule(inputDeps);
			JobHandle init0 = new LenDecodersInitJob { __Data = __Data }.Schedule(inputDeps);
			JobHandle init1 = new LiteralDecoderInitJob { __Data = __Data }.Schedule(inputDeps);
			JobHandle init2 = new MiscInitJob { __Data = __Data }.Schedule(inputDeps);
			inputDeps = JobHandle.CombineDependencies(init0, init1, init2);
			inputDeps = new DecodeJob { __Data = __Data }.Schedule(inputDeps);

			new FreeJob { __Data = __Data }.Schedule(inputDeps);

			return inputDeps;
		}
	}
}
