using System.Collections.Generic;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Infrastructure.Services
{
    /// <summary>Process-wide presentation adapter. It owns no gameplay state.</summary>
    public static class HapticFeedbackRuntime
    {
        private const string PreferenceKey = "GravityPuzzle.VibrationEnabled";
        private static readonly Dictionary<int, float> shredderProgressByPiece = new Dictionary<int, float>();
        private static HapticFeedbackConfig config;
        private static int lastOneShotFrame = -1;
        private static int lastOneShotPriority;
        private static float lastShredderIntensity = -1f;
        private static float lastUiTime;
        private static float lastImpactTime;
        private static float lastShredderCompletionTime;
        private static float lastResultTime;
        private static bool shredderPatternPlaying;

        public static void Configure(HapticFeedbackConfig value)
        {
            StopAll();
            config = value;
            DeviceHaptics.Configure(value);
            if (config == null)
                Debug.LogWarning("[Haptics] HapticFeedbackConfig is not assigned on the bootstrap; haptics are disabled.");
        }

        public static void NotifyApplicationPaused(bool paused)
        {
            if (paused)
                StopAll();
        }

        public static void NotifyPreferenceChanged(bool enabled)
        {
            if (!enabled)
                StopAll();
        }

        public static void PieceSelected() => TryPulse(config != null ? config.PieceSelected : default, config != null ? config.UiPriority : 0, config != null ? config.UiCooldownMilliseconds : 0, ref lastUiTime);
        public static void InvalidDrop() => TryPulse(config != null ? config.InvalidDrop : default, config != null ? config.UiPriority : 0, config != null ? config.InvalidDropCooldownMilliseconds : 0, ref lastUiTime);
        public static void ValidDrop() => TryPulse(config != null ? config.ValidDrop : default, config != null ? config.UiPriority : 0, config != null ? config.UiCooldownMilliseconds : 0, ref lastUiTime);
        public static void HammerImpact(bool bombDefused)
        {
            if (config == null) return;
            if (bombDefused) TryPattern(config.BombDefused, config.ImpactPriority, config.ImpactCooldownMilliseconds, ref lastImpactTime);
            else TryPulse(config.HammerImpact, config.ImpactPriority, config.ImpactCooldownMilliseconds, ref lastImpactTime);
        }
        public static void RocketImpact() => TryPulse(config != null ? config.RocketImpact : default, config != null ? config.ImpactPriority : 0, config != null ? config.ImpactCooldownMilliseconds : 0, ref lastImpactTime);
        public static void FreezeActivated() { if (config != null) TryPattern(config.FreezeActivated, config.ImpactPriority, config.ImpactCooldownMilliseconds, ref lastImpactTime); }
        public static void LevelCompleted() { if (config != null) TryPattern(config.LevelCompleted, config.ResultPriority, config.ResultCooldownMilliseconds, ref lastResultTime); }
        public static void LevelFailed() { if (config != null) TryPattern(config.LevelFailed, config.ResultPriority, config.ResultCooldownMilliseconds, ref lastResultTime); }

        public static void BeginShredder(int pieceId) { if (IsEnabled() && config != null && config.ShredderPatternEnabled) { shredderProgressByPiece[pieceId] = 0f; RefreshShredderPattern(); } }
        public static void UpdateShredderProgress(int pieceId, float feedProgress)
        {
            if (!shredderProgressByPiece.ContainsKey(pieceId)) return;
            shredderProgressByPiece[pieceId] = Mathf.Clamp01(feedProgress);
            RefreshShredderPattern();
        }
        public static void EndShredder(int pieceId, bool completed)
        {
            if (!shredderProgressByPiece.Remove(pieceId)) return;
            if (shredderProgressByPiece.Count > 0) { RefreshShredderPattern(); return; }
            StopShredderPattern();
            if (completed && config != null) TryPulse(config.ShredderPieceCompleted, config.ShredderCompletionPriority, config.ShredderCompletionCooldownMilliseconds, ref lastShredderCompletionTime);
        }

        public static void StopAll()
        {
            shredderProgressByPiece.Clear();
            StopShredderPattern();
            DeviceHaptics.Cancel();
        }

        private static bool IsEnabled() => config != null && PlayerPrefs.GetInt(PreferenceKey, 1) == 1 && !Application.isEditor;
        private static void RefreshShredderPattern()
        {
            if (!IsEnabled() || shredderProgressByPiece.Count == 0) { StopShredderPattern(); return; }
            float leastProgress = 1f;
            foreach (float value in shredderProgressByPiece.Values) leastProgress = Mathf.Min(leastProgress, value);
            // The least-progressed feed controls intensity so a newly entering piece does not inherit a faded motor.
            float curveProgress = Mathf.Clamp01(config.ShredderIntensityByFeedProgress.Evaluate(leastProgress));
            float intensity = Mathf.Lerp(config.ShredderStartIntensity, config.ShredderEndIntensity, curveProgress);
            if (shredderPatternPlaying && Mathf.Abs(intensity - lastShredderIntensity) < config.ShredderIntensityUpdateThreshold) return;
            DeviceHaptics.StartRepeating(config.ShredderOnMilliseconds, config.ShredderOffMilliseconds, intensity);
            shredderPatternPlaying = true;
            lastShredderIntensity = intensity;
        }
        private static void StopShredderPattern() { if (!shredderPatternPlaying) return; DeviceHaptics.Cancel(); shredderPatternPlaying = false; lastShredderIntensity = -1f; }
        private static void TryPulse(HapticFeedbackConfig.Pulse pulse, int priority, int cooldown, ref float lastTime)
        {
            if (!IsEnabled() || !pulse.Enabled || !TryAcquireOneShot(priority, cooldown, ref lastTime)) return;
            // Android exposes one vibrator channel. A one-shot therefore replaces the
            // waveform briefly; the next active shredder feed tick restarts its pattern.
            // Core Haptics layers players, so on iOS the loop keeps running and stays tracked.
            if (DeviceHaptics.OneShotReplacesRepeating)
            {
                shredderPatternPlaying = false;
                lastShredderIntensity = -1f;
            }
            DeviceHaptics.Pulse(pulse.DurationMilliseconds, pulse.Intensity);
        }
        private static void TryPattern(HapticFeedbackConfig.Pattern pattern, int priority, int cooldown, ref float lastTime)
        {
            if (!IsEnabled() || !pattern.Enabled || !TryAcquireOneShot(priority, cooldown, ref lastTime)) return;
            DeviceHaptics.Pattern(pattern.First, pattern.Second, pattern.Third, pattern.GapMilliseconds);
        }
        private static bool TryAcquireOneShot(int priority, int cooldown, ref float lastTime)
        {
            if (Time.unscaledTime - lastTime < cooldown / 1000f) return false;
            if (lastOneShotFrame == Time.frameCount && priority <= lastOneShotPriority) return false;
            lastOneShotFrame = Time.frameCount; lastOneShotPriority = priority; lastTime = Time.unscaledTime; return true;
        }
    }

    /// <summary>Platform vibration backend: Android Vibrator waveforms or the iOS Core Haptics plugin.</summary>
    internal static class DeviceHaptics
    {
#if UNITY_IOS && !UNITY_EDITOR
        public static readonly bool OneShotReplacesRepeating = false;
        private static float iosSharpness;

        public static void Configure(HapticFeedbackConfig config) => iosSharpness = config != null ? config.IosSharpness : 0f;
#else
        public static readonly bool OneShotReplacesRepeating = true;

        public static void Configure(HapticFeedbackConfig config) { }
#endif

        public static void Cancel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBackend.Cancel();
#elif UNITY_IOS && !UNITY_EDITOR
            IosBackend.GravityHaptics_Cancel();
#endif
        }

        public static void Pulse(int duration, float intensity)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBackend.StartWaveform(new[] { 0L, (long)duration }, new[] { 0, AndroidBackend.ToAmplitude(intensity) }, -1);
#elif UNITY_IOS && !UNITY_EDITOR
            IosBackend.GravityHaptics_Pulse(Mathf.Clamp01(intensity), duration, iosSharpness);
#endif
        }

        public static void StartRepeating(int on, int off, float intensity)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBackend.StartWaveform(new[] { 0L, (long)on, (long)off }, new[] { 0, AndroidBackend.ToAmplitude(intensity), 0 }, 0);
