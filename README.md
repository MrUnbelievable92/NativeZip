# NativeZip
A Unity.Burst compatible, auto-jobified implementation of the LZMA SDK.
Note: NativeZip does not produce .7z archives. It compresses data using its own binary format.

# Motivation
While several ZIP libraries are available for C#, they typically execute their compression and decompression logic as managed code and rely on external native DLLs (often limited to particular platforms) for the performance-critical work. A Unity Burst-compatible implementation enables native execution speed while integrating seamlessly with the Unity Job System.

In addition, the original LZMA SDK leaves opportunities for further parallelization during the initialization stages of both compression and decompression, and contains several performance-related inefficiencies. NativeZip addresses these limitations through additional parallelism and targeted optimizations.

Finally, unlike most ZIP compression libraries, NativeZip exposes the full set of compression parameters, allowing fine-grained control over the behavior of the compression algorithm.

# Usage
Within the root namespace `NativeZip` you have two types available to you:
- `ZipCompressed`, as the `Unity.Collections.NativeContainer`, handling this library's API (requires manual memory management via `Dispose()` and `Dispose(JobHandle)`).
- `CompressionSettings` with detailed XML documentation for all of the available compression parameters

To compress data, first Create a `ZipCompressed` instance:
```csharp
public ZipCompressed(Allocator allocator, CompressionSettings settings = default, long initialCapacity = 128)
```
Then you are able to compress your data with the following methods:
```csharp
public JobHandle Compress(void* data, long numBytes, JobHandle inputDeps)
```
```csharp
public JobHandle Compress(NativeArray<byte> data, JobHandle inputDeps)
```
```csharp
public void Compress(void* data, long numBytes)
```
```csharp
public void Compress(NativeArray<byte> data)
```
Note:

- The variants not using `JobHandle` are jobified variants that enforce immediate completion.
- Once a compression has been requested, you will not be able to compress anything else with that particular instance of `ZipCompressed`.

Unfortunately, there is no common interface for any `NativeCollection`, which is the reason for only exposing native pointers and native byte arrays to the API. Fortunately, you can easily convert any custom collection to a byte array with the following code snippet:
```csharp
void* dataPtr = myNativeContainer.GetUnsafeReadOnlyPtr();
int numBytes = sizeof(T) * myNativeContainer.Length;
NativeArray<byte> alias = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(dataPtr, numBytes, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS        
NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref alias, NativeArrayUnsafeUtility.GetAtomicSafetyHandle(myNativeContainer));
#endif
```

Once either data has been compressed or once a `ZipCompressed` has been deserialized, you have acceess to the properties...
```csharp
public readonly long DecompressedSizeInBytes
```
... and ...
```csharp
public readonly long CompressedSizeInBytes
```
... allowing you to allocate enough memory for the decompression process.

To decompress a piece of data, use one of the following analogous decompression methods:
```csharp
public JobHandle Decompress(void* dst, JobHandle inputDeps)
```
```csharp
public JobHandle Decompress(NativeArray<byte> dst, JobHandle inputDeps)
```
```csharp
public void Decompress(void* dst)
```
```csharp
public void Decompress(NativeArray<byte> dst)
```
Note:

- The variants not using `JobHandle` are jobified variants that enforce immediate completion.
- You can perform decompression from a single `ZipCompressed` instance as many times as you wish.

Lastly, serialization is implemented and exposed via the following methods:
```csharp
public void Serialize(System.IO.Stream stream)
```
```csharp
public static ZipCompressed Deserialize(System.IO.Stream stream, Allocator allocator)
```
# How To Install This Library

It is highly encouraged to use the Scoped Registries feature for installing NativeZip.

Installing using Scoped Registries:
- Open your Unity project.
- Go to Edit → Project Settings → Package Manager.
- Under Scoped Registries, click + to add a new registry.
- Enter the registry details:

<blockquote>
<ul>
<li>Name: MrUnbelievable</li>
<li>URL: https://registry.npmjs.org</li>
<li>Scopes: com.mrunbelievable</li>
</ul>
</blockquote>

- Click Save.
- Open Window → Package Manager.
- In the package list, select My Registries.
- Install NativeZip from the registry.

## Why use a scoped registry?
- Easy updates – Receive new versions directly through Unity’s Package Manager without manual downloads.
- Version management – Switch, lock, or roll back versions cleanly using Unity’s built-in tooling.
- Cleaner projects – No need to store package files inside your repository or project folder.
- Dependency resolution – Unity automatically handles dependencies and compatibility.
- Team-friendly – Everyone on the project uses the same source and versions with minimal setup.
- Faster setup – Install in a few clicks instead of manually importing or maintaining local packages.
- No IDE clutter – The package source code does not appear in your IDE, keeping your workspace focused on your own project code.

# Donations

If this repository has been valuable to your projects and you'd like to support my work, consider making a donation.

[![donateBTC](https://github.com/MrUnbelievable92/MaxMath/blob/master/donate_bitcoin.png)](https://raw.githubusercontent.com/MrUnbelievable92/MaxMath/master/bitcoin_address.txt)
[![donatePP](https://github.com/MrUnbelievable92/MaxMath/blob/master/donate_paypal.png)](https://www.paypal.com/donate/?hosted_button_id=MARSK3E7WZP9C)
