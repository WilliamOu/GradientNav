Shader "Custom/SafetyWallNoZTest"
{
    Properties
    {
        _MainColor ("Glow Color", Color) = (1, 0, 0, 1) 
        _Radius ("Head Radius (Meters)", Float) = 0.5 
        _HandRadius ("Hand Radius (Meters)", Float) = 0.2
        _Softness ("Edge Softness", Range(0.01, 1.0)) = 0.2
        
        _PlayerPos ("Head Position", Vector) = (0,0,0,0)
        _LHandPos ("Left Hand Position", Vector) = (0,0,0,0)
        _RHandPos ("Right Hand Position", Vector) = (0,0,0,0)
    }
    SubShader
    {
        // Force it to draw over UI (which is layer 3990)
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" }
        
        ZTest Always 
        ZWrite Off    
        Blend SrcAlpha OneMinusSrcAlpha 
        Cull Off 

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata 
            { 
                float4 vertex : POSITION; 
                UNITY_VERTEX_INPUT_INSTANCE_ID 
            };

            struct v2f 
            { 
                float4 vertex : SV_POSITION; 
                float3 worldPos : TEXCOORD0;
                // So it renders in VR
                UNITY_VERTEX_OUTPUT_STEREO 
            };

            float4 _MainColor;
            float _Radius;      // Head Radius
            float _HandRadius;  // Hand Radius
            float _Softness;
            
            float4 _PlayerPos;
            float4 _LHandPos;
            float4 _RHandPos;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o); 
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); 

                // 1. Calculate Distances
                float dHead = distance(i.worldPos, _PlayerPos.xyz);
                float dLeft = distance(i.worldPos, _LHandPos.xyz);
                float dRight = distance(i.worldPos, _RHandPos.xyz);

                // 2. Calculate Alpha Contributions Independently
                //    (If distance < radius, alpha goes up)
                float alphaHead = 1.0 - smoothstep(_Radius - _Softness, _Radius, dHead);
                
                // Combine hand distances (we can just take the closest hand first)
                float closestHand = min(dLeft, dRight);
                float alphaHand = 1.0 - smoothstep(_HandRadius - _Softness, _HandRadius, closestHand);

                // 3. Combine: Take the maximum visibility required by either head or hands
                float finalAlphaMask = max(alphaHead, alphaHand);
                
                float4 finalColor = _MainColor;
                finalColor.a *= finalAlphaMask;
                
                return finalColor;
            }
            ENDCG
        }
    }
}