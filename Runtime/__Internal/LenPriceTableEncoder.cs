using System.Runtime.CompilerServices;

namespace NativeZip
{
    unsafe internal struct LenPriceTableEncoder
    {
        private void* _basePtr;

    	private uint TableSize;
    	private uint m_NumPosStates;
        private BitEncoder _choice;
        private BitEncoder _choice2;

    	private uint* _prices         { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (uint*)_basePtr; }
    	private uint* _counters       { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _prices + (Base.kNumLenSymbols << Base.kNumPosStatesBitsEncodingMax); }
        private BitEncoder*_highCoder { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (BitEncoder*)(_counters + Base.kNumPosStatesEncodingMax); }
        private BitEncoder* _midCoder { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _highCoder + (1 << Base.kNumHighLenBits); }
        private BitEncoder* _lowCoder { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _midCoder + (m_NumPosStates << Base.kNumMidLenBits); }
    
        public LenPriceTableEncoder(byte posStateBits, ushort numFastBytes, void* basePtr)
        {
            _basePtr = basePtr;
            m_NumPosStates = 1u << posStateBits;
            TableSize = (uint)numFastBytes + 1 - Base.kMatchMinLen;
            _choice = new BitEncoder();
            _choice2 = new BitEncoder();
        	_choice.Init();
        	_choice2.Init();
        }
        
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long RequiredBytes(byte posStateBits)
        {
            return ((long)sizeof(uint) * (Base.kNumLenSymbols << Base.kNumPosStatesBitsEncodingMax))
                 + ((long)sizeof(uint) * Base.kNumPosStatesEncodingMax)
                 + ((long)sizeof(BitEncoder) * (1 << Base.kNumHighLenBits))
                 + ((long)sizeof(BitEncoder) * (1u << (posStateBits + Base.kNumMidLenBits)))
                 + ((long)sizeof(BitEncoder) * (1u << (posStateBits + Base.kNumLowLenBits)));
        }
        
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Init()
        {
            BitTreeEncoder.Init(_highCoder, Base.kNumHighLenBits);
        	for (uint posState = 0; posState < m_NumPosStates; posState++)
        	{
    			BitTreeEncoder.Init(_midCoder + (posState << Base.kNumMidLenBits), Base.kNumMidLenBits);
    			BitTreeEncoder.Init(_lowCoder + (posState << Base.kNumLowLenBits), Base.kNumLowLenBits);
        	}
        	for (uint posState = 0; posState < m_NumPosStates; posState++)
        	{
                UpdateTable(posState);
        	}
        }
    
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public uint GetPrice(uint symbol, uint posState)
    	{
    		return _prices[posState * Base.kNumLenSymbols + symbol];
    	}
    
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	void UpdateTable(uint posState)
    	{
    		SetPrices(posState, _prices, posState * Base.kNumLenSymbols);
    		_counters[posState] = TableSize;
    	}
        
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetPrices(uint posState, uint* prices, uint st)
        {
        	uint a0 = _choice.GetPrice0();
        	uint a1 = _choice.GetPrice1();
        	uint b0 = a1 + _choice2.GetPrice0();
        	uint b1 = a1 + _choice2.GetPrice1();
        	uint i = 0;
        	for (i = 0; i < Base.kNumLowLenSymbols; i++)
        	{
        		if (i >= TableSize)
                {
                    return;
                }
        		
        		prices[st + i] = a0 + BitTreeEncoder.GetPrice(_lowCoder + (posState << Base.kNumLowLenBits), Base.kNumLowLenBits, i);
        	}
        	while (i++ < Base.kNumLowLenSymbols + Base.kNumMidLenSymbols)
        	{
        		if (i >= TableSize)
                {
                    return;
                }
        			
        		prices[st + i] = b0 + BitTreeEncoder.GetPrice(_midCoder + (posState << Base.kNumMidLenBits), Base.kNumMidLenBits, i - Base.kNumLowLenSymbols);
        	}
        	while (i++ < TableSize)
            {
                prices[st + i] = b1 + BitTreeEncoder.GetPrice(_highCoder, Base.kNumHighLenBits, i - Base.kNumLowLenSymbols - Base.kNumMidLenSymbols);
            }
        }
        
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public void Encode(ref RangeEncoder rangeEncoder, uint symbol, uint posState)
    	{
        	if (symbol < Base.kNumLowLenSymbols)
        	{
        		_choice.Encode(ref rangeEncoder, false);
        		BitTreeEncoder.Encode(_lowCoder + (posState << Base.kNumLowLenBits), Base.kNumLowLenBits, ref rangeEncoder, symbol);
        	}
        	else
        	{
        		symbol -= Base.kNumLowLenSymbols;
        		_choice.Encode(ref rangeEncoder, true);
        		if (symbol < Base.kNumMidLenSymbols)
        		{
        			_choice2.Encode(ref rangeEncoder, false);
        			BitTreeEncoder.Encode(_midCoder + (posState << Base.kNumMidLenBits), Base.kNumMidLenBits, ref rangeEncoder, symbol);
        		}
        		else
        		{
        			_choice2.Encode(ref rangeEncoder, true);
        			BitTreeEncoder.Encode(_highCoder, Base.kNumHighLenBits, ref rangeEncoder, symbol - Base.kNumMidLenSymbols);
        		}
        	}

    		if (--_counters[posState] == 0)
            {
                UpdateTable(posState);
            }
    	}
    }
}
