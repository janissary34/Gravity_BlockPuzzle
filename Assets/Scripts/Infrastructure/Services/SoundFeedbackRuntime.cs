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
        private static bool preferenceLoaded;
        private static bool soundEnabled = true;
        private static readonly ShuffledClipSequence buttonClicks = new ShuffledClipSequence();
        private static readonly ShuffledClipSequence pieceDrops = new ShuffledClipSequence();
        private static readonly ShuffledClipSequence hammerBreaks = new ShuffledClipSequence();

        public static event Action<bool> SoundEnabledChanged;

        public static bool IsSoundEnabled
        {
            get
            {
                EnsurePreferenceLoaded();
                return soundEnabled;
            }
        }

        public static void Configure(SoundFeedbackConfig value, AudioSource audioSource)
        {
            config = value;
            source = audioSource;
            buttonClicks.Reset(config != null ? config.ButtonClickClips : null);
            pieceDrops.Reset(config != null ? config.PieceDropClips : null);
            hammerBreaks.Reset(config != null ? config.HammerBreakClips : null);

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

        /// <summary>Releases the source when its owning scene object is destroyed.</summary>
        public static void Release(AudioSource audioSource)
        {
            if (source == audioSource)
                source = null;
        }

        public static void ButtonClicked()
        {
            if (config != null) Play(buttonClicks.Next(), config.ButtonClickVolume);
        }

        public static void PieceDropped()
        {
            if (config != null) Play(pieceDrops.Next(), config.PieceDropVolume);
        }

        public static void HammerBreak()
        {
            if (config != null) Play(hammerBreaks.Next(), config.HammerBreakVolume);
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

            SoundEnabledChanged?.Invoke(enabled);
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
