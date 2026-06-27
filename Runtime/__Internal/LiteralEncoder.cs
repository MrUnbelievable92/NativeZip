using System.Runtime.CompilerServices;

using static MaxMath.math;

namespace NativeZip
{
    unsafe internal struct LiteralEncoder
    {
        private const int ENCODERS = 0x300;

		private uint m_PosMask;
        private BitEncoder* Coders;
        
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public LiteralEncoder(int numPosBits, void* basePtr)
    	{
            m_PosMask = bitmask32((uint)numPosBits);
            Coders = (BitEncoder*)basePtr;
    	}
        
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public static long RequiredBytes(int numPosBits, int numPrevBits)
    	{
    		return (sizeof(BitEncoder) * (long)ENCODERS) << (numPrevBits + numPosBits);
    	}
    
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public void Init(int numPosBits, int numPrevBits)
    	{
    		long numStates = (long)ENCODERS << (numPrevBits + numPosBits);
    		for (uint i = 0; i < numStates; i++)
    		{
                Coders[i].Init(); 
    		}
    	}
    
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public BitEncoder* GetSubCoder(int numPrevBits, uint pos, byte prevByte)
    	{ 
    		return Coders + ENCODERS * (((pos & m_PosMask) << numPrevBits) + (uint)(prevByte >> (8 - numPrevBits))); 
    	}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Encode(BitEncoder* m_Encoders, ref RangeEncoder rangeEncoder, byte symbol)
        {
        	uint context = 1;
        	for (int i = 7; i >= 0; i--)
        	{
        		uint bit = (uint)((symbol >> i) & 1);
        		m_Encoders[context].Encode(ref rangeEncoder, bit != 0);
        		context = (context << 1) | bit;
        	}
        }
        
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EncodeMatched(BitEncoder* m_Encoders, ref RangeEncoder rangeEncoder, byte matchByte, byte symbol)
        {
        	uint context = 1;
        	uint bit = (uint)((symbol >> 7) & 1);
        	uint state = context;
        	uint matchBit = (uint)((matchByte >> 7) & 1);
        	state += (1 + matchBit) << 8;
        	bool same = matchBit == bit;
        	m_Encoders[state].Encode(ref rangeEncoder, bit != 0);
        	context = (context << 1) | bit;
        	for (int i = 6; i >= 0; i--)
        	{
        		bit = (uint)((symbol >> i) & 1);
        		state = context;
        		if (same)
        		{
        			matchBit = (uint)((matchByte >> i) & 1);
        			state += (1 + matchBit) << 8;
        			same = matchBit == bit;
        		}
        		m_Encoders[state].Encode(ref rangeEncoder, bit != 0);
        		context = (context << 1) | bit;
        	}
        }
        
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetPrice(BitEncoder* m_Encoders, bool matchMode, byte matchByte, byte symbol)
        {
        	uint price = 0;
        	uint context = 1;
        	int i = 7;
        	if (matchMode)
        	{
        		while (i-- >= 0)
        		{
        			uint matchBit = (uint)(matchByte >> i) & 1;
        			uint bit = (uint)(symbol >> i) & 1;
        			price += m_Encoders[((1 + matchBit) << 8) + context].GetPrice(tobool(bit));
        			context = (context << 1) | bit;
        			if (matchBit != bit)
        			{
        	            while (--i >= 0)
        	            {
        	            	bit = (uint)(symbol >> i) & 1;
        	            	price += m_Encoders[context].GetPrice(tobool(bit));
        	            	context = (context << 1) | bit;
        	            }
        				break;
        			}
        		}
        	}

        	return price;
        }
    }
}
