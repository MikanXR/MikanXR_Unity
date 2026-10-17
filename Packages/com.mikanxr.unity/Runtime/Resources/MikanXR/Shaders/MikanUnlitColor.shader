// A flat color for the helpers the host's own cameras see, such as the camera frustum and light
// cone lines: the material color times the vertex color a line renderer carries. Shipped with
// the package so a host build that never included Unity's unlit shaders still has it.
Shader "MikanXR/UnlitColor"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "Color"
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(float4 vertex : POSITION, fixed4 color : COLOR)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.color = color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color * i.color;
            }
            ENDCG
        }
    }
}
