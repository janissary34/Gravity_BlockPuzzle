using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace GravityPuzzle
{
    public class LevelTimerUI : MonoBehaviour
    {
        [Header("Timer UI")]
        [Tooltip("The text component that displays the remaining time (e.g., 01:30)")]
        public Text timerText;
        
        [Header("Fail Popup")]
        [Tooltip("The popup panel to show when the timer runs out")]
        public GameObject failPopupPanel;

        private PrototypeBoard board;

        private void Start()
        {
            if (failPopupPanel != null)
                failPopupPanel.SetActive(false);

            board = Object.FindObjectOfType<PrototypeBoard>();
        }

        private void Update()
        {
            if (board == null)
                return;

            if (board.TimeLimit > 0f && timerText != null)
            {
                int totalSeconds = Mathf.CeilToInt(board.TimeRemaining);
                int minutes = totalSeconds / 60;
                int seconds = totalSeconds % 60;
                timerText.text = $"{minutes:00}:{seconds:00}";
                
                if (totalSeconds <= 5)
                {
                    timerText.color = Color.red;
                }
            }
            else if (timerText != null && timerText.gameObject.activeSelf)
            {
                // If there's no time limit, you might want to hide the timer.
                timerText.gameObject.SetActive(false);
            }
        }

        // Called from PrototypeBoard when the fail condition is met.
        public void ShowFailPopup()
        {
            if (failPopupPanel != null)
                failPopupPanel.SetActive(true);
        }

        // Hook this up to your "Retry" button's OnClick event in the inspector
        public void OnRetryClicked()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }

        // Hook this up to your "Main Menu" button's OnClick event in the inspector
        public void OnMainMenuClicked()
        {
            // Assuming 0 is the main menu scene index. Change this if your main menu has a specific name, e.g., "MainMenu"
            SceneManager.LoadScene(0); 
        }
    }
}
