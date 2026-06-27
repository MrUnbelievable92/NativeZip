using System.Runtime.CompilerServices;
using static MaxMath.math;

namespace NativeZip
{
	unsafe internal static class BitTreeEncoder
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Init(BitEncoder* Models, int NumBitLevels)
		{
			Models[0] = default;
			for (uint i = 1; i < (1 << NumBitLevels); i++)
			{
				Models[i].Init();
			}
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Encode(BitEncoder* Models, int NumBitLevels, ref RangeEncoder rangeEncoder, uint symbol)
		{
			uint m = 1;
			for (int bitIndex = NumBitLevels; bitIndex > 0; )
			{
				bitIndex--;
				uint bit = (symbol >> bitIndex) & 1;
				Models[m].Encode(ref rangeEncoder, bit != 0);
				m = (m << 1) | bit;
			}
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint GetPrice(BitEncoder* Models, int NumBitLevels, uint symbol)
		{
			uint price = 0;
			uint m = 1;
			for (int bitIndex = NumBitLevels; bitIndex > 0; )
			{
				bitIndex--;
				uint bit = (symbol >> bitIndex) & 1;
				price += Models[m].GetPrice(tobool(bit));
				m = (m << 1) + bit;
			}
			return price;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint ReverseGetPrice(BitEncoder* Models, int NumBitLevels, uint startIndex, uint symbol)
		{
			uint price = 0;
			uint m = 1;
			for (int i = NumBitLevels; i > 0; i--)
			{
				uint bit = symbol & 1;
				symbol >>= 1;
				price += Models[startIndex + m].GetPrice(tobool(bit));
				m = (m << 1) | bit;
			}
			return price;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void ReverseEncode(BitEncoder* Models, int NumBitLevels, uint startIndex, ref RangeEncoder rangeEncoder, uint symbol)
		{
			uint m = 1;
			for (int i = 0; i < NumBitLevels; i++)
			{
				uint bit = symbol & 1;
				Models[startIndex + m].Encode(ref rangeEncoder, bit != 0);
				m = (m << 1) | bit;
				symbol >>= 1;
			}
		}
	}

	unsafe static class BitTreeDecoder
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Init(BitDecoder* Models, int NumBitLevels)
		{
			Models[0] = default;
			for (uint i = 1; i < (1 << NumBitLevels); i++)
			{
				Models[i].Init();
			}
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint Decode(ref RangeDecoder rangeDecoder, BitDecoder* Models, int NumBitLevels)
		{
			uint m = 1;
			for (int bitIndex = NumBitLevels; bitIndex > 0; bitIndex--)
			{
				m = (m << 1) + Models[m].Decode(ref rangeDecoder);
			}
			return m - (1u << NumBitLevels);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint ReverseDecode(BitDecoder* Models, uint startIndex, ref RangeDecoder rangeDecoder, int NumBitLevels)
		{
			uint m = 1;
			uint symbol = 0;
			for (int bitIndex = 0; bitIndex < NumBitLevels; bitIndex++)
			{
				uint bit = Models[startIndex + m].Decode(ref rangeDecoder);
				m <<= 1;
				m += bit;
				symbol |= (bit << bitIndex);
			}
			return symbol;
		}
	}
}
