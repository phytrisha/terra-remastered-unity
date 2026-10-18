// Low-poly foliage / bark for URP (Forward / Forward+): flat facet normals turned toward the viewer (two-sided leaves),
// wrap diffuse, back-light translucency, received shadows, fog, and vertex wind that grows with height above the
// object pivot. Bark and leaves of one plant must share the wind values so the crown never detaches from the trunk.
// Baked GI: probe-lit (ambient from light probes, baked sun shadows beyond the realtime shadow distance from probe
// occlusion / shadowmask); the Meta pass gives the lightmapper the leaf colour for green bounce light.
Shader "TerraRemastered/LowPolyFoliage"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.35, 0.65, 0.16, 1)
        _Wrap ("Wrap lighting", Range(0, 1)) = 0.35
        _Translucency ("Back-light translucency", Range(0, 2)) = 0.6
        _TransColor ("Translucency tint", Color) = (0.85, 1.0, 0.35, 1)
        _AmbientBoost ("Ambient boost", Range(0.5, 2)) = 1.1
        _WindStrength ("Wind (m per m of height)", Float) = 0.012
        _WindSpeed ("Wind speed", Float) = 0.9
        _Flutter ("Leaf flutter (m)", Float) = 0.015
        _Smoothness ("Sheen", Range(0, 1)) = 0.25
        [Toggle] _TwoSided ("Two sided", Float) = 1
        _FacetJitter ("Facet value jitter", Range(0, 0.2)) = 0.06
        _ShadeTint ("Shade tint (multiplies ambient)", Color) = (0.86, 0.92, 1.08, 1)
        [Toggle] _CastRealtime ("Cast realtime shadows (off: baked only, e.g. crowns)", Float) = 1
        _ShadowHoles ("Shadow holes (0 = solid)", Range(0, 0.8)) = 0
        _HoleScale ("Shadow hole size (1/m)", Float) = 0.8
        // read by the Progressive lightmapper only: RGB = share of light passing through (leaves, crowns), black = opaque
        [NoScaleOffset] _TransparencyLM ("Lightmapper transmission", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _TransColor, _ShadeTint;
            float _Wrap, _Translucency, _AmbientBoost, _WindStrength, _WindSpeed, _Flutter, _Smoothness, _TwoSided, _FacetJitter, _ShadowHoles, _HoleScale, _CastRealtime;
        CBUFFER_END
        // gust fronts (globals from WindGusts; 0 = calm sway only): xy downwind dir, z band frequency (rad/m), w strength
        float4 _GustParams; float _GustTime;

        float Hash3(float3 p) { p = frac(p * float3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return frac((p.x + p.y) * p.z); }
        // smooth 3D value noise (two octaves) for the perforated crown shadows
        float VNoise(float3 p)
        {
            float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
            float n000 = Hash3(i), n100 = Hash3(i + float3(1,0,0)), n010 = Hash3(i + float3(0,1,0)), n110 = Hash3(i + float3(1,1,0));
            float n001 = Hash3(i + float3(0,0,1)), n101 = Hash3(i + float3(1,0,1)), n011 = Hash3(i + float3(0,1,1)), n111 = Hash3(i + float3(1,1,1));
            return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
        }
        float HoleNoise(float3 p) { return VNoise(p) * 0.65 + VNoise(p * 2.3 + 7.1) * 0.35; }

        float3 Wind(float3 posWS)
        {
            float3 origin = UNITY_MATRIX_M._m03_m13_m23;
            float h = max(0.0, posWS.y - origin.y);
            float t = _Time.y * _WindSpeed;
            float ph = dot(origin.xz, float2(0.37, 0.21));
            float gust = 0.65 + 0.35 * sin(t * 0.21 + ph * 0.15);
            // a front passing: narrow band of stronger sway that travels downwind, plants lean with it
            float front = pow(saturate(sin(dot(origin.xz, _GustParams.xy) * _GustParams.z - _GustTime)), 6.0) * _GustParams.w;
            gust *= 1.0 + front;
            float sway = (sin(t + ph) + 0.35 * sin(t * 2.7 + ph * 1.3)) * gust;
            float3 o = float3(sway, 0, sway * 0.45) * _WindStrength * h;
            o += float3(_GustParams.x, 0, _GustParams.y) * front * _WindStrength * h * 0.9;
            float fl = sin(t * 4.1 + dot(posWS, float3(1.7, 1.1, 2.3))) * _Flutter * saturate(h * 2.0) * gust;
            o += float3(fl * 0.5, fl, fl * 0.4);
            return posWS + o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SoftShadows.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fog : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 ws = Wind(TransformObjectToWorld(v.positionOS.xyz));
                o.positionWS = ws; o.positionCS = TransformWorldToHClip(ws); o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 n = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (dot(n, viewDir) < 0) n = -n;                       // the visible side faces the viewer
                half4 shadowMask = SAMPLE_SHADOWMASK(float2(0, 0));   // probe occlusion (not lightmapped)
                Light light = GetMainLight();
                float shadow = SoftMainLightShadowBaked(i.positionWS, n, i.positionCS.xy, shadowMask, light.direction);
                float ndl = dot(n, light.direction);
                float diff = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                float trans = saturate(-ndl) * _Translucency * pow(saturate(dot(-viewDir, light.direction) * 0.5 + 0.5), 2.0);
                // per-facet value jitter (constant over a face): faces read as separate planes, like folded paper
                half3 albedo = _BaseColor.rgb * (1.0 + (Hash3(round(n * 97.0) + 0.37) - 0.5) * 2.0 * _FacetJitter);
                half3 ambient = SampleSH(n) * _AmbientBoost * _ShadeTint.rgb;
            #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                ambient *= ao.indirectAmbientOcclusion; shadow *= ao.directAmbientOcclusion;
            #endif
                half3 col = albedo * (ambient + light.color * diff * shadow);
                col += albedo * _TransColor.rgb * light.color * trans * shadow;
                float3 hv = normalize(light.direction + viewDir);
                col += light.color * pow(saturate(dot(n, hv)), 24.0) * _Smoothness * 0.25 * shadow;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = Wind(TransformObjectToWorld(v.positionOS.xyz));
                o.positionWS = ws;
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
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
                o.positionCS = cs; return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                // perforated crowns: irregular gaps let light through as soft dapples (the crown itself stays solid)
                if (_CastRealtime < 0.5) clip(-1);                              // baked-only casters (crowns)
                if (_ShadowHoles > 0) clip(HoleNoise(i.positionWS * _HoleScale) - _ShadowHoles);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { Varyings o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformWorldToHClip(Wind(TransformObjectToWorld(v.positionOS.xyz))); return o; }
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = Wind(TransformObjectToWorld(v.positionOS.xyz)); o.positionCS = TransformWorldToHClip(o.positionWS); return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (dot(n, _WorldSpaceCameraPos - i.positionWS) < 0) n = -n;
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
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = UnityMetaVertexPosition(v.positionOS.xyz, v.uv1, v.uv2); return o; }
            half4 frag(Varyings i) : SV_Target
            {
                UnityMetaInput m = (UnityMetaInput)0;
                m.Albedo = _BaseColor.rgb;
                return UnityMetaFragment(m);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
