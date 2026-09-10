using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Owns the presentation-only interactions for the authored settings popup.
    /// Audio routing remains owned by the existing SettingsPanelButton component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPopupView : MonoBehaviour
    {
        private const string VibrationPreferenceKey = "GravityPuzzle.VibrationEnabled";

        [SerializeField] private Button closeButton;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button soundToggleButton;
        [SerializeField] private Button musicToggleButton;
        [SerializeField] private Button vibrationToggleButton;
        [SerializeField] private Image soundToggleImage;
        [SerializeField] private Image musicToggleImage;
        [SerializeField] private Image vibrationToggleImage;
        [SerializeField] private Sprite toggleOffSprite;
        [SerializeField] private Sprite toggleOnSprite;
        [SerializeField] private string mainMenuSceneName = "Main_Menu";

        private bool soundEnabled;
        private bool musicEnabled;
        private bool vibrationEnabled;
        private PrototypeBoard pausedBoard;

        public event Action Closed;

        private void Awake()
        {
            soundEnabled = PlayerPrefs.GetInt("GravityPuzzle.SoundEnabled", 1) == 1;
            musicEnabled = PlayerPrefs.GetInt("GravityPuzzle.MusicEnabled", 1) == 1;
            vibrationEnabled = PlayerPrefs.GetInt(VibrationPreferenceKey, 1) == 1;

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
            if (resumeButton != null)
                resumeButton.onClick.AddListener(Close);
            if (quitButton != null)
                quitButton.onClick.AddListener(QuitToMainMenu);
            if (soundToggleButton != null)
                soundToggleButton.onClick.AddListener(ToggleSoundVisual);
            if (musicToggleButton != null)
                musicToggleButton.onClick.AddListener(ToggleMusic);
            if (vibrationToggleButton != null)
                vibrationToggleButton.onClick.AddListener(ToggleVibration);

            RefreshToggleVisuals();
        }

        private void OnEnable()
        {
            // The popup can be opened through a parent overlay as well as the
            // Settings button. Its active state is therefore the authoritative
            // presentation signal for this pause owner.
            if (pausedBoard != null)
                return;

            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard != null && activeBoard.TryPauseTimer(this))
                pausedBoard = activeBoard;
        }

        private void OnDisable()
        {
            ResumeTimer();
        }

        private void OnDestroy()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
            if (resumeButton != null)
                resumeButton.onClick.RemoveListener(Close);
            if (quitButton != null)
                quitButton.onClick.RemoveListener(QuitToMainMenu);
            if (soundToggleButton != null)
                soundToggleButton.onClick.RemoveListener(ToggleSoundVisual);
            if (musicToggleButton != null)
                musicToggleButton.onClick.RemoveListener(ToggleMusic);
            if (vibrationToggleButton != null)
                vibrationToggleButton.onClick.RemoveListener(ToggleVibration);

            ResumeTimer();
        }

        private void Close()
        {
            Closed?.Invoke();
            gameObject.SetActive(false);
        }

        private void ToggleSoundVisual()
        {
            soundEnabled = !soundEnabled;
            PlayerPrefs.SetInt("GravityPuzzle.SoundEnabled", soundEnabled ? 1 : 0);
            PlayerPrefs.Save();
            RefreshToggleVisuals();
        }

        private void ToggleMusic()
        {
            musicEnabled = !musicEnabled;
            PlayerPrefs.SetInt("GravityPuzzle.MusicEnabled", musicEnabled ? 1 : 0);
            PlayerPrefs.Save();
            RefreshToggleVisuals();
        }

        private void ToggleVibration()
        {
            vibrationEnabled = !vibrationEnabled;
            PlayerPrefs.SetInt(VibrationPreferenceKey, vibrationEnabled ? 1 : 0);
            PlayerPrefs.Save();
            RefreshToggleVisuals();
        }

        private void RefreshToggleVisuals()
        {
            if (soundToggleImage != null)
                soundToggleImage.sprite = soundEnabled ? toggleOnSprite : toggleOffSprite;
            if (musicToggleImage != null)
                musicToggleImage.sprite = musicEnabled ? toggleOnSprite : toggleOffSprite;
            if (vibrationToggleImage != null)
                vibrationToggleImage.sprite = vibrationEnabled ? toggleOnSprite : toggleOffSprite;
        }

        private void QuitToMainMenu()
        {
            Closed?.Invoke();
            ResumeTimer();
            SceneManager.LoadScene(mainMenuSceneName);
        }

        private void ResumeTimer()
        {
            if (pausedBoard == null)
                return;

            pausedBoard.ResumeTimer(this);
            pausedBoard = null;
        }
    }
}
