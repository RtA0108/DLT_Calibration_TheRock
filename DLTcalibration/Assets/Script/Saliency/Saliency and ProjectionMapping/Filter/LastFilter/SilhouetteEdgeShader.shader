Shader "Hidden/SilhouetteEdgeShader"
{
    Properties {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _EdgeThreshold ("Edge Threshold", Float) = 0.1
    }

    SubShader {
        Tags { "RenderType" = "Opaque" }
        Cull Off ZWrite Off ZTest Always
        Pass {
            Name "SilhouetteEdge"

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _EdgeThreshold;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float3 cL = tex2D(_MainTex, i.uv + float2(-_MainTex_TexelSize.x, 0)).rgb;
                float3 cR = tex2D(_MainTex, i.uv + float2(_MainTex_TexelSize.x, 0)).rgb;
                float3 cT = tex2D(_MainTex, i.uv + float2(0, _MainTex_TexelSize.y)).rgb;
                float3 cB = tex2D(_MainTex, i.uv + float2(0, -_MainTex_TexelSize.y)).rgb;

                float dx = length(cR - cL);
                float dy = length(cT - cB);
                float edge = dx + dy;

                float strong = step(_EdgeThreshold, edge);
                return fixed4(strong, strong, strong, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
