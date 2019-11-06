Shader "Unlit/DebrisTest"
{
	Properties {
		_MainTex ("Particle Texture", 2D) = "white" {}
		_Scale ("Particle Scale", Float) = 1.0
	}

	SubShader
	{
		Tags { "IgnoreProjector"="True" "RenderType"="Opaque" "PreviewType"="Plane" }
        LOD 250
        ZWrite On
        Cull Back
        Lighting Off
        Fog { Mode Off }

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#pragma target 5.0
			
			#include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
			};
			
			StructuredBuffer<float2> _Positions;
			StructuredBuffer<uint> _Alive;
			float _Scale;
			
			v2f vert (appdata v, uint instanceID : SV_InstanceID)
			{
				v.vertex.xy *= _Scale;

				uint idx = _Alive[instanceID];
				v.vertex.xy += _Positions[idx];

				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv,_MainTex);
				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				clip(0.5 - length(i.uv - 0.5));
				return tex2D(_MainTex, i.uv);
			}
			ENDCG
		}
	}
}