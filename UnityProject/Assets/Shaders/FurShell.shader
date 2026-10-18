// Shell fur for the HD tiger (TigerFur adds one material per shell to its SkinnedMeshRenderer; Unity draws the skinned
// mesh once per extra material and skins it only once). Per shell (_Layer 0..1 = height in the coat):
//   vertex: pushed out along the normal by the hair length, combed along the growth direction (UV1 "FurFlow", in the
//           UV0 tangent frame, so it bends with the skinned tangents) and pulled down a little by gravity, both growing
//           with the square of the height (hairs curve instead of tilting)
//   pixel:  two strand populations (guard hairs + a denser, shorter undercoat) from per-cell hashes in UV0, density
//           corrected per vertex (UV2.x = UV units per metre), each strand tapering to a point; alpha-to-coverage with
//           MSAA for clean edges. Hair length mask in UV2.y (0 = eyes, nose, whiskers, ...)
//   clumps: strands lean toward the centre of a coarser clump cell with height (wet-look clumping of real fur)
//   AO:     baked ambient occlusion in the vertex colour (Blender / Cycles) darkens creases, strongest at the roots
//   light:  colour from the base map (strand-to-strand variation, lighter tips, root occlusion), wrapped diffuse mixed
//           with strand (Kajiya-Kay) diffuse, two shifted Kajiya-Kay specular lobes, translucency when backlit, soft
//           main-light shadows (SoftShadows.hlsl), SH ambient, optional camera rim light (_FurRim*, set by TigerEncounter)
Shader "TerraRemastered/FurShell"
{
    Properties
    {
        _BaseMap ("Base colour", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Layer ("Shell height (0-1)", Range(0, 1)) = 0.5
        _FurLength ("Hair length (m)", Float) = 0.016
        _Density ("Guard hairs per metre (model)", Float) = 380
        _Elongate ("Strand footprint stretch along the flow", Float) = 2.5
        _UnderDensity ("Undercoat multiplier", Float) = 2.3
        _Thickness ("Strand radius at the root (cell)", Range(0.05, 0.7)) = 0.5
        _Comb ("Comb", Float) = 1.6
        _Gravity ("Gravity", Float) = 0.25
        _RootAO ("Root occlusion", Range(0, 1)) = 0.32
        _ColorVar ("Strand colour variation", Range(0, 0.5)) = 0.14
        _TipLighten ("Tip lightening", Range(0, 0.5)) = 0.1
        _Wrap ("Diffuse wrap", Range(0, 1)) = 0.45
        _SpecColor1 ("Primary spec", Color) = (0.32, 0.32, 0.32, 1)
        _SpecShift1 ("Primary shift", Float) = 0.12
        _SpecPow1 ("Primary exponent", Float) = 90
        _SpecColor2 ("Secondary spec (x albedo)", Color) = (0.35, 0.33, 0.3, 1)
        _SpecShift2 ("Secondary shift", Float) = -0.15
        _SpecPow2 ("Secondary exponent", Float) = 18
        _Translucency ("Backlit translucency", Float) = 0.35
        _Clump ("Clumping (tips toward clump centres)", Range(0, 1)) = 0.45
        _ClumpSize ("Clump size (strand cells)", Float) = 2.5
        _BakedAO ("Baked AO strength", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "FurShell"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            AlphaToMask On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SoftShadows.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _BaseColor;
                float _Layer, _FurLength, _Density, _UnderDensity, _Thickness, _Comb, _Gravity, _Elongate;
                half _RootAO, _ColorVar, _TipLighten, _Wrap;
                half4 _SpecColor1, _SpecColor2; float _SpecShift1, _SpecPow1, _SpecShift2, _SpecPow2; half _Translucency;
                half _Clump, _BakedAO; float _ClumpSize;
            CBUFFER_END
            float3 _FurRimDir; half4 _FurRimColor;                     // globals (camera rim light), zero = off

            struct A
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0; float2 flow : TEXCOORD1; float2 data : TEXCOORD2; half4 color : COLOR;
            };
            struct V
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 posWS : TEXCOORD1;
                float3 nWS : TEXCOORD2; float3 strandWS : TEXCOORD3; float2 data : TEXCOORD4; half fog : TEXCOORD5;
                float2 flow : TEXCOORD6; half ao : TEXCOORD7;
            };

            float2 hash22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            V vert(A v)
            {
                V o;
                VertexPositionInputs pi = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs ni = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                float3 flowWS = v.flow.x * ni.tangentWS + v.flow.y * ni.bitangentWS;
                flowWS = dot(flowWS, flowWS) > 1e-6 ? normalize(flowWS) : 0;
                float h = _Layer, len = _FurLength * v.data.y;
                float3 bend = (flowWS * _Comb + float3(0, -1, 0) * _Gravity) * len;
                float3 p = pi.positionWS + ni.normalWS * len * h + bend * h * h;
                o.posWS = p; o.positionCS = TransformWorldToHClip(p);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap); o.nWS = ni.normalWS; o.data = v.data; o.flow = v.flow; o.ao = v.color.r;
                o.strandWS = normalize(ni.normalWS * max(len, 1e-4) + bend * 2.0 * h);          // d(position)/d(h)
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // coverage of one strand population at this height: hashed cell centre, random length, taper to a point
            // uv is first rotated into the flow frame (the flow is given in the UV tangent frame) and stretched along it,
            // so strand footprints are elongated in the growth direction like real coat grain
            half Strands(float2 uv, float2 flow, float cells, float h, float lenMin, float radius, out float rnd)
            {
                float2 along = dot(flow, flow) > 1e-6 ? normalize(flow) : float2(0, 1), across = float2(-along.y, along.x);
                float2 g = float2(dot(uv, across), dot(uv, along) / _Elongate) * cells, cell = floor(g), f = frac(g);
                float2 r2 = hash22(cell);
                rnd = r2.x;
                float2 c = 0.5 + (hash22(cell + 31.7) - 0.5) * 0.55;
                // clumping: toward the jittered centre of a clump cell, more with height (strands meet at their tips)
                float2 kc = (floor(g / _ClumpSize) + 0.25 + 0.5 * hash22(floor(g / _ClumpSize) + 7.3)) * _ClumpSize;
                float2 pull = (kc - cell - c) * saturate(_Clump * h * h * 1.6);
                c += clamp(pull, -0.42, 0.42);                     // stays inside its own cell (else the tip is clipped)
                float strandLen = lerp(lenMin, 1.0, r2.y);
                float d = length(f - c);
                float rad = radius * saturate(1.0 - h / strandLen);
                // pixel footprint in strand cells (analytic: fwidth(d) jumps at cell borders and speckled the edges)
                float fw = max(length(fwidth(g)), 1e-5);
                half cov = (h <= strandLen) ? saturate((rad - d) / fw + 0.5) : 0;
                // strands smaller than ~2 px cannot be resolved: each pixel hit a random strand or gap (salt-and-pepper
                // noise that crawled in motion), so they fade to the population's mean coverage and colour at this height
                float lMean = 0.5 * (max(h, lenMin) + 1.0);
                float alive = h <= lenMin ? 1.0 : saturate((1.0 - h) / (1.0 - lenMin));
                float rMean = radius * saturate(1.0 - h / lMean);
                half mean = saturate(alive * 3.14159 * rMean * rMean * 1.15);
                half resolve = saturate((1.0 / fw - 1.5) / 3.0);
                rnd = lerp(0.5, rnd, resolve);
                return lerp(mean, cov, resolve);
            }

            half4 frag(V i) : SV_Target
            {
                if (i.data.y < 0.02) discard;
                float h = _Layer;
                float cells = _Density / max(i.data.x, 0.05);
                float rA, rB;
                half guard = Strands(i.uv, i.flow, cells, h, 0.55, _Thickness, rA);
                half under = Strands(i.uv + 0.37, i.flow, cells * _UnderDensity, h, 0.25, _Thickness * 0.85, rB) * (h < 0.55 ? 1 : 0);
                half cover = max(guard, under);
                clip(cover - 0.02);
                float rnd = guard >= under ? rA : rB;

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                albedo *= 1.0 + (rnd - 0.5) * 2.0 * _ColorVar;
                albedo = lerp(albedo, 1.0 - (1.0 - albedo) * 0.75, _TipLighten * h * h);       // sun-bleached tips
                half baked = lerp(1.0, i.ao, _BakedAO);                                    // creases from the bake
                half ao = lerp(_RootAO, 1.0, h) * lerp(baked, 1.0, h * 0.5);              // occlusion fades toward the tips

                float3 n = normalize(i.nWS), t = normalize(i.strandWS);
                float3 vdir = normalize(GetWorldSpaceViewDir(i.posWS));
                Light light = GetMainLight();
                float3 l = light.direction;
                half shadow = SoftMainLightShadow(i.posWS, n, i.positionCS.xy, half4(1, 1, 1, 1), l);
                // diffuse: wrapped Lambert on the skin normal, softened toward the strand's own (Kajiya-Kay) term
                half nl = saturate((dot(n, l) + _Wrap) / (1 + _Wrap));
                half tl = dot(t, l);
                half kk = sqrt(saturate(1 - tl * tl));
                half diff = lerp(nl, kk * nl, 0.35);
                // specular: two shifted lobes along the strand
                float3 hv = normalize(l + vdir);
                float3 t1 = normalize(t + n * _SpecShift1), t2 = normalize(t + n * _SpecShift2);
                half th1 = dot(t1, hv), th2 = dot(t2, hv);
                half dirAtten = smoothstep(-1.0, 0.0, tl) * nl;
                half3 spec = (_SpecColor1.rgb * pow(saturate(sqrt(saturate(1 - th1 * th1))), _SpecPow1)
                            + _SpecColor2.rgb * albedo * pow(saturate(sqrt(saturate(1 - th2 * th2))), _SpecPow2)) * dirAtten * (0.35 + 0.65 * h);
                // translucency: hair glows when the light is behind it (strongest at the tips)
                half trans = pow(saturate(dot(vdir, -l)), 4.0) * _Translucency * h;
                half3 lit = (albedo * (diff + trans) + spec) * light.color * shadow;
                half3 amb = SampleSH(n) * albedo;
                // camera rim light (white room): silhouette strands catch a cool backlight
                half rim = pow(1.0 - saturate(dot(n, vdir)), 3.0) * saturate(dot(n, -_FurRimDir) * 0.5 + 0.5) * h;
                half3 col = (lit + amb) * ao + _FurRimColor.rgb * rim * albedo;
                col = MixFog(col, i.fog);
                return half4(col, cover);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
