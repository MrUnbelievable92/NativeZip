using System;
using Unity.Burst.CompilerServices;
using DevTools;

namespace NativeZip
{
	public enum MatchFinder : byte
	{
		BT2 = 2,
		BT4 = 4,
	};

    public struct CompressionSettings : IEquatable<CompressionSettings>
	{
		private byte _DictionarySize;
		private byte _PositionStateBits;
		private byte _LiteralContextBits;
		private byte _LiteralPositionBits;
		private ushort _FastBytes;
		private MatchFinder _MatchFinder;

		/// <summary>	The log2 of the sliding window size in bytes used by LZMA to find repeated byte sequences.
		/// <para>	Larger values can improve compression for data with long-distance repetitions.		</para>
		/// <para>	Minimum: 12; Maximum: 30; Default Value: 22.	</para>
		/// </summary>
		public int DictionarySize 
		{ 
			[return: AssumeRange(12L, 30L)]
			readonly get => _DictionarySize;
			set
			{
Assert.IsBetween(value, 12, 30);

				_DictionarySize = (byte)value;
			}
		}
		
		/// <summary>	The number of low-order bits of the current byte position that LZMA uses to form a small integer called the position state. That position state selects which copy of certain probability tables the coder uses.
		/// <para>	If the data has a small-period structure e.g. records with fixed layout, headers every N bytes, repeating fields, or any format where the probability of a match/length depends on position mod N then having different probability models for different position state values lets the compressor learn those position-dependent patterns and encode them more efficiently. 	</para>
		/// <para>	Larger values lead to linearly growing memory requirements and have a small negative impact on compression and decompression speed. 	</para>
		/// <para>	Minimum: 0; Maximum: 4; Default Value: 2.	</para>
		/// </summary>
		public int PositionStateBits
		{ 
			[return: AssumeRange(0L, 4L)]
			readonly get => _PositionStateBits;
			set
			{
Assert.IsBetween(value, 0, 4);

				_PositionStateBits = (byte)value;
			}
		}

		/// <summary>	The number of high-order bits of the previous decoded byte used to select the literal sub-model (i.e. the literal context). Controls how many different contexts the literal coder uses based on the recent byte value.
		/// <para>	Larger values can improve compression for data where the previous byte strongly predicts the next byte. </para>
		/// <para>	Larger values have exponentially growing memory requirements and slightly slower encoding and decoding speeds.  </para>
		/// <para>	Minimum: 0; Maximum: 8; Default Value: 3.	</para>
		/// </summary>
		public int LiteralContextBits
		{ 
			[return: AssumeRange(0L, 8L)]
			readonly get => _LiteralContextBits;
			set
			{
Assert.IsBetween(value, 0, 8);

				_LiteralContextBits = (byte)value;
			}
		}

		/// <summary>	The number of low-order bits of the current position included in the literal context.
		/// <para>	The literal coder predicts the next byte based on what came just before and where in the stream it is. Larger values allow the compressor to learn different byte distributions at different positions. This is useful when a stream has a small-period structure: e.g. fixed-size records, columns, headers at fixed offsets, or other position-dependent patterns.		</para>
		/// <para>	Larger values have linearly growing memory requirements and a small negative effect on compression and decompression speed.		</para>
		/// <para>	Minimum: 0; Maximum: 4; Default Value: 0.	</para>
		/// </summary>
		public int LiteralPositionBits
		{ 
			[return: AssumeRange(0L, 4L)]
			readonly get => _LiteralPositionBits;
			set
			{
Assert.IsBetween(value, 0, 4);

				_LiteralPositionBits = (byte)value;
			}
		}

		/// <summary>	The encoders fast path will attempt to find matches and extend them up to <see cref="FastBytes"/> bytes when considering match candidates. Raising this value makes the encoder search for longer matches with the fast search logic; lowering it makes the encoder consider only shorter matches in that fast stage.
		/// <para>	Larger values have a considerably negative impact on compression and decompression speed, with negligible memory cost.	</para>
		/// <para>	Minimum: 5; Maximum: 273; Default Value: 32.	</para>
		/// </summary>
		public int FastBytes
		{ 
			[return: AssumeRange(5L, 273L)]
			readonly get => _FastBytes;
			set
			{
Assert.IsBetween(value, 5, 273);

				_FastBytes = (ushort)value;
			}
		}
		
		/// <summary>	The binary-tree match finder algorithm used to find matches in the dictionary.
		/// <para>	<see cref="MatchFinder.BT4"/> generally offers the best compression ratio because it finds good long matches, but uses more memory and is slower than <see cref="MatchFinder.BT2"/>, which does not put as much effort into finding matches.  	</para>
		/// <para>	Either <see cref="MatchFinder.BT2"/> or <see cref="MatchFinder.BT4"/>; Default Value: <see cref="MatchFinder.BT4"/>.	</para>
		/// </summary>
		public MatchFinder MatchFinder
		{ 
			readonly get => _MatchFinder;
			set
			{
Assert.IsTrue(value == MatchFinder.BT2 || value == MatchFinder.BT4);

				_MatchFinder = value;
			}
		}

		public static CompressionSettings Default => new CompressionSettings
		{
			DictionarySize = 22,
			PositionStateBits = 2,
			LiteralContextBits = 3,
			LiteralPositionBits = 0,
			FastBytes = 32,
			MatchFinder = MatchFinder.BT4
		};

        public readonly bool Equals(CompressionSettings other)
        {
			return this.DictionarySize == other.DictionarySize
				 & this.PositionStateBits == other.PositionStateBits
				 & this.LiteralContextBits == other.LiteralContextBits
				 & this.LiteralPositionBits == other.LiteralPositionBits
				 & this.FastBytes == other.FastBytes
				 & this.MatchFinder == other.MatchFinder;
        }
    }
}
