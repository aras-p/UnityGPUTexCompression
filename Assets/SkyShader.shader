Shader "Skybox/Custom"
{
SubShader
{
    Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
    Cull Off ZWrite Off
    Pass
    {
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag

        #include "UnityCG.cginc"

        struct appdata_t {
            float4 vertex : POSITION;
        };

        struct v2f {
            float4 vertex : SV_POSITION;
            float3 uv : TEXCOORD0;
        };

        v2f vert (appdata_t v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.uv = v.vertex.xyz;
            return o;
        }

        half4 frag (v2f i) : SV_Target
        {
            float3 dir = i.uv * 0.5 + 0.5;
            return half4(frac(dir * 30), frac(dir.x*50+dir.z*50));
        }
        ENDCG
    }
}

Fallback Off
}
