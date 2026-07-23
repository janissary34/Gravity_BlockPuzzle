using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace GravityPuzzle
{
    /// <summary>
    /// Component to control your Main Menu UI scene.
    /// Attach this to your Main Menu Canvas or Controller GameObject.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [Tooltip("Button to start playing the active level.")]
        public Button playButton;

        [Tooltip("Optional. Text component displaying current level number (e.g. 'Level 1').")]
        public TMP_Text levelText;

        [Header("Scene Settings")]
        [Tooltip("Name of your gameplay level scene (e.g. 'Scene1'). Leave blank to load Build Index 1.")]
        public string gameSceneName = "Scene1";

        private void Start()
        {
            if (playButton != null)
                playButton.onClick.AddListener(OnPlayClicked);

            UpdateLevelText();
        }

        public void UpdateLevelText()
        {
            if (levelText != null)
            {
                int currentLevel = GravityLevelRuntime.CurrentLevelNumber;
                levelText.text = $"Level {currentLevel}";
            }
        }

        public void OnPlayClicked()
        {
            // Tells the level runtime to skip any prototype menus and immediately start the level
            GravityLevelRuntime.RequestRestart();

            if (!string.IsNullOrEmpty(gameSceneName))
            {
                SceneManager.LoadScene(gameSceneName);
            }
            else
            {
                SceneManager.LoadScene(1);
            }
        }
    }
}