#elif UNITY_IOS && !UNITY_EDITOR
            IosBackend.GravityHaptics_StartRepeating(on, off, Mathf.Clamp01(intensity), iosSharpness);
#endif
        }

        public static void Pattern(HapticFeedbackConfig.Pulse a, HapticFeedbackConfig.Pulse b, HapticFeedbackConfig.Pulse c, int gap)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidBackend.StartWaveform(new[] { 0L, (long)a.DurationMilliseconds, (long)gap, (long)b.DurationMilliseconds, (long)gap, (long)c.DurationMilliseconds }, new[] { 0, AndroidBackend.ToAmplitude(a.Intensity), 0, AndroidBackend.ToAmplitude(b.Intensity), 0, AndroidBackend.ToAmplitude(c.Intensity) }, -1);
#elif UNITY_IOS && !UNITY_EDITOR
            // A disabled pulse is sent with zero intensity so the native side keeps its timing slot silent.
            IosBackend.GravityHaptics_Pattern(
                IosIntensity(a), a.DurationMilliseconds,
                IosIntensity(b), b.DurationMilliseconds,
                IosIntensity(c), c.DurationMilliseconds,
                gap, iosSharpness);
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        private static float IosIntensity(HapticFeedbackConfig.Pulse pulse) => pulse.Enabled ? Mathf.Clamp01(pulse.Intensity) : 0f;

        /// <summary>Entry points of Assets/Plugins/iOS/GravityHaptics.mm.</summary>
        private static class IosBackend
        {
            [System.Runtime.InteropServices.DllImport("__Internal")] public static extern void GravityHaptics_Pulse(float intensity, int durationMilliseconds, float sharpness);
            [System.Runtime.InteropServices.DllImport("__Internal")] public static extern void GravityHaptics_Pattern(float intensityA, int durationA, float intensityB, int durationB, float intensityC, int durationC, int gapMilliseconds, float sharpness);
            [System.Runtime.InteropServices.DllImport("__Internal")] public static extern void GravityHaptics_StartRepeating(int onMilliseconds, int offMilliseconds, float intensity, float sharpness);
            [System.Runtime.InteropServices.DllImport("__Internal")] public static extern void GravityHaptics_Cancel();
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private static class AndroidBackend
        {
            // VibrationEffect requires API 26; the project's minimum SDK is lower.
            private const int VibrationEffectMinApiLevel = 26;
            private static int apiLevel = -1;

            public static int ToAmplitude(float value) => Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255f), 1, 255);

            public static void Cancel()
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator")) vibrator?.Call("cancel");
            }

            public static void StartWaveform(long[] timings, int[] amplitudes, int repeat)
            {
                if (apiLevel < 0)
                    using (AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION")) apiLevel = version.GetStatic<int>("SDK_INT");
                if (apiLevel < VibrationEffectMinApiLevel) return;
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                using (AndroidJavaClass vibrationEffect = new AndroidJavaClass("android.os.VibrationEffect"))
                using (AndroidJavaObject effect = vibrationEffect.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, repeat)) vibrator?.Call("vibrate", effect);
            }
        }
#endif
    }
}
