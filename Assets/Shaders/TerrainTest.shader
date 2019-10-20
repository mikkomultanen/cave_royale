Shader "Unlit/TerrainTest"
{
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _Scale ("Scale", Vector) = (1, 1, 1, 1)
        _Iterations("Iterations", Range(1, 10)) = 3
        _Threshold("Threshold", Range(0, 1)) = 0.5
        _ThresholdAmplitude("Threshold Amplitude", Range(0, 1)) = 0.1
        _Seed("Seed", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
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
            #include "SimplexNoise3D.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Scale;
            float _Iterations;
            float _Threshold;
            float _ThresholdAmplitude;
            float _Seed;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };
            
            v2f vert (appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv,_MainTex) * _Scale.xy;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }
            
            float fractalNoise(float2 position) {
                float o = 0;
                float w = 0.5;
                float s = 1;
                for (int i = 0; i < _Iterations; i++) {
                    float3 coord = float3(position * s, _Seed);
                    float n = abs(snoise(coord));
                    n = 1 - n;
                    n *= n;
                    n *= n;
                    o += n * w;
                    s *= 2.0;
                    w *= 0.5;
                }
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                float n = fractalNoise(i.uv);
                float v = snoise(float3(i.uv, _Seed + 1));
                float b = step(_Threshold + _ThresholdAmplitude * v, n);
                b -= step(i.screenPos.x, 0.01);
                b -= step(0.99, i.screenPos.x);
                b -= step(i.screenPos.y, 0.01);
                b -= step(0.99, i.screenPos.y);
                clip(0.5 - b);
                return float4(1,1,1,1);
            }
            ENDCG
        }
    }
}
