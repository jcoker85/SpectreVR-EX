using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace SpectreEX
{
    // The modern settings behind Shift + Options. Everything here is the remake's own, so it lives apart
    // from GameSettings, which mirrors the original's options. Each value starts from what the project
    // is set to, and a change is saved under its key and applied at once.
    public static class GraphicsOptions
    {
        public const int MinWidth = 640, MinHeight = 480;

        // Keys
        public const string RayTracing = "gfx.rt", Shadows = "gfx.rt.shadows", Skybox = "gfx.skybox", SkyBrightness = "gfx.skybox.brightness",
            Reflections = "gfx.ssr", ReflectionDistance = "gfx.ssr.distance", ReflectionSamples = "gfx.ssr.samples", ReflectionBounces = "gfx.ssr.bounces",
            Lighting = "gfx.gi", LightingDistance = "gfx.gi.distance", LightingSamples = "gfx.gi.samples", LightingBounces = "gfx.gi.bounces",
            Occlusion = "gfx.ao", OcclusionDistance = "gfx.ao.distance", OcclusionSamples = "gfx.ao.samples",
            AntiAliasing = "gfx.aa", TaaQuality = "gfx.taa.quality", DlssMode = "gfx.dlss.mode", DlssSharpness = "gfx.dlss.sharpness";

        public const int AaOff = 0, AaTaa = 1, AaDlss = 2;
        public static readonly string[] AaNames = { "Off", "TAA", "DLSS" };
        public static readonly string[] TaaNames = { "Low", "Medium", "High" };
        public static readonly string[] DlssNames = { "DLAA", "Quality", "Balanced", "Performance", "Ultra Performance" };
        static readonly uint[] DlssLevels = { 4, 2, 1, 0, 3 };                     // NVIDIA.DLSSQuality
        static readonly float[] DlssScale = { 100f, 66.7f, 58f, 50f, 33.3f };      // render scale, percent
        const int CustomQuality = 3;                                               // past the Low-Medium-High tiers

        public static readonly string[] ModeNames = { "Windowed", "Borderless", "Fullscreen" };
        static readonly FullScreenMode[] Modes = { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen };

        const string Ultra = "Ultra";

        static readonly Dictionary<string, int> values = new Dictionary<string, int>();
        static int reflectionTier, lightingTier, occlusionTier, plainSkyType;
        static float occlusionIntensity;
        static HDAdditionalCameraData.ClearColorMode plainClear;

        // The star map. SkyBrightness is its exposure in tenths; high enough and the stars pass the
        // volume's bloom threshold and glow.
        static Cubemap SkyPicture => Resources.Load<Cubemap>("Skybox/Stars");

        public static int Get(string key) => values.TryGetValue(key, out int v) ? v : 0;
        public static bool On(string key) => Get(key) != 0;
        public static bool DlssAvailable => HDDynamicResolutionPlatformCapabilities.DLSSDetected;

        public static void Set(string key, int value)
        {
            values[key] = value;
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
            Apply();
        }

        static void Default(string key, int value) => values[key] = PlayerPrefs.GetInt(key, value);

        // Every size the display offers from 640x480 up, smallest first
        public static List<Vector2Int> Resolutions()
        {
            var sizes = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (size.x >= MinWidth && size.y >= MinHeight && !sizes.Contains(size)) sizes.Add(size);
            }
            sizes.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return sizes;
        }

        public static int ModeIndex => Mathf.Max(0, System.Array.IndexOf(Modes, Screen.fullScreenMode));

        public static void SetResolution(Vector2Int size)
        {
            PlayerPrefs.SetInt("gfx.width", size.x);
            PlayerPrefs.SetInt("gfx.height", size.y);
            PlayerPrefs.Save();
            Screen.SetResolution(size.x, size.y, Screen.fullScreenMode);
        }

        public static void SetMode(int index)
        {
            PlayerPrefs.SetInt("gfx.mode", index);
            PlayerPrefs.Save();
            Screen.fullScreenMode = Modes[index];
        }

        // Ultra is the last level in the project, so existing saved levels keep their numbers, but it
        // leads the list: best first
        public static List<string> QualityNames()
        {
            var names = new List<string>(QualitySettings.names);
            names.Remove(Ultra);
            names.Insert(0, Ultra);
            return names;
        }

        public static string QualityName => QualitySettings.names[QualitySettings.GetQualityLevel()];

        public static void SetQuality(string name)
        {
            int index = System.Array.IndexOf(QualitySettings.names, name);
            PlayerPrefs.SetInt("gfx.quality", index);
            PlayerPrefs.Save();
            ApplyQuality(index);
            Apply();
        }

        // At startup: what was chosen last time, or the project's own settings if nothing was
        public static void Load()
        {
            if (PlayerPrefs.HasKey("gfx.quality")) ApplyQuality(PlayerPrefs.GetInt("gfx.quality"));
            ReadDefaults();
            if (PlayerPrefs.HasKey("gfx.mode")) Screen.fullScreenMode = Modes[Mathf.Clamp(PlayerPrefs.GetInt("gfx.mode"), 0, Modes.Length - 1)];
            if (PlayerPrefs.HasKey("gfx.width")) Screen.SetResolution(PlayerPrefs.GetInt("gfx.width"), PlayerPrefs.GetInt("gfx.height"), Screen.fullScreenMode);
            DynamicResolutionHandler.SetDynamicResScaler(RenderScale, DynamicResScalePolicyType.ReturnsPercentage);
            Apply();
        }

        static float RenderScale() =>
            Get(AntiAliasing) == AaDlss && DlssAvailable ? DlssScale[Mathf.Clamp(Get(DlssMode), 0, DlssScale.Length - 1)] : 100f;

        static void ReadDefaults()
        {
            Default(RayTracing, 1);
            Default(Shadows, 1);
            Default(Skybox, 0);
            Default(SkyBrightness, 100);
            var profile = Profile();
            if (profile != null && profile.TryGet<VisualEnvironment>(out var environment)) plainSkyType = environment.skyType.value;
            if (profile != null && profile.TryGet<ScreenSpaceReflection>(out var reflections))
            {
                reflectionTier = reflections.quality.value;
                Default(Reflections, reflections.enabled.value ? 1 : 0);
                Default(ReflectionDistance, Mathf.RoundToInt(reflections.rayLength));
                Default(ReflectionSamples, reflections.sampleCount.value);
                Default(ReflectionBounces, reflections.bounceCount.value);
            }
            if (profile != null && profile.TryGet<GlobalIllumination>(out var lighting))
            {
                lightingTier = lighting.quality.value;
                Default(Lighting, lighting.enable.value ? 1 : 0);
                Default(LightingDistance, Mathf.RoundToInt(lighting.rayLength));
                Default(LightingSamples, lighting.sampleCount.value);
                Default(LightingBounces, lighting.bounceCount.value);
            }
            if (profile != null && profile.TryGet<ScreenSpaceAmbientOcclusion>(out var occlusion))
            {
                occlusionTier = occlusion.quality.value;
                occlusionIntensity = occlusion.intensity.value;
                Default(Occlusion, 1);
                Default(OcclusionDistance, Mathf.RoundToInt(occlusion.rayLength));
                Default(OcclusionSamples, occlusion.sampleCount);
            }
            var camera = Camera();
            if (camera != null) plainClear = camera.clearColorMode;
            int aa = camera != null && camera.allowDeepLearningSuperSampling ? AaDlss : camera != null && camera.antialiasing == HDAdditionalCameraData.AntialiasingMode.None ? AaOff : AaTaa;
            Default(AntiAliasing, aa);
            Default(TaaQuality, camera != null ? (int)camera.TAAQuality : 1);
            Default(DlssMode, 2);
            Default(DlssSharpness, 0);
        }

        // The vSync choice stays as it is whichever level is picked: a level's own setting would change it
        static void ApplyQuality(int index)
        {
            int vSync = QualitySettings.vSyncCount;
            QualitySettings.SetQualityLevel(Mathf.Clamp(index, 0, QualitySettings.names.Length - 1), true);
            QualitySettings.vSyncCount = vSync;
        }

        static VolumeProfile Profile()
        {
            var volume = Object.FindFirstObjectByType<Volume>();
            return volume != null ? volume.profile : null;   // a runtime copy, so the hand-tuned asset stays as it is
        }

        static HDAdditionalCameraData Camera()
        {
            var main = UnityEngine.Camera.main;
            return main != null ? main.GetComponent<HDAdditionalCameraData>() : null;
        }

        static void Apply()
        {
            ApplyRayTracing();
            ApplyAntiAliasing();
            ApplySkybox();
        }

        // Off is the original's plain sky. The horizon band blends over the picture while it's on.
        static void ApplySkybox()
        {
            var picture = SkyPicture;
            bool on = On(Skybox) && picture != null;
            var profile = Profile();
            if (profile != null && profile.TryGet<VisualEnvironment>(out var environment))
            {
                if (on)
                {
                    if (!profile.TryGet<HDRISky>(out var sky)) sky = profile.Add<HDRISky>(true);
                    sky.hdriSky.Override(picture);
                    sky.exposure.Override(Get(SkyBrightness) / 10f);
                }
                environment.skyType.Override(on ? (int)SkyType.HDRI : plainSkyType);
            }
            var camera = Camera();
            if (camera != null) camera.clearColorMode = on ? HDAdditionalCameraData.ClearColorMode.Sky : plainClear;
            if (GameManager.Instance != null) GameManager.Instance.UseSkyBand(on);
        }

        // Off falls back to the screen-space versions of the same effects. While ray tracing, each effect
        // leaves its quality tier, or the tier would overrule these values.
        static void ApplyRayTracing()
        {
            bool tracing = On(RayTracing);
            var mode = tracing ? RayCastingMode.RayTracing : RayCastingMode.RayMarching;
            var profile = Profile();
            if (profile != null && profile.TryGet<ScreenSpaceReflection>(out var reflections))
            {
                reflections.enabled.Override(On(Reflections));
                reflections.tracing.Override(mode);
                reflections.quality.Override(tracing ? CustomQuality : reflectionTier);
                reflections.rayLength = Get(ReflectionDistance);
                reflections.sampleCount.Override(Get(ReflectionSamples));
                reflections.bounceCount.Override(Get(ReflectionBounces));
                reflections.denoise = true;
                reflections.fullResolution = true;
            }
            if (profile != null && profile.TryGet<GlobalIllumination>(out var lighting))
            {
                lighting.enable.Override(On(Lighting));
                lighting.tracing.Override(mode);
                lighting.quality.Override(tracing ? CustomQuality : lightingTier);
                lighting.rayLength = Get(LightingDistance);
                lighting.sampleCount.Override(Get(LightingSamples));
                lighting.bounceCount.Override(Get(LightingBounces));
                lighting.denoise = true;
            }
            if (profile != null && profile.TryGet<ScreenSpaceAmbientOcclusion>(out var occlusion))
            {
                occlusion.intensity.Override(On(Occlusion) ? occlusionIntensity : 0f);
                occlusion.rayTracing.Override(tracing);
                occlusion.quality.Override(tracing ? CustomQuality : occlusionTier);
                occlusion.rayLength = Get(OcclusionDistance);
                occlusion.sampleCount = Get(OcclusionSamples);
                occlusion.denoise = true;
            }
            foreach (var light in Object.FindObjectsByType<HDAdditionalLightData>(FindObjectsSortMode.None))
                light.useRayTracedShadows = tracing && On(Shadows);
        }

        // Sharpness only counts with DLSS's automatic settings off, so its render scale is set here instead
        static void ApplyAntiAliasing()
        {
            var camera = Camera();
            if (camera == null) return;
            int aa = Get(AntiAliasing);
            bool dlss = aa == AaDlss && DlssAvailable;
            camera.allowDeepLearningSuperSampling = dlss;
            camera.deepLearningSuperSamplingUseCustomQualitySettings = true;
            camera.deepLearningSuperSamplingQuality = DlssLevels[Mathf.Clamp(Get(DlssMode), 0, DlssLevels.Length - 1)];
            camera.deepLearningSuperSamplingUseCustomAttributes = true;
            camera.deepLearningSuperSamplingUseOptimalSettings = false;
            camera.deepLearningSuperSamplingSharpening = Get(DlssSharpness) / 100f;
            camera.antialiasing = aa == AaOff ? HDAdditionalCameraData.AntialiasingMode.None : HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            camera.TAAQuality = (HDAdditionalCameraData.TAAQualityLevel)Get(TaaQuality);
        }
    }
}
