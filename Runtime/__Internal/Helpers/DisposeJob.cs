using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace NativeZip
{
    [BurstCompile]
    [NativeContainer]
    unsafe internal struct DisposeJob : IJob
    {
        [NativeDisableUnsafePtrRestriction]
        internal void* Ptr;
        internal Allocator Allocator;
    
        internal DisposeJob(void* ptr, Allocator allocator)
        {
            Ptr = ptr;
            Allocator = allocator;
        }
    
        public readonly void Execute()
        {
            UnsafeUtility.Free(Ptr, Allocator);
        }
    }
}
