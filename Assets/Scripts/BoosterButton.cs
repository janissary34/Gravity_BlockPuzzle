using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GravityPuzzle
{
    /// <summary>
    /// Attach this script directly to any Booster UI Button GameObject (e.g. rocket_booster_btn, hammer_btn).
    /// Starts count at initialCount (default 3), updates child text,
    /// decrements count by 1 when booster animation completes, and locks button when count reaches 0.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class BoosterButton : MonoBehaviour, IPointerClickHandler
    {
        [Header("Booster Count Settings")]
        [SerializeField, Tooltip("Starting number of booster uses per level.")]
        private int initialCount = 3;

        [Header("UI Text References")]
        [SerializeField, Tooltip("Text component displaying count (TextMeshPro). Auto-found if null.")]
        private TextMeshProUGUI countTmpText;

        [SerializeField, Tooltip("Text component displaying count (Legacy UI Text). Auto-found if null.")]
        private Text countUiText;

        [Header("Events")]
        [Tooltip("Event fired when the booster button is clicked and has remaining uses.")]
        public UnityEvent onBoosterClicked;

        private Button button;
        private RocketBooster rocketBooster;
        private HammerBooster hammerBooster;
        private TimerBooster timerBooster;
        private int remainingCount = 3;
        private int lastDispatchFrame = -1;

        public int RemainingCount => remainingCount;
        public bool HasUses => remainingCount > 0;
        public Button ButtonComponent => button;

        private void Awake()
        {
            button = GetComponent<Button>();
            rocketBooster = GetComponent<RocketBooster>();
            hammerBooster = GetComponent<HammerBooster>();
            timerBooster = GetComponent<TimerBooster>();
            remainingCount = initialCount;
            FindCountTextReferences();
        }

        private void OnEnable()
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (button != null)
            {
                button.onClick.RemoveListener(HandleButtonClick);
                button.onClick.AddListener(HandleButtonClick);
            }

            UpdateCountUI();
            RefreshButtonState();
        }

        private void OnDisable()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleButtonClick);
            }
        }

        /// <summary>
        /// Resets the booster count back to initialCount (default 3) or specified number.
        /// </summary>
        public void ResetCount(int count = -1)
        {
            remainingCount = count >= 0 ? count : initialCount;
            UpdateCountUI();
            RefreshButtonState();
        }

        /// <summary>
        /// Attempts to consume 1 use count. Decrements count by 1 and updates UI text.
        /// Returns true if successful, false if 0 uses remaining.
        /// </summary>
        public bool TryConsumeUse()
        {
            if (remainingCount <= 0)
            {
                RefreshButtonState();
                return false;
            }

            remainingCount--;
            if (remainingCount < 0) remainingCount = 0;
            Debug.Log($"[BoosterButton] Decremented count to: {remainingCount}");
            UpdateCountUI();
            RefreshButtonState();
            return true;
        }

        private void HandleButtonClick()
        {
            DispatchBoosterClick();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                DispatchBoosterClick();
            }
        }

        private void DispatchBoosterClick()
        {
            if (lastDispatchFrame == Time.frameCount)
            {
                return;
            }

            lastDispatchFrame = Time.frameCount;

            if (remainingCount <= 0)
            {
                RefreshButtonState();
                return;
            }

            // A button controls only the booster component on the same prefab.
            // This avoids routing a click to a stale or unrelated booster instance.
            if (rocketBooster != null)
            {
                rocketBooster.ActivateRocketBooster();
            }
            else if (hammerBooster != null)
            {
                hammerBooster.ActivateHammerBooster();
            }
            else if (timerBooster != null)
            {
                timerBooster.PlayTimerBoosterSequence();
            }

            onBoosterClicked?.Invoke();
        }

        private void FindCountTextReferences()
        {
            if (countTmpText != null || countUiText != null)
                return;

            // 1. Search all children for a text component on a GameObject containing "count" in name
            var allTmps = GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var tmp in allTmps)
            {
                if (tmp != null && tmp.gameObject.name.ToLower().Contains("count"))
                {
                    countTmpText = tmp;
                    return;
                }
            }

            var allTexts = GetComponentsInChildren<Text>(true);
            foreach (var txt in allTexts)
            {
                if (txt != null && txt.gameObject.name.ToLower().Contains("count"))
                {
                    countUiText = txt;
                    return;
                }
            }

            // 2. Direct name search
            Transform child = transform.Find("count_txt") ?? transform.Find("Count_txt") ?? transform.Find("count") ?? transform.Find("Count");
            if (child != null)
            {
                countTmpText = child.GetComponent<TextMeshProUGUI>();
                countUiText = child.GetComponent<Text>();
                if (countTmpText != null || countUiText != null) return;
            }

            // 3. Fallback: if multiple text components exist, take the last child
            if (allTmps.Length > 1)
            {
                countTmpText = allTmps[allTmps.Length - 1];
                return;
            }
            if (allTexts.Length > 1)
            {
                countUiText = allTexts[allTexts.Length - 1];
                return;
            }

            if (allTmps.Length > 0) countTmpText = allTmps[0];
            else if (allTexts.Length > 0) countUiText = allTexts[0];
        }

        public void UpdateCountUI()
        {
            FindCountTextReferences();
            string str = remainingCount.ToString();
            if (countTmpText != null)
            {
                countTmpText.text = str;
                Debug.Log($"[BoosterButton] Updated countTmpText on '{countTmpText.gameObject.name}' to '{str}'");
            }
            if (countUiText != null)
            {
                countUiText.text = str;
                Debug.Log($"[BoosterButton] Updated countUiText on '{countUiText.gameObject.name}' to '{str}'");
            }
        }

        public void RefreshButtonState()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null)
            {
                button.interactable = remainingCount > 0;
            }
        }
    }
}
