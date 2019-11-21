// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "CaveRoyale/TerrainDebug" {
    SubShader {
        Tags 
        { 
            "RenderType"="Opaque"
            "PreviewType"="Plane"
        }
        LOD 250
        ZWrite On
        Cull Back
        Lighting Off
        Fog { Mode Off }

        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

			sampler2D _Terrain;

            //Fragment Shader
            float4 frag (v2f_img i) : COLOR {
                float4 color = tex2D (_Terrain, i.uv);
                clip(color.a - 0.5);
                color.a = 1;
                return color;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}