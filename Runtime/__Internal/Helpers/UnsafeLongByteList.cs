using DevTools;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

using static MaxMath.math;

namespace NativeZip
{
    [NativeContainer]
    unsafe internal struct UnsafeLongByteList : INativeDisposable
    {
        [NativeDisableUnsafePtrRestriction] internal byte* Ptr;
        internal long Length;
        internal long Capacity;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal Allocator Allocator;
#else
        internal readonly Allocator Allocator;
#endif
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal UnsafeLongByteList(Allocator allocator, long initialCapacity)
        {
            Length = 0;
            Capacity = ceillog2(initialCapacity);
            Allocator = allocator;
            Ptr = (byte*)UnsafeUtility.Malloc(initialCapacity, 1, allocator);
        }

        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Realloc(long length)
        {
            byte* newPtr = (byte*)UnsafeUtility.Malloc(length, 1, Allocator);
            UnsafeUtility.MemCpy(newPtr, Ptr, Length);
            UnsafeUtility.Free(Ptr, Allocator);
            
            Ptr = newPtr;
            Capacity = length;
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Grow(long capacity)
        {
            if (capacity > Capacity)
            {
                Realloc(capacity);
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void TrimExcess()
        {
            Realloc(Length);
        }
        
        public readonly void Dispose()
        {
Assert.IsNotNull(Ptr);
Assert.IsGreater((int)Allocator, (int)Allocator.None);
        
            UnsafeUtility.Free(Ptr, Allocator);
        }
        
        public readonly JobHandle Dispose(JobHandle inputDeps)
        {
Assert.IsNotNull(Ptr);
Assert.IsGreater((int)Allocator, (int)Allocator.None);
    
            inputDeps = new DisposeJob(Ptr, Allocator).Schedule(inputDeps);
    
            return inputDeps;
        }
    }
}
