// Additive energy shell drawn over the tiger as an extra material during the portal transition (TigerPortalFx).
//   low-poly tiger (_Faceted 1): flat facet normals -> every facet edge catches the fresnel; facets flicker
//   HD tiger (_Faceted 0): smooth fresnel; a scan band sweeps along _ScanDir (head -> tail) and the shell behind it
//   cools off, revealing the fur ("materialize")
// _Intensity 0 = invisible (the material is removed again when the transition ends).
Shader "TerraRemastered/EnergyShell"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.45, 1.6, 2.2, 1)
        _Intensity ("Intensity", Float) = 0
        _Faceted ("Faceted (low-poly)", Float) = 0
        _Push ("Push along normal (m)", Float) = 0.004
        _ScanOrigin ("Scan origin (world)", Vector) = (0, 0, 0, 0)
        _ScanDir ("Scan direction (world)", Vector) = (0, 0, 1, 0)
        _Scan ("Scan position (m along dir, -1 = off)", Float) = -1
        _ScanWidth ("Scan band width (m)", Float) = 0.12
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Shell"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _Intensity, _Faceted, _Push; float4 _ScanOrigin, _ScanDir; float _Scan, _ScanWidth;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; float3 nWS : TEXCOORD1; };

            float hash31(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }

            V vert(A v)
            {
                V o;
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float3 p = TransformObjectToWorld(v.positionOS.xyz) + n * _Push;
                o.posWS = p; o.nWS = n; o.positionCS = TransformWorldToHClip(p);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.posWS));
                float3 flatN = normalize(cross(ddy(i.posWS), ddx(i.posWS)));
                if (dot(flatN, i.nWS) < 0) flatN = -flatN;
                float3 n = normalize(lerp(normalize(i.nWS), flatN, _Faceted));
                float fres = pow(1.0 - saturate(dot(n, viewDir)), 2.2);
                float t = _Time.y;
                // facet / cell flicker: a hash per ~6 cm world cell, re-rolled ~12 times a second
                float3 cellP = floor(i.posWS / 0.06);
                float flick = hash31(cellP + floor(t * 12.0));
                float sparkle = step(0.93, flick) * (0.6 + 0.4 * _Faceted);
                float body = (0.04 + 0.96 * fres) * (0.75 + 0.5 * flick * _Faceted);   // rim-lit, the fur/facets stay readable
                // scan band (s = metres from the tail toward the head; the band runs head -> tail): bright at the band,
                // shell still on where the band has not been yet (tail side), cooled off where it has passed
                float s = dot(i.posWS - _ScanOrigin.xyz, normalize(_ScanDir.xyz));
                float scanOn = step(0.0, _Scan);
                float band = exp(-pow((s - _Scan) / max(_ScanWidth, 1e-3), 2.0)) * scanOn;
                float ahead = lerp(1.0, saturate((_Scan - s) / _ScanWidth + 0.5), scanOn);
                float e = (body * ahead + sparkle * ahead + band * 1.6) * _Intensity;
                return half4(_Color.rgb * e, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
