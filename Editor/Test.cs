using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using MaxMath;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using System.IO;

namespace NativeZip.Tests
{
    [BurstCompile(CompileSynchronously = true)]
    unsafe internal struct CompareEqualJob : IJob
    {
        public NativeArray<byte> src;
        public NativeArray<byte> test;
        public NativeReference<bool> result;

        public void Execute()
        {
            result.Value = true;

            int length = src.Length;
            byte* srcPtr = (byte*)src.GetUnsafeReadOnlyPtr();
            byte* testPtr = (byte*)test.GetUnsafeReadOnlyPtr();

            while (length >= 32)
            {
                if (!(*(byte32*)srcPtr).Equals(*(byte32*)testPtr))
                {
                    result.Value = false;
                    return;
                }

                srcPtr += 32;
                testPtr += 32;
                length -= 32;
            }

            while (length-- != 0)
            {
                if (*srcPtr++ != *testPtr++)
                {
                    result.Value = false;
                    return;
                }
            }
        }
    }

    unsafe public static class Test
    {
        delegate byte ByteStreamGenerator(ref Random32 rng, int index);

        private static CompressionSettings RandomSettings(ref Random32 rng) => new CompressionSettings
        {
            DictionarySize = rng.NextInt(12, 30 + 1),
            PositionStateBits = rng.NextInt(0, 4 + 1),
            LiteralContextBits = rng.NextInt(0, 8 + 1),
            LiteralPositionBits = rng.NextInt(0, 4 + 1),
            FastBytes = rng.NextInt(5, 273 + 1),
            MatchFinder = rng.NextBool() ? MatchFinder.BT4 : MatchFinder.BT2,
        };

        const int MIN_BYTES = 1_000;
        const int MAX_BYTES = 1_000_000;

        [Test]
        [Timeout(int.MaxValue)]
        public static void Same()
        {
            static byte SameGen(ref Random32 rng, int index) => 21;

            Random32 rng = Random32.New;
            BaseTest(SameGen, ref rng);
        }

        [Test]
        [Timeout(int.MaxValue)]
        public static void Rep()
        {
            static byte RepGen(ref Random32 rng, int index) => (byte)(index % 24);
            
            Random32 rng = Random32.New;
            BaseTest(RepGen, ref rng);
        }

        [Test]
        [Timeout(int.MaxValue)]
        public static void Range()
        {
            static byte RangeGen(ref Random32 rng, int index) => (byte)rng.NextInt(11, 44);

            Random32 rng = Random32.New;
            BaseTest(RangeGen, ref rng);
        }

        [Test]
        [Timeout(int.MaxValue)]
        public static void Random()
        {
            static byte RandomGen(ref Random32 rng, int index) => (byte)rng.NextInt();

            Random32 rng = Random32.New;
            BaseTest(RandomGen, ref rng);
        }

        private static void BaseTest(ByteStreamGenerator generator, ref Random32 rng)
        {
            for (int i = 0; i < 4; i++)
            {
                int length = rng.NextInt(MIN_BYTES, MAX_BYTES);

                NativeArray<byte> src = new NativeArray<byte>(length, Allocator.Persistent);
                for (int j = 0; j < length; j++)
                {
                    src[j] = generator(ref rng, j);
                }

                CompressionSettings settings = RandomSettings(ref rng);
                ZipCompressed dst = new ZipCompressed(Allocator.Persistent, settings);

                JobHandle job = default;
                try
                {
                    job = dst.Compress(src, job);
                }
                catch
                {
                    src.Dispose(job);
                    dst.Dispose(job);

                    throw;
                }
                NativeArray<byte> test = new NativeArray<byte>(length, Allocator.Persistent);

                try
                {
                    job.Complete();
                    using (FileStream stream = File.Create("E:/nativezip.bin"))
                    {
                        dst.Serialize(stream);
                    }
                    dst.Dispose();
                    using (FileStream stream = File.OpenRead("E:/nativezip.bin"))
                    {
                        dst = ZipCompressed.Deserialize(stream, Allocator.Persistent);
                    }
                    File.Delete("E:/nativezip.bin");
                    job = dst.Decompress(test, job);
                }
                catch
                {
                    src.Dispose(job);
                    dst.Dispose(job);
                    test.Dispose(job);
                
                    throw;
                }
                
                NativeReference<bool> testResult = new NativeReference<bool>(Allocator.Persistent);
                
                try
                {
                    job = new CompareEqualJob { src = src, test = test, result = testResult }.Schedule(job);
                    job.Complete();
                    Assert.IsTrue(testResult.Value);
                }
                catch
                {
                    string s = string.Empty;
                    for (int k = 0; k < length; k++)
                    {
                        s += src[k].ToString() + "\r\n";
                    }
                    DevTools.GenericExtensions.LogState<CompressionSettings>(settings);
                    System.IO.File.WriteAllText("E:/fail.cs", s);
                 
                    throw;
                 
                }
                finally
                {
                    UnityEngine.Debug.Log(src.Length);
                    UnityEngine.Debug.Log(dst.CompressedSizeInBytes);
                    UnityEngine.Debug.Log("");
                
                    src.Dispose(job);
                    dst.Dispose(job);
                    test.Dispose(job);
                    testResult.Dispose(job);
                }
            }
        }
    }
}