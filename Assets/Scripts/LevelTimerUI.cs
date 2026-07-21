using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace GravityPuzzle
{
    /// <summary>
    /// Manages the level countdown timer and handles the game over (fail) flow.
    /// This script is designed to be fully modular so it can be dragged into any project.
    /// </summary>
    public class LevelTimerUI : MonoBehaviour
    {
        [Header("Timer UI")]
        [Tooltip("The text component that displays the remaining time (e.g., 01:30)")]
        public TMP_Text timerText;
        
        [Header("Fail Popup")]
        [Tooltip("The popup panel to show when the timer runs out")]
        public GameObject failPopupPanel;

        [Header("Buttons")]
        [Tooltip("Drag your Retry button here")]
        public Button retryButton;
        
        [Tooltip("Drag your Main Menu button here")]
        public Button mainMenuButton;

        // Global flag that other systems (like PuzzleDragController) can check to disable input
        public static bool IsGameOver { get; private set; }

        private float timeRemaining;
        private bool hasTimeLimit;

        private void Awake()
        {
            // Always ensure the game state is reset when this script wakes up (e.g. on scene reload)
            IsGameOver = false;
        }

        private void Start()
        {
            if (failPopupPanel != null)
                failPopupPanel.SetActive(false);
                
            if (retryButton != null)
                retryButton.onClick.AddListener(OnRetryClicked);
                
            if (mainMenuButton != null)
                mainMenuButton.onClick.AddListener(OnMainMenuClicked);

            // Fetch the active level definition to get the dynamically configured time limit
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            
            if (level != null && level.timeLimit > 0f)
            {
                hasTimeLimit = true;
                timeRemaining = level.timeLimit;
            }
            else
            {
                hasTimeLimit = false;
                if (timerText != null)
                {
                    timerText.gameObject.SetActive(false);
                }
            }
            
            UpdateTimerDisplay();
        }

        private void Update()
        {
            // Stop updating if the game is over or there is no timer
            if (IsGameOver || !hasTimeLimit)
                return;

            timeRemaining -= Time.deltaTime;

            if (timeRemaining <= 0f)
            {
                timeRemaining = 0f;
                ShowFailPopup();
            }

            UpdateTimerDisplay();
        }

        private void UpdateTimerDisplay()
        {
            if (timerText == null) return;

            int totalSeconds = Mathf.CeilToInt(timeRemaining);
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            timerText.text = $"{minutes:00}:{seconds:00}";
            
            if (totalSeconds <= 5 && totalSeconds > 0)
            {
                timerText.color = Color.red;
            }
            else
            {
                // Maintain default white color before the final 5 seconds
                timerText.color = Color.white; 
            }
        }

        public void ShowFailPopup()
        {
            IsGameOver = true;
            
            if (failPopupPanel != null)
                failPopupPanel.SetActive(true);
        }

        // Hook this up to your "Retry" button's OnClick event in the inspector
        public void OnRetryClicked()
        {
            IsGameOver = false;
            // Tell the runtime to skip the main menu and immediately start this level again
            GravityLevelRuntime.RequestRestart();
            // Reload the current level from scratch
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }

        // Hook this up to your "Main Menu" button's OnClick event in the inspector
        public void OnMainMenuClicked()
        {
            IsGameOver = false;
            // Assuming 0 is the main menu scene index. Change this if your main menu has a specific name.
            SceneManager.LoadScene(0); 
        }
    }
}
