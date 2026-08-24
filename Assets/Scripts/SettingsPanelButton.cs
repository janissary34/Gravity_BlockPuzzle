using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle
{
    /// <summary>Opens and closes the authored settings panel from Settings_btn.</summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanelButton : MonoBehaviour
    {
        private const string SoundPreferenceKey = "GravityPuzzle.SoundEnabled";
        private const string MusicPreferenceKey = "GravityPuzzle.MusicEnabled";

        [Header("Authored UI References")]
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button soundButton;
        [SerializeField] private Button musicButton;

        [Header("Authored Audio References")]
        [SerializeField] private AudioSource[] soundSources;
        [SerializeField] private AudioSource[] musicSources;
        private bool soundEnabled;
        private bool musicEnabled;

        private void Awake()
        {
            soundEnabled = PlayerPrefs.GetInt(SoundPreferenceKey, 1) == 1;
            musicEnabled = PlayerPrefs.GetInt(MusicPreferenceKey, 1) == 1;

            if (settingsButton == null)
                settingsButton = GetComponent<Button>();

            if (settingsPanel != null)
                settingsPanel.SetActive(false);

            if (settingsButton != null)
                settingsButton.onClick.AddListener(ToggleSettingsPanel);
            else
                Debug.LogWarning("[Settings] Settings button reference is missing.", this);

            if (soundButton != null)
                soundButton.onClick.AddListener(ToggleSound);
            else
                Debug.LogWarning("[Settings] Sound button reference is missing.", this);

            if (musicButton != null)
                musicButton.onClick.AddListener(ToggleMusic);
            else
                Debug.LogWarning("[Settings] Music button reference is missing.", this);

            ApplyAudioState();
        }

        private void OnDestroy()
        {
            if (settingsButton != null)
                settingsButton.onClick.RemoveListener(ToggleSettingsPanel);
            if (soundButton != null)
                soundButton.onClick.RemoveListener(ToggleSound);
            if (musicButton != null)
                musicButton.onClick.RemoveListener(ToggleMusic);
        }

        public void ToggleSettingsPanel()
        {
            if (settingsPanel != null)
                settingsPanel.SetActive(!settingsPanel.activeSelf);
        }

        public void ToggleSound()
        {
            soundEnabled = !soundEnabled;
            PlayerPrefs.SetInt(SoundPreferenceKey, soundEnabled ? 1 : 0);
            PlayerPrefs.Save();
            ApplyAudioState();
        }

        public void ToggleMusic()
        {
            musicEnabled = !musicEnabled;
            PlayerPrefs.SetInt(MusicPreferenceKey, musicEnabled ? 1 : 0);
            PlayerPrefs.Save();
            ApplyAudioState();
        }

        private void ApplyAudioState()
        {
            ApplyMuteState(soundSources, !soundEnabled);
            ApplyMuteState(musicSources, !musicEnabled);
        }

        private static void ApplyMuteState(AudioSource[] sources, bool muted)
        {
            if (sources == null)
                return;

            for (int index = 0; index < sources.Length; index++)
            {
                AudioSource source = sources[index];
                if (source != null)
                    source.mute = muted;
            }
        }
    }
}
