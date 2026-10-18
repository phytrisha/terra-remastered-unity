// Sky cloud card: texture painted on black, alpha from brightness. Unlit, no fog, drawn behind distant terrain.
Shader "TerraRemastered/CloudCard"
{
    Properties
    {
        _MainTex ("Cloud (on black)", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _AlphaGain ("Alpha gain", Float) = 1.6
        _HazeColor ("Haze colour", Color) = (0.75, 0.85, 0.95, 1)
        _Haze ("Haze amount", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Unlit"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; half4 _Tint, _HazeColor; float _AlphaGain, _Haze;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = TRANSFORM_TEX(v.uv, _MainTex); return o; }
            half4 frag(V i) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                half a = saturate(max(c.r, max(c.g, c.b)) * _AlphaGain);
                half3 col = lerp(c / max(a, 1e-3), _HazeColor.rgb, _Haze) * _Tint.rgb;
                a *= _Tint.a;
                return half4(col * a, a);       // premultiplied
            }
            ENDHLSL
        }
    }
}
