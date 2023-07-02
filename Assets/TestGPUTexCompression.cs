using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class TestGPUTexCompression : MonoBehaviour
{
    public Texture m_SourceTexture;
    public ComputeShader m_EncodeShader;
    public ComputeShader m_RMSEShader;
    private RenderTexture m_TempTexture;
    private Texture2D m_DestTexture;
    private CommandBuffer m_Cmd;
    private Vector2 m_RMSE = Vector2.zero;
    private GraphicsBuffer m_RMSEBuffer1;
    private GraphicsBuffer m_RMSEBuffer2;

    public void Start()
    {
        if (m_SourceTexture == null || m_SourceTexture.dimension != TextureDimension.Tex2D)
        {
            Debug.LogWarning($"Source texture is null or not 2D");
            return;
        }

        int width = m_SourceTexture.width;
        int height = m_SourceTexture.height;
        width = (width + 3) / 4 * 4;
        height = (height + 3) / 4 * 4;
        m_DestTexture = new Texture2D(width, height, GraphicsFormat.RGBA_DXT5_SRGB,
            TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate)
        {
            name = "BCn Compression Target"
        };
        m_DestTexture.Apply(false, true);

        m_TempTexture = new RenderTexture(width / 4, height / 4, GraphicsFormat.R32G32B32A32_SInt, GraphicsFormat.None)
        {
            name = "BCn Compression Temp",
            enableRandomWrite = true,
        };
        
        m_Cmd = new CommandBuffer();
        m_Cmd.name = "GPU BCn Compression";
        m_Cmd.SetComputeTextureParam(m_EncodeShader, 0, "_Source", m_SourceTexture);
        m_Cmd.SetComputeTextureParam(m_EncodeShader, 0, "_Target", m_TempTexture);
        m_Cmd.DispatchCompute(m_EncodeShader, 0, m_TempTexture.width, m_TempTexture.height, 1);
        m_Cmd.CopyTexture(m_TempTexture, 0, m_DestTexture, 0);
        m_Cmd.Blit(m_DestTexture, new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive));
        Camera.main.AddCommandBuffer(CameraEvent.AfterForwardAlpha, m_Cmd);
    }

    public void OnDestroy()
    {
        DestroyImmediate(m_DestTexture);
        DestroyImmediate(m_TempTexture);
        m_Cmd?.Dispose(); m_Cmd = null; 
        m_RMSEBuffer1?.Dispose(); m_RMSEBuffer1 = null;
        m_RMSEBuffer2?.Dispose(); m_RMSEBuffer2 = null;
    }

    public void Update()
    {
        if (m_DestTexture == null || m_RMSEShader == null)
            return;

        int rmseFactor = 128;
        int bufferSize1 = m_SourceTexture.width * m_SourceTexture.height / rmseFactor;
        if (m_RMSEBuffer1 == null || m_RMSEBuffer1.count != bufferSize1)
        {
            m_RMSEBuffer1?.Dispose();
            m_RMSEBuffer1 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bufferSize1, 8);
        }
        int bufferSize2 = bufferSize1 / rmseFactor;
        if (m_RMSEBuffer2 == null || m_RMSEBuffer2.count != bufferSize2)
        {
            m_RMSEBuffer2?.Dispose();
            m_RMSEBuffer2 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bufferSize2, 8);
        }

        m_RMSEShader.SetInt("_TextureWidth", m_SourceTexture.width);
        m_RMSEShader.SetTexture(0, "_TextureA", m_SourceTexture);
        m_RMSEShader.SetTexture(0, "_TextureB", m_DestTexture);
        m_RMSEShader.SetBuffer(0, "_BufferOutput", m_RMSEBuffer1);
        m_RMSEShader.Dispatch(0, m_SourceTexture.width/128, m_SourceTexture.height, 1);
        
        m_RMSEShader.SetBuffer(1, "_BufferInput", m_RMSEBuffer1);
        m_RMSEShader.SetBuffer(1, "_BufferOutput", m_RMSEBuffer2);
        m_RMSEShader.Dispatch(1, m_RMSEBuffer1.count/128, 1, 1);

        Vector2[] data = new Vector2[m_RMSEBuffer2.count];
        m_RMSEBuffer2.GetData(data);
        Vector2 err = Vector2.zero;
        foreach (var e in data)
            err += e;

        m_RMSE.x = Mathf.Sqrt(err.x / (m_SourceTexture.width * m_SourceTexture.height)) / 3.0f;
        m_RMSE.y = Mathf.Sqrt(err.y / (m_SourceTexture.width * m_SourceTexture.height));
    }

    public void OnGUI()
    {
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(2,2,2));
        var label = $"RMSE: {m_RMSE.x:F3}, {m_RMSE.y:F3}";
        var rect = new Rect(5, 5, 400, 20);
        GUI.color = Color.black;
        GUI.Label(rect, label);
        rect.x -= 2;
        rect.y -= 2;
        GUI.color = Color.white;
        GUI.Label(rect, label);
    }
}
