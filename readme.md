# Realtime DXT/BCn compression, in Unity, on the GPU

Small testbed to see how compute shaders can be used to do texture compression _on the GPU_ in Unity.

![Screenshot](/screenshot.png?raw=true "Screenshot")

### Outline of how to do GPU texture compression:

1. Input is any 2D texture (regular texture, render texture etc.) that the GPU can sample.
2. We'll need a temporary `RenderTexture` that is 4x smaller than the destination texture on each axis, i.e. each "pixel" in it is one BCn block.
   Format of the texture is `GraphicsFormat.R32G32_SInt` (64 bits) for DXT1/BC1, and `GraphicsFormat.R32G32B32A32_SInt` (128 bits) otherwise. We'll want to
   make it writable from a compute shader by setting `enableRandomWrite=true`.
3. Output is same size as input (plus any padding to be multiple-of-4 size) `Texture2D` using one of compressed formats (DXT1/BC1, DXT5/BC3 etc.).
   We only need it to exist on the GPU, so create Texture2D with `TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate`
   flags to save some time, and call `Apply(false, true)` on it; the last argument ditches the CPU side memory copy.
4. A compute shader reads input texture from step 1, does {whatever GPU texture compression you do}, and writes into the "one pixel per BCn block"
   temporary texture from step 2.
5. Now we must copy from temporary "one pixel per BCn block" texture (step 2) into actual destination texture (step 3). `Graphics.CopyTexture`
   or `CommandBuffer.CopyTexture` with just source and destination textures *will not work* (since that one checks "does width and height match",
   which they don't - they differ 4x on each axis).
   But, `Graphics.CopyTexture` (or CommandBuffer equivalent) that takes `srcElement` and `dstElement` arguments (zeroes for the largest mip level)
   *does work*!
7. Profit! 📈

### What is in this project:

Project is based on Unity 2022.3.4. There's one scene that renders things, compresses the rendered result and displays it on screen. The display on screen also shows
the difference (multiplied 2x) between original and compressed, as well as alpha channel and difference of that between original and compressed.

Actual GPU texture compressors are just code taken from external projects, under `GPUTexCompression/External`:

* `AMD_Compressonator`: [AMD Compressonator](https://github.com/GPUOpen-Tools/compressonator/tree/master/cmp_core/shaders), rev 7d929e9 (2023 Jan 26), MIT license. BC1 and BC3
  compression with a tunable quality level.
* `FastBlockCompress`: [Microsoft Xbox ATG](https://github.com/microsoft/Xbox-ATG-Samples/tree/main/XDKSamples/Graphics/FastBlockCompress/Shaders), rev 180fa6d
  (2018 Dec 14), MIT license. BC1 and BC3 compression (ignores quality setting).

It is extremely likely that better real-time compute shader texture compressors are possible, the two above are just the ones I found that were already written in HLSL. There's also [Betsy](https://github.com/darksylinc/betsy) but that one is written in GLSL, and possibly some others. This example is not so much about compressor
itself, but rather "how to plug that into Unity".


Timings for compression of 1280x720 image into BC3 format on several configurations I tried:

|  | GeForce 3080 Ti (D3D11, D3D12, Vulkan) | Apple M1 Max (Metal) |
|:--- |:--- |:--- |
|XDK  | 0.01ms, RMSE 3.877, 2.006 | 0.01ms, RMSE 3.865, 1.994 |
|AMD q<0.5 | 0.01ms, RMSE 3.562, 2.006 | 0.17ms, RMSE 3.563, 1.994 |
|AMD q<0.8 | 0.01ms, RMSE 2.817, 2.006 | 0.83ms, RMSE 2.819, 1.994 |
|AMD q<=1  | 3.10ms, RMSE 2.544, 1.534 | 117ms😲, RMSE 2.544, 1.524 |

On Apple/Metal the AMD compressor at "high" quality level is _astonishingly_ slow when using the default (FXC) HLSL shader compiler. However, switching
to a more modern DXC shader compiler `#pragma use_dxc metal` does not work at all, gives a "Error creating compute pipeline state: Compiler
encountered an internal error" failure when the compute shader is actually used. Fun!



