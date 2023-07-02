using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class TestGPUTexCompression : MonoBehaviour
{
    public Texture m_SourceTexture;
    public EncodeBCn.Format m_Format = EncodeBCn.Format.BC1;
    [Range(0, 1)] public float m_Quality = 0.25f;
    public ComputeShader m_EncodeShader;
    public ComputeShader m_RMSEShader;

    private EncodeBCn m_Encoder;
    private Texture2D m_DestTexture;
    private CommandBuffer m_Cmd;

    private Vector2 m_RMSE = Vector2.zero;
    private GraphicsBuffer m_RMSEBuffer1;
    private GraphicsBuffer m_RMSEBuffer2;

    private EncodeBCn.Format m_CurFormat = EncodeBCn.Format.None;
    private float m_CurQuality = -1;

    public void Start()
    {
        if (m_SourceTexture == null || m_SourceTexture.dimension != TextureDimension.Tex2D)
        {
            Debug.LogWarning($"Source texture is null or not 2D");
            return;
        }
        m_Encoder = new EncodeBCn(m_EncodeShader);

        UpdateDestTexture();

        m_Cmd = new CommandBuffer();
        m_Cmd.name = "GPU BCn Compression";
        UpdateCommandBuffer();
        Camera.main.AddCommandBuffer(CameraEvent.AfterForwardAlpha, m_Cmd);
    }

    public void OnDestroy()
    {
        DestroyImmediate(m_DestTexture);
        m_Cmd?.Dispose(); m_Cmd = null; 
        m_RMSEBuffer1?.Dispose(); m_RMSEBuffer1 = null;
        m_RMSEBuffer2?.Dispose(); m_RMSEBuffer2 = null;
    }

    void UpdateCommandBuffer()
    {
        m_Cmd.Clear();
        m_Encoder.Encode(m_Cmd, m_SourceTexture, m_SourceTexture.width, m_SourceTexture.height, m_DestTexture, m_Format, m_Quality);
        m_Cmd.Blit(m_DestTexture, new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive));
    }

    void UpdateDestTexture()
    {
        int width = m_SourceTexture.width;
        int height = m_SourceTexture.height;
        if (m_Format != EncodeBCn.Format.None)
        {
            width = (width + 3) / 4 * 4;
            height = (height + 3) / 4 * 4;
        }

        GraphicsFormat gfxFormat = m_Format switch
        {
            EncodeBCn.Format.None => m_SourceTexture.graphicsFormat,
            EncodeBCn.Format.BC1 => GraphicsFormat.RGBA_DXT1_SRGB,
            EncodeBCn.Format.BC3 => GraphicsFormat.RGBA_DXT5_SRGB,
            _ => GraphicsFormat.None
        };
        m_DestTexture = new Texture2D(width, height, gfxFormat,
            TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate)
        {
            name = "BCn Compression Target"
        };
        m_DestTexture.Apply(false, true);
    }

    public void Update()
    {
        if (m_DestTexture == null || m_RMSEShader == null)
            return;
        
        if (m_Quality != m_CurQuality || m_Format != m_CurFormat)
        {
            UpdateDestTexture();
            UpdateCommandBuffer();
            m_CurQuality = m_Quality;
            m_CurFormat = m_Format;
        }

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
