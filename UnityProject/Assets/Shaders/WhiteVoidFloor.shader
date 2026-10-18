// Floor of the abstract white showcase room: near-white, lit by the main light with realtime shadows (contact shadow
// under the tiger), faint concentric rings and radial lines around the origin that fade out into the white horizon.
Shader "TerraRemastered/WhiteVoidFloor"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.93, 0.94, 0.95, 1)
        _LineColor ("Line colour", Color) = (0.62, 0.70, 0.74, 1)
        _HorizonColor ("Horizon", Color) = (0.97, 0.975, 0.98, 1)
        _FadeStart ("Fade start (m)", Float) = 6
        _FadeEnd ("Fade end (m)", Float) = 38
        _ShadowStrength ("Shadow strength", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SoftShadows.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _LineColor, _HorizonColor; float _FadeStart, _FadeEnd, _ShadowStrength;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            V vert(A v) { V o; o.positionWS = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }
            float LineAA(float x, float w) { float d = fwidth(x); return 1 - smoothstep(w - d, w + d, abs(frac(x + 0.5) - 0.5)); }
            half4 frag(V i) : SV_Target
            {
                float2 p = i.positionWS.xz;
                float r = length(p);
                float rings = LineAA(r / 1.5, 0.012) * 0.55 + LineAA(r / 6.0, 0.006) * 0.45;
                float spokes = LineAA(atan2(p.y, p.x) / 6.2831853 * 24, 0.008 * max(1, 6 / max(r, 0.5))) * smoothstep(1.5, 4, r) * 0.35;
                float lines = saturate(rings + spokes) * (1 - smoothstep(_FadeStart * 0.5, _FadeEnd * 0.6, r));
                half3 col = lerp(_BaseColor.rgb, _LineColor.rgb, lines * 0.5);
                Light l = GetMainLight();
                float sh = lerp(1, SoftMainLightShadow(i.positionWS, float3(0, 1, 0), i.positionCS.xy, half4(1, 1, 1, 1), l.direction), _ShadowStrength);
            #if defined(_SCREEN_SPACE_OCCLUSION)
                sh *= lerp(1, GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS)).indirectAmbientOcclusion, 0.6);
            #endif
                col *= sh;
                float fade = smoothstep(_FadeStart, _FadeEnd, length(i.positionWS - _WorldSpaceCameraPos));
                return half4(lerp(col, _HorizonColor.rgb, fade), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; };
            float4 vert(A v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; };
            float4 vert(A v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half4 frag() : SV_Target { return half4(0, 1, 0, 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
