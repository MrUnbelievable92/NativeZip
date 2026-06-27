using System.Runtime.CompilerServices;

namespace NativeZip
{
    unsafe internal struct LenDecoder
    {
        private readonly BitDecoder* basePtr;
    	private BitDecoder m_Choice;
    	private BitDecoder m_Choice2;
    	private readonly uint m_NumPosStates;
    	private readonly BitDecoder* m_HighCoder => basePtr;
    	private readonly BitDecoder* m_MidCoder => m_HighCoder + (1 << Base.kNumHighLenBits);
    	private readonly BitDecoder* m_LowCoder => m_MidCoder + (m_NumPosStates << Base.kNumMidLenBits);
    	
    	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public LenDecoder(uint numPosStates, void* basePtr)
    	{
            this.basePtr = (BitDecoder*)basePtr;
    		m_Choice = new BitDecoder();
    		m_Choice2 = new BitDecoder();
    		m_NumPosStates = numPosStates;
    	}
    	
    	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public void Init()
    	{
    		m_Choice.Init();
    		m_Choice2.Init();
    		BitTreeDecoder.Init(m_HighCoder, Base.kNumHighLenBits);
    		for (uint posState = 0; posState < m_NumPosStates; posState++)
    		{
    			BitTreeDecoder.Init(m_MidCoder + (posState << Base.kNumMidLenBits), Base.kNumMidLenBits);
    			BitTreeDecoder.Init(m_LowCoder + (posState << Base.kNumLowLenBits), Base.kNumLowLenBits);
    		}
    	}
    	
    	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public uint Decode(ref RangeDecoder rangeDecoder, uint posState)
    	{
    		if (m_Choice.Decode(ref rangeDecoder) == 0)
    		{
    			return BitTreeDecoder.Decode(ref rangeDecoder, m_LowCoder + (posState << Base.kNumLowLenBits), Base.kNumLowLenBits);
    		}
    		else
    		{
    			uint symbol = Base.kNumLowLenSymbols;
    			if (m_Choice2.Decode(ref rangeDecoder) == 0)
    			{
    				symbol += BitTreeDecoder.Decode(ref rangeDecoder, m_MidCoder + (posState << Base.kNumMidLenBits), Base.kNumMidLenBits);
    			}
    			else
    			{
    				symbol += Base.kNumMidLenSymbols;
    				symbol += BitTreeDecoder.Decode(ref rangeDecoder, m_HighCoder, Base.kNumHighLenBits);
    			}
    			return symbol;
    		}
    	}
    }
}
