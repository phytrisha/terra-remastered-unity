// Portal disc for the tiger's leap: swirling bright rim with streaks, inner area brightening to pure white as _Open
// goes 0 -> 1. Unlit, additive over the scene, faces the camera (the object is placed/rotated by TigerEncounter).
Shader "TerraRemastered/Portal"
{
    Properties
    {
        _Color ("Rim colour", Color) = (0.75, 0.95, 1.0, 1)
        _Open ("Open 0-1", Range(0, 1)) = 0
        _Intensity ("Intensity", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Portal"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _Open, _Intensity;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv * 2 - 1; return o; }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            half4 frag(V i) : SV_Target
            {
                float r = length(i.uv), ang = atan2(i.uv.y, i.uv.x);
                float R = lerp(0.05, 0.92, smoothstep(0, 1, _Open));
                float t = _Time.y;
                float swirl = sin(ang * 7 + r * 18 - t * 9) * 0.5 + 0.5;
                float streak = pow(saturate(sin(ang * 23 - t * 14 + r * 6)), 12);
                float rim = smoothstep(0.08, 0.0, abs(r - R)) * (0.7 + 0.6 * swirl);
                float halo = smoothstep(0.35, 0.0, abs(r - R)) * 0.35 * (0.6 + streak);
                // thin veil inside the rim: the tiger stays visible as it leaps through (the HUD flash does the white-out)
                float inner = smoothstep(R, R - 0.15, r) * smoothstep(0.2, 0.9, _Open) * (0.12 + 0.18 * swirl * smoothstep(R * 0.4, R, r));
                float3 col = _Color.rgb * (rim + halo) * _Intensity + inner * 1.2;
                float a = saturate(inner + rim * 0.6 + halo * 0.3) * smoothstep(1.0, 0.85, r);
                return half4(col * smoothstep(1.0, 0.85, r), a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
