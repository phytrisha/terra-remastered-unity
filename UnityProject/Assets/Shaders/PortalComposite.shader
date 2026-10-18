// Full-screen UI composite for the tiger's portal: shows the white room (portal camera texture, opaque) inside a
// circle centred on the leaping tiger, with a living energy rim; outside the circle the jungle stays visible.
// Used on a RawImage of a screen-space overlay canvas (TigerEncounter / TigerPortalFx).
//   rim: wobbling edge (two noise octaves), bright core + soft halo with travelling streaks, glints sprayed outward
//   inside near the rim: refraction ripple and a chromatic split of the room image, fading toward the centre
//   _Flash: white-cyan burst over the inside when the rim passes the tiger (the low-poly -> HD swap)
Shader "Hidden/TerraRemastered/PortalComposite"
{
    Properties
    {
        _MainTex ("Portal view", 2D) = "white" {}
        _Center ("Centre (px)", Vector) = (0, 0, 0, 0)
        _Radius ("Radius (px)", Float) = 0
        _Glow ("Rim glow", Float) = 1
        _Flash ("Flash", Float) = 0
        _RimColor ("Rim colour", Color) = (0.75, 0.97, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _Center; float _Radius, _Glow, _Flash; float4 _RimColor;
            struct A { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A v) { V o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y);
            }

            float4 frag(V i) : SV_Target
            {
                float2 px = i.uv * _ScreenParams.xy;
                float2 d2 = px - _Center.xy;
                float d = length(d2);
                float ang = atan2(d2.y, d2.x);
                float t = _Time.y;
                // wobbling edge: noise around the circle (wraps: sampled on the unit circle), scaled with the radius
                float2 ring = float2(cos(ang), sin(ang));
                float wob = (vnoise(ring * 2.2 + t * 1.7) - 0.5) * 2.0 + (vnoise(ring * 6.0 - t * 3.1) - 0.5) * 0.8;
                float R = _Radius + wob * (5.0 + _Radius * 0.025) * step(1.0, _Radius);
                float inside = saturate((R - d) / 2.0 + 0.5);                        // ~2 px antialiased edge

                // refraction ripple + chromatic split near the rim (inside), fading toward the centre
                float edge = exp(-max(0, R - d) / (18.0 + R * 0.06));
                float2 dir = d2 / max(d, 1.0);
                float ripple = sin((R - d) * 0.09 - t * 9.0) * edge;
                float2 uvOff = dir * (ripple * 6.0 + edge * 10.0) / _ScreenParams.xy;
                float2 ca = dir * edge * 7.0 / _ScreenParams.xy;
                float3 room;
                room.r = tex2D(_MainTex, i.uv - uvOff + ca).r;
                room.g = tex2D(_MainTex, i.uv - uvOff).g;
                room.b = tex2D(_MainTex, i.uv - uvOff - ca).b;

                // rim: tight bright core + soft halo, modulated by travelling streaks
                float streak = 0.55 + 0.45 * sin(ang * 9 + t * 7) * sin(ang * 4 - t * 11);
                float core = exp(-abs(d - R) / 2.5);
                float halo = exp(-max(0, d - R) / 22.0) * step(R, d) * 0.55 + exp(-max(0, R - d) / 10.0) * step(d, R) * 0.35;
                // glints: cells along the rim (angle x outward distance) that flare, drifting outward over time
                float n = 90.0;
                float cell = floor((ang / 6.2832 + 0.5) * n);
                float outward = (d - R) / 26.0 - t * 2.6 + hash(float2(cell, 7.0)) * 10.0;
                float gc = floor(outward);
                float h = hash(float2(cell, gc));
                float2 gp = float2(frac((ang / 6.2832 + 0.5) * n) - 0.5, frac(outward) - 0.5);
                float glint = step(0.82, h) * exp(-dot(gp, gp) * 60.0) * saturate(1.0 - (d - R) / 160.0) * step(R, d);

                float rim = (core + halo * streak) * _Glow * step(1.0, _Radius);
                float3 col = lerp(_RimColor.rgb * rim * 1.6, room, inside) + _RimColor.rgb * core * _Glow * 0.6 * inside;
                col += _RimColor.rgb * glint * 2.2 * _Glow;
                // flash over the portal interior (strongest at the rim)
                col += lerp(float3(0.85, 0.97, 1.0), _RimColor.rgb, edge) * _Flash * inside * (0.15 + 0.85 * edge);
                return float4(col, saturate(inside + rim + glint * _Glow));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
