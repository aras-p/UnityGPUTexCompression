Shader "Unlit/Blit Copy Show Alpha"
{
    Properties
    {
        _SourceTex ("Source", 2D) = "" {}
        _MainTex ("Texture", 2D) = "" {}
    }
    SubShader {
        Pass {
            ZTest Always Cull Off ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            Texture2D _SourceTex;
            Texture2D _MainTex;
            SamplerState my_point_clamp_sampler;
            float4 _MainTex_ST;

            struct appdata_t {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv.xy, _MainTex);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 col = _MainTex.Sample(my_point_clamp_sampler, i.uv);
                half4 src = _SourceTex.Sample(my_point_clamp_sampler, i.uv);
                
                float diag = i.uv.x * 2;
                if (diag >= 0.8 && diag < 1.0)
                    col.rgb = abs(col.rgb - src.rgb) * 2;
                if (diag >= 1.0 && diag < 1.2)
                    col.rgb = col.a;
                if (diag >= 1.2 && diag < 1.4)
                    col.rgb = abs(col.a - src.a) * 2;
                return col;
            }
            ENDCG

        }
    }
    Fallback Off
}
