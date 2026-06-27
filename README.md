# NativeZip
A Unity.Burst compatible, auto-jobified implementation of the LZMA SDK.
The resulting compression data uses a custom header and is currently not compatible with the 7z file format.

# Motivation
While several ZIP libraries are available for C#, they typically execute their compression and decompression logic as managed code and rely on external native DLLs for the performance-critical work. A Unity Burst-compatible implementation enables native execution speed while integrating seamlessly with the Unity Job System.

In addition, the original LZMA SDK leaves opportunities for further parallelization during the initialization stages of both compression and decompression, and contains several performance-related inefficiencies. NativeZip addresses these limitations through additional parallelism and targeted optimizations.

Finally, unlike most ZIP compression libraries, NativeZip exposes the full set of compression parameters, allowing fine-grained control over the behavior of the compression algorithm.
