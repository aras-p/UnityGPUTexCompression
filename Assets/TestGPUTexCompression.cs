using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class TestGPUTexCompression : MonoBehaviour
{
    public Texture m_SourceTexture;
    public EncodeBCn.Format m_Format = EncodeBCn.Format.BC3_AMD;
    [Range(0, 1)] public float m_Quality = 0.25f;
    public ComputeShader m_EncodeShader;
    public ComputeShader m_RMSEShader;
    public Material m_BlitMaterial;

    private EncodeBCn m_Encoder;
    private Texture2D m_DestTexture;
    private CommandBuffer m_Cmd;

    private Vector2 m_RMSE = Vector2.zero;
    private GraphicsBuffer m_RMSEBuffer1;
    private GraphicsBuffer m_RMSEBuffer2;

    private EncodeBCn.Format m_CurFormat = EncodeBCn.Format.None;
    private float m_CurQuality = -1;

    private GUIContent[] m_FormatLabels =
        ((EncodeBCn.Format[]) Enum.GetValues(typeof(EncodeBCn.Format))).Select(f => new GUIContent(f.ToString())).ToArray();
    
    readonly FrameTiming[] m_FrameTimings = new FrameTiming[1];
    private float m_GpuTimesAccum = 0;
    private float m_CpuTimesAccum = 0;
    private int m_FramesAccum = 0;
    private float m_AvgFrameTime = 0;

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
        m_Cmd.Blit(m_DestTexture, new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive), m_BlitMaterial);
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
            EncodeBCn.Format.BC1_AMD => GraphicsFormat.RGBA_DXT1_SRGB,
            EncodeBCn.Format.BC1_XDK => GraphicsFormat.RGBA_DXT1_SRGB,
            EncodeBCn.Format.BC3_AMD => GraphicsFormat.RGBA_DXT5_SRGB,
            EncodeBCn.Format.BC3_XDK => GraphicsFormat.RGBA_DXT5_SRGB,
            _ => GraphicsFormat.None
        };
        m_DestTexture = new Texture2D(width, height, gfxFormat,
            TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate)
        {
            name = "BCn Compression Target"
        };
        m_DestTexture.Apply(false, true);
    }

    void CalculateRMSE()
    {
        //if (Time.renderedFrameCount % 16 != 0)
        //    return;

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

        AsyncGPUReadback.Request(m_RMSEBuffer2, request =>
        {
            if (request.done)
            {
                var data = request.GetData<Vector2>();
                Vector2 err = Vector2.zero;
                foreach (var e in data)
                    err += e;

                m_RMSE.x = Mathf.Sqrt(err.x / (m_SourceTexture.width * m_SourceTexture.height)) / 3.0f;
                m_RMSE.y = Mathf.Sqrt(err.y / (m_SourceTexture.width * m_SourceTexture.height));
            }
        });
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

        CalculateFrameTimes();
        CalculateRMSE();
    }

    void CalculateFrameTimes()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, m_FrameTimings) != 1)
            return; // feature is off, or waiting for GPU readback

        // Looks like it reports GPU times as zeroes when GPU load is "high enough", so that's not
        // terribly useful (at least on DX11 / GeForce 3080 Ti). Let's report CPU frame time then :(
        //if (m_FrameTimings[0].gpuFrameTime == 0)
        //    return; // disjoint frames, GPU restart, high GPU load (?) etc.

        if (m_FrameTimings[0].cpuTimeFrameComplete < m_FrameTimings[0].cpuTimePresentCalled)
            return; // sanity check

        m_CpuTimesAccum += (float) m_FrameTimings[0].cpuFrameTime;
        m_GpuTimesAccum += (float) m_FrameTimings[0].gpuFrameTime;
        m_FramesAccum++;
        if (m_GpuTimesAccum > 500 || m_CpuTimesAccum > 500)
        {
            //m_AvgGpuTime = m_GpuTimesAccum / m_FramesAccum;
            m_AvgFrameTime = m_CpuTimesAccum / m_FramesAccum;
            m_CpuTimesAccum = 0.0f;
            m_GpuTimesAccum = 0.0f;
            m_FramesAccum = 0;
        }
    }

    void OnGUI()
    {
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(2,2,2));
        
        GUI.Box(new Rect(5, 5, 510, 120), GUIContent.none, GUI.skin.window);

        // format
        m_Format = (EncodeBCn.Format)GUI.Toolbar(new Rect(10, 10, 500, 20), (int)m_Format, m_FormatLabels);
        
        // quality slider
        DrawLabelWithOutline(new Rect(10, 40, 100, 20), new GUIContent("Quality"));
        int quality = Mathf.RoundToInt(m_Quality * 10);
        m_Quality = GUI.HorizontalSlider(new Rect(60, 45, 100, 20), quality, 0, 10) / 10.0f;
        DrawLabelWithOutline(new Rect(170, 40, 100, 20), new GUIContent(m_Quality.ToString("F1")));
        
        // stats
        var label = $"{m_SourceTexture.width}x{m_SourceTexture.height} {m_Format} q={m_Quality:F1}\nRMSE: RGB {m_RMSE.x:F3}, {m_RMSE.y:F3}\nFrame time: {m_AvgFrameTime:F3}ms ({SystemInfo.graphicsDeviceName} on {SystemInfo.graphicsDeviceType})";
        var rect = new Rect(10, 70, 600, 60);
        DrawLabelWithOutline(rect, new GUIContent(label));
    }

    static void DrawLabelWithOutline(Rect rect, GUIContent label)
    {
        GUI.color = Color.black;
        rect.x -= 1; rect.y -= 1; GUI.Label(rect, label);
        rect.x += 2; GUI.Label(rect, label);
        rect.y += 2; GUI.Label(rect, label);
        rect.x -= 2; GUI.Label(rect, label);
        GUI.color = Color.white;
        rect.x += 1; rect.y -= 1; GUI.Label(rect, label);
    }
}
