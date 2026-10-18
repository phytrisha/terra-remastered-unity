using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// Raymarched height fog lit by the main light, with its realtime shadows (shafts through the canopy).
/// One fullscreen pass before post-processing (bloom picks up the shafts): reads camera colour + depth + the main-light shadow map, writes a new colour
/// target that becomes the camera colour. Distance haze beyond `maxDistance` stays with RenderSettings fog.
public class VolumetricFogFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Shader shader;
        [Min(0)] public float density = 0.018f;            // extinction per metre at the base height
        public float baseHeight = -5f;                      // densest at/below this height (lake level)
        [Min(0.1f)] public float heightFalloff = 9f;       // metres for density to fall to 1/e above baseHeight
        [Min(0)] public float minDensity = 0.004f;         // floor so canopy air still scatters
        [Min(1)] public float maxDistance = 110f;
        [Range(8, 64)] public int steps = 28;
        [Range(1, 4)] public int downsample = 2;            // march resolution divisor
        [Range(-0.9f, 0.95f)] public float anisotropy = 0.6f;   // forward scattering toward the sun
        [Min(0)] public float sunScale = 1.4f;
        public Color ambient = new(0.55f, 0.66f, 0.62f);    // in-scatter from sky/canopy, linear
        [Min(0)] public float ambientScale = 0.35f;
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public Settings settings = new();
    /// Runtime switch (e.g. the white showcase room): skips the pass without touching the renderer asset.
    public static bool Suppressed;
    /// Cameras that skip the fog (the white showcase room and its portal camera).
    public static readonly System.Collections.Generic.HashSet<Camera> ExcludedCameras = new();
    Material material;
    FogPass pass;

    public override void Create()
    {
        if (!settings.shader) settings.shader = Shader.Find("Hidden/TerraRemastered/VolumetricFog");
        if (settings.shader) material = CoreUtils.CreateEngineMaterial(settings.shader);
        pass = new FogPass { renderPassEvent = settings.passEvent };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!material || settings.density <= 0 || Suppressed) return;
        if (ExcludedCameras.Contains(renderingData.cameraData.camera)) return;
        var type = renderingData.cameraData.cameraType;
        if (type == CameraType.Preview || type == CameraType.Reflection) return;
        pass.Setup(material, settings);
        pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

    class FogPass : ScriptableRenderPass
    {
        static readonly int ParamsA = Shader.PropertyToID("_FogParamsA"), ParamsB = Shader.PropertyToID("_FogParamsB"),
                            Ambient = Shader.PropertyToID("_FogAmbient");
        Material material; Settings s;

        class PassData { public Material material; public TextureHandle source; public TextureHandle fog; }
        static readonly int FogTex = Shader.PropertyToID("_FogTex");

        public void Setup(Material m, Settings settings) { material = m; s = settings; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;

            material.SetVector(ParamsA, new Vector4(s.density, s.baseHeight, 1f / s.heightFalloff, s.minDensity));
            material.SetVector(ParamsB, new Vector4(s.maxDistance, s.steps, s.anisotropy, s.sunScale));
            material.SetColor(Ambient, s.ambient * s.ambientScale);

            var src = resources.activeColorTexture;
            var desc = renderGraph.GetTextureDesc(src);
            desc.clearBuffer = false; desc.msaaSamples = MSAASamples.None; desc.depthBufferBits = DepthBits.None;

            // 1) march at reduced resolution (fog is low frequency; full res cost ~3 ms on the M4)
            var fogDesc = desc;
            fogDesc.name = "_VolumetricFogMarch"; fogDesc.format = GraphicsFormat.R16G16B16A16_SFloat; fogDesc.sizeMode = TextureSizeMode.Explicit;
            fogDesc.width = Mathf.Max(1, desc.width / s.downsample); fogDesc.height = Mathf.Max(1, desc.height / s.downsample);
            fogDesc.filterMode = FilterMode.Bilinear;
            var fog = renderGraph.CreateTexture(fogDesc);
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Volumetric Fog March", out var data))
            {
                data.material = material; data.source = src; data.fog = TextureHandle.nullHandle;
                builder.UseTexture(src);                                             // bound as _BlitTexture (unused)
                builder.UseTexture(resources.cameraDepthTexture);                    // read as _CameraDepthTexture
                if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture);   // sampled via the URP shadow globals
                builder.SetRenderAttachment(fog, 0);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0));
            }

            // 2) composite at full resolution: scene * transmittance + in-scatter
            desc.name = "_VolumetricFogColor";
            var dst = renderGraph.CreateTexture(desc);
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Volumetric Fog Composite", out var data))
            {
                data.material = material; data.source = src; data.fog = fog;
                builder.UseTexture(src);
                builder.UseTexture(fog);
                builder.SetRenderAttachment(dst, 0);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    d.material.SetTexture(FogTex, (RTHandle)d.fog);
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 1);
                });
            }
            resources.cameraColor = dst;
        }
    }
}
