using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WuWa
{
    /// Asset references the graphics applier needs at runtime (created by the editor pass).
    public class WuWaGraphicsRefs : ScriptableObject
    {
        public UniversalRenderPipelineAsset urp;
        public UniversalRendererData renderer;
        public VolumeProfile post;
    }

    /// Pushes OptionsData into engine/game state. Group appliers are idempotent.
    public static class SettingsAppliers
    {
        static OptionsData D { get { return SettingsStore.D; } }
        static WuWaGraphicsRefs _refs;
        static bool _refsTried;
        static bool _ssaoOriginal, _ssaoCaptured;
        static ScriptableRendererFeature _ssao;

        public static void ApplyAll()
        {
            ApplyAudio(); ApplyControls(); ApplyGameplay(); ApplyAccessibility(); ApplyGraphics(); ApplyDisplay();
        }

        public static void Apply(string key)
        {
            if (key.StartsWith("audio.")) ApplyAudio();
            else if (key.StartsWith("ctl.")) ApplyControls();
            else if (key.StartsWith("play.")) ApplyGameplay();
            else if (key.StartsWith("acc.")) { ApplyAccessibility(); ApplyGameplay(); }
            else if (key == "gfx.displayMode" || key == "gfx.resolution" || key == "gfx.vsync" || key == "gfx.frameCap") ApplyDisplay();
            else if (key.StartsWith("gfx.")) ApplyGraphics();
        }

        // ---------------------------------------------------------------- audio
        public static void ApplyAudio()
        {
            AudioListener.volume = Mathf.Clamp01(D.masterVol);
            MusicDirector.BgmMul = Mathf.Clamp01(D.bgmVol);
            AudioMan.SfxMul = Mathf.Clamp01(D.sfxVol);
        }

        // ---------------------------------------------------------------- controls
        public static void ApplyControls()
        {
            InputService.DeadzoneL = D.deadzoneL;
            InputService.DeadzoneR = D.deadzoneR;
            InputService.StickCurve = D.stickCurve == 0 ? 1f : D.stickCurve == 2 ? 0.8f : 1.4f;
            InputService.PadAccel = D.padAccel;
            try { InputSystem.settings.defaultButtonPressPoint = Mathf.Clamp(D.triggerThreshold, 0.1f, 0.9f); } catch { }

            ThirdPersonCamera.MouseSensX = D.mouseSensX * 0.004f;
            ThirdPersonCamera.MouseSensY = D.mouseSensY * 0.004f;
            ThirdPersonCamera.PadYawRate = 40f + 1.8f * D.padSensX;
            ThirdPersonCamera.PadPitchRate = 30f + 1.4f * D.padSensY;
            ThirdPersonCamera.PadAccel = D.padAccel;
            ThirdPersonCamera.InvertX = D.invertX;
            ThirdPersonCamera.InvertY = D.invertY;
            ThirdPersonCamera.LockCamTrack = D.lockCamTrack;
            ThirdPersonCamera.LockAssist = D.lockAssist == 0 ? 1.8f : D.lockAssist == 2 ? 5.5f : 3.2f;
            ThirdPersonCamera.MoveCamCorrect = D.moveCamCorrect;
            ThirdPersonCamera.DistanceSetting = D.camDistance;
            ThirdPersonCamera.CombatDistanceSetting = D.camCombatDistance;

            HapticsService.Intensity = D.vibration;
            HapticsService.Combat = D.vibCombat; HapticsService.Move = D.vibMove; HapticsService.Fx = D.vibFx; HapticsService.UI = D.vibUI;
            HapticsService.LightBar = D.lightBar;

            PlayerController.SprintMode = D.holdToggleSprint ? 1 : D.sprintMode;
            PlayerController.AutoSprintDelay = new[] { -1f, 2f, 3.5f, 5f }[Mathf.Clamp(D.autoSprintDelay, 0, 3)];
            Glyph.Current = (Glyph.Style)Mathf.Clamp(D.glyphStyle, 0, 3);

            // right-click dodge is a binding override on the KB/M dodge binding
            var dodge = InputService.Action("Player/Dodge");
            if (dodge != null)
            {
                for (int i = 0; i < dodge.bindings.Count; i++)
                {
                    if (dodge.bindings[i].path != "<Mouse>/rightButton") continue;
                    if (D.dodgeRmb) dodge.RemoveBindingOverride(i);
                    else dodge.ApplyBindingOverride(i, "");
                }
            }
        }

        // ---------------------------------------------------------------- gameplay
        public static void ApplyGameplay()
        {
            CameraShaker.Mul = Mathf.Clamp(D.shakeMul, 0f, 2f);
            Hitstop.Mul = Mathf.Clamp(D.hitstopMul, 0f, 2f);
            Hitstop.SlowMoMul = Mathf.Clamp01(D.slowMoMul);
            VFXLibrary.FlashMul = D.reduceFlash ? 0f : Mathf.Clamp01(D.flashMul);
            DamageNumbers.Enabled = D.dmgNumbers > 0;
            DamageNumbers.CritOnly = D.dmgNumbers == 1;
            DamageNumbers.Scale = Mathf.Clamp(D.dmgScale, 0.5f, 2f);
            MapSystem.MinimapEnabled = D.minimap;
            MapSystem.MinimapRadius = new[] { 70f, 120f, 200f }[Mathf.Clamp(D.minimapRadius, 0, 2)];
            MapSystem.MinimapMode = Mathf.Clamp(D.minimapMode, 0, 1);
            MapDiscovery.RevealAll = D.mapRevealAll;
            HUDController.ShowQuestTracker = D.questTracker;
            HUDController.ShowFps = D.showFps;
            HUDController.ApplyScale(Mathf.Clamp(D.hudScale, 0.6f, 1.5f));
            DialogueSystem.CharsPerSecond = new[] { 24f, 42f, 80f, 9999f }[Mathf.Clamp(D.dialogueSpeed, 0, 3)];
            DialogueSystem.AutoAdvance = D.dialogueAuto;
            DialogueSystem.TextScale = Mathf.Clamp(D.textScale, 0.8f, 1.5f);
            SaveSystem.NoticeMode = D.autosaveNotice;
        }

        // ---------------------------------------------------------------- accessibility
        public static void ApplyAccessibility()
        {
            Palette.SetColorblind(D.colorblind);
            Palette.TelegraphMul = D.timingAssist == 0 ? 1f : D.timingAssist == 1 ? 1.15f : 1.3f;
            PlayerController.TimingAssist = D.timingAssist;
            PlayerCombat.BufferTime = D.timingAssist == 0 ? 0.5f : D.timingAssist == 1 ? 0.65f : 0.8f;
        }

        // ---------------------------------------------------------------- graphics
        static WuWaGraphicsRefs Refs
        {
            get
            {
                if (_refs == null && !_refsTried) { _refsTried = true; _refs = Resources.Load<WuWaGraphicsRefs>("WuWaGraphicsRefs"); }
                return _refs;
            }
        }

        public static void ApplyPreset(int preset)
        {
            D.quality = preset;
            if (preset == 0) { D.renderScale = 0.8f; D.shadowQuality = 1; D.aa = 1; D.ssao = false; D.grassDensity = 1; D.grassDistance = 0; D.decoDistance = 0; }
            else if (preset == 1) { D.renderScale = 1f; D.shadowQuality = 2; D.aa = 2; D.ssao = true; D.grassDensity = 2; D.grassDistance = 1; D.decoDistance = 1; }
            else if (preset == 2) { D.renderScale = 1f; D.shadowQuality = 3; D.aa = 2; D.ssao = true; D.grassDensity = 3; D.grassDistance = 2; D.decoDistance = 2; }
        }

        public static void ApplyGraphics()
        {
            var urp = (Refs != null && Refs.urp != null) ? Refs.urp : GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                urp.renderScale = Mathf.Clamp(D.renderScale, 0.5f, 1.5f);
                switch (Mathf.Clamp(D.shadowQuality, 0, 3))
                {
                    case 0: urp.shadowDistance = 0f; break;
                    case 1: urp.shadowDistance = 45f; urp.shadowCascadeCount = 2; urp.mainLightShadowmapResolution = 1024; break;
                    case 2: urp.shadowDistance = 80f; urp.shadowCascadeCount = 3; urp.mainLightShadowmapResolution = 2048; break;
                    default: urp.shadowDistance = 120f; urp.shadowCascadeCount = 4; urp.mainLightShadowmapResolution = 2048; break;
                }
            }
            // SSAO renderer feature (restore the asset's own value when we go away)
            if (Refs != null && Refs.renderer != null && Refs.renderer.rendererFeatures != null)
            {
                foreach (var f in Refs.renderer.rendererFeatures)
                {
                    if (f == null || !f.name.Contains("ScreenSpaceAmbientOcclusion")) continue;
                    if (!_ssaoCaptured) { _ssaoOriginal = f.isActive; _ssaoCaptured = true; _ssao = f; }
                    if (f.isActive != D.ssao) f.SetActive(D.ssao);
                }
            }
            var cam = Camera.main;
            if (cam != null)
            {
                var extra = cam.GetUniversalAdditionalCameraData();
                extra.antialiasing = D.aa == 0 ? AntialiasingMode.None : D.aa == 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                extra.antialiasingQuality = AntialiasingQuality.High;
            }
            var vol = Object.FindAnyObjectByType<Volume>();
            if (vol != null && vol.sharedProfile != null)
            {
                var prof = vol.profile;                 // instance, never the asset
                Bloom bloom; if (prof.TryGet(out bloom)) bloom.active = D.bloom;
                Vignette vig; if (prof.TryGet(out vig)) vig.active = D.vignette;
                ScreenSpaceLensFlare flare; if (prof.TryGet(out flare)) flare.active = D.lensFlare;
                ColorAdjustments ca; if (prof.TryGet(out ca)) { ca.postExposure.overrideState = true; ca.postExposure.value = 0.18f + Mathf.Clamp(D.brightness, -1f, 1f); }
            }
            GrassField.ApplyQuality(D.grassDensity, D.grassDistance);
            PerfTuner.DistanceMul = new[] { 0.7f, 1f, 1.3f }[Mathf.Clamp(D.decoDistance, 0, 2)];
            PerfTuner.Reapply();
            ThirdPersonCamera.FovBase = Mathf.Clamp(D.fov, 45f, 75f);
            HUDController.ShowFps = D.showFps;
        }

        public static void ApplyDisplay()
        {
            QualitySettings.vSyncCount = D.vsync ? 1 : 0;
            Application.targetFrameRate = D.vsync ? -1 : D.frameCap;
            var mode = D.displayMode == 0 ? FullScreenMode.ExclusiveFullScreen : D.displayMode == 2 ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            int w = D.resW > 0 ? D.resW : Screen.currentResolution.width;
            int h = D.resH > 0 ? D.resH : Screen.currentResolution.height;
            if (Screen.fullScreenMode != mode || Screen.width != w || Screen.height != h)
                Screen.SetResolution(w, h, mode);
        }

        public static void RestoreEditorAssets()
        {
            if (_ssaoCaptured && _ssao != null && _ssao.isActive != _ssaoOriginal) _ssao.SetActive(_ssaoOriginal);
        }
    }
}
