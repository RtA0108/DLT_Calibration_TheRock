// 카메라에서 본 깊이(뷰 공간 거리)를 색의 R 채널에 기록한다. 배경은 0.
// 안쪽 가림 경계 필터(SaliencyUtils.FilterVerticesByOcclusionEdges)가 깊이 단차를 찾는 데 쓴다.
Shader "Hidden/LinearDepthColor"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off
            Lighting Off
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float depth : TEXCOORD0;
            };

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.depth = o.pos.w; // 원근 투영에서 clip w = 카메라 앞 방향 거리
                return o;
            }

            float4 frag(v2f i) : SV_Target {
                return float4(i.depth, 0, 0, 1);
            }
            ENDCG
        }
    }
}
