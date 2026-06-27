using Unity.Collections.LowLevel.Unsafe;

namespace NativeZip
{
    unsafe internal struct SourceData
    {
        [NativeDisableContainerSafetyRestriction]
        internal void* _ptr;
        internal long _numBytes;
    }
}
