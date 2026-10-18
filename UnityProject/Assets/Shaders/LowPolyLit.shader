// Low-poly surface shader (terrain, rocks, ridges, mountains, props) for URP Forward/Forward+.
// The look comes from light, not texture: flat facet normals from screen derivatives, crisp Lambert key light (no
// wrap) so every face gets its own value, a small per-facet value/hue jitter (hash of the facet normal: coplanar faces
// stay equal), shadowed/ambient light pulled toward a cool tint (warm sun, cool shade), a faint top-down value gradient,
// matte with a soft sheen. Baked GI: lightmaps (directional) or probes, shadowmask, SSAO, fog. Meta pass for baking.
Shader "TerraRemastered/LowPolyLit"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.5, 0.5, 0.5, 1)
        _FacetJitter ("Facet value jitter", Range(0, 0.2)) = 0.07
        _HueJitter ("Facet hue jitter", Range(0, 0.1)) = 0.02
        _FacetScale ("Facet hash resolution", Float) = 97
        _ShadeTint ("Shade tint (multiplies ambient)", Color) = (0.86, 0.92, 1.08, 1)
        _TopLight ("Top faces brighter", Range(0, 0.3)) = 0.06
        _Sheen ("Sheen", Range(0, 1)) = 0.12
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _ShadeTint;
            float _FacetJitter, _HueJitter, _TopLight, _Sheen, _Cull, _FacetScale;
        CBUFFER_END
        float Hash3(float3 p) { p = frac(p * float3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return frac((p.x + p.y) * p.z); }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SoftShadows.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 lightmapUV : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fog : TEXCOORD1;
                float2 lightmapUV : TEXCOORD2; float3 normalWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.lightmapUV = v.lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (dot(n, i.normalWS) < 0) n = -n;                                   // keep the authored side
                if (!front) n = -n;
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);

                // per-facet variation: same random for every pixel of a face (and for coplanar neighbours)
                // nearly flat terrain needs a finer resolution (its faces differ by small tilts only)
                float h = Hash3(round(n * _FacetScale) + 0.37);
                float h2 = Hash3(round(n * _FacetScale * 0.63) + 11.1);
                half3 albedo = _BaseColor.rgb * (1.0 + (h - 0.5) * 2.0 * _FacetJitter);
                albedo *= 1.0 + float3(h2 - 0.5, 0, 0.5 - h2) * 2.0 * _HueJitter;     // warm/cool nudge
                albedo *= 1.0 + _TopLight * saturate(n.y);

            #if defined(LIGHTMAP_ON)
                half3 gi = SampleLightmap(i.lightmapUV, 0, n);
                half4 shadowMask = SAMPLE_SHADOWMASK(i.lightmapUV);
            #else
                half3 gi = SampleSH(n);
                half4 shadowMask = unity_ProbesOcclusion;
            #endif
                gi *= _ShadeTint.rgb;
                Light light = GetMainLight();
                float shadow = SoftMainLightShadowBaked(i.positionWS, n, i.positionCS.xy, shadowMask, light.direction);
            #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                gi *= ao.indirectAmbientOcclusion; shadow *= ao.directAmbientOcclusion;
            #endif
                float ndl = saturate(dot(n, light.direction));
                half3 col = albedo * (gi + light.color * ndl * shadow);
                float3 hv = normalize(light.direction + viewDir);
                col += light.color * pow(saturate(dot(n, hv)), 16.0) * _Sheen * 0.15 * shadow;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz); float3 nws = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - ws);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, ld));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(v.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (dot(n, i.normalWS) < 0) n = -n;
                return half4(n, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature EDITOR_VISUALIZATION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
            float4 vert(Attributes v) : SV_POSITION { return UnityMetaVertexPosition(v.positionOS.xyz, v.uv1, v.uv2); }
            half4 frag() : SV_Target { UnityMetaInput m = (UnityMetaInput)0; m.Albedo = _BaseColor.rgb; return UnityMetaFragment(m); }
            ENDHLSL
        }
    }
    FallBack Off
}
