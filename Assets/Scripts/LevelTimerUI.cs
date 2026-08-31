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
        public static LevelTimerUI Active { get; private set; }

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

        [Header("Scene Navigation")]
        [Tooltip("Name of your Main Menu scene. Leave blank to reload current level directly.")]
        public string mainMenuSceneName = "";

        // Global flag that other systems (like PuzzleDragController) can check to disable input
        public static bool IsGameOver { get; private set; }

        private PrototypeBoard board;
        private int lastDisplayedSecond = -1;
        private bool timerPresentationLocked;

        private void Awake()
        {
            // Always ensure the game state is reset when this script wakes up (e.g. on scene reload)
            IsGameOver = false;

            // Keep the fail overlay out of the EventSystem raycast results as
            // soon as the scene is initialized. Waiting until Start leaves a
            // frame in which its full-panel Image can reject a booster target
            // tap, depending on Unity's component update order.
            if (failPopupPanel != null)
                failPopupPanel.SetActive(false);
        }

        private void OnEnable()
        {
            Active = this;
            BindBoard(PrototypeBoard.Active);
        }

        private void OnDisable()
        {
            BindBoard(null);

            if (Active == this)
                Active = null;
        }

        private void Start()
        {
            if (retryButton != null)
                retryButton.onClick.AddListener(OnRetryClicked);
                
            if (mainMenuButton != null)
                mainMenuButton.onClick.AddListener(OnMainMenuClicked);

            SetTimerVisible(false);
        }

        private void Update()
        {
            BindBoard(PrototypeBoard.Active);

            bool hasTimeLimit = board != null && board.TimeLimit > 0f && !timerPresentationLocked;
            SetTimerVisible(hasTimeLimit);
            if (!hasTimeLimit)
                return;

            UpdateTimerDisplay(board.TimeRemaining);
        }

        private void BindBoard(PrototypeBoard nextBoard)
        {
            if (board == nextBoard)
                return;

            if (board != null)
            {
                board.LevelFailed -= HandleLevelFailed;
                board.GameStateChanged -= HandleGameStateChanged;
            }

            board = nextBoard;
            lastDisplayedSecond = -1;
            timerPresentationLocked = board != null &&
                (board.GameState == GravityPuzzle.Core.StateMachine.GameState.LevelComplete ||
                 board.GameState == GravityPuzzle.Core.StateMachine.GameState.Result);

            if (board != null)
            {
                board.LevelFailed += HandleLevelFailed;
                board.GameStateChanged += HandleGameStateChanged;
            }
        }

        private void HandleLevelFailed()
        {
            ShowFailPopup();
        }

        private void HandleGameStateChanged(
            GravityPuzzle.Core.StateMachine.GameState previousState,
            GravityPuzzle.Core.StateMachine.GameState nextState)
        {
            timerPresentationLocked =
                nextState == GravityPuzzle.Core.StateMachine.GameState.LevelComplete ||
                nextState == GravityPuzzle.Core.StateMachine.GameState.Result;

            if (timerPresentationLocked)
                SetTimerVisible(false);
        }

        private void SetTimerVisible(bool visible)
        {
            if (timerText != null && timerText.gameObject.activeSelf != visible)
                timerText.gameObject.SetActive(visible);
        }

        private void UpdateTimerDisplay(float timeRemaining)
        {
            if (timerText == null) return;

            int totalSeconds = Mathf.CeilToInt(timeRemaining);
            if (totalSeconds == lastDisplayedSecond)
                return;

            lastDisplayedSecond = totalSeconds;
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
            GravityLevelRuntime.RequestRestart();
            if (!string.IsNullOrEmpty(mainMenuSceneName))
            {
                SceneManager.LoadScene(mainMenuSceneName);
            }
            else
            {
                Scene activeScene = SceneManager.GetActiveScene();
                SceneManager.LoadScene(activeScene.name);
            }
        }
    }
}
