// Copies a texture of raw bytes (the packed depth, the shadow ratio) into an sRGB render
// texture so its bytes survive: the color channels are written as the exact sRGB decode of
// their stored values, which the target's sRGB write encodes back to the same bytes. Alpha
// is never sRGB converted and passes through. The source is read at its texel centers, so
// it must be the target's size.
Shader "MikanXR/SrgbRawBytes"
{
    Properties
    {
        _MainTex ("Raw", 2D) = "white" {}
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

            // The piecewise sRGB curve, not GammaToLinearSpace's approximation, which is off
            // by more than a byte at the low end
            float3 SrgbToLinearExact(float3 c)
            {
                float3 low = c / 12.92;
                float3 high = pow((c + 0.055) / 1.055, 2.4);
                return lerp(high, low, step(c, 0.04045));
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float4 raw = tex2D(_MainTex, i.uv);
                return float4(SrgbToLinearExact(raw.rgb), raw.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
