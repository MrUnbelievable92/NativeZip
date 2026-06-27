using MaxMath;
using MaxMath.Intrinsics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static MaxMath.math;

namespace NativeZip
{
    [StructLayout(LayoutKind.Sequential)]
    unsafe internal struct Optimal
    {
    	public uint4 Backs;

    	public uint BackPrev2;
    	public uint BackPrev;

        private uint BitField;

    	public uint PosPrev
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
                return BitField & bitmask32(13u);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (constexpr.IS_TRUE(value == 0))
                {
                    BitField &= ~bitmask32(13u);
                }

                BitField = value | (BitField & ~bitmask32(13u));
            }
        }
    	public uint PosPrev2
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
                return BitField >> (32 - 13);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (constexpr.IS_TRUE(value == 0))
                {
                    BitField &= ~bitmask32(13u, 32u - 13u);
                }

                BitField = (value << (32 - 13)) | (BitField & ~bitmask32(13u, 32u - 13u));
            }
        }
    	public Base.State State
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
                return new Base.State { Index = (byte)((BitField >> 15) & bitmask32(4u)) };
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (constexpr.IS_TRUE(value.Index == 0))
                {
                    BitField &= ~bitmask32(4u, 15u);
                }

                BitField = ((uint)value.Index << 15) | (BitField & ~bitmask32(4u, 15u));
            }
        }
    
    	public bool Prev1IsChar
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
                return testbit(BitField, 13);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (constexpr.IS_CONST(value))
                {
                    if (value)
                    {
                        BitField |= 1u << 13;
                    }
                    else
                    {
                        BitField &= ~(1u << 13);
                    }
                }
                
                BitField = (touint(value) << 13) | (BitField & ~bitmask32(1u, 13u));
            }
        }
    	public bool Prev2
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
                return testbit(BitField, 14);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (constexpr.IS_CONST(value))
                {
                    if (value)
                    {
                        BitField |= 1u << 14;
                    }
                    else
                    {
                        BitField &= ~(1u << 14);
                    }
                }
                
                BitField = (touint(value) << 14) | (BitField & ~bitmask32(1u, 14u));
            }
        }

        public readonly bool Prev1IsChar_AND_Prev2
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return (bitmask32(2u, 13u) & BitField) == bitmask32(2u, 13u);
            }
        }
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public void MakeAsChar() { BackPrev = uint.MaxValue; Prev1IsChar = false; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
    	public void MakeAsShortRep() { BackPrev = 0; Prev1IsChar = false; }

    	public readonly bool IsShortRep => BackPrev == 0;
    }
}
