using UnityEngine;
using UnityEngine.UI;
using GravityPuzzle.Presentation.Views;

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
        private bool listenersBound;
        private PrototypeBoard pausedBoard;
        private SettingsPopupView settingsPopupView;

        private void Awake()
        {
            soundEnabled = PlayerPrefs.GetInt(SoundPreferenceKey, 1) == 1;
            musicEnabled = PlayerPrefs.GetInt(MusicPreferenceKey, 1) == 1;

            if (settingsButton == null)
                settingsButton = GetComponent<Button>();

            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
                settingsPopupView = settingsPanel.GetComponent<SettingsPopupView>();
            }

            BindListeners();
            ApplyAudioState();
        }

        private void OnEnable()
        {
            BindListeners();
        }

        private void BindListeners()
        {
            if (listenersBound)
                return;

            if (settingsPopupView != null)
                settingsPopupView.Closed += CloseSettingsPanel;

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

            listenersBound = true;
        }

        private void OnDisable()
        {
            RemoveListenersAndResumeTimer();
        }

        private void OnDestroy()
        {
            RemoveListenersAndResumeTimer();
        }

        private void RemoveListenersAndResumeTimer()
        {
            if (!listenersBound)
                return;

            if (settingsButton != null)
                settingsButton.onClick.RemoveListener(ToggleSettingsPanel);
            if (soundButton != null)
                soundButton.onClick.RemoveListener(ToggleSound);
            if (musicButton != null)
                musicButton.onClick.RemoveListener(ToggleMusic);
            if (settingsPopupView != null)
                settingsPopupView.Closed -= CloseSettingsPanel;

            listenersBound = false;
            ResumeTimer();
        }

        public void ToggleSettingsPanel()
        {
            if (settingsPanel != null)
                SetSettingsPanelVisible(!settingsPanel.activeSelf);
        }

        /// <summary>Called by the popup's close control to restore timer ownership.</summary>
        public void CloseSettingsPanel()
        {
            SetSettingsPanelVisible(false);
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

        private void SetSettingsPanelVisible(bool visible)
        {
            if (settingsPanel == null)
                return;

            if (visible)
            {
                settingsPanel.SetActive(true);
                PauseTimer();
                return;
            }

            ResumeTimer();
            settingsPanel.SetActive(false);
        }

        private void PauseTimer()
        {
            if (pausedBoard != null)
                return;

            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard != null && activeBoard.TryPauseTimer(this))
                pausedBoard = activeBoard;
        }

        private void ResumeTimer()
        {
            if (pausedBoard == null)
                return;

            pausedBoard.ResumeTimer(this);
            pausedBoard = null;
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
