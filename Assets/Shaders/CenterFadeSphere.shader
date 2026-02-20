Shader "Custom/CenterFadeSphere"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,1)
        _FadePower ("Fade Sharpness", Range(0.1, 10)) = 2.0
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True"}
        LOD 100
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : NORMAL;
                float3 viewDir : TEXCOORD0;
            };

            float4 _Color;
            float _FadePower;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Normalize the vectors
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(i.viewDir);
                
                // N dot V is 1 at the center, 0 at the grazing edges
                float nDotV = saturate(dot(n, v));
                
                // Apply a power curve to control how fast it fades
                float alphaFade = pow(nDotV, _FadePower);
                
                return fixed4(_Color.rgb, _Color.a * alphaFade);
            }
            ENDCG
        }
    }
}