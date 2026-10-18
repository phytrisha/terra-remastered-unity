// Low-poly water for URP: vertex waves on a tessellated mesh, flat facet normals from screen derivatives,
// depth-based shallow/deep tint and shoreline foam (needs the camera depth texture), sun glint, fog.
// _BaseFoam > 0 turns it into white water (cascades); _Flow scrolls the waves along a direction (streams).
// Streams also carry UVs (x across 0-1, y metres downstream): _Streaks draws faceted foam streaks travelling with the
// flow (more along the banks); the shore foam laps (its width breathes with noise and time).
Shader "TerraRemastered/LowPolyWater"
{
    Properties
    {
        _ShallowColor ("Shallow (a = alpha)", Color) = (0.36, 0.82, 0.80, 0.55)
        _DeepColor ("Deep (a = alpha)", Color) = (0.06, 0.40, 0.52, 0.95)
        _HorizonColor ("Fresnel / sky reflection", Color) = (0.70, 0.88, 0.95, 1)
        _FoamColor ("Foam", Color) = (0.92, 0.97, 1.0, 1)
        _WaveHeight ("Wave height (m)", Float) = 0.06
        _WaveScale ("Wave scale (1/m)", Float) = 0.7
        _WaveSpeed ("Wave speed", Float) = 0.9
        _Flow ("Flow dir xz, speed w", Vector) = (0, 0, 1, 0)
        _DepthFade ("Depth to full deep colour (m)", Float) = 3.0
        _FoamDist ("Shore foam width (m)", Float) = 0.25
        _BaseFoam ("Base foam (white water)", Range(0, 1)) = 0
        _Gloss ("Glint sharpness", Range(8, 1024)) = 180
        _Spec ("Glint strength", Float) = 1.5
        _Streaks ("Flow streaks (UV meshes)", Range(0, 1)) = 0
        _StreakSpeed ("Streak speed (m/s)", Float) = 0.9
        _Lap ("Shore foam lapping", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "SoftShadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _HorizonColor, _FoamColor;
                float _WaveHeight, _WaveScale, _WaveSpeed;
                float4 _Flow;
                float _DepthFade, _FoamDist, _BaseFoam, _Gloss, _Spec, _Streaks, _StreakSpeed, _Lap;
            CBUFFER_END
            float Hash2(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }
            float VNoise2(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash2(i), Hash2(i + float2(1, 0)), f.x), lerp(Hash2(i + float2(0, 1)), Hash2(i + 1), f.x), f.y); }

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float fog : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            float Wave(float2 p, float t)
            {
                return sin(p.x + t) * 0.5 + sin(p.y * 1.3 - t * 1.2) * 0.35 + sin((p.x + p.y) * 0.7 + t * 0.7) * 0.4
                     + sin((p.x - p.y) * 2.1 - t * 1.9) * 0.15;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float2 p = ws.xz * _WaveScale - _Flow.xz * _Flow.w * _Time.y;
                ws.y += Wave(p, _Time.y * _WaveSpeed) * _WaveHeight;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (n.y < 0) n = -n;
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float shadow = lerp(0.55, 1.0, light.shadowAttenuation) * CloudShadow(i.positionWS);

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float thick = max(0, sceneZ - i.screenPos.w);
                float d = saturate(thick / _DepthFade);

                half3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, d);
                half3 ambient = SampleSH(n);
                col *= ambient * 0.7 + light.color * saturate(dot(n, light.direction)) * shadow * 0.55;
                float fres = pow(1.0 - saturate(dot(n, viewDir)), 4.0);
                col = lerp(col, _HorizonColor.rgb, fres * 0.65);
                float3 h = normalize(light.direction + viewDir);
                col += light.color * pow(saturate(dot(n, h)), _Gloss) * _Spec * light.shadowAttenuation;

                float lap = 1.0 + _Lap * (VNoise2(i.positionWS.xz * 2.2 + _Time.y * 0.35) - 0.5) * 1.4 * (0.75 + 0.25 * sin(_Time.y * 1.3 + i.positionWS.x * 0.8));
                float foam = saturate(1.0 - thick / max(_FoamDist * lap, 1e-3));
                if (_Streaks > 0)
                {   // streaks: cells stretched 8x along the flow, moving downstream; stronger near the banks
                    float2 q = float2(i.uv.x * 9.0, (i.uv.y - _Time.y * _StreakSpeed) * 1.1);
                    float sn = VNoise2(q) * 0.65 + VNoise2(q * 2.3 + 7.1) * 0.35;
                    float bank = 0.55 + 0.45 * pow(abs(i.uv.x * 2.0 - 1.0), 2.0);
                    float st = smoothstep(0.6, 0.66, sn * bank + 0.12) * _Streaks;
                    foam = max(foam, st * 0.7);
                }
                foam = max(foam * foam, _BaseFoam * (0.75 + 0.25 * sin(i.positionWS.y * 9.0 + _Time.y * 6.0)));
                half3 foamCol = _FoamColor.rgb * (ambient * 0.6 + light.color * shadow * 0.6);
                col = lerp(col, foamCol, saturate(foam));

                half a = lerp(_ShallowColor.a, _DeepColor.a, d);
                a = max(a, fres * 0.85);
                a = max(a, saturate(foam));
                col = MixFog(col, i.fog);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
