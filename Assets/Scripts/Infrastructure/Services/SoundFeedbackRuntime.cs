using System;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Infrastructure.Services
{
    /// <summary>
    /// Process-wide presentation adapter for sound cues and global mute state, mirroring <see cref="HapticFeedbackRuntime"/>.
    /// It owns no gameplay state. The scene bootstrap supplies the config and the AudioSource that plays the cues.
    /// </summary>
    public static class SoundFeedbackRuntime
    {
        public const string SoundPreferenceKey = "GravityPuzzle.SoundEnabled";

        private static SoundFeedbackConfig config;
        private static AudioSource source;
        private static AudioSource clockTickingSource;
        private static bool preferenceLoaded;
        private static bool soundEnabled = true;
        private static bool clockTickingRequested;
        private static float nextBarFillSoundTime;
        private static readonly ShuffledClipSequence buttonClicks = new ShuffledClipSequence();
        private static readonly ShuffledClipSequence pieceDrops = new ShuffledClipSequence();
        private static readonly ShuffledClipSequence barFills = new ShuffledClipSequence();
        private static readonly ShuffledClipSequence hammerBreaks = new ShuffledClipSequence();
        private static readonly ShuffledClipSequence freezeTimerImpacts = new ShuffledClipSequence();
        private static readonly ShuffledClipSequence rocketLaunches = new ShuffledClipSequence();

        public static event Action<bool> SoundEnabledChanged;

        public static bool IsSoundEnabled
        {
            get
            {
                EnsurePreferenceLoaded();
                return soundEnabled;
            }
        }

        public static void Configure(
            SoundFeedbackConfig value,
            AudioSource audioSource,
            AudioSource clockTickSource = null)
        {
            if (clockTickingSource != null && clockTickingSource != clockTickSource && clockTickingSource.isPlaying)
                clockTickingSource.Stop();

            config = value;
            source = audioSource;
            clockTickingSource = clockTickSource;
            clockTickingRequested = false;
            nextBarFillSoundTime = 0f;

            buttonClicks.Reset(config != null ? config.ButtonClickClips : null);
            pieceDrops.Reset(config != null ? config.PieceDropClips : null);
            barFills.Reset(config != null ? config.BarFillClips : null);
            hammerBreaks.Reset(config != null ? config.HammerBreakClips : null);
            freezeTimerImpacts.Reset(config != null ? config.FreezeTimerImpactClips : null);
            rocketLaunches.Reset(config != null ? config.RocketLaunchClips : null);

            if (clockTickingSource != null)
            {
                clockTickingSource.playOnAwake = false;
                clockTickingSource.loop = true;
                clockTickingSource.clip = config != null ? config.ClockTickingClip : null;
                clockTickingSource.volume = config != null ? config.ClockTickingVolume : 1f;
                if (clockTickingSource.isPlaying)
                    clockTickingSource.Stop();
            }

            soundEnabled = PlayerPrefs.GetInt(SoundPreferenceKey, 1) == 1;
            preferenceLoaded = true;
            ApplySoundState(soundEnabled);

            if (config == null)
                Debug.LogWarning("[Sound] SoundFeedbackConfig is not assigned on the bootstrap; sound cues are disabled.");
            else if (source == null)
                Debug.LogWarning("[Sound] No AudioSource is assigned on the bootstrap; sound cues are disabled.");
        }

        /// <summary>Applies a sound preference toggle immediately to all active audio playback.</summary>
        public static void NotifyPreferenceChanged(bool enabled)
        {
            soundEnabled = enabled;
            preferenceLoaded = true;
            ApplySoundState(enabled);
        }

        /// <summary>Releases the sources when their owning scene object is destroyed.</summary>
        public static void Release(AudioSource audioSource, AudioSource clockTickSource = null)
        {
            if (source == audioSource)
                source = null;

            if (clockTickSource != null && clockTickingSource == clockTickSource)
            {
                if (clockTickingSource.isPlaying)
                    clockTickingSource.Stop();
                clockTickingSource = null;
                clockTickingRequested = false;
            }
        }

        public static void ButtonClicked()
        {
            if (config != null) Play(buttonClicks.Next(), config.ButtonClickVolume);
        }

        public static void PieceDropped()
        {
            if (config != null) Play(pieceDrops.Next(), config.PieceDropVolume);
        }

        public static void ProgressBarFilled()
        {
            if (config == null)
                return;

            float now = Time.unscaledTime;
            if (now < nextBarFillSoundTime)
                return;

            nextBarFillSoundTime = now + config.BarFillMinInterval;
            Play(barFills.Next(), config.BarFillVolume);
        }

        public static void HammerBreak()
        {
            if (config != null) Play(hammerBreaks.Next(), config.HammerBreakVolume);
        }

        public static void FreezeTimerImpact()
        {
            if (config != null) Play(freezeTimerImpacts.Next(), config.FreezeTimerImpactVolume);
        }

        public static void RocketLaunch()
        {
            if (config != null) Play(rocketLaunches.Next(), config.RocketLaunchVolume);
        }

        /// <summary>
        /// Controls the looping countdown-urgency clock ticking track. Stops immediately
        /// whenever the countdown pauses, freezes, exits the urgency window, or sound is muted.
        /// </summary>
        public static void SetClockTickingActive(bool active)
        {
            if (clockTickingRequested == active)
            {
                if (active && clockTickingSource != null && IsSoundEnabled &&
                    clockTickingSource.clip != null && !clockTickingSource.isPlaying)
                {
                    clockTickingSource.Play();
                }
                return;
            }

            clockTickingRequested = active;
            UpdateClockTickingPlayback();
        }

        private static void EnsurePreferenceLoaded()
        {
            if (preferenceLoaded)
                return;

            soundEnabled = PlayerPrefs.GetInt(SoundPreferenceKey, 1) == 1;
            preferenceLoaded = true;
            AudioListener.volume = soundEnabled ? 1f : 0f;
        }

        private static void ApplySoundState(bool enabled)
        {
            AudioListener.volume = enabled ? 1f : 0f;
            if (source != null)
            {
                source.mute = !enabled;
                if (!enabled)
                    source.Stop();
            }

            UpdateClockTickingPlayback();

            SoundEnabledChanged?.Invoke(enabled);
        }

        private static void UpdateClockTickingPlayback()
        {
            if (clockTickingSource == null)
                return;

            bool enabled = IsSoundEnabled;
            clockTickingSource.mute = !enabled;

            if (!clockTickingRequested || !enabled || config == null || config.ClockTickingClip == null)
            {
                if (clockTickingSource.isPlaying)
                    clockTickingSource.Stop();
                return;
            }

            if (clockTickingSource.clip != config.ClockTickingClip)
                clockTickingSource.clip = config.ClockTickingClip;
            clockTickingSource.volume = config.ClockTickingVolume;
            clockTickingSource.loop = true;

            if (!clockTickingSource.isPlaying)
                clockTickingSource.Play();
        }

        private static void Play(AudioClip clip, float volume)
        {
            if (clip == null || source == null || !IsSoundEnabled)
                return;

            source.PlayOneShot(clip, volume);
        }

        /// <summary>
        /// Plays every clip once in random order, then reshuffles. With three or more clips the
        /// first clip of a new round never repeats the last clip of the previous round.
        /// </summary>
        private sealed class ShuffledClipSequence
        {
            private AudioClip[] clips;
            private int[] order = new int[0];
            private int cursor;
            private int lastIndex = -1;

            public void Reset(AudioClip[] value)
            {
                clips = value;
                int count = clips != null ? clips.Length : 0;
                if (order.Length != count)
                    order = new int[count];
                cursor = count;
                lastIndex = -1;
            }

            public AudioClip Next()
            {
                if (order.Length == 0)
                    return null;

                if (cursor >= order.Length)
                    Reshuffle();

                lastIndex = order[cursor++];
                return clips[lastIndex];
            }

            private void Reshuffle()
            {
                for (int index = 0; index < order.Length; index++)
                    order[index] = index;

                for (int index = order.Length - 1; index > 0; index--)
                {
                    int swapIndex = UnityEngine.Random.Range(0, index + 1);
                    (order[index], order[swapIndex]) = (order[swapIndex], order[index]);
                }

                if (order.Length > 2 && order[0] == lastIndex)
                    (order[0], order[order.Length - 1]) = (order[order.Length - 1], order[0]);

                cursor = 0;
            }
        }
    }
}
