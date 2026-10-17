// A flat color for the helpers the host's own cameras see, such as the camera frustum lines.
// Shipped with the package so a host build that never included Unity's unlit shaders still has it.
Shader "MikanXR/UnlitColor"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 0, 1)
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

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag() : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
