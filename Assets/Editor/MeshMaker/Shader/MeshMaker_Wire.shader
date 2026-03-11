Shader "Hidden/MeshMaker/PreviewWireframe"
{
    Properties
    {
        _BaseColor("Base Color (Transparent)", Color) = (0.2, 0.2, 0.2, 0.3)
        _WireColor("Wire Color", Color) = (0, 1, 0, 1)
        _WireThickness("Wire Thickness", Range(0, 1)) = 0.95
    }
        SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
            };

            struct v2g {
                float4 pos : SV_POSITION;
            };

            struct g2f {
                float4 pos : SV_POSITION;
                float3 barycentric : TEXCOORD0;
            };

            float _WireThickness;
            fixed4 _BaseColor;
            fixed4 _WireColor;

            v2g vert(appdata v) {
                v2g o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            [maxvertexcount(3)]
            void geom(triangle v2g i[3], inout TriangleStream<g2f> triangleStream) {
                g2f o;
                o.pos = i[0].pos;
                o.barycentric = float3(1, 0, 0);
                triangleStream.Append(o);

                o.pos = i[1].pos;
                o.barycentric = float3(0, 1, 0);
                triangleStream.Append(o);

                o.pos = i[2].pos;
                o.barycentric = float3(0, 0, 1);
                triangleStream.Append(o);
            }

            fixed4 frag(g2f i) : SV_Target {
                float3 unitWidth = fwidth(i.barycentric);
                float3 edge = smoothstep(float3(0,0,0), unitWidth * _WireThickness * 2.0, i.barycentric);
                float minEdge = min(min(edge.x, edge.y), edge.z);

                return lerp(_WireColor, _BaseColor, minEdge);
            }
            ENDCG
        }
    }
}