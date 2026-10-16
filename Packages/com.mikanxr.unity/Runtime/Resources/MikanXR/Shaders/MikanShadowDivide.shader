// The shadow buffer: the shadowed render divided by the unshadowed one. Pixels the same in both
// (the character, the white background) come out white; a catcher comes out as its shadow
// survival. The epsilon keeps a black pixel from dividing by zero.
Shader "MikanXR/ShadowDivide"
{
    Properties
    {
        _MainTex ("Shadowed", 2D) = "white" {}
        _MikanReferenceTex ("Unshadowed", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _MikanReferenceTex;

            fixed4 frag(v2f_img i) : SV_Target
            {
                const float epsilon = 1.0 / 255.0;
                float3 shadowed = tex2D(_MainTex, i.uv).rgb;
                float3 reference = tex2D(_MikanReferenceTex, i.uv).rgb;
                float3 survival = saturate((shadowed + epsilon) / (reference + epsilon));
                return fixed4(survival, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
