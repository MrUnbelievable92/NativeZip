using DevTools;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace NativeZip
{
    [NativeContainer]
    [NativeContainerSupportsDeallocateOnJobCompletion]
    [StructLayout(LayoutKind.Sequential)]
    unsafe public struct ZipCompressed : INativeDisposable
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
internal AtomicSafetyHandle m_Safety;
internal static readonly SharedStatic<int> _staticSafetyId = SharedStatic<int>.GetOrCreate<ZipCompressed>();
[NativeSetClassTypeToNullOnSchedule] private DisposeSentinel m_DisposeSentinel;
#endif

        [NativeDisableUnsafePtrRestriction]
        internal UnsafeLongByteList* _data;

        [NativeDisableUnsafePtrRestriction]
        internal SourceData* _srcData; 
        internal CompressionSettings _settings;
        
        private bool _isZipped;
        public bool IsZipped 
        { 
            readonly get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return _isZipped;
            }
            private set
            {
                _isZipped = value;
            }
        }

        public readonly long DecompressedSizeInBytes
        {
            get
            {
Assert.IsTrue(IsZipped);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return *(long*)(_data->Ptr + 1) & 0x0000_FFFF_FFFF_FFFF;
            }
        }

        public readonly long CompressedSizeInBytes
        {
            get
            {
Assert.IsTrue(IsZipped);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return _data->Length;
            }
        }

        public readonly bool IsCreated => _data != null;

        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ZipCompressed(Allocator allocator, CompressionSettings settings = default, long initialCapacity = 128)
        {
Assert.IsNotSmaller(initialCapacity, 0);

            _data = (UnsafeLongByteList*)UnsafeUtility.Malloc(sizeof(UnsafeLongByteList), UnsafeUtility.AlignOf<UnsafeLongByteList>(), allocator);
            *_data = new UnsafeLongByteList(allocator, initialCapacity);
            _srcData = (SourceData*)UnsafeUtility.Malloc(sizeof(SourceData), UnsafeUtility.AlignOf<SourceData>(), allocator);
            _settings = settings;
            _isZipped = false;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 2, allocator);
AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, _staticSafetyId.Data);
#endif
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle Compress(void* data, long bytes, JobHandle inputDeps)
        {
            _srcData->_ptr = data;
            _srcData->_numBytes = bytes;

            return Encoder.Schedule(_settings, this, inputDeps);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle Compress(NativeArray<byte> data, JobHandle inputDeps)
        {
            IsZipped = true;
            inputDeps = new GetSourceDataJob { _srcData = _srcData, _src = data }.Schedule(inputDeps);

            return Encoder.Schedule(_settings, this, inputDeps);
        }

        public void Compress(void* src, long bytes) => Compress(src, bytes, default(JobHandle)).Complete();
        public void Compress(NativeArray<byte> src) => Compress(src, default(JobHandle)).Complete();


        public JobHandle Decompress(void* dst, JobHandle inputDeps)
        {
Assert.IsTrue(IsZipped);

            _srcData->_ptr = dst;

            return Decoder.Schedule(this, inputDeps);
        }

        public JobHandle Decompress(NativeArray<byte> dst, JobHandle inputDeps)
        {
Assert.IsTrue(IsZipped);
Assert.IsNotSmaller(dst.Length, DecompressedSizeInBytes);

            inputDeps = new GetSourceDataJob { _srcData = _srcData, _src = dst }.Schedule(inputDeps);
            
            return Decoder.Schedule(this, inputDeps);
        }

        public void Decompress(void* dst)              => Decompress(dst, default(JobHandle)).Complete();
        public void Decompress(NativeArray<byte> dst)  => Decompress(dst, default(JobHandle)).Complete();
        
        public void TrimExcess()
        {
Assert.IsTrue(IsZipped);

            _data->TrimExcess();
        }

        public readonly void* GetUnsafeReadOnlyPtr()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            return _data->Ptr;
        }

        public void Dispose()
        {
Assert.IsNotNull(_data);

            Allocator allocator = _data->Allocator;
            _data->Dispose();
            UnsafeUtility.Free(_data, allocator);
            UnsafeUtility.Free(_srcData, allocator);
            _data = null;
            _srcData = null;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
Assert.IsNotNull(_data);

            Allocator allocator = _data->Allocator;
            JobHandle dataJob = _data->Dispose(inputDeps);
            JobHandle dataptrJob = new DisposeJob(_data, allocator).Schedule(inputDeps);
            JobHandle srcJob = new DisposeJob(_srcData, allocator).Schedule(inputDeps);
            _data = null;
            _srcData = null;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
            return JobHandle.CombineDependencies(dataJob, dataptrJob, srcJob);
        }

        public void Serialize(Stream stream)
        {
Assert.IsTrue(IsZipped);

            long size = CompressedSizeInBytes;

            for (int i = 0; i < 8; i++)
            {
                stream.WriteByte((byte)(size >> (i * 8)));
            }

            for (long i = 0; i < size; i++)
            {
                stream.WriteByte(_data->Ptr[i]);
            }
        }

        public static ZipCompressed Deserialize(Stream stream, Allocator allocator)
        {
            long size = 0;

            for (int i = 0; i < 8; i++)
            {
                size |= (long)stream.ReadByte() << (i * 8);
            }

            ZipCompressed ret = new ZipCompressed(allocator, initialCapacity: size);
            ret.IsZipped = true;
            ret._data->Length = ret._data->Capacity = size;
            for (long i = 0; i < size; i++)
            {
                ret._data->Ptr[i] = (byte)stream.ReadByte();
            }

            return ret;
        }
    }
}
