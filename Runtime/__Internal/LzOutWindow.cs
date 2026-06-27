using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace NativeZip
{
	unsafe internal struct OutWindow
	{
		[NativeDisableUnsafePtrRestriction] 
		private readonly byte* _stream;
		private long _pos;

		public OutWindow(byte* stream)
		{
			_stream = stream;
			_pos = 0;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void CopyBlock(uint distance, uint len)
		{
			long pos = _pos - distance - 1;
			while (len-- != 0)
			{
				_stream[_pos++] = _stream[pos++];
			}
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public readonly byte GetByte(uint distance)
		{
			return _stream[_pos - distance - 1];
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void PutByte(byte b)
		{
			_stream[_pos++] = b;
		}
	}
}
