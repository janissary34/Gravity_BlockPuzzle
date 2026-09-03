using System.Collections.Generic;
using TMPro;
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

        [Header("HUD Suppression")]
        [Tooltip("Normal HUD groups hidden while a Rocket or Hammer target mode is armed. Their original state is restored afterward.")]
        [SerializeField] private CanvasGroup[] gameplayHudGroups;

        [Header("Booster Button Focus")]
        [SerializeField] private CanvasGroup rocketBoosterButtonGroup;
        [SerializeField] private CanvasGroup hammerBoosterButtonGroup;
        [SerializeField] private CanvasGroup timerBoosterButtonGroup;

        [Header("Hammer Cell Outlines")]
        [SerializeField] private HammerTargetCellOutlineView hammerCellOutlinePrefab;
        [SerializeField, Min(1)] private int hammerCellOutlinePrewarmCount = 128;

        [Header("Copy")]
        [SerializeField] private string rocketTitle = "Rocket";
        [SerializeField] private string rocketInstruction = "Select a piece to eliminate";
        [SerializeField] private string hammerTitle = "Hammer";
        [SerializeField] private string hammerInstruction = "Select a block to break";

        private readonly Stack<HammerTargetCellOutlineView> availableHammerOutlines =
            new Stack<HammerTargetCellOutlineView>();
        private readonly List<HammerTargetCellOutlineView> activeHammerOutlines =
            new List<HammerTargetCellOutlineView>();
        private readonly List<PuzzlePiece.TargetableCell> targetableCellBuffer =
            new List<PuzzlePiece.TargetableCell>();
        private readonly List<HudGroupState> hiddenHudGroupStates =
            new List<HudGroupState>();
        private readonly List<HudGroupState> hiddenBoosterButtonStates =
            new List<HudGroupState>();

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

        private void Awake()
        {
            Active = this;
            PrewarmHammerOutlines();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            ClearPieceHighlights();
            RestoreGameplayHud();
            RestoreInactiveBoosterButtons();
            if (Active == this)
                Active = null;
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

        private void ShowInternal(Mode mode)
        {
            HideGameplayHud();
            HideInactiveBoosterButtons(mode);
            SetVisible(true);

            bool isRocket = mode == Mode.Rocket;
            if (titleText != null)
                titleText.text = isRocket ? rocketTitle : hammerTitle;
            if (instructionText != null)
                instructionText.text = isRocket ? rocketInstruction : hammerInstruction;
            if (rocketIcon != null)
                rocketIcon.gameObject.SetActive(isRocket);
            if (hammerIcon != null)
                hammerIcon.gameObject.SetActive(!isRocket);

            SetSelectionGlow(isRocket);
            PlaySelectionSparkles(isRocket);

            if (isRocket)
                ApplyRocketPieceHighlights();
            else
                ShowHammerCellOutlines();
        }

        private void SetVisible(bool visible)
        {
            if (boardDimBackdrop != null)
                boardDimBackdrop.SetActive(visible);
            if (targetingUi != null)
                targetingUi.SetActive(visible);

            if (!visible)
            {
                SetSelectionGlow(null);
                StopSelectionSparkles();
                ClearPieceHighlights();
                ReturnHammerCellOutlines();
                RestoreGameplayHud();
                RestoreInactiveBoosterButtons();
            }
        }

        private void HideGameplayHud()
        {
            if (hiddenHudGroupStates.Count > 0 || gameplayHudGroups == null)
                return;

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
            StopSelectionSparkles();

            if (isRocket)
            {
                PlaySparkles(rocketButtonSparkles);
                PlaySparkles(rocketIconSparkles);
                return;
            }

            PlaySparkles(hammerButtonSparkles);
            PlaySparkles(hammerIconSparkles);
        }

        private void SetSelectionGlow(bool? isRocket)
        {
            if (rocketButtonGlow != null)
                rocketButtonGlow.SetActive(isRocket.HasValue && isRocket.Value);
            if (hammerButtonGlow != null)
                hammerButtonGlow.SetActive(isRocket.HasValue && !isRocket.Value);
        }

        private void StopSelectionSparkles()
        {
            StopSparkles(rocketButtonSparkles);
            StopSparkles(hammerButtonSparkles);
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

                bool isValidTarget = !piece.IsBeingShredded && !piece.IsFrozen;
                piece.SetBoosterTargeted(isValidTarget);
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
                if (piece == null || piece.IsBeingShredded)
                    continue;

                targetableCellBuffer.Clear();
                piece.CollectTargetableCells(targetableCellBuffer);
                LineRenderer sourceOutline = piece.Outline;
                Material material = sourceOutline != null ? sourceOutline.sharedMaterial : null;
                int sortingOrder = sourceOutline != null ? sourceOutline.sortingOrder : 0;
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
                    piece.SetBoosterTargeted(false);
            }
        }
    }
}
