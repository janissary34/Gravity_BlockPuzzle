using System.Collections.Generic;
using TMPro;
using GravityPuzzle.Config;
using GravityPuzzle.Infrastructure.Services;
using GravityPuzzle.Gameplay.Tutorial;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Scene-authored presentation for an armed board-targeting booster.
    /// This component owns UI and highlight visibility only; boosters retain
    /// all target validation and gameplay mutation authority.
    /// </summary>
    public sealed class BoosterTargetingPresentation : MonoBehaviour
    {
        public enum Mode
        {
            Rocket,
            Hammer
        }

        public static BoosterTargetingPresentation Active { get; private set; }

        [Header("Scene Presentation")]
        [SerializeField] private GameObject boardDimBackdrop;
        [SerializeField] private GameObject targetingUi;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text instructionText;
        [SerializeField] private Image rocketIcon;
        [SerializeField] private Image hammerIcon;

        [Header("Selection Sparkles")]
        [Tooltip("Loops while Rocket targeting is armed.")]
        [SerializeField] private ParticleSystem rocketButtonSparkles;
        [Tooltip("Loops while Hammer targeting is armed.")]
        [SerializeField] private ParticleSystem hammerButtonSparkles;
        [Tooltip("Loops while the Rocket icon is visible in the targeting header.")]
        [SerializeField] private ParticleSystem rocketIconSparkles;
        [Tooltip("Loops while the Hammer icon is visible in the targeting header.")]
        [SerializeField] private ParticleSystem hammerIconSparkles;

        [Header("Selection Glow")]
        [Tooltip("A scene-authored glow child behind the Rocket button.")]
        [SerializeField] private GameObject rocketButtonGlow;
        [Tooltip("A scene-authored glow child behind the Hammer button.")]
        [SerializeField] private GameObject hammerButtonGlow;
        [Tooltip("A scene-authored glow child behind the Freeze Timer button.")]
        [SerializeField] private GameObject timerButtonGlow;
        [Tooltip("Loops while Freeze Timer focus or targeting is armed.")]
        [SerializeField] private ParticleSystem timerButtonSparkles;
        [Header("HUD Suppression")]
        [Tooltip("Normal HUD groups hidden while a Rocket or Hammer target mode is armed. Their original state is restored afterward.")]
        [SerializeField] private CanvasGroup[] gameplayHudGroups;

        [Header("Booster Button Focus")]
        [SerializeField] private CanvasGroup rocketBoosterButtonGroup;
        [SerializeField] private CanvasGroup hammerBoosterButtonGroup;
        [SerializeField] private CanvasGroup timerBoosterButtonGroup;
        [Tooltip("Optional world dimmer renderer. Its order is raised above board pieces during a tutorial focus.")]
        [SerializeField] private SpriteRenderer boardDimRenderer;
        [SerializeField, Min(1)] private int tutorialDimSortingOrder = 100;
        [Tooltip("Scene-authored bright target and crosshair rendered over the tutorial dimmer.")]
        [SerializeField] private TutorialTargetIndicatorView tutorialTargetIndicator;

        [Header("Hammer Cell Outlines")]
        [SerializeField] private HammerTargetCellOutlineView hammerCellOutlinePrefab;
        [SerializeField, Min(1)] private int hammerCellOutlinePrewarmCount = 128;

        [Header("Copy")]
        [SerializeField] private string rocketTitle = "Rocket";
        [SerializeField] private string rocketInstruction = "Select a piece to eliminate";
        [SerializeField] private string hammerTitle = "Hammer";
        [SerializeField] private string hammerInstruction = "Select a block to break";
        [SerializeField] private string freezeTimerTitle = "Freeze Timer";
        [SerializeField] private string freezeTimerInstruction = "Tap the Freeze Timer booster";

        private readonly Stack<HammerTargetCellOutlineView> availableHammerOutlines =
            new Stack<HammerTargetCellOutlineView>();
        private readonly List<HammerTargetCellOutlineView> activeHammerOutlines =
            new List<HammerTargetCellOutlineView>();
        private readonly List<PuzzlePiece.TargetableCell> targetableCellBuffer =
            new List<PuzzlePiece.TargetableCell>();
        private readonly List<HudGroupState> hiddenHudGroupStates =
            new List<HudGroupState>();
        private readonly List<GameObjectState> hiddenHudObjectStates =
            new List<GameObjectState>();
        private readonly List<HudGroupState> hiddenBoosterButtonStates =
            new List<HudGroupState>();
        private bool boosterButtonGroupsValid;
        private TutorialHandView cachedTutorialHand;
        private bool wasTutorialFocusActive;
        private readonly struct HudGroupState
        {
            public readonly CanvasGroup Group;
            public readonly float Alpha;
            public readonly bool Interactable;
            public readonly bool BlocksRaycasts;

            public HudGroupState(CanvasGroup group)
            {
                Group = group;
                Alpha = group.alpha;
                Interactable = group.interactable;
                BlocksRaycasts = group.blocksRaycasts;
            }
        }

        private readonly struct GameObjectState
        {
            public readonly GameObject Target;
            public readonly bool WasActive;

            public GameObjectState(GameObject target)
            {
                Target = target;
                WasActive = target != null && target.activeSelf;
            }
        }

        private void Awake()
        {
            Active = this;
            if (boardDimRenderer == null && boardDimBackdrop != null)
                boardDimRenderer = boardDimBackdrop.GetComponent<SpriteRenderer>();
            if (boardDimRenderer != null)
                boardDimRenderer.sortingOrder = tutorialDimSortingOrder;

            EnsureBoosterButtonGroups();
            PrewarmHammerOutlines();
            ResolveButtonGlowReferences();
            ResolveSparkleReferences();
            SetVisible(false);
            RefreshBoosterAvailability();
        }

        private void EnsureBoosterButtonGroups()
        {
            if (boosterButtonGroupsValid &&
                rocketBoosterButtonGroup != null &&
                hammerBoosterButtonGroup != null &&
                timerBoosterButtonGroup != null)
            {
                return;
            }

            if (rocketBoosterButtonGroup == null ||
                (rocketBoosterButtonGroup.GetComponent<RocketBooster>() == null &&
                 rocketBoosterButtonGroup.GetComponent<BoosterButton>() == null))
            {
                RocketBooster rocket = GetComponentInChildren<RocketBooster>(true);
                if (rocket == null)
                    rocket = Object.FindFirstObjectByType<RocketBooster>(FindObjectsInactive.Include);
                if (rocket != null)
                    rocketBoosterButtonGroup = rocket.GetComponent<CanvasGroup>();
            }

            if (hammerBoosterButtonGroup == null ||
                (hammerBoosterButtonGroup.GetComponent<HammerBooster>() == null &&
                 hammerBoosterButtonGroup.GetComponent<BoosterButton>() == null))
            {
                HammerBooster hammer = GetComponentInChildren<HammerBooster>(true);
                if (hammer == null)
                    hammer = Object.FindFirstObjectByType<HammerBooster>(FindObjectsInactive.Include);
                if (hammer != null)
                    hammerBoosterButtonGroup = hammer.GetComponent<CanvasGroup>();
            }

            if (timerBoosterButtonGroup == null ||
                (timerBoosterButtonGroup.GetComponent<FreezeTimerBooster>() == null &&
                 timerBoosterButtonGroup.GetComponent<TimerBooster>() == null &&
                 timerBoosterButtonGroup.GetComponent<BoosterButton>() == null))
            {
                FreezeTimerBooster freeze = GetComponentInChildren<FreezeTimerBooster>(true);
                if (freeze == null)
                    freeze = Object.FindFirstObjectByType<FreezeTimerBooster>(FindObjectsInactive.Include);
                if (freeze != null)
                    timerBoosterButtonGroup = freeze.GetComponent<CanvasGroup>();
                else
                {
                    TimerBooster timer = GetComponentInChildren<TimerBooster>(true);
                    if (timer == null)
                        timer = Object.FindFirstObjectByType<TimerBooster>(FindObjectsInactive.Include);
                    if (timer != null)
                        timerBoosterButtonGroup = timer.GetComponent<CanvasGroup>();
                }

                if (timerBoosterButtonGroup == null)
                {
                    BoosterButton timerBtn = BoosterFirstUseTutorialRuntime.GetButton(BoosterRewardType.FreezeTimer);
                    if (timerBtn != null)
                        timerBoosterButtonGroup = timerBtn.GetComponent<CanvasGroup>();
                }
            }

            if (rocketBoosterButtonGroup != null &&
                hammerBoosterButtonGroup != null &&
                timerBoosterButtonGroup != null)
            {
                boosterButtonGroupsValid = true;
            }
        }

        private void OnEnable()
        {
            BoosterInventoryRuntime.Current.CountChanged += HandleInventoryCountChanged;
            RefreshBoosterAvailability();
        }

        private void OnDisable()
        {
            BoosterInventoryRuntime.Current.CountChanged -= HandleInventoryCountChanged;
        }

        private void OnDestroy()
        {
            ClearPieceHighlights();
            RestoreGameplayHud();
            RestoreInactiveBoosterButtons();
            if (Active == this)
                Active = null;
        }

        private void HandleInventoryCountChanged(BoosterRewardType boosterType, int count)
        {
            RefreshBoosterAvailability();
        }

        /// <summary>Inventory authority decides whether a booster exists in the HUD at all.</summary>
        private void RefreshBoosterAvailability()
        {
            if (hiddenBoosterButtonStates.Count > 0)
                return;

            SetBoosterAvailability(rocketBoosterButtonGroup, BoosterRewardType.Rocket);
            SetBoosterAvailability(hammerBoosterButtonGroup, BoosterRewardType.Hammer);
            SetBoosterAvailability(timerBoosterButtonGroup, BoosterRewardType.FreezeTimer);
        }

        private static void SetBoosterAvailability(CanvasGroup group, BoosterRewardType boosterType)
        {
            if (group == null)
                return;

            BoosterButton button = group.GetComponent<BoosterButton>();
            if (button != null)
            {
                button.RefreshButtonState();
                return;
            }

            bool introduced = GravityLevelRuntime.CurrentLevelNumber >= BoosterButton.GetIntroductionLevel(boosterType) &&
                              (BoosterInventoryRuntime.Current.IsUnlocked(boosterType) || BoosterInventoryRuntime.Current.GetCount(boosterType) > 0);
            group.alpha = introduced ? 1f : 0f;
            group.interactable = introduced;
            group.blocksRaycasts = introduced;
        }

        public static void Show(Mode mode)
        {
            if (Active != null)
                Active.ShowInternal(mode);
        }

        public static void Hide()
        {
            if (Active != null)
                Active.SetVisible(false);
        }

        /// <summary>
        /// Reuses the authored dimmer and button glow while a first-use lesson
        /// permits exactly one booster button. The targeting header remains
        /// hidden until that button has actually armed its board action.
        /// </summary>
        public static void ShowFirstUseBoosterFocus(BoosterRewardType boosterType)
        {
            if (Active == null)
            {
                Active = Object.FindFirstObjectByType<BoosterTargetingPresentation>(FindObjectsInactive.Include);
                if (Active != null)
                    Active.EnsureBoosterButtonGroups();
            }

            if (Active != null)
                Active.ShowFirstUseBoosterFocusInternal(boosterType);
        }

        /// <summary>
        /// Lets a booster's existing button-refresh code respect the temporary
        /// visibility owned by this presentation. This avoids a competing
        /// component re-enabling a non-selected button during targeting.
        /// </summary>
        public static bool IsBoosterButtonSuppressed(CanvasGroup buttonGroup)
        {
            return Active != null &&
                   buttonGroup != null &&
                   Active.ContainsHiddenBoosterButton(buttonGroup);
        }

        /// <summary>
        /// Replaces normal all-target feedback with one explicit tutorial
        /// target. The dimmer stays up; only this visual marker renders above it.
        /// </summary>
        public static void ShowFirstUseBoardTarget(
            BoosterRewardType boosterType,
            PuzzlePiece expectedPiece,
            Vector2 hammerCellCenter,
            Vector2 hammerCellSize)
        {
            if (Active != null)
                Active.ShowFirstUseBoardTargetInternal(
                    boosterType,
                    expectedPiece,
                    hammerCellCenter,
                    hammerCellSize);
        }

        private void ShowInternal(Mode mode)
        {
            HideGameplayHud();
            HideInactiveBoosterButtons(mode);
            SetVisible(true);

            bool isRocket = mode == Mode.Rocket;
            ConfigureTargetingHeader(isRocket ? BoosterRewardType.Rocket : BoosterRewardType.Hammer);

            SetSelectionGlow(isRocket);
            PlaySelectionSparkles(isRocket);

            if (isRocket)
                ApplyRocketPieceHighlights();
            else
                ShowHammerCellOutlines();

            // Hammer outline setup clears the previous Rocket/Tutorial state,
            // so acquire the shared foreground lease only after either mode's
            // mode-specific feedback has been rebuilt.
            SetBoardPiecesInForeground(true);
        }

        private void ShowFirstUseBoosterFocusInternal(BoosterRewardType boosterType)
        {
            EnsureBoosterButtonGroups();
            ResolveButtonGlowReferences();
            ResolveSparkleReferences();

            HideGameplayHud();
            HideInactiveBoosterButtons(boosterType);
            if (boardDimBackdrop != null)
                boardDimBackdrop.SetActive(true);

            // The feature panel is part of the tutorial contract: leaving it
            // inactive while its dark backdrop is visible strands the player
            // without the booster prompt. Target validity remains owned by the
            // individual booster, not by this presentation.
            if (targetingUi != null)
                targetingUi.SetActive(true);
            ConfigureTargetingHeader(boosterType);

            CanvasGroup activeGroup = boosterType == BoosterRewardType.Rocket
                ? rocketBoosterButtonGroup
                : boosterType == BoosterRewardType.Hammer
                    ? hammerBoosterButtonGroup
                    : timerBoosterButtonGroup;

            if (activeGroup != null)
            {
                activeGroup.alpha = 1f;
                activeGroup.interactable = true;
                activeGroup.blocksRaycasts = true;
            }

            SetSelectionGlow(boosterType);
            PlaySelectionSparkles(boosterType);
        }

        private void ConfigureTargetingHeader(BoosterRewardType boosterType)
        {
            bool isRocket = boosterType == BoosterRewardType.Rocket;
            bool isHammer = boosterType == BoosterRewardType.Hammer;

            if (titleText != null)
            {
                titleText.text = isRocket
                    ? rocketTitle
                    : isHammer ? hammerTitle : freezeTimerTitle;
            }

            if (instructionText != null)
            {
                instructionText.text = isRocket
                    ? rocketInstruction
                    : isHammer ? hammerInstruction : freezeTimerInstruction;
            }

            if (rocketIcon != null)
                rocketIcon.gameObject.SetActive(isRocket);
            if (hammerIcon != null)
                hammerIcon.gameObject.SetActive(isHammer);
        }

        private void ShowFirstUseBoardTargetInternal(
            BoosterRewardType boosterType,
            PuzzlePiece expectedPiece,
            Vector2 hammerCellCenter,
            Vector2 hammerCellSize)
        {
            HideGameplayHud();
            ClearPieceHighlights();
            ReturnHammerCellOutlines();
            tutorialTargetIndicator?.Hide();

            if (boosterType == BoosterRewardType.Rocket && expectedPiece != null)
            {
                expectedPiece.SetBoosterTargeted(true);
                expectedPiece.SetTutorialTargeted(true, tutorialDimSortingOrder + 2);
                return;
            }

            if (boosterType == BoosterRewardType.Hammer)
            {
                if (expectedPiece != null)
                {
                    expectedPiece.SetBoosterTargeted(true);
                    expectedPiece.SetTutorialTargeted(true, tutorialDimSortingOrder + 2);
                    if (!UsesWholePieceHammerHighlight(expectedPiece))
                    {
                        ShowHammerCellOutlineForTutorial(
                            expectedPiece,
                            hammerCellCenter,
                            hammerCellSize,
                            tutorialDimSortingOrder + 2);
                    }
                }
                tutorialTargetIndicator?.Hide();
                return;
            }
        }

        private void SetVisible(bool visible)
        {
            if (boardDimBackdrop != null)
                boardDimBackdrop.SetActive(visible);
            if (targetingUi != null)
                targetingUi.SetActive(visible);

            if (!visible)
            {
                tutorialTargetIndicator?.Hide();
                IBoosterFirstUseTutorialGate gate = BoosterFirstUseTutorialRuntime.Gate;
                bool hammerFirstUse = gate != null && gate.IsFirstUseActive(BoosterRewardType.Hammer);
                bool rocketFirstUse = gate != null && gate.IsFirstUseActive(BoosterRewardType.Rocket);
                bool timerFirstUse = gate != null && gate.IsFirstUseActive(BoosterRewardType.FreezeTimer);

                if (!hammerFirstUse && !rocketFirstUse && !timerFirstUse)
                    SetSelectionGlow((BoosterRewardType?)null);
                else
                {
                    BoosterRewardType? focus = rocketFirstUse
                        ? BoosterRewardType.Rocket
                        : hammerFirstUse ? BoosterRewardType.Hammer : BoosterRewardType.FreezeTimer;
                    SetSelectionGlow(focus);
                }
                StopSelectionSparkles();
                ClearPieceHighlights();
                ReturnHammerCellOutlines();
                RestoreGameplayHud();
                RestoreInactiveBoosterButtons();
            }
        }

        private void HideGameplayHud()
        {
            if (hiddenHudGroupStates.Count > 0 || hiddenHudObjectStates.Count > 0)
                return;

            ResolveGameplayHudReferences();

            if (gameplayHudGroups != null)
            {
                for (int index = 0; index < gameplayHudGroups.Length; index++)
                {
                    CanvasGroup group = gameplayHudGroups[index];
                    if (group == null || ContainsHiddenHudGroup(group))
                        continue;

                    hiddenHudGroupStates.Add(new HudGroupState(group));
                    group.alpha = 0f;
                    group.interactable = false;
                    group.blocksRaycasts = false;
                }
            }

            Transform uiRoot = targetingUi != null ? targetingUi.transform.parent : null;
            if (uiRoot != null)
            {
                for (int i = 0; i < uiRoot.childCount; i++)
                {
                    Transform child = uiRoot.GetChild(i);
                    if (child == null || child.gameObject == targetingUi)
                        continue;

                    if (IsBoosterButtonHierarchy(child) || IsTutorialElement(child))
                        continue;

                    CanvasGroup cg = child.GetComponent<CanvasGroup>();
                    if (cg != null)
                    {
                        if (!ContainsHiddenHudGroup(cg))
                        {
                            hiddenHudGroupStates.Add(new HudGroupState(cg));
                            cg.alpha = 0f;
                            cg.interactable = false;
                            cg.blocksRaycasts = false;
                        }
                    }
                    else if (child.gameObject.activeSelf)
                    {
                        hiddenHudObjectStates.Add(new GameObjectState(child.gameObject));
                        child.gameObject.SetActive(false);
                    }
                }
            }
        }

        private bool ContainsHiddenHudGroup(CanvasGroup group)
        {
            for (int index = 0; index < hiddenHudGroupStates.Count; index++)
            {
                if (hiddenHudGroupStates[index].Group == group)
                    return true;
            }

            return false;
        }

        private void RestoreGameplayHud()
        {
            for (int index = 0; index < hiddenHudGroupStates.Count; index++)
            {
                HudGroupState state = hiddenHudGroupStates[index];
                if (state.Group == null)
                    continue;

                state.Group.alpha = state.Alpha;
                state.Group.interactable = state.Interactable;
                state.Group.blocksRaycasts = state.BlocksRaycasts;
            }
            hiddenHudGroupStates.Clear();

            for (int index = 0; index < hiddenHudObjectStates.Count; index++)
            {
                GameObjectState state = hiddenHudObjectStates[index];
                if (state.Target == null)
                    continue;

                state.Target.SetActive(state.WasActive);
            }
            hiddenHudObjectStates.Clear();
        }

        private void ResolveGameplayHudReferences()
        {
            Transform uiRoot = targetingUi != null ? targetingUi.transform.parent : null;
            if (uiRoot == null)
                return;

            List<CanvasGroup> groups = new List<CanvasGroup>();
            if (gameplayHudGroups != null)
            {
                for (int i = 0; i < gameplayHudGroups.Length; i++)
                {
                    if (gameplayHudGroups[i] != null && !groups.Contains(gameplayHudGroups[i]))
                        groups.Add(gameplayHudGroups[i]);
                }
            }

            for (int i = 0; i < uiRoot.childCount; i++)
            {
                Transform child = uiRoot.GetChild(i);
                if (child == null || child.gameObject == targetingUi)
                    continue;

                if (IsBoosterButtonHierarchy(child) || IsTutorialElement(child))
                    continue;

                CanvasGroup cg = child.GetComponent<CanvasGroup>();
                if (cg != null && !groups.Contains(cg))
                    groups.Add(cg);
            }

            if (groups.Count > 0)
                gameplayHudGroups = groups.ToArray();
        }

        private bool IsTutorialElement(Transform t)
        {
            if (t == null)
                return false;

            if (t.GetComponent<TutorialHandView>() != null || t.GetComponentInChildren<TutorialHandView>(true) != null)
                return true;

            if (t.name.IndexOf("Tutorial", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private bool IsBoosterButtonHierarchy(Transform t)
        {
            if (t == null)
                return false;

            if (rocketBoosterButtonGroup != null && (t == rocketBoosterButtonGroup.transform || rocketBoosterButtonGroup.transform.IsChildOf(t) || t.IsChildOf(rocketBoosterButtonGroup.transform)))
                return true;
            if (hammerBoosterButtonGroup != null && (t == hammerBoosterButtonGroup.transform || hammerBoosterButtonGroup.transform.IsChildOf(t) || t.IsChildOf(hammerBoosterButtonGroup.transform)))
                return true;
            if (timerBoosterButtonGroup != null && (t == timerBoosterButtonGroup.transform || timerBoosterButtonGroup.transform.IsChildOf(t) || t.IsChildOf(timerBoosterButtonGroup.transform)))
                return true;

            if (t.GetComponent<BoosterButton>() != null || t.GetComponentInChildren<BoosterButton>(true) != null)
                return true;
            if (t.name.IndexOf("Booster", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private void ResolveSparkleReferences()
        {
            if (rocketButtonSparkles == null && rocketBoosterButtonGroup != null)
                rocketButtonSparkles = rocketBoosterButtonGroup.GetComponentInChildren<ParticleSystem>(true);

            if (hammerButtonSparkles == null && hammerBoosterButtonGroup != null)
                hammerButtonSparkles = hammerBoosterButtonGroup.GetComponentInChildren<ParticleSystem>(true);

            if (timerButtonSparkles == null && timerBoosterButtonGroup != null)
                timerButtonSparkles = timerBoosterButtonGroup.GetComponentInChildren<ParticleSystem>(true);

            if (rocketIconSparkles == null && rocketIcon != null)
                rocketIconSparkles = rocketIcon.GetComponentInChildren<ParticleSystem>(true);

            if (hammerIconSparkles == null && hammerIcon != null)
                hammerIconSparkles = hammerIcon.GetComponentInChildren<ParticleSystem>(true);
        }

        private void HideInactiveBoosterButtons(Mode mode)
        {
            if (hiddenBoosterButtonStates.Count > 0)
                return;

            if (mode == Mode.Rocket)
            {
                HideBoosterButton(hammerBoosterButtonGroup);
                HideBoosterButton(timerBoosterButtonGroup);
                return;
            }

            HideBoosterButton(rocketBoosterButtonGroup);
            HideBoosterButton(timerBoosterButtonGroup);
        }

        private void HideInactiveBoosterButtons(BoosterRewardType boosterType)
        {
            if (hiddenBoosterButtonStates.Count > 0)
                return;

            if (boosterType != BoosterRewardType.Rocket)
                HideBoosterButton(rocketBoosterButtonGroup);
            if (boosterType != BoosterRewardType.Hammer)
                HideBoosterButton(hammerBoosterButtonGroup);
            if (boosterType != BoosterRewardType.FreezeTimer)
                HideBoosterButton(timerBoosterButtonGroup);
        }

        private void HideBoosterButton(CanvasGroup group)
        {
            if (group == null || ContainsHiddenBoosterButton(group))
                return;

            hiddenBoosterButtonStates.Add(new HudGroupState(group));
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        private bool ContainsHiddenBoosterButton(CanvasGroup group)
        {
            for (int index = 0; index < hiddenBoosterButtonStates.Count; index++)
            {
                if (hiddenBoosterButtonStates[index].Group == group)
                    return true;
            }

            return false;
        }

        private void RestoreInactiveBoosterButtons()
        {
            for (int index = 0; index < hiddenBoosterButtonStates.Count; index++)
            {
                HudGroupState state = hiddenBoosterButtonStates[index];
                if (state.Group == null)
                    continue;

                state.Group.alpha = state.Alpha;
                state.Group.interactable = state.Interactable;
                state.Group.blocksRaycasts = state.BlocksRaycasts;
            }

            hiddenBoosterButtonStates.Clear();
        }

        private void PlaySelectionSparkles(bool isRocket)
        {
            PlaySelectionSparkles(isRocket ? BoosterRewardType.Rocket : BoosterRewardType.Hammer);
        }

        private void PlaySelectionSparkles(BoosterRewardType boosterType)
        {
            StopSelectionSparkles();

            if (boosterType == BoosterRewardType.Rocket)
            {
                PlaySparkles(rocketButtonSparkles);
                PlaySparkles(rocketIconSparkles);
                return;
            }

            if (boosterType == BoosterRewardType.Hammer)
            {
                PlaySparkles(hammerButtonSparkles);
                PlaySparkles(hammerIconSparkles);
                return;
            }

            if (boosterType == BoosterRewardType.FreezeTimer)
            {
                PlaySparkles(timerButtonSparkles);
            }
        }

        private void ResolveButtonGlowReferences()
        {
            EnsureBoosterButtonGroups();

            if (rocketButtonGlow == null && rocketBoosterButtonGroup != null)
            {
                BoosterButton button = rocketBoosterButtonGroup.GetComponent<BoosterButton>();
                if (button != null && button.SelectionGlow != null)
                    rocketButtonGlow = button.SelectionGlow;
                if (rocketButtonGlow == null)
                {
                    Transform glow = rocketBoosterButtonGroup.transform.Find("RocketSelectionGlow");
                    if (glow != null)
                        rocketButtonGlow = glow.gameObject;
                }
            }

            if (hammerButtonGlow == null && hammerBoosterButtonGroup != null)
            {
                BoosterButton button = hammerBoosterButtonGroup.GetComponent<BoosterButton>();
                if (button != null && button.SelectionGlow != null)
                    hammerButtonGlow = button.SelectionGlow;
                if (hammerButtonGlow == null)
                {
                    Transform glow = hammerBoosterButtonGroup.transform.Find("HammerSelectionGlow");
                    if (glow != null)
                        hammerButtonGlow = glow.gameObject;
                }
            }

            if (timerButtonGlow == null && timerBoosterButtonGroup != null)
            {
                BoosterButton button = timerBoosterButtonGroup.GetComponent<BoosterButton>();
                if (button != null && button.SelectionGlow != null)
                    timerButtonGlow = button.SelectionGlow;
                if (timerButtonGlow == null)
                {
                    TimerBooster tb = timerBoosterButtonGroup.GetComponent<TimerBooster>();
                    if (tb != null && tb.SelectionGlow != null)
                        timerButtonGlow = tb.SelectionGlow;
                }
                if (timerButtonGlow == null)
                {
                    Transform glow = timerBoosterButtonGroup.transform.Find("TimerSelectionGlow");
                    if (glow != null)
                        timerButtonGlow = glow.gameObject;
                }
                if (timerButtonGlow == null)
                {
                    for (int i = 0; i < timerBoosterButtonGroup.transform.childCount; i++)
                    {
                        Transform c = timerBoosterButtonGroup.transform.GetChild(i);
                        if (c.name.IndexOf("glow", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            timerButtonGlow = c.gameObject;
                            break;
                        }
                    }
                }
            }

            if (timerButtonGlow == null)
            {
                BoosterButton timerBtn = BoosterFirstUseTutorialRuntime.GetButton(BoosterRewardType.FreezeTimer);
                if (timerBtn != null && timerBtn.SelectionGlow != null)
                    timerButtonGlow = timerBtn.SelectionGlow;
            }
        }

        private void SetSelectionGlow(bool? isRocket)
        {
            if (!isRocket.HasValue)
            {
                SetSelectionGlow((BoosterRewardType?)null);
                return;
            }

            SetSelectionGlow(isRocket.Value ? BoosterRewardType.Rocket : BoosterRewardType.Hammer);
        }

        private void SetSelectionGlow(BoosterRewardType? focusedBooster)
        {
            EnsureBoosterButtonGroups();
            ResolveButtonGlowReferences();

            bool rocketActive = focusedBooster == BoosterRewardType.Rocket;
            bool hammerActive = focusedBooster == BoosterRewardType.Hammer;
            bool timerActive = focusedBooster == BoosterRewardType.FreezeTimer;

            if (rocketButtonGlow != null && rocketButtonGlow.activeSelf != rocketActive)
                rocketButtonGlow.SetActive(rocketActive);
            if (rocketBoosterButtonGroup != null)
            {
                BoosterButton button = rocketBoosterButtonGroup.GetComponent<BoosterButton>();
                button?.SetSelectionGlowVisible(rocketActive);
            }

            if (hammerButtonGlow != null && hammerButtonGlow.activeSelf != hammerActive)
                hammerButtonGlow.SetActive(hammerActive);
            if (hammerBoosterButtonGroup != null)
            {
                BoosterButton button = hammerBoosterButtonGroup.GetComponent<BoosterButton>();
                button?.SetSelectionGlowVisible(hammerActive);
            }

            if (timerButtonGlow != null && timerButtonGlow.activeSelf != timerActive)
                timerButtonGlow.SetActive(timerActive);
            if (timerBoosterButtonGroup != null)
            {
                BoosterButton button = timerBoosterButtonGroup.GetComponent<BoosterButton>();
                button?.SetSelectionGlowVisible(timerActive);

                TimerBooster tb = timerBoosterButtonGroup.GetComponent<TimerBooster>();
                if (tb != null && tb.SelectionGlow != null && tb.SelectionGlow.activeSelf != timerActive)
                    tb.SelectionGlow.SetActive(timerActive);
            }

            if (timerActive)
            {
                BoosterButton timerBtn = BoosterFirstUseTutorialRuntime.GetButton(BoosterRewardType.FreezeTimer);
                timerBtn?.SetSelectionGlowVisible(true);
            }
            else if (hammerActive)
            {
                BoosterButton hammerBtn = BoosterFirstUseTutorialRuntime.GetButton(BoosterRewardType.Hammer);
                hammerBtn?.SetSelectionGlowVisible(true);
            }
            else if (rocketActive)
            {
                BoosterButton rocketBtn = BoosterFirstUseTutorialRuntime.GetButton(BoosterRewardType.Rocket);
                rocketBtn?.SetSelectionGlowVisible(true);
            }
        }

        private void Update()
        {
            IBoosterFirstUseTutorialGate gate = BoosterFirstUseTutorialRuntime.Gate;
            bool isTimer = gate != null && (gate.IsBoosterHighlighted(BoosterRewardType.FreezeTimer) || gate.IsFirstUseActive(BoosterRewardType.FreezeTimer));
            bool isHammer = gate != null && (gate.IsBoosterHighlighted(BoosterRewardType.Hammer) || gate.IsFirstUseActive(BoosterRewardType.Hammer));
            bool isRocket = gate != null && (gate.IsBoosterHighlighted(BoosterRewardType.Rocket) || gate.IsFirstUseActive(BoosterRewardType.Rocket));
            bool isAnyTutorialActive = isTimer || isHammer || isRocket;

            if (isAnyTutorialActive)
            {
                wasTutorialFocusActive = true;
                EnsureBoosterButtonGroups();

                if (boardDimBackdrop != null && !boardDimBackdrop.activeSelf)
                    boardDimBackdrop.SetActive(true);

                BoosterRewardType focus = isTimer ? BoosterRewardType.FreezeTimer : (isHammer ? BoosterRewardType.Hammer : BoosterRewardType.Rocket);
                HideInactiveBoosterButtons(focus);
                SetSelectionGlow(focus);

                TutorialHandView hand = cachedTutorialHand != null ? cachedTutorialHand : TutorialHandView.Active;
                if (hand == null)
                {
                    cachedTutorialHand = Object.FindFirstObjectByType<TutorialHandView>(FindObjectsInactive.Include);
                    hand = cachedTutorialHand;
                }
                if (hand != null && (!hand.gameObject.activeSelf || !hand.IsPlaying))
                {
                    hand.RefreshActiveTutorial();
                }
            }
            else if (wasTutorialFocusActive)
            {
                wasTutorialFocusActive = false;
                SetVisible(false);
            }
        }

        private void StopSelectionSparkles()
        {
            StopSparkles(rocketButtonSparkles);
            StopSparkles(hammerButtonSparkles);
            StopSparkles(timerButtonSparkles);
            StopSparkles(rocketIconSparkles);
            StopSparkles(hammerIconSparkles);
        }

        private static void PlaySparkles(ParticleSystem sparkles)
        {
            if (sparkles == null)
                return;

            sparkles.Clear(true);
            sparkles.Play(true);
        }

        private static void StopSparkles(ParticleSystem sparkles)
        {
            if (sparkles != null)
                sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void ApplyRocketPieceHighlights()
        {
            var pieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                if (piece == null)
                    continue;

                // Rocket explicitly breaks ice before carrying the underlying
                // piece, so frozen pieces are valid rocket targets.
                bool isValidTarget = !piece.IsShredderInteractionLocked;
                piece.SetBoosterTargeted(isValidTarget);
            }
        }

        private void SetBoardPiecesInForeground(bool isForeground)
        {
            var pieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                if (piece != null)
                    piece.SetBoosterTargetingForeground(isForeground, tutorialDimSortingOrder + 2);
            }
        }

        private void PrewarmHammerOutlines()
        {
            if (hammerCellOutlinePrefab == null)
                return;

            for (int index = 0; index < hammerCellOutlinePrewarmCount; index++)
            {
                // Pool prewarming is the sole permitted runtime instantiation
                // path for this presentation-only effect.
                HammerTargetCellOutlineView outline = Instantiate(
                    hammerCellOutlinePrefab,
                    transform);
                outline.Hide();
                availableHammerOutlines.Push(outline);
            }
        }

        private void ShowHammerCellOutlines()
        {
            ClearPieceHighlights();
            ReturnHammerCellOutlines();
            if (hammerCellOutlinePrefab == null)
            {
                Debug.LogWarning(
                    "[BoosterTargetingPresentation] Hammer cell outline prefab is not assigned.",
                    this);
                return;
            }

            var pieces = PuzzlePiece.ActivePieces;
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                PuzzlePiece piece = pieces[pieceIndex];
                if (piece == null || piece.IsFrozen || piece.IsShredderInteractionLocked)
                    continue;

                // Hammer defuses an armed bomb as one gameplay action. Reuse
                // the piece's composite outline so targeting reflects that
                // whole-piece outcome instead of suggesting a single-cell hit.
                if (UsesWholePieceHammerHighlight(piece))
                {
                    piece.SetBoosterTargeted(true);
                    continue;
                }

                targetableCellBuffer.Clear();
                piece.CollectTargetableCells(targetableCellBuffer);
                LineRenderer sourceOutline = piece.Outline;
                Material material = sourceOutline != null ? sourceOutline.sharedMaterial : null;
                int sortingOrder = Mathf.Max(
                    tutorialDimSortingOrder + 2,
                    sourceOutline != null ? sourceOutline.sortingOrder : 0);
                for (int cellIndex = 0; cellIndex < targetableCellBuffer.Count; cellIndex++)
                {
                    if (availableHammerOutlines.Count == 0)
                    {
                        Debug.LogWarning(
                            "[BoosterTargetingPresentation] Hammer outline pool capacity was exhausted.",
                            this);
                        return;
                    }

                    HammerTargetCellOutlineView outline = availableHammerOutlines.Pop();
                    outline.Show(targetableCellBuffer[cellIndex], material, sortingOrder);
                    activeHammerOutlines.Add(outline);
                }
            }
        }

        private static bool UsesWholePieceHammerHighlight(PuzzlePiece piece)
        {
            return piece != null && piece.IsBomb && !piece.IsBombDefused;
        }

        private void ShowHammerCellOutlineForTutorial(
            PuzzlePiece piece,
            Vector2 hammerCellCenter,
            Vector2 hammerCellSize,
            int sortingOrder)
        {
            if (availableHammerOutlines.Count == 0 || hammerCellOutlinePrefab == null)
                return;

            LineRenderer sourceOutline = piece != null ? piece.Outline : null;
            Material material = sourceOutline != null ? sourceOutline.sharedMaterial : null;
            if (material == null && hammerCellOutlinePrefab != null)
            {
                LineRenderer prefabLine = hammerCellOutlinePrefab.GetComponent<LineRenderer>();
                if (prefabLine != null)
                    material = prefabLine.sharedMaterial;
            }

            PuzzlePiece.TargetableCell cell = new PuzzlePiece.TargetableCell(hammerCellCenter, hammerCellSize);
            HammerTargetCellOutlineView outline = availableHammerOutlines.Pop();
            outline.Show(cell, material, sortingOrder);
            activeHammerOutlines.Add(outline);
        }

        private void ReturnHammerCellOutlines()
        {
            for (int index = 0; index < activeHammerOutlines.Count; index++)
            {
                HammerTargetCellOutlineView outline = activeHammerOutlines[index];
                if (outline == null)
                    continue;

                outline.Hide();
                availableHammerOutlines.Push(outline);
            }

            activeHammerOutlines.Clear();
        }

        private static void ClearPieceHighlights()
        {
            var pieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                if (piece != null)
                {
                    piece.SetBoosterTargeted(false);
                    piece.SetTutorialTargeted(false);
                    piece.SetBoosterTargetingForeground(false);
                }
            }
        }
    }
}
