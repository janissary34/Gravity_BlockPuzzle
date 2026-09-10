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

        [Tooltip("Slider that visually represents the remaining level time.")]
        [SerializeField] private Slider timerSlider;

        [Header("Fail Popup")]
        [Tooltip("The popup panel to show when the timer runs out")]
        public GameObject failPopupPanel;

        [Header("Buttons")]
        [Tooltip("Drag your Retry button here")]
        public Button retryButton;
        
        [Tooltip("Drag your Main Menu button here")]
        public Button mainMenuButton;

        [Header("Retry Confirmation Panel")]
        [Tooltip("Confirmation panel shown before the current level is restarted.")]
        [SerializeField] private GameObject retryConfirmationPanel;

        [Tooltip("HUD button that opens the retry confirmation panel.")]
        [SerializeField] private Button openRetryConfirmationButton;

        [Tooltip("Button in the confirmation panel that restarts the current level.")]
        [SerializeField] private Button confirmRetryButton;

        [Tooltip("Button in the confirmation panel that dismisses it.")]
        [SerializeField] private Button closeRetryConfirmationButton;

        [Tooltip("Legacy settings handler on the replay icon. It is closed before the retry panel opens.")]
        [SerializeField] private SettingsPanelButton retryButtonSettingsHandler;

        [Header("Scene Navigation")]
        [Tooltip("Name of your Main Menu scene. Leave blank to reload current level directly.")]
        public string mainMenuSceneName = "";

        // Global flag that other systems (like PuzzleDragController) can check to disable input
        public static bool IsGameOver { get; private set; }

        private PrototypeBoard board;
        private int lastDisplayedSecond = -1;
        private bool timerPresentationLocked;
        private float configuredSliderTimeLimit = -1f;

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

            if (retryConfirmationPanel != null)
                retryConfirmationPanel.SetActive(false);
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

            if (openRetryConfirmationButton != null)
                openRetryConfirmationButton.onClick.AddListener(OpenRetryConfirmation);

            if (confirmRetryButton != null)
                confirmRetryButton.onClick.AddListener(OnRetryClicked);

            if (closeRetryConfirmationButton != null)
                closeRetryConfirmationButton.onClick.AddListener(CloseRetryConfirmation);

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
            UpdateTimerSlider(board.TimeRemaining);
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
            configuredSliderTimeLimit = -1f;
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

            if (timerSlider != null && timerSlider.gameObject.activeSelf != visible)
                timerSlider.gameObject.SetActive(visible);
        }

        private void UpdateTimerSlider(float timeRemaining)
        {
            if (timerSlider == null || board == null)
                return;

            float timeLimit = Mathf.Max(0f, board.TimeLimit);
            if (!Mathf.Approximately(configuredSliderTimeLimit, timeLimit))
            {
                configuredSliderTimeLimit = timeLimit;
                timerSlider.minValue = 0f;
                timerSlider.maxValue = timeLimit;
            }

            timerSlider.SetValueWithoutNotify(Mathf.Clamp(timeRemaining, 0f, timeLimit));
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
                // Keep the authored black timer colour until the final warning window.
                timerText.color = Color.black;
            }
        }

        public void ShowFailPopup()
        {
            IsGameOver = true;
            
            if (failPopupPanel != null)
                failPopupPanel.SetActive(true);
        }

        public void OpenRetryConfirmation()
        {
            if (retryButtonSettingsHandler != null)
                retryButtonSettingsHandler.CloseSettingsPanel();

            if (retryConfirmationPanel != null)
                retryConfirmationPanel.SetActive(true);
        }

        public void CloseRetryConfirmation()
        {
            if (retryConfirmationPanel != null)
                retryConfirmationPanel.SetActive(false);
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
