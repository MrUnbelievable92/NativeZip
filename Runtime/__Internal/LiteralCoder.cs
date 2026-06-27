using System.Runtime.CompilerServices;

namespace NativeZip
{
    unsafe internal struct LiteralDecoder
    {
    	public const long DECODERS = 0x300;
    	private uint m_PosMask;
    	
    	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public LiteralDecoder(uint lp)
    	{
            m_PosMask = MaxMath.math.bitmask32(lp);
    	}
    	
    	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public readonly void Init(BitDecoder* m_Coders, uint lp, uint lc)
    	{
    		uint numStates = 1u << (int)(lc + lp);
    		for (uint i = 0; i < numStates * DECODERS; i++)
    		{
    			m_Coders[i].Init();
    		}
    	}
    	
    	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public readonly byte DecodeNormal(ref RangeDecoder rangeDecoder, BitDecoder* range)
    	{
    		uint symbol = 1;
    		do
    		{
    			symbol = (symbol << 1) | range[symbol].Decode(ref rangeDecoder);
    		}
    			
    		while (symbol < 0x100);
    		return (byte)symbol;
    	}
    	
    	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public readonly byte DecodeWithMatchByte(ref RangeDecoder rangeDecoder, byte matchByte, BitDecoder* range)
    	{
    		uint symbol = 1;
    		do
    		{
    			uint matchBit = (uint)(matchByte >> 7) & 1;
    			matchByte <<= 1;
    			uint bit = range[((1 + matchBit) << 8) + symbol].Decode(ref rangeDecoder);
    			symbol = (symbol << 1) | bit;
    			if (matchBit != bit)
    			{
    				while (symbol < 0x100)
    				{
    					symbol = (symbol << 1) | range[symbol].Decode(ref rangeDecoder);
    				}
    					
    				break;
    			}
    		}
    		while (symbol < 0x100);
    		return (byte)symbol;
    	}
    	
    	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public readonly uint GetState(uint pos, byte prevByte, uint lc)
    	{ 
    		return ((pos & m_PosMask) << (int)lc) + (uint)(prevByte >> (int)(8 - lc)); 
    	}
    	
    	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public readonly byte DecodeNormal(ref RangeDecoder rangeDecoder, uint pos, byte prevByte, BitDecoder* m_Coders, uint lc)
    	{ 
    		return DecodeNormal(ref rangeDecoder, m_Coders + GetState(pos, prevByte, lc) * DECODERS); 
    	}
    
    	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public readonly byte DecodeWithMatchByte(ref RangeDecoder rangeDecoder, uint pos, byte prevByte, byte matchByte, BitDecoder* m_Coders, uint lc)
    	{ 
    		return DecodeWithMatchByte(ref rangeDecoder, matchByte, m_Coders + GetState(pos, prevByte, lc) * DECODERS); 
    	}
    }
}
