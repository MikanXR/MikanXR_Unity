// Packs the rendering camera's depth into the RGBA8 encoding the MikanXR editor reads as
// PACK_DEPTH_RGBA: linear eye depth normalized between the camera's near and far planes,
// clamped below 1, spread over four bytes. The editor's own depth mask writes stencils the same
// way, so a Unity surface and a stencil at one distance compare equal in the compositor.
Shader "MikanXR/PackDepthRGBA"
{
    Properties
    {
        _MikanZNear ("Near", Float) = 0.1
        _MikanZFar ("Far", Float) = 20.0
        [Toggle] _MikanFlipY ("Flip Y", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float _MikanZNear;
            float _MikanZFar;
            float _MikanFlipY;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                if (_MikanFlipY > 0.5)
                {
                    o.uv.y = 1.0 - o.uv.y;
                }
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                float eyeDepth = LinearEyeDepth(rawDepth);
                float zNorm = (eyeDepth - _MikanZNear) / (_MikanZFar - _MikanZNear);
                zNorm = clamp(zNorm, 0.0, 0.999999);

                float4 encoded = frac(float4(1.0, 255.0, 65025.0, 16581375.0) * zNorm);
                encoded -= encoded.yzww * float4(1.0 / 255.0, 1.0 / 255.0, 1.0 / 255.0, 0.0);
                return encoded;
            }
            ENDCG
        }
    }
    Fallback Off
}
