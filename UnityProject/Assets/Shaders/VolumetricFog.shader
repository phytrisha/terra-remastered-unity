// Raymarched height fog for VolumetricFogFeature: exponential height density, main-light in-scattering
// (Henyey-Greenstein) with realtime shadows -> visible shafts through the canopy, plus a flat ambient term.
//   Pass 0 "March"     (half resolution): RGB = in-scattered light, A = transmittance
//   Pass 1 "Composite" (full resolution): scene * T + scatter, fog texture sampled bilinearly
// Per-pixel interleaved-gradient jitter of the step start hides banding at ~28 steps.
Shader "Hidden/TerraRemastered/VolumetricFog"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "March"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

            float4 _FogParamsA;   // density, base height, 1/falloff, min density
            float4 _FogParamsB;   // max distance, steps, anisotropy g, sun scale
            float4 _FogAmbient;

            // height mist + a thin floor of canopy haze that fades out above the treetops (sky rays stay clear)
            float Density(float y)
            {
                float floorD = _FogParamsA.w * saturate((32.0 - y) / 14.0);
                return max(floorD, _FogParamsA.x * exp(-max(0.0, y - _FogParamsA.y) * _FogParamsA.z));
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float rawDepth = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool sky = rawDepth <= 1e-6;
            #else
                bool sky = rawDepth >= 1.0 - 1e-6;
            #endif
                float3 cam = _WorldSpaceCameraPos;
                float3 ray = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP) - cam;
                float len = max(length(ray), 1e-4);
                float3 dir = ray / len;
                float dist = sky ? _FogParamsB.x : min(len, _FogParamsB.x);

                int steps = (int)_FogParamsB.y;
                float dt = dist / steps;
                float jitter = InterleavedGradientNoise(i.positionCS.xy, 0);

                Light sun = GetMainLight();
                float g = _FogParamsB.z, cosT = dot(dir, sun.direction);
                float phase = (1.0 - g * g) / pow(max(1e-3, 1.0 + g * g - 2.0 * g * cosT), 1.5);   // HG * 4pi (isotropic = 1)
                float3 sunL = sun.color * (_FogParamsB.w * phase);

                float T = 1.0; float3 scatter = 0;
                for (int k = 0; k < steps; k++)
                {
                    float3 p = cam + dir * ((k + jitter) * dt);
                    float a = exp(-Density(p.y) * dt);
                    float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                    scatter += T * (1.0 - a) * (sunL * shadow + _FogAmbient.rgb);
                    T *= a;
                }
                return half4(scatter, T);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            TEXTURE2D_X(_FogTex);
            half4 Frag(Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, i.texcoord);
                half4 fog = SAMPLE_TEXTURE2D_X(_FogTex, sampler_LinearClamp, i.texcoord);
                return half4(col.rgb * fog.a + fog.rgb, col.a);
            }
            ENDHLSL
        }
    }
}
