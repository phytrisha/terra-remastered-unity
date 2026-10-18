// Fake god-ray card: additive, soft across its width, fading toward the bottom and when seen edge-on.
Shader "TerraRemastered/LightShaft"
{
    Properties
    {
        _Color ("Colour", Color) = (1.0, 0.92, 0.7, 1)
        _Intensity ("Intensity", Float) = 0.18
        _EdgeFade ("Edge-on fade power", Float) = 1.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _Intensity, _EdgeFade;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float facing : TEXCOORD1; };
            V vert(A v)
            {
                V o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                o.facing = abs(dot(n, normalize(_WorldSpaceCameraPos - ws)));
                o.uv = v.uv;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float across = sin(i.uv.x * 3.14159);
                across *= across;
                float along = smoothstep(0.0, 0.3, i.uv.y) * (1.0 - smoothstep(0.45, 0.95, i.uv.y));
                float m = across * along * pow(saturate(i.facing), _EdgeFade) * _Intensity;
                return half4(_Color.rgb * m, 0);
            }
            ENDHLSL
        }
    }
}
