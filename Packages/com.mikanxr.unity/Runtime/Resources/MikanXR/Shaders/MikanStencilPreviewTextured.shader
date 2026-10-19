// A textured stencil as the host sees it: the stencil's own image (a depth proxy's captured frame,
// an occlusion mesh's atlas) drawn opaque and unlit, so it sits in the depth buffer like scene
// geometry and the avatar reads as in front of or behind it. Only the host's own cameras render
// it; a Mikan camera swaps the stencil to its pass material around its renders.
Shader "MikanXR/StencilPreviewTextured"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "Preview"
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            sampler2D _MainTex;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(_Color.rgb * tex2D(_MainTex, i.uv).rgb, 1);
            }
            ENDCG
        }
    }
}
