// Wide soft main-light shadows for the low-poly shaders (LowPolyLit, LowPolyFoliage, WhiteVoidFloor).
// URP's own filter is a fixed few-texel tent, so the 4096 cascade gives crisp edges; the reference renders have broad
// path-traced penumbrae. Here: 16 Poisson-disk taps (each a hardware 2x2 compare) spread over a disc of radius
// _TRSoftShadowRadius metres (default 0.5) in the plane perpendicular to the light, rotated per pixel (interleaved
// gradient noise) so the taps blend instead of ghosting. Tree crowns cast perforated shadows (LowPolyFoliage
// _ShadowHoles), which this wide filter turns into soft dapples of light on the floor. Realtime/baked mixing is the same as URP's MainLightShadow.
// Include after Lighting.hlsl.
#ifndef TR_SOFT_SHADOWS_INCLUDED
#define TR_SOFT_SHADOWS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

float _TRSoftShadowRadius;   // global (TigerEncounter / SoftShadowSettings); 0 = default below

half SoftMainLightShadow(float3 positionWS, float3 normalWS, float2 pixel, half4 shadowMask, float3 lightDir)
{
    half4 params = GetMainLightShadowParams();
    half realtime = 1;
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    static const float2 disk[16] = {
        float2(-0.942, -0.399), float2(0.946, -0.769), float2(-0.094, -0.929), float2(0.345, 0.294),
        float2(-0.916, 0.458), float2(-0.815, -0.879), float2(-0.383, 0.277), float2(0.975, 0.756),
        float2(0.443, -0.975), float2(0.537, -0.474), float2(-0.265, -0.419), float2(0.792, 0.191),
        float2(-0.242, 0.997), float2(-0.814, 0.914), float2(0.200, 0.786), float2(0.144, -0.141) };
    float R = _TRSoftShadowRadius > 0 ? _TRSoftShadowRadius : 0.5;
    // taps spread in the RECEIVER's plane (a light-plane disc would dip under sloped ground and self-shadow it),
    // lifted a little toward the light so nearby geometry of the same surface doesn't occlude them
    float3 t = normalize(cross(normalWS, abs(normalWS.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0)));
    float3 b = cross(normalWS, t);
    float3 lift = lightDir * (0.03 + R * 0.12);
    float s, c; sincos(InterleavedGradientNoise(pixel, 0) * 6.2831853, s, c);
    float sum = 0;
    [unroll] for (int k = 0; k < 16; k++)
    {
        float2 d = float2(c * disk[k].x - s * disk[k].y, s * disk[k].x + c * disk[k].y) * (R * 0.72);   // disc radius ~1.37: normalise
        float4 sc = TransformWorldToShadowCoord(positionWS + t * d.x + b * d.y + lift);
        sum += (BEYOND_SHADOW_FAR(sc)) ? 1.0 : SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, sc.xyz);
    }
    realtime = LerpWhiteTo(sum / 16.0, params.x);
#endif
    half baked = 1;
#if defined(CALCULATE_BAKED_SHADOWS)
    baked = BakedShadow(shadowMask, _MainLightOcclusionProbes, params);
#endif
    half fade = 1;
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    fade = GetMainLightShadowFade(positionWS);
#endif
    return MixRealtimeAndBakedShadows(realtime, baked, fade);
}

// Baked + realtime: the canopy casts only BAKED shadows (soft penumbra from the sun's angular size, gap pattern from
// its transmission texture); trunks, plants and the tiger also cast realtime. Receivers take the darker of both, so
// static shadows are never doubled and dynamic ones still ground the tiger. Probe-lit objects get their baked part
// from probe occlusion (one value per object, soft by nature).
// Drifting cumulus shadows (globals from CloudShadows; 0 = off): x = 1 / cloud size (1/m), y = strength,
// z / w = fade-in distance from the camera (m): near the viewer the light stays as baked, the hills / lake / mountains
// get slow moving shade. _CloudOffset = drift (m).
float4 _CloudShadow; float2 _CloudOffset;
float CloudHash(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }
float CloudNoise(float2 p)
{
    float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(CloudHash(i), CloudHash(i + float2(1, 0)), f.x), lerp(CloudHash(i + float2(0, 1)), CloudHash(i + 1), f.x), f.y);
}
half CloudShadow(float3 positionWS)
{
    if (_CloudShadow.y <= 0) return 1;
    float2 p = (positionWS.xz + _CloudOffset) * _CloudShadow.x;
    float n = CloudNoise(p) * 0.65 + CloudNoise(p * 2.3 + 5.2) * 0.35;
    float cloud = smoothstep(0.52, 0.68, n);                                    // ~30 % cover, soft edges
    float fade = smoothstep(_CloudShadow.z, _CloudShadow.w, distance(positionWS.xz, _WorldSpaceCameraPos.xz));
    return 1 - cloud * _CloudShadow.y * fade;
}

half SoftMainLightShadowBaked(float3 positionWS, float3 normalWS, float2 pixel, half4 shadowMask, float3 lightDir)
{
    half4 params = GetMainLightShadowParams();
    half realtime = 1;
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    realtime = SoftMainLightShadow(positionWS, normalWS, pixel, half4(1, 1, 1, 1), lightDir);   // realtime only (baked = 1)
#endif
    half baked = 1;
#if defined(CALCULATE_BAKED_SHADOWS)
    baked = BakedShadow(shadowMask, _MainLightOcclusionProbes, params);
#endif
    return min(realtime, baked) * CloudShadow(positionWS);
}
#endif
