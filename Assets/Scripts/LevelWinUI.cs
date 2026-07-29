using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace GravityPuzzle
{
    /// <summary>
    /// Component to control custom Win UI scenes or victory popups.
    /// Attach this to your Win UI Scene or Victory Popup Canvas and wire up your UI buttons.
    /// </summary>
    public class LevelWinUI : MonoBehaviour
    {
        [Header("UI Buttons")]
        [Tooltip("Button to advance to the next level and load the game scene.")]
        public Button nextLevelButton;

        [Tooltip("Button to restart the current level.")]
        public Button retryButton;

        [Tooltip("Button to return to the main menu.")]
        public Button mainMenuButton;

        [Header("Scene Settings")]
        [Tooltip("Name of your main gameplay level scene (e.g. 'Scene1'). Leave blank to reload the active scene.")]
        public string gameSceneName = "Scene1";

        [Tooltip("Name of your main menu scene (e.g. 'Main_Menu').")]
        public string mainMenuSceneName = "Main_Menu";

        private void Start()
        {
            if (nextLevelButton != null)
                nextLevelButton.onClick.AddListener(OnNextLevelClicked);

            if (retryButton != null)
                retryButton.onClick.AddListener(OnRetryClicked);

            if (mainMenuButton != null)
                mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        }

        public void OnNextLevelClicked()
        {
            // Advance to the next level index if available
            if (GravityLevelRuntime.HasNextLevel)
            {
                GravityLevelRuntime.TryAdvanceToNextLevel();
            }

            // Flag restart so main menu is skipped and active level starts immediately
            GravityLevelRuntime.RequestRestart();
            LoadGameScene();
        }

        public void OnRetryClicked()
        {
            GravityLevelRuntime.RequestRestart();
            LoadGameScene();
        }

        public void OnMainMenuClicked()
        {
            if (!string.IsNullOrEmpty(mainMenuSceneName))
                SceneManager.LoadScene(mainMenuSceneName);
            else
                SceneManager.LoadScene("Main_Menu");
        }

        private void LoadGameScene()
        {
            if (!string.IsNullOrEmpty(gameSceneName))
            {
                SceneManager.LoadScene(gameSceneName);
            }
            else
            {
                Scene activeScene = SceneManager.GetActiveScene();
                SceneManager.LoadScene(activeScene.name);
            }
        }
    }
}
