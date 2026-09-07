using UnityEngine;
using UnityEngine.UI;

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
        [SerializeField] private Button soundToggleButton;
        [SerializeField] private Button vibrationToggleButton;
        [SerializeField] private Image soundToggleImage;
        [SerializeField] private Image vibrationToggleImage;
        [SerializeField] private Sprite toggleOffSprite;
        [SerializeField] private Sprite toggleOnSprite;

        private bool soundEnabled;
        private bool vibrationEnabled;

        private void Awake()
        {
            soundEnabled = PlayerPrefs.GetInt("GravityPuzzle.SoundEnabled", 1) == 1;
            vibrationEnabled = PlayerPrefs.GetInt(VibrationPreferenceKey, 1) == 1;

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
            if (soundToggleButton != null)
                soundToggleButton.onClick.AddListener(ToggleSoundVisual);
            if (vibrationToggleButton != null)
                vibrationToggleButton.onClick.AddListener(ToggleVibration);

            RefreshToggleVisuals();
        }

        private void OnDestroy()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
            if (soundToggleButton != null)
                soundToggleButton.onClick.RemoveListener(ToggleSoundVisual);
            if (vibrationToggleButton != null)
                vibrationToggleButton.onClick.RemoveListener(ToggleVibration);
        }

        private void Close()
        {
            gameObject.SetActive(false);
        }

        private void ToggleSoundVisual()
        {
            soundEnabled = !soundEnabled;
            PlayerPrefs.SetInt("GravityPuzzle.SoundEnabled", soundEnabled ? 1 : 0);
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
            if (vibrationToggleImage != null)
                vibrationToggleImage.sprite = vibrationEnabled ? toggleOnSprite : toggleOffSprite;
        }
    }
}
