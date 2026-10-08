using System;
using GravityPuzzle.Config;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Presentation owner for the authored New Booster popup. Gameplay can
    /// subscribe to <see cref="Continued"/> without the popup owning booster state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NewBoosterPanelView : MonoBehaviour
    {
        [SerializeField] private Button closeButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private GameObject[] hudObjectsToHide;
        [SerializeField] private UnityEvent continueClicked;
        [SerializeField] private BoosterRewardConfig previewRewardConfig;
        [SerializeField] private BoosterRewardContentView[] rewardContents;

        private PrototypeBoard pausedBoard;
        private BoosterRewardConfig selectedRewardConfig;
        private bool[] hudObjectsWereActive;
        private bool hudIsHidden;

        public event Action Continued;
        public event Action Dismissed;
        public event Action<BoosterRewardConfig> Claimed;

        private void Awake()
        {
            // Reward reveals are mandatory and complete through Claim. Keeping
            // the legacy close control visible suggests an unsupported exit and
            // can leave the reward/tutorial state half-applied.
            if (closeButton != null)
                closeButton.gameObject.SetActive(false);
            if (continueButton != null)
                continueButton.onClick.AddListener(Continue);
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

            if (selectedRewardConfig != null)
            {
                Debug.LogWarning("[New Booster Panel] A reward popup may only close through Claim.", this);
                return;
            }

            HideRewardContents();
            SetOverlayVisible(false);
            Dismissed?.Invoke();
        }

        public void Continue()
        {
            if (selectedRewardConfig == null)
            {
                Debug.LogWarning("[New Booster Panel] Claim requires a selected reward.", this);
                return;
            }

            BoosterRewardConfig claimedReward = selectedRewardConfig;
            selectedRewardConfig = null;
            Close();

            // The popup must restore its own HUD state before the tutorial
            // focus applies its dimmer and input restrictions.
            Claimed?.Invoke(claimedReward);
            Continued?.Invoke();
            continueClicked?.Invoke();
        }

        private void ApplySelectedReward()
        {
            BoosterRewardConfig targetConfig = selectedRewardConfig != null
                ? selectedRewardConfig
                : previewRewardConfig;
            if (targetConfig == null || rewardContents == null)
                return;

            bool foundMatch = false;
            for (int index = 0; index < rewardContents.Length; index++)
            {
                BoosterRewardContentView content = rewardContents[index];
                if (content == null)
                    continue;

                bool shouldShow = content.RewardConfig == targetConfig;
                content.gameObject.SetActive(shouldShow);
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
                    content.gameObject.SetActive(false);
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
            target.SetActive(visible);

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
