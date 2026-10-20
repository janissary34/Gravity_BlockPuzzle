using TMPro;
using GravityPuzzle.Config;
using GravityPuzzle.Infrastructure.Services;
using GravityPuzzle.Gameplay.Tutorial;
using GravityPuzzle.Presentation.Views;
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
        [Header("UI Text References")]
        [SerializeField, Tooltip("Text component displaying count (TextMeshPro). Auto-found if null.")]
        private TextMeshProUGUI countTmpText;

        [SerializeField, Tooltip("Text component displaying count (Legacy UI Text). Auto-found if null.")]
        private Text countUiText;

        [SerializeField, Tooltip("Optional CanvasGroup to manage button appearance. Auto-found if null.")]
        private CanvasGroup buttonCanvasGroup;

        [Header("Locked State UI")]
        [SerializeField, Tooltip("GameObject enabled when the booster is locked (e.g. lock icon, lock overlay). Auto-found if null.")]
        private GameObject lockRoot;

        [SerializeField, Tooltip("Optional Image for lock icon. Auto-found if null.")]
        private Image lockImage;

        [SerializeField, Tooltip("Text displaying the level at which this booster unlocks. Auto-found if null.")]
        private TextMeshProUGUI unlockLevelTmpText;

        [SerializeField, Tooltip("Legacy Text displaying the unlock level. Auto-found if null.")]
        private Text unlockLevelUiText;

        [SerializeField, Tooltip("Format string for unlock level text (e.g. 'Level {0}' or 'Lv.{0}' or '{0}').")]
        private string unlockLevelFormat = "Level {0}";

        [SerializeField, Tooltip("Optional GameObject containing count badge/text to hide while locked. Auto-found if null.")]
        private GameObject countRoot;

        [Header("Selection Glow")]
        [SerializeField, Tooltip("Scene/prefab authored glow shown when introduced or selected. Auto-found if null.")]
        private GameObject selectionGlow;

        [Header("Events")]
        [Tooltip("Event fired when the booster button is clicked and has remaining uses.")]
        public UnityEvent onBoosterClicked;

        private Button button;
        private RocketBooster rocketBooster;
        private HammerBooster hammerBooster;
        private TimerBooster timerBooster;
        private FreezeTimerBooster freezeTimerBooster;
        private BoosterRewardType boosterType;
        private bool hasBoosterType;
        private int lastDispatchFrame = -1;

        public int RemainingCount
        {
            get
            {
                if (!hasBoosterType)
                    return 0;

                int count = BoosterInventoryRuntime.Current.GetCount(boosterType);
                if (count <= 0 && BoosterFirstUseTutorialRuntime.Gate != null &&
                    (BoosterFirstUseTutorialRuntime.Gate.IsBoosterHighlighted(boosterType) ||
                     BoosterFirstUseTutorialRuntime.Gate.IsFirstUseActive(boosterType)))
                {
                    return 1;
                }

                return count;
            }
        }
        public bool HasUses => RemainingCount > 0;
        public Button ButtonComponent => button != null ? button : (button = GetComponent<Button>());
        public bool HasBoosterType
        {
            get
            {
                if (!hasBoosterType)
                    hasBoosterType = TryResolveBoosterType(out boosterType);
                return hasBoosterType;
            }
        }

        public BoosterRewardType BoosterType
        {
            get
            {
                if (!hasBoosterType)
                    hasBoosterType = TryResolveBoosterType(out boosterType);
                return boosterType;
            }
        }

        public GameObject SelectionGlow
        {
            get
            {
                if (selectionGlow == null)
                    FindSelectionGlowReferences();
                return selectionGlow;
            }
        }

        public void SetSelectionGlowVisible(bool visible)
        {
            if (selectionGlow == null)
                FindSelectionGlowReferences();

            if (selectionGlow != null && selectionGlow.activeSelf != visible)
                selectionGlow.SetActive(visible);

            if (timerBooster == null)
                timerBooster = GetComponent<TimerBooster>();
            if (timerBooster != null && timerBooster.SelectionGlow != null && timerBooster.SelectionGlow.activeSelf != visible)
                timerBooster.SelectionGlow.SetActive(visible);
        }

        public static int GetIntroductionLevel(BoosterRewardType type)
        {
            return type switch
            {
                BoosterRewardType.FreezeTimer => 4,
                BoosterRewardType.Hammer => 6,
                BoosterRewardType.Rocket => 10,
                _ => 1
            };
        }

        public bool IsIntroduced
        {
            get
            {
                if (!HasBoosterType)
                    return true;

                if (GravityLevelRuntime.IsEditorLevelPreview && HasLevelPreviewUses())
                    return true;

                if (BoosterFirstUseTutorialRuntime.Gate != null &&
                    (BoosterFirstUseTutorialRuntime.Gate.IsBoosterHighlighted(boosterType) ||
                     BoosterFirstUseTutorialRuntime.Gate.IsFirstUseActive(boosterType)))
                    return true;

                int introLevel = GetIntroductionLevel(boosterType);
                if (GravityLevelRuntime.CurrentLevelNumber < introLevel)
                    return false;

                if (BoosterInventoryRuntime.Current.IsUnlocked(boosterType))
                    return true;

                if (HasUses)
                    return true;

                return false;
            }
        }


        private void Awake()
        {
            button = GetComponent<Button>();
            if (buttonCanvasGroup == null)
                buttonCanvasGroup = GetComponent<CanvasGroup>();
            rocketBooster = GetComponent<RocketBooster>();
            hammerBooster = GetComponent<HammerBooster>();
            timerBooster = GetComponent<TimerBooster>();
            freezeTimerBooster = GetComponent<FreezeTimerBooster>();
            hasBoosterType = TryResolveBoosterType(out boosterType);
            FindCountTextReferences();
            FindSelectionGlowReferences();
            FindLockReferences();
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

            if (hasBoosterType)
                BoosterInventoryRuntime.Current.CountChanged += HandleInventoryCountChanged;
            BoosterFirstUseTutorialRuntime.RegisterButton(this);
            IBoosterFirstUseTutorialGate gate = BoosterFirstUseTutorialRuntime.Gate;
            bool shouldGlow = hasBoosterType && gate != null &&
                (gate.IsBoosterHighlighted(boosterType) || gate.IsFirstUseActive(boosterType));
            SetSelectionGlowVisible(shouldGlow);
            UpdateCountUI();
            RefreshButtonState();
        }

        private void OnDisable()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleButtonClick);
            }

            if (hasBoosterType)
                BoosterInventoryRuntime.Current.CountChanged -= HandleInventoryCountChanged;
        }

        /// <summary>
        /// Resets this booster's temporary editor-preview balance. Production
        /// balances are intentionally changed only through the inventory.
        /// </summary>
        public void ResetCount(int count = -1)
        {
#if UNITY_EDITOR
            if (hasBoosterType)
                BoosterInventoryRuntime.SetEditorPreviewCount(boosterType, Mathf.Max(0, count));
#endif
            UpdateCountUI();
            RefreshButtonState();
        }

        /// <summary>
        /// Applies an editor-only level-preview count. Release builds ignore
        /// level allowances: persistent inventory remains the sole authority.
        /// </summary>
        public void ConfigureLevelUseCount(int count)
        {
#if UNITY_EDITOR
            if (hasBoosterType && GravityLevelRuntime.IsEditorLevelPreview)
                BoosterInventoryRuntime.SetEditorPreviewCount(boosterType, count);
#endif
            UpdateCountUI();
            RefreshButtonState();
        }

        /// <summary>
        /// Attempts to consume 1 use count. Decrements count by 1 and updates UI text.
        /// Returns true if successful, false if 0 uses remaining.
        /// </summary>
        public bool TryConsumeUse()
        {
            if (!hasBoosterType || !BoosterInventoryRuntime.Current.TryConsume(boosterType))
            {
                RefreshButtonState();
                return false;
            }

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

            if (!HasUses)
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
            else if (freezeTimerBooster != null)
            {
                freezeTimerBooster.ActivateFreezeBooster();
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

        private void FindSelectionGlowReferences()
        {
            if (selectionGlow != null)
                return;

            Transform child = transform.Find("HammerSelectionGlow")
                ?? transform.Find("RocketSelectionGlow")
                ?? transform.Find("TimerSelectionGlow")
                ?? transform.Find("selectionGlow")
                ?? transform.Find("SelectionGlow");

            if (child == null)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    Transform candidate = transform.GetChild(i);
                    if (candidate.name.IndexOf("glow", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        child = candidate;
                        break;
                    }
                }
            }

            if (child == null)
            {
                if (timerBooster == null) timerBooster = GetComponent<TimerBooster>();
                if (timerBooster != null && timerBooster.SelectionGlow != null)
                    child = timerBooster.SelectionGlow.transform;
            }

            if (child != null)
                selectionGlow = child.gameObject;
        }

        private void FindLockReferences()
        {
            if (lockRoot == null)
            {
                Transform child = transform.Find("Lock") ??
                                  transform.Find("lock") ??
                                  transform.Find("LockRoot") ??
                                  transform.Find("lockRoot") ??
                                  transform.Find("Lock_img") ??
                                  transform.Find("lock_img") ??
                                  transform.Find("LockedOverlay") ??
                                  transform.Find("lockedOverlay");
                if (child == null)
                {
                    for (int i = 0; i < transform.childCount; i++)
                    {
                        Transform candidate = transform.GetChild(i);
                        if (candidate.name.IndexOf("lock", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            child = candidate;
                            break;
                        }
                    }
                }
                if (child != null)
                    lockRoot = child.gameObject;
            }

            if (lockImage == null && lockRoot != null)
            {
                lockImage = lockRoot.GetComponent<Image>() ?? lockRoot.GetComponentInChildren<Image>(true);
            }

            if (unlockLevelTmpText == null && unlockLevelUiText == null)
            {
                var allTmps = GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var tmp in allTmps)
                {
                    if (tmp != null && (tmp.gameObject.name.IndexOf("unlock", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        tmp.gameObject.name.IndexOf("level", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        tmp.gameObject.name.IndexOf("lock_txt", System.StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        unlockLevelTmpText = tmp;
                        break;
                    }
                }

                if (unlockLevelTmpText == null)
                {
                    var allTexts = GetComponentsInChildren<Text>(true);
                    foreach (var txt in allTexts)
                    {
                        if (txt != null && (txt.gameObject.name.IndexOf("unlock", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            txt.gameObject.name.IndexOf("level", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            txt.gameObject.name.IndexOf("lock_txt", System.StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            unlockLevelUiText = txt;
                            break;
                        }
                    }
                }
            }

            if (countRoot == null)
            {
                Transform banner = transform.Find("Banner_img") ??
                                   transform.Find("banner_img") ??
                                   transform.Find("CountBadge") ??
                                   transform.Find("countBadge");
                if (banner != null)
                    countRoot = banner.gameObject;
            }
        }

        public void UpdateCountUI()
        {
            string str = RemainingCount.ToString();
            if (countTmpText != null)
            {
                countTmpText.text = str;
            }
            if (countUiText != null)
            {
                countUiText.text = str;
            }
        }

        public void RefreshButtonState()
        {
            if (buttonCanvasGroup == null)
            {
                buttonCanvasGroup = GetComponent<CanvasGroup>();
            }

            if (BoosterTargetingPresentation.IsBoosterButtonSuppressed(buttonCanvasGroup))
            {
                buttonCanvasGroup.alpha = 0f;
                buttonCanvasGroup.interactable = false;
                buttonCanvasGroup.blocksRaycasts = false;
                if (button != null)
                    button.interactable = false;
                SetSelectionGlowVisible(false);
                return;
            }

            bool isIntroduced = IsIntroduced;

            if (!isIntroduced)
            {
                SetSelectionGlowVisible(false);

                // Keep booster button visible on HUD from level 1.
                // It remains non-interactable with locked visuals displayed.
                if (buttonCanvasGroup != null)
                {
                    buttonCanvasGroup.alpha = 1f;
                    buttonCanvasGroup.interactable = false;
                    buttonCanvasGroup.blocksRaycasts = false;
                }

                if (button != null)
                {
                    button.interactable = false;
                }

                int introLevel = GetIntroductionLevel(boosterType);
                string unlockStr = string.Format(unlockLevelFormat, introLevel);

                if (lockRoot != null)
                    lockRoot.SetActive(true);
                if (lockImage != null)
                    lockImage.gameObject.SetActive(true);

                if (unlockLevelTmpText != null)
                {
                    unlockLevelTmpText.text = unlockStr;
                    unlockLevelTmpText.gameObject.SetActive(true);
                }
                if (unlockLevelUiText != null)
                {
                    unlockLevelUiText.text = unlockStr;
                    unlockLevelUiText.gameObject.SetActive(true);
                }

                if (countRoot != null)
                    countRoot.SetActive(false);
                else
                {
                    if (countTmpText != null)
                        countTmpText.gameObject.SetActive(false);
                    if (countUiText != null)
                        countUiText.gameObject.SetActive(false);
                }

                return;
            }

            // Unlocked state: hide lock visuals, show count visuals.
            if (lockRoot != null)
                lockRoot.SetActive(false);
            if (lockImage != null)
                lockImage.gameObject.SetActive(false);
            if (unlockLevelTmpText != null)
                unlockLevelTmpText.gameObject.SetActive(false);
            if (unlockLevelUiText != null)
                unlockLevelUiText.gameObject.SetActive(false);

            if (countRoot != null)
                countRoot.SetActive(true);
            if (countTmpText != null)
                countTmpText.gameObject.SetActive(true);
            if (countUiText != null)
                countUiText.gameObject.SetActive(true);

            bool isTutorialActive = HasBoosterType && BoosterFirstUseTutorialRuntime.Gate != null &&
                (BoosterFirstUseTutorialRuntime.Gate.IsBoosterHighlighted(BoosterType) ||
                 BoosterFirstUseTutorialRuntime.Gate.IsFirstUseActive(BoosterType));

            if (isTutorialActive)
            {
                SetSelectionGlowVisible(true);
            }
            else
            {
                SetSelectionGlowVisible(false);
            }

            bool canInteract = HasUses || isTutorialActive;
            if (buttonCanvasGroup != null)
            {
                buttonCanvasGroup.alpha = canInteract ? 1f : 0.55f;
                buttonCanvasGroup.interactable = canInteract;
                buttonCanvasGroup.blocksRaycasts = canInteract;
            }

            if (button != null)
            {
                button.interactable = canInteract;
            }
        }

        private bool HasLevelPreviewUses()
        {
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null)
                return false;

            return boosterType switch
            {
                BoosterRewardType.FreezeTimer => level.timerBoosterCount > 0,
                BoosterRewardType.Hammer => level.hammerBoosterCount > 0,
                BoosterRewardType.Rocket => level.rocketBoosterCount > 0,
                _ => false
            };
        }

        private void HandleInventoryCountChanged(BoosterRewardType changedType, int count)
        {
            if (!hasBoosterType || changedType != boosterType)
                return;

            UpdateCountUI();
            RefreshButtonState();
        }

        private bool TryResolveBoosterType(out BoosterRewardType resolvedType)
        {
            if (rocketBooster == null) rocketBooster = GetComponent<RocketBooster>();
            if (rocketBooster != null)
            {
                resolvedType = BoosterRewardType.Rocket;
                return true;
            }

            if (hammerBooster == null) hammerBooster = GetComponent<HammerBooster>();
            if (hammerBooster != null)
            {
                resolvedType = BoosterRewardType.Hammer;
                return true;
            }

            if (timerBooster == null) timerBooster = GetComponent<TimerBooster>();
            if (freezeTimerBooster == null) freezeTimerBooster = GetComponent<FreezeTimerBooster>();
            if (timerBooster != null || freezeTimerBooster != null)
            {
                resolvedType = BoosterRewardType.FreezeTimer;
                return true;
            }

            resolvedType = default;
            return false;
        }
    }
}
