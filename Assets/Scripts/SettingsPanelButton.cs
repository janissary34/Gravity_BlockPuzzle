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

        [SerializeField] private GameObject settingsPanel;
        private Button settingsButton;
        private Button soundButton;
        private Button musicButton;
        private bool soundEnabled;
        private bool musicEnabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ConnectAuthoredSettingsUi()
        {
            EnsureConnected();
        }

        /// <summary>Re-applies the settings button hookup after every level reload.</summary>
        public static void EnsureConnected()
        {
            GameObject settingsObject = FindSceneObject("Settings_btn");
            if (settingsObject != null && settingsObject.GetComponent<SettingsPanelButton>() == null)
                settingsObject.AddComponent<SettingsPanelButton>();
        }

        private void Awake()
        {
            soundEnabled = PlayerPrefs.GetInt(SoundPreferenceKey, 1) == 1;
            musicEnabled = PlayerPrefs.GetInt(MusicPreferenceKey, 1) == 1;

            settingsButton = GetComponent<Button>();
            if (settingsButton == null)
            {
                settingsButton = gameObject.AddComponent<Button>();
                settingsButton.targetGraphic = GetComponent<Graphic>();
            }

            settingsPanel = settingsPanel != null
                ? settingsPanel
                : FindSceneObject("Setting_panel");

            if (settingsPanel != null)
                settingsPanel.SetActive(false);

            settingsButton.onClick.AddListener(ToggleSettingsPanel);
            ConnectAudioButton("Sound_btn", ToggleSound, out soundButton);
            ConnectAudioButton("Music_btn", ToggleMusic, out musicButton);
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

        private void ConnectAudioButton(string objectName, UnityEngine.Events.UnityAction action, out Button button)
        {
            GameObject buttonObject = FindSceneObject(objectName);
            button = buttonObject != null ? buttonObject.GetComponent<Button>() : null;
            if (button != null)
                button.onClick.AddListener(action);
        }

        private void ApplyAudioState()
        {
            // Looping sources are treated as music; all other sources are SFX.
            // This lets the two UI controls be toggled independently.
            foreach (AudioSource source in FindObjectsOfType<AudioSource>(true))
                source.mute = source.loop ? !musicEnabled : !soundEnabled;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate.scene.IsValid() && candidate.name == objectName)
                    return candidate;
            }

            return null;
        }
    }
}
