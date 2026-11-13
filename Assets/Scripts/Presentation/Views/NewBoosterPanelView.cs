using System;
using GravityPuzzle.Config;
using GravityPuzzle.Infrastructure.Services;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Presentation owner for the authored New Booster and New Feature popup. Gameplay can
    /// subscribe to <see cref="Continued"/> without the popup owning booster state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NewBoosterPanelView : MonoBehaviour
    {
        [SerializeField] private Button closeButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private Transform boosterContentRoot;
        [SerializeField] private GameObject[] hudObjectsToHide;
        [SerializeField] private UnityEvent continueClicked;
        [SerializeField] private BoosterRewardConfig previewRewardConfig;
        [SerializeField] private BoosterRewardContentView[] rewardContents;

        private PrototypeBoard pausedBoard;
        private BoosterRewardConfig selectedRewardConfig;
        private bool[] hudObjectsWereActive;
        private bool hudIsHidden;
        private bool isOpening;

        public event Action Continued;
        public event Action Dismissed;
        public event Action<BoosterRewardConfig> Claimed;

        public static string GetFeatureTargetNameForLevel(int levelNumber)
        {
            return levelNumber switch
            {
                4 => "Freeze_Timer",
                6 => "Hammer",
                8 => "Ice_block",
                10 => "Rocket",
                15 => "BombBlock",
                21 => "CombinedBlock",
                _ => null
            };
        }

        private void Awake()
        {
            if (closeButton == null)
            {
                Transform btn = transform.Find("CloseButton");
                if (btn != null)
                    closeButton = btn.GetComponent<Button>();
            }

            if (closeButton != null)
            {
                closeButton.gameObject.SetActive(true);
                closeButton.onClick.AddListener(Continue);
            }

            if (continueButton == null)
            {
                Transform btn = transform.Find("ContinueButton");
                if (btn != null)
                    continueButton = btn.GetComponent<Button>();
            }

            if (continueButton != null)
                continueButton.onClick.AddListener(Continue);

            ResolveBoosterContentRoot();

            // The overlay is authored inactive, so Awake can first run inside
            // SetOverlayVisible(true). Hiding here would close the panel and
            // its selected content in the same frame it is being opened.
            if (isOpening)
                return;

            HideAllFeatureContents();

            if (overlayRoot != null)
                overlayRoot.SetActive(false);
        }

        private void OnEnable()
        {
            ApplySelectedReward();

            if (pausedBoard != null)
                return;

            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard != null && activeBoard.TryPauseTimer(this))
                pausedBoard = activeBoard;
        }

        private void OnDisable()
        {
            ResumeTimer();
            RestoreHud();
        }

        private void OnDestroy()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Continue);
            if (continueButton != null)
                continueButton.onClick.RemoveListener(Continue);

            ResumeTimer();
            RestoreHud();
        }

        public void Show()
        {
            SetOverlayVisible(true);
        }

        /// <summary>
        /// Shows this popup for authored level milestone features (Freeze Timer, Hammer,
        /// Rocket, Ice block, Bomb block, Combined block). Activates only the matching
        /// child inside BoosterContent and deactivates all others.
        /// </summary>
        public bool ShowMilestoneFeatureForLevel(int levelNumber, PrototypeBoard board = null)
        {
            string targetFeature = GetFeatureTargetNameForLevel(levelNumber);
            Transform contentRoot = ResolveBoosterContentRoot();

            if (string.IsNullOrEmpty(targetFeature) || contentRoot == null)
            {
                HideAllFeatureContents();
                SetOverlayVisible(false);
                return false;
            }

            bool foundMatch = false;
            int childCount = contentRoot.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = contentRoot.GetChild(i);
                bool isMatch = MatchesFeatureName(child.name, targetFeature);
                child.gameObject.SetActive(isMatch);
                if (isMatch)
                    foundMatch = true;
            }

            if (!foundMatch)
            {
                Debug.LogWarning(
                    $"[New Booster Panel] Feature '{targetFeature}' for Level {levelNumber} not found under BoosterContent.",
                    this);
                HideAllFeatureContents();
                SetOverlayVisible(false);
                return false;
            }

            if (board != null)
            {
                pausedBoard = board;
                board.TryPauseTimer(this);
            }
            else
            {
                PrototypeBoard activeBoard = PrototypeBoard.Active;
                if (activeBoard != null && activeBoard.TryPauseTimer(this))
                    pausedBoard = activeBoard;
            }

            GrantInitialBoosterIfApplicable(levelNumber);
            SetOverlayVisible(true);
            return true;
        }

        /// <summary>
        /// Shows this popup with the scene-authored visual bound to the supplied
        /// config. The visual objects are activated in place; no prefab is
        /// instantiated at runtime.
        /// </summary>
        public void Show(BoosterRewardConfig rewardConfig)
        {
            if (rewardConfig == null)
            {
                Debug.LogWarning("[New Booster Panel] A reward config is required to show the panel.", this);
                return;
            }

            selectedRewardConfig = rewardConfig;
            SetOverlayVisible(true);
            ApplySelectedReward();
        }

        public void Close()
        {
            if (!IsOverlayVisible)
                return;

            HideRewardContents();
            HideAllFeatureContents();
            SetOverlayVisible(false);
            ResumeTimer();
            Dismissed?.Invoke();
        }

        public void Continue()
        {
            BoosterRewardConfig claimedReward = selectedRewardConfig;
            selectedRewardConfig = null;

            Close();

            // The popup must restore its own HUD state before the tutorial
            // focus applies its dimmer and input restrictions.
            if (claimedReward != null)
                Claimed?.Invoke(claimedReward);

            Continued?.Invoke();
            continueClicked?.Invoke();
        }

        public void HideAllFeatureContents()
        {
            Transform contentRoot = ResolveBoosterContentRoot();
            if (contentRoot == null)
                return;

            int childCount = contentRoot.childCount;
            for (int i = 0; i < childCount; i++)
            {
                contentRoot.GetChild(i).gameObject.SetActive(false);
            }
        }

        private Transform ResolveBoosterContentRoot()
        {
            if (boosterContentRoot != null)
                return boosterContentRoot;

            boosterContentRoot = transform.Find("BoosterContent");
            if (boosterContentRoot == null && overlayRoot != null)
                boosterContentRoot = overlayRoot.transform.Find("NewBooster_featurePanel/BoosterContent");

            if (boosterContentRoot == null)
            {
                Transform[] transforms = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].name == "BoosterContent")
                    {
                        boosterContentRoot = transforms[i];
                        break;
                    }
                }
            }

            return boosterContentRoot;
        }

        private static bool MatchesFeatureName(string childName, string targetFeature)
        {
            if (string.Equals(childName, targetFeature, StringComparison.OrdinalIgnoreCase))
                return true;

            string normChild = childName.Replace("_", "").Replace(" ", "");
            string normTarget = targetFeature.Replace("_", "").Replace(" ", "");
            return string.Equals(normChild, normTarget, StringComparison.OrdinalIgnoreCase);
        }

        private void GrantInitialBoosterIfApplicable(int levelNumber)
        {
            BoosterRewardType? type = levelNumber switch
            {
                4 => BoosterRewardType.FreezeTimer,
                6 => BoosterRewardType.Hammer,
                10 => BoosterRewardType.Rocket,
                _ => null
            };

            if (!type.HasValue)
                return;

            if (BoosterInventoryRuntime.Current.GetCount(type.Value) <= 0)
            {
                int grant = type.Value == BoosterRewardType.FreezeTimer ? 1 : 3;
                BoosterInventoryRuntime.Current.Grant(type.Value, grant);
            }
        }

        private void ApplySelectedReward()
        {
            if (selectedRewardConfig == null)
                return;

            BoosterRewardConfig targetConfig = selectedRewardConfig;
            if (rewardContents == null)
                return;

            bool foundMatch = false;
            for (int index = 0; index < rewardContents.Length; index++)
            {
                BoosterRewardContentView content = rewardContents[index];
                if (content == null)
                    continue;

                bool shouldShow = content.RewardConfig == targetConfig;
                content.SetVisible(shouldShow);
                foundMatch |= shouldShow;
            }

            if (!foundMatch)
            {
                Debug.LogWarning(
                    $"[New Booster Panel] No authored content is bound to '{targetConfig.name}'.",
                    this);
            }
        }

        private void HideRewardContents()
        {
            if (rewardContents == null)
                return;

            for (int index = 0; index < rewardContents.Length; index++)
            {
                BoosterRewardContentView content = rewardContents[index];
                if (content != null)
                    content.SetVisible(false);
            }
        }

        private void ResumeTimer()
        {
            if (pausedBoard == null)
                return;

            pausedBoard.ResumeTimer(this);
            pausedBoard = null;
        }

        private bool IsOverlayVisible => overlayRoot != null ? overlayRoot.activeSelf : gameObject.activeSelf;

        private void SetOverlayVisible(bool visible)
        {
            if (visible)
                HideHud();

            GameObject target = overlayRoot != null ? overlayRoot : gameObject;
            isOpening = visible;
            target.SetActive(visible);
            isOpening = false;

            // The timer HUD and targeting UI share this canvas. Keep the modal
            // above all of them so their graphics cannot consume its button
            // raycasts.
            if (visible)
                target.transform.SetAsLastSibling();

            if (!visible)
                RestoreHud();
        }

        private void HideHud()
        {
            if (hudIsHidden || hudObjectsToHide == null)
                return;

            hudObjectsWereActive = new bool[hudObjectsToHide.Length];
            for (int index = 0; index < hudObjectsToHide.Length; index++)
            {
                GameObject hudObject = hudObjectsToHide[index];
                if (hudObject == null)
                    continue;

                hudObjectsWereActive[index] = hudObject.activeSelf;
                if (hudObjectsWereActive[index])
                    hudObject.SetActive(false);
            }

            hudIsHidden = true;
        }

        private void RestoreHud()
        {
            if (!hudIsHidden || hudObjectsToHide == null)
                return;

            for (int index = 0; index < hudObjectsToHide.Length; index++)
            {
                GameObject hudObject = hudObjectsToHide[index];
                if (hudObject != null && hudObjectsWereActive != null && hudObjectsWereActive[index])
                    hudObject.SetActive(true);
            }

            hudObjectsWereActive = null;
            hudIsHidden = false;
        }
    }
}
