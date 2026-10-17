// A stencil as the host sees it: a translucent tint that shows where the editor's stencils sit
// without hiding what stands behind them. A stencil with a texture of its own (a depth proxy's
// captured frame, an occlusion mesh's atlas) shows it through _MainTex, set per renderer. Only the
// host's own cameras render it; a Mikan camera swaps the stencil to its pass material around its
// renders.
Shader "MikanXR/StencilPreview"
{
    Properties
    {
        _Color ("Color", Color) = (0.2, 0.8, 1.0, 0.35)
        _MainTex ("Texture", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Preview"
            Cull [_Cull]
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

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
                float shade : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;

                // Faces turned toward the viewer read brighter, so a box keeps its shape
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 viewDir = normalize(WorldSpaceViewDir(v.vertex));
                o.shade = 0.6 + 0.4 * abs(dot(worldNormal, viewDir));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 albedo = tex2D(_MainTex, i.uv).rgb;
                return fixed4(_Color.rgb * albedo * i.shade, _Color.a);
            }
            ENDCG
        }
    }
}
