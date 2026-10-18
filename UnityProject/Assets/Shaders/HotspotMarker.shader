// Camera-facing hotspot marker for the tiger showcase: bright core dot, thin ring and a pulse wave expanding outward.
// Drawn after transparents with ZTest Always at reduced alpha behind geometry, so markers on the far side stay findable.
Shader "TerraRemastered/HotspotMarker"
{
    Properties
    {
        _Color ("Colour", Color) = (0.45, 0.95, 1.0, 1)
        _Size ("Size (m)", Float) = 0.06
        _Hover ("Hover 0-1", Range(0, 1)) = 0
        _Fade ("Fade 0-1", Range(0, 1)) = 1
        _Phase ("Pulse phase", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Marker"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _Size, _Hover, _Fade, _Phase;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 screen : TEXCOORD1; float eyeDepth : TEXCOORD2; };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v);
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float s = _Size * (1.0 + 0.35 * _Hover) * 2.6;                       // quad covers the pulse wave
                float3 ws = center + (right * (v.uv.x - 0.5) + up * (v.uv.y - 0.5)) * s;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv * 2 - 1;
                o.screen = ComputeScreenPos(o.positionCS);
                o.eyeDepth = -TransformWorldToView(center).z;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float r = length(i.uv) * 2.6;                                         // in units of _Size
                float core = smoothstep(0.42, 0.30, r);
                float ring = smoothstep(0.10, 0.0, abs(r - (0.85 + 0.15 * _Hover))) ;
                float t = frac(_Time.y * 0.6 + _Phase);
                float wave = smoothstep(0.12, 0.0, abs(r - (0.9 + t * 1.6))) * (1 - t);
                float a = core * 1.4 + ring * 0.9 + wave * 0.7;
                // dimmer when hidden behind the tiger (scene depth closer than the marker)
                float sceneEye = LinearEyeDepth(SampleSceneDepth(i.screen.xy / i.screen.w), _ZBufferParams);
                float hidden = sceneEye < i.eyeDepth - 0.04 ? 0.3 : 1.0;
                return half4(_Color.rgb * a * _Fade * hidden * (1.0 + _Hover), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
