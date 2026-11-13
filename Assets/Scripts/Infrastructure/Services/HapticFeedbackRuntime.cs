using System.Collections.Generic;
using GravityPuzzle.Config;
using Solo.MOST_IN_ONE;
using UnityEngine;

namespace GravityPuzzle.Infrastructure.Services
{
    /// <summary>
    /// Process-wide presentation adapter. It owns no gameplay state.
    /// Decides which cue plays and when (preference, cooldowns, priority, shredder ticks);
    /// MOST_HapticFeedback is the only device backend.
    /// </summary>
    public static class HapticFeedbackRuntime
    {
        private const string PreferenceKey = "GravityPuzzle.VibrationEnabled";
        private static readonly HashSet<int> activeShredderFeeds = new HashSet<int>();
        private static HapticFeedbackConfig config;
        private static float lastHapticTime = float.NegativeInfinity;
        private static int lastHapticPriority = int.MinValue;
        private static float lastUiTime = float.NegativeInfinity;
        private static float lastImpactTime = float.NegativeInfinity;
        private static float lastShredderTickTime = float.NegativeInfinity;
        private static float lastShredderCompletionTime = float.NegativeInfinity;
        private static float lastResultTime = float.NegativeInfinity;

        public static void Configure(HapticFeedbackConfig value)
        {
            StopAll();
            config = value;
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

        public static void PieceSelected() { if (config != null) TryPlay(config.PieceSelected, config.UiPriority, config.UiCooldownMilliseconds, ref lastUiTime); }
        public static void InvalidDrop() { if (config != null) TryPlay(config.InvalidDrop, config.UiPriority, config.InvalidDropCooldownMilliseconds, ref lastUiTime); }
        public static void ValidDrop() { if (config != null) TryPlay(config.ValidDrop, config.UiPriority, config.UiCooldownMilliseconds, ref lastUiTime); }
        public static void HammerImpact(bool bombDefused)
        {
            if (config == null) return;
            TryPlay(bombDefused ? config.BombDefused : config.HammerImpact, config.ImpactPriority, config.ImpactCooldownMilliseconds, ref lastImpactTime);
        }
        public static void RocketImpact() { if (config != null) TryPlay(config.RocketImpact, config.ImpactPriority, config.ImpactCooldownMilliseconds, ref lastImpactTime); }
        public static void FreezeActivated() { if (config != null) TryPlay(config.FreezeActivated, config.ImpactPriority, config.ImpactCooldownMilliseconds, ref lastImpactTime); }
        public static void LevelCompleted() { if (config != null) TryPlay(config.LevelCompleted, config.ResultPriority, config.ResultCooldownMilliseconds, ref lastResultTime); }
        public static void LevelFailed() { if (config != null) TryPlay(config.LevelFailed, config.ResultPriority, config.ResultCooldownMilliseconds, ref lastResultTime); }

        public static void BeginShredder(int pieceId)
        {
            if (config == null || !config.ShredderTick.Enabled) return;
            activeShredderFeeds.Add(pieceId);
        }

        /// <summary>Called from the shredder's owned feed tick; plays a repeating tick at the configured interval.</summary>
        public static void TickShredder()
        {
            if (activeShredderFeeds.Count == 0 || config == null) return;
            TryPlay(config.ShredderTick, config.ShredderTickPriority, config.ShredderTickIntervalMilliseconds, ref lastShredderTickTime);
        }

        public static void EndShredder(int pieceId, bool completed)
        {
            activeShredderFeeds.Remove(pieceId);
            if (completed && config != null)
                TryPlay(config.ShredderPieceCompleted, config.ShredderCompletionPriority, config.ShredderCompletionCooldownMilliseconds, ref lastShredderCompletionTime);
        }

        public static void StopAll()
        {
            activeShredderFeeds.Clear();
            MOST_HapticFeedback.Stop();
        }

        private static bool IsEnabled() => config != null && PlayerPrefs.GetInt(PreferenceKey, 1) == 1 && !Application.isEditor;

        private static void TryPlay(HapticFeedbackConfig.Cue cue, int priority, int cooldownMilliseconds, ref float lastCueTime)
        {
            if (!IsEnabled() || !cue.Enabled) return;

            float now = Time.unscaledTime;
            if (now - lastCueTime < cooldownMilliseconds / 1000f) return;

            // Global anti-spam gate: rapid cues collapse into one, but a more important cue
            // (e.g. level result after a drop tick) is never swallowed by a less important one.
            bool withinGlobalInterval = now - lastHapticTime < config.GlobalMinIntervalMilliseconds / 1000f;
            if (withinGlobalInterval && priority <= lastHapticPriority) return;

            lastCueTime = now;
            lastHapticTime = now;
            lastHapticPriority = priority;
            MOST_HapticFeedback.Generate(cue.Type);
        }
    }
}
