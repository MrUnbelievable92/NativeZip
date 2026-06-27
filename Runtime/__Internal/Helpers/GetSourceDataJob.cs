using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace NativeZip
{
    [BurstCompile]
    unsafe internal struct GetSourceDataJob : IJob
    {
        [NativeDisableUnsafePtrRestriction]
        internal SourceData* _srcData;
        [ReadOnly]
        internal NativeArray<byte> _src;

        public void Execute()
        {
            _srcData->_ptr = _src.GetUnsafeReadOnlyPtr();
            _srcData->_numBytes = _src.Length;
        }
    }
}
