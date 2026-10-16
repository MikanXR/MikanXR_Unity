Shader "Custom/RGBAPackDepth"
{
	Properties
	{
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_SceneScale ("Scale applied to the mikan scene", Float) = 1.0
	}
	SubShader
	{
		Pass
		{
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			
			uniform sampler2D _MainTex;
			uniform sampler2D _CameraDepthTexture;
			uniform half4 _MainTex_TexelSize;
			uniform float _SceneScale;

			struct input
			{
				float4 pos : POSITION;
				half2 uv : TEXCOORD0;
			};

			struct output
			{
				float4 pos : SV_POSITION;
				half2 uv : TEXCOORD0;
			};


			output vert(input i)
			{
				output o;
				o.pos = UnityObjectToClipPos(i.pos);
				o.uv = MultiplyUV(UNITY_MATRIX_TEXTURE0, i.uv);

				return o;
			}
			
			fixed4 frag(output o) : COLOR
			{
				float depth = tex2D(_CameraDepthTexture, o.uv).r / _SceneScale;
				float depthNorm = min(Linear01Depth(depth), 0.999999);

				return EncodeFloatRGBA(depthNorm);
			}
			
			ENDCG
		}
	} 
}