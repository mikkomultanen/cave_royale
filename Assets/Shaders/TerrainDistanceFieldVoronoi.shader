// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "CaveRoyale/Voronoi" {
    Properties 
    {
		_MainTex ("Detail", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    float _TerrainDistanceFieldMultiplier;

    half4 Sample (float2 uv) {
        return tex2D(_MainTex, uv);
    }

    half4 SampleBox (float2 uv, float delta) {
        float4 o = _MainTex_TexelSize.xyxy * float2(-delta, delta).xxyy;
        half4 s =
            Sample(uv + o.xy) + Sample(uv + o.zy) +
            Sample(uv + o.xw) + Sample(uv + o.zw);
        return s * 0.25f;
    }

    float2 Normal (float2 uv) {
        float3 o = float3(_MainTex_TexelSize.xy * 0.5, 0);
        float t = (2 * tex2D(_MainTex, uv + o.zy) - 1) * _TerrainDistanceFieldMultiplier;
        float b = (2 * tex2D(_MainTex, uv - o.zy) - 1) * _TerrainDistanceFieldMultiplier;
        float l = (2 * tex2D(_MainTex, uv - o.xz) - 1) * _TerrainDistanceFieldMultiplier;
        float r = (2 * tex2D(_MainTex, uv + o.xz) - 1) * _TerrainDistanceFieldMultiplier;
        float2 n = float2(r - l, t - b);
        float len = length(n);
        return n / max(0.2, len);
    }

    float Distance (float2 v) {
        v = abs(v);
        if (v.x > 0.9) {
            v.x -= 0.5;
        } 
        if (v.y > 0.9) {
            v.y -= 0.5;
        }
        return length(v);
    }

    ENDCG

    SubShader {
        Tags { "RenderType"="Opaque" }
	   	Cull Back
		Lighting Off
        ZWrite Off
        ZTest Always

        Pass { // 0 init position
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            //Fragment Shader
            half4 frag (v2f_img i) : COLOR {
                fixed value = tex2D (_MainTex, i.uv);
                if (value == 0) {
                    return half4(0,0, i.uv);
                } else {
                    return half4(i.uv, 0, 0);
                }
            }
            ENDCG
        }

        Pass { // 1 JFA step
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            int _Offset;

            //Fragment Shader
            half4 frag (v2f_img i) : COLOR {
                float stepwidth = _MainTex_TexelSize.xy * _Offset;
    
                float bestDistance = 9999.0;
                half2 bestCoord = half2(0, 0);
                
                float bestInnerDistance = 9999.0;
                half2 bestInnerCoord = half2(0, 0);

                half2 seedCoord;
                float2 v;
                float distsq;
                for (int y = -1; y <= 1; ++y) {
                    for (int x = -1; x <= 1; ++x) {
                        float2 sampleCoord = i.uv + float2(x,y) * stepwidth;
                        
                        half4 data = tex2D (_MainTex, sampleCoord);
                        seedCoord = data.xy;
                        v = seedCoord - i.uv;
                        distsq = dot(v, v);
                        if (all(seedCoord) && distsq < bestDistance) {
                            bestDistance = distsq;
                            bestCoord = seedCoord;
                        }
                        seedCoord = data.zw;
                        v = seedCoord - i.uv;
                        distsq = dot(v, v);
                        if (all(seedCoord) && distsq < bestInnerDistance) {
                            bestInnerDistance = distsq;
                            bestInnerCoord = seedCoord;
                        }
                    }
                }

                return half4(bestCoord, bestInnerCoord);
            }
            ENDCG
        }

        Pass { // 2 calculate distance
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 _DistanceScale;
            float _DistanceMultiplier;

            //Fragment Shader
            float4 frag (v2f_img i) : COLOR {
                float4 data = tex2D (_MainTex, i.uv);
                
                float2 bestCoord = data.xy;
                float2 v = (i.uv - bestCoord) * _DistanceScale.zw;
                float d = Distance(v) / _DistanceMultiplier;

                float2 bestInnerCoord = data.zw;
                float2 innerV = (bestInnerCoord - i.uv) * _DistanceScale.zw;
                float2 innerD = Distance(innerV) / _DistanceMultiplier;

                float distance = 0.5 * lerp(1, saturate(d), all(bestCoord)) +
                    0.5 * lerp(0, 1 - saturate(innerD), all(bestInnerCoord));
                
                return float4(distance, 0, 0, 1);
            }
            ENDCG
        }

        Pass { // 3 box filter
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float _BoxOffset;

            //Fragment Shader
            half4 frag (v2f_img i) : COLOR {
                return SampleBox(i.uv, _BoxOffset);
            }
            ENDCG
        }

        Pass { // 4 calculate normal 
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            //Fragment Shader
            half4 frag (v2f_img i) : COLOR {
                float h = tex2D(_MainTex, i.uv);
                return half4(h, 0.5 * Normal(i.uv) + 0.5, 1);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}