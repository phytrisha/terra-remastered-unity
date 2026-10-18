// Portal-transition particles (TigerPortalFx), premultiplied HDR: they add light AND slightly cover what is behind, so
// they read both on the jungle and inside the near-white portal room (pure additive vanished there). Colour/alpha from
// the particle:
//   _Mode 0 = sparkle: 4-point star + soft core, twinkling per particle (custom stream TEXCOORD0.z = stable random)
//   _Mode 1 = shard: a flat triangle mesh particle (uv corners (0,0) (1,0) (0,1)) with glowing edges, the low-poly
//             facets breaking off the tiger
//   _Mode 2 = shockwave: a thin energy ring (one big billboard growing over its life) at the swap
Shader "TerraRemastered/Sparkle"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 3
        _Mode ("Mode (0 sparkle, 1 shard, 2 ring)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+30" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) float _Intensity, _Mode; CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 uv : TEXCOORD0; half4 color : COLOR; };
            V vert(A v)
            {
                V o; float3 p = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(p); o.uv = v.uv; o.color = v.color;
                // fade out close to the camera: the camera flies through the trail (screen-filling bloom blobs otherwise)
                o.color.a *= saturate((distance(p, _WorldSpaceCameraPos) - 0.6) / 0.8);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float m;
                if (_Mode < 0.5)
                {
                    float2 p = (i.uv.xy - 0.5) * 2.0;
                    float r = length(p);
                    float core = exp(-r * r * 18.0);
                    float star = exp(-abs(p.x) * 26.0) * saturate(1.0 - abs(p.y)) + exp(-abs(p.y) * 26.0) * saturate(1.0 - abs(p.x));
                    float tw = 0.55 + 0.45 * sin(_Time.y * 23.0 + i.uv.z * 61.0);
                    m = (core * 1.4 + star * 0.8 * tw) * saturate(1.0 - r);
                }
                else if (_Mode > 1.5)
                {
                    float r = length(i.uv.xy - 0.5) * 2.0;
                    m = exp(-pow((r - 0.86) / 0.05, 2.0)) * 1.6 + exp(-pow((r - 0.8) / 0.18, 2.0)) * 0.35;
                }
                else
                {
                    float w = 1.0 - i.uv.x - i.uv.y;
                    float e = min(min(i.uv.x, i.uv.y), w);
                    m = 0.25 + exp(-e * 40.0) * 1.6;
                }
                return half4(i.color.rgb * i.color.a * m * _Intensity, saturate(m * 0.55) * i.color.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
