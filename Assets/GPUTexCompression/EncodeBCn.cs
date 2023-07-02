using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class EncodeBCn
{
    public enum Format
    {
        None,
        BC1,
        BC3,
    }
    
    static readonly int s_Prop_Source = Shader.PropertyToID("_Source");
    static readonly int s_Prop_Target = Shader.PropertyToID("_Target");
    static readonly int s_Prop_Quality = Shader.PropertyToID("_Quality");
    static readonly int s_Prop_EncodeBCn_Temp = Shader.PropertyToID("_EncodeBCn_Temp");

    private ComputeShader m_EncodeShader;
    private int m_EncodeKernelBC1;
    private int m_EncodeKernelBC3;

    public EncodeBCn(ComputeShader encodeShader)
    {
        Assert.IsNotNull(encodeShader);
        m_EncodeShader = encodeShader;
        m_EncodeKernelBC1 = m_EncodeShader.FindKernel("EncodeBC1");
        m_EncodeKernelBC3 = m_EncodeShader.FindKernel("EncodeBC3");
    }

    public void Encode(CommandBuffer cmb,
        RenderTargetIdentifier source, int sourceWidth, int sourceHeight,
        RenderTargetIdentifier target,
        Format format, float quality)
    {
        if (format == Format.None)
        {
            cmb.CopyTexture(source, 0, target, 0);
            return;
        }
        int tempWidth = (sourceWidth + 3) / 4;
        int tempHeight = (sourceHeight + 3) / 4;
        var desc = new RenderTextureDescriptor
        {
            width = tempWidth,
            height = tempHeight,
            graphicsFormat = format == Format.BC1 ? GraphicsFormat.R32G32_SInt : GraphicsFormat.R32G32B32A32_SInt,
            dimension = TextureDimension.Tex2D,
            enableRandomWrite = true,
            msaaSamples = 1
        };
        int kernelIndex = format == Format.BC1 ? m_EncodeKernelBC1 : m_EncodeKernelBC3;
        cmb.GetTemporaryRT(s_Prop_EncodeBCn_Temp, desc);
        cmb.SetComputeFloatParam(m_EncodeShader, s_Prop_Quality, quality);
        cmb.SetComputeTextureParam(m_EncodeShader, kernelIndex, s_Prop_Source, source);
        cmb.SetComputeTextureParam(m_EncodeShader, kernelIndex, s_Prop_Target, s_Prop_EncodeBCn_Temp);
        cmb.DispatchCompute(m_EncodeShader, kernelIndex, tempWidth, tempHeight, 1);
        cmb.CopyTexture(s_Prop_EncodeBCn_Temp, 0, target, 0);
        cmb.ReleaseTemporaryRT(s_Prop_EncodeBCn_Temp);
    }
}
