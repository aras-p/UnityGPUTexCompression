# Realtime DXT/BCn compression, in Unity, on the GPU

Small testbed to see how compute shaders can be used to do texture compression _on the GPU_ in Unity. Basically the steps are as such:

1. Input is any 2D texture (regular texture, render texture etc.) that the GPU can sample.
2. We'll need a temporary `RenderTexture` that is 4x smaller than the destination texture on each axis, i.e. each "pixel" in it is one BCn block.
   Format of the texture is `GraphicsFormat.R32G32_SInt` (64 bits) for DXT1/BC1, and `GraphicsFormat.R32G32B32A32_SInt` (128 bits) otherwise. We'll want to
   make it writable from a compute shader by setting `enableRandomWrite=true`.
3. Output is same sized (plus any padding to be multiple-of-4 size) `Texture2D` using one of compressed formats (DXT1/BC1, DXT5/BC3 etc.).
   We only need it to exist on the GPU, so create Texture2D with `TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate`
   flags to save some time, and call `Apply(false, true)` on it; the last argument ditches the CPU side memory copy.
4. A compute shader reads input texture from step 1, does {whatever GPU texture compression you do}, and writes into the "one pixel per BCn block"
   temporary texture from step 2.
5. Now we must copy from temporary "one pixel per BCn block" texture (step 2) into actual destination texture (step 3). `Graphics.CopyTexture`
   or `CommandBuffer.CopyTexture` with just source and destination textures *will not work* (since that one checks "does width and height match",
   which they don't - they differ 4x on each axis).
   But, `Graphics.CopyTexture` (or CommandBuffer equivalent) that takes `srcElement` and `dstElement` arguments (zeroes for the largest mip level)
   *does work*!
7. Profit!


Actual BCn compressor codes are from other projects, under `GPUTexCompression/External`:

* `AMD_Compressonator`: [AMD Compressonator](https://github.com/GPUOpen-Tools/compressonator/tree/master/cmp_core/shaders), rev 7d929e9 (2023 Jan 26).
* `FastBlockCompress`: [Microsoft Xbox ATG](https://github.com/microsoft/Xbox-ATG-Samples/tree/main/XDKSamples/Graphics/FastBlockCompress/Shaders), rev 180fa6d
  (2018 Dec 14).

