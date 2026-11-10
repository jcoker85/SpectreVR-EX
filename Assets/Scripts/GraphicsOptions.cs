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
            Reflections = "gfx.ssr", ReflectionMode = "gfx.ssr.mode", ReflectionDistance = "gfx.ssr.distance", ReflectionSamples = "gfx.ssr.samples", ReflectionBounces = "gfx.ssr.bounces",
            Lighting = "gfx.gi", LightingMode = "gfx.gi.mode", LightingDistance = "gfx.gi.distance", LightingSamples = "gfx.gi.samples", LightingBounces = "gfx.gi.bounces",
            Occlusion = "gfx.ao", OcclusionDistance = "gfx.ao.distance", OcclusionSamples = "gfx.ao.samples",
            AntiAliasing = "gfx.aa", TaaQuality = "gfx.taa.quality", DlssScale = "gfx.dlss.scale", DlssSharpness = "gfx.dlss.sharpness",
            MotionBlurAmount = "gfx.motionblur";

        public const int AaOff = 0, AaTaa = 1, AaDlss = 2;
        public static readonly string[] AaNames = { "None", "TAA", "DLSS" };
        public static readonly string[] TaaNames = { "Low", "Medium", "High" };
        // GI Mode and Reflect Mode: Full traces every ray; Mixed takes what it can from the picture on screen
        // and traces only what that misses, which costs far less when a wall or debris fills the view
        public const int TraceFull = 0, TraceMixed = 1;
        public static readonly string[] TraceModeNames = { "Full", "Mixed" };
        const int CustomQuality = 3;                                               // past the Low-Medium-High tiers

        public static readonly string[] ModeNames = { "Windowed", "Borderless", "Fullscreen" };
        static readonly FullScreenMode[] Modes = { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen };

        const string Ultra = "Ultra";

        static readonly Dictionary<string, int> values = new Dictionary<string, int>();
        static int reflectionTier, lightingTier, occlusionTier, plainSkyType;
        static float occlusionIntensity;
        static bool skyHidden, menuShown;
        static int gameVSync, gameCulling;
        static bool gameCustomFrame;
        static FrameSettings gameFrame;
        static FrameSettingsOverrideMask gameFrameMask;

        // What the camera leaves out behind a menu. Not passthrough: HDRP draws the overlay UI inside the
        // camera's own render, so a skipped camera leaves the player's screen black.
        static readonly FrameSettingsField[] MenuOff =
        {
            FrameSettingsField.RayTracing, FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI,
            FrameSettingsField.Volumetrics, FrameSettingsField.ShadowMaps, FrameSettingsField.ContactShadows, FrameSettingsField.ScreenSpaceShadows,
            FrameSettingsField.Postprocess, FrameSettingsField.MotionVectors, FrameSettingsField.OpaqueObjects, FrameSettingsField.TransparentObjects,
            FrameSettingsField.AtmosphericScattering, FrameSettingsField.Refraction, FrameSettingsField.Distortion, FrameSettingsField.Decals,
            FrameSettingsField.SubsurfaceScattering, FrameSettingsField.ReflectionProbe, FrameSettingsField.PlanarProbe, FrameSettingsField.SkyReflection,
            FrameSettingsField.CustomPass,
        };
        static HDAdditionalCameraData.ClearColorMode plainClear;

        // The star map. SkyBrightness is its exposure in tenths; high enough and the stars pass the
        // volume's bloom threshold and glow.
        static Cubemap SkyPicture => Resources.Load<Cubemap>("Skybox/Stars");

        public static int Get(string key) => values.TryGetValue(key, out int v) ? v : 0;
        public static bool On(string key) => Get(key) != 0;
        public static bool DlssAvailable => HDDynamicResolutionPlatformCapabilities.DLSSDetected;

        // The choice in force: a saved DLSS on a machine without it runs as TAA, and DLSS isn't offered
        public static int Aa => Get(AntiAliasing) == AaDlss && !DlssAvailable ? AaTaa : Get(AntiAliasing);
        public static List<string> AaChoices() => new List<string>(AaNames).GetRange(0, DlssAvailable ? AaNames.Length : AaDlss);

        public static void Set(string key, int value)
        {
            values[key] = value;
            Prefs.SetInt(key, value);
            Prefs.Save();
            Apply();
        }

        static void Default(string key, int value) => values[key] = Prefs.GetInt(key, value);

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
            Prefs.SetInt("gfx.width", size.x);
            Prefs.SetInt("gfx.height", size.y);
            Prefs.Save();
            Screen.SetResolution(size.x, size.y, Screen.fullScreenMode);
        }

        public static void SetMode(int index)
        {
            Prefs.SetInt("gfx.mode", index);
            Prefs.Save();
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

        // The hidden map has the plain sky behind it, whatever the setting
        public static void HideSky(bool hidden)
        {
            skyHidden = hidden;
            ApplySkybox();
        }

        public static void SetQuality(string name)
        {
            int index = System.Array.IndexOf(QualitySettings.names, name);
            Prefs.SetInt("gfx.quality", index);
            Prefs.Save();
            ApplyQuality(index);
            Apply();
        }

        // At startup: what was chosen last time, or the project's own settings if nothing was
        public static void Load()
        {
            if (Prefs.HasKey("gfx.quality")) ApplyQuality(Prefs.GetInt("gfx.quality"));
            ReadDefaults();
            if (Prefs.HasKey("gfx.mode")) Screen.fullScreenMode = Modes[Mathf.Clamp(Prefs.GetInt("gfx.mode"), 0, Modes.Length - 1)];
            if (Prefs.HasKey("gfx.width")) Screen.SetResolution(Prefs.GetInt("gfx.width"), Prefs.GetInt("gfx.height"), Screen.fullScreenMode);
            DynamicResolutionHandler.SetDynamicResScaler(RenderScale, DynamicResScalePolicyType.ReturnsPercentage);
            Apply();
        }

        static float RenderScale() =>
            Aa == AaDlss ? Get(DlssScale) : 100f;

        // NVIDIA's own level nearest the render scale, for its tuning
        static uint DlssLevel(int percent) => percent >= 100 ? 4u : percent >= 66 ? 2u : percent >= 58 ? 1u : percent >= 50 ? 0u : 3u;

        // The owner's defaults (2026-11-09), set here rather than in the hand-tuned volume.
        // Ray tracing starts off where the machine can't do it.
        static void ReadDefaults()
        {
            Default(RayTracing, SystemInfo.supportsRayTracing ? 1 : 0);
            Default(Shadows, 1);
            Default(Skybox, 0);
            Default(SkyBrightness, 100);
            var profile = Profile();
            if (profile != null && profile.TryGet<VisualEnvironment>(out var environment)) plainSkyType = environment.skyType.value;
            if (profile != null && profile.TryGet<ScreenSpaceReflection>(out var reflections))
            {
                reflectionTier = reflections.quality.value;
                Default(Reflections, reflections.enabled.value ? 1 : 0);
                Default(ReflectionMode, TraceFull);
                Default(ReflectionDistance, 200);
                Default(ReflectionSamples, 4);
                Default(ReflectionBounces, 2);
            }
            if (profile != null && profile.TryGet<GlobalIllumination>(out var lighting))
            {
                lightingTier = lighting.quality.value;
                Default(Lighting, lighting.enable.value ? 1 : 0);
                Default(LightingMode, TraceFull);
                Default(LightingDistance, 100);
                Default(LightingSamples, 4);
                Default(LightingBounces, 2);
            }
            if (profile != null && profile.TryGet<ScreenSpaceAmbientOcclusion>(out var occlusion))
            {
                occlusionTier = occlusion.quality.value;
                occlusionIntensity = occlusion.intensity.value;
                Default(Occlusion, 1);
                Default(OcclusionDistance, 50);
                Default(OcclusionSamples, 8);
            }
            var camera = Camera();
            if (camera != null) plainClear = camera.clearColorMode;
            int aa = camera != null && camera.allowDeepLearningSuperSampling ? AaDlss : camera != null && camera.antialiasing == HDAdditionalCameraData.AntialiasingMode.None ? AaOff : AaTaa;
            Default(AntiAliasing, aa);
            Default(TaaQuality, camera != null ? (int)camera.TAAQuality : 1);
            Default(DlssScale, 65);
            Default(DlssSharpness, 0);
            Default(MotionBlurAmount, 0);
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
            ApplyMotionBlur();
        }

        // The amount, in percent, overrides the default volume's intensity in the scene volume's runtime
        // copy; 0 turns the blur off.
        static void ApplyMotionBlur()
        {
            var profile = Profile();
            if (profile == null) return;
            if (!profile.TryGet<MotionBlur>(out var blur)) blur = profile.Add<MotionBlur>();
            blur.intensity.Override(Get(MotionBlurAmount) / 100f);
        }

        // While a menu, the startup movie or the save dialog covers the whole screen, the camera behind
        // them draws nothing but the UI (no objects, ray tracing or effects) and the frame rate is held at
        // 60. The game's own camera settings and vSync come back with the game.
        public static void MenuShown(bool shown)
        {
            if (shown == menuShown) return;
            menuShown = shown;
            var camera = Camera();
            if (camera != null)
            {
                var view = camera.GetComponent<UnityEngine.Camera>();
                if (shown)
                {
                    gameCulling = view.cullingMask;
                    gameCustomFrame = camera.customRenderingSettings;
                    gameFrame = camera.renderingPathCustomFrameSettings;
                    gameFrameMask = camera.renderingPathCustomFrameSettingsOverrideMask;
                    view.cullingMask = 0;
                    camera.customRenderingSettings = true;
                    foreach (var field in MenuOff)
                    {
                        camera.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                        camera.renderingPathCustomFrameSettings.SetEnabled(field, false);
                    }
                }
                else
                {
                    view.cullingMask = gameCulling;
                    camera.customRenderingSettings = gameCustomFrame;
                    camera.renderingPathCustomFrameSettings = gameFrame;
                    camera.renderingPathCustomFrameSettingsOverrideMask = gameFrameMask;
                }
            }
            if (shown)
            {
                gameVSync = QualitySettings.vSyncCount;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
            }
            else
            {
                QualitySettings.vSyncCount = gameVSync;
                Application.targetFrameRate = -1;
            }
        }

        // Off is the original's plain sky. The horizon band blends over the picture while it's on.
        static void ApplySkybox()
        {
            var picture = SkyPicture;
            bool on = On(Skybox) && picture != null && !skyHidden;
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
                reflections.tracing.Override(tracing && Get(ReflectionMode) == TraceMixed ? RayCastingMode.Mixed : mode);
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
                lighting.tracing.Override(tracing && Get(LightingMode) == TraceMixed ? RayCastingMode.Mixed : mode);
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
            int aa = Aa;
            bool dlss = aa == AaDlss;
            camera.allowDeepLearningSuperSampling = dlss;
            camera.deepLearningSuperSamplingUseCustomQualitySettings = true;
            camera.deepLearningSuperSamplingQuality = DlssLevel(Get(DlssScale));
            camera.deepLearningSuperSamplingUseCustomAttributes = true;
            camera.deepLearningSuperSamplingUseOptimalSettings = false;
            camera.deepLearningSuperSamplingSharpening = Get(DlssSharpness) / 100f;
            camera.antialiasing = aa == AaOff ? HDAdditionalCameraData.AntialiasingMode.None : HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            camera.TAAQuality = (HDAdditionalCameraData.TAAQualityLevel)Get(TaaQuality);
        }
    }
}
