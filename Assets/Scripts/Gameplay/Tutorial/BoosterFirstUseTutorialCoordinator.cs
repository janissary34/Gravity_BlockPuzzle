using System;
using System.Collections.Generic;
using GravityPuzzle.Config;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Input;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Infrastructure.Services;
using GravityPuzzle.Presentation.Views;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Tutorial
{
    public enum BoosterFirstUseTutorialState
    {
        NotStarted,
        PendingRequiredUse,
        AwaitingBoosterTap,
        AwaitingExactTarget,
        ResolvingEffect,
        Completed
    }

    public interface IBoosterFirstUseTutorialGate
    {
        bool BlocksBoardDrag { get; }
        bool IsBoosterHighlighted(BoosterRewardType boosterType);
        bool AllowsBoosterActivation(BoosterRewardType boosterType);
        bool AllowsTarget(BoosterRewardType boosterType, BoardTargetResolver.Target target);
        void NotifyBoosterActivated(BoosterRewardType boosterType);
        void NotifyEffectApplied(BoosterRewardType boosterType);
    }

    /// <summary>Persisted state is intentionally separate from game lifecycle.</summary>
    public static class BoosterFirstUseTutorialProgress
    {
        private const string KeyPrefix = "GravityPuzzle.BoosterFirstUse.";
        private const string InventoryRepairKeyPrefix = "GravityPuzzle.BoosterFirstUse.InventoryRepair.";

        public static BoosterFirstUseTutorialState GetState(BoosterRewardConfig config)
        {
            if (config == null)
                return BoosterFirstUseTutorialState.NotStarted;

            int rawState = PlayerPrefs.GetInt(GetKey(config), (int)BoosterFirstUseTutorialState.NotStarted);
            return Enum.IsDefined(typeof(BoosterFirstUseTutorialState), rawState)
                ? (BoosterFirstUseTutorialState)rawState
                : BoosterFirstUseTutorialState.NotStarted;
        }

        public static void SetState(BoosterRewardConfig config, BoosterFirstUseTutorialState state)
        {
            if (config == null)
                return;

            PlayerPrefs.SetInt(GetKey(config), (int)state);
            PlayerPrefs.Save();
        }

        /// <summary>Editor tooling uses this to replay a first-use lesson.</summary>
        public static void ClearState(BoosterRewardConfig config)
        {
            if (config == null)
                return;

            PlayerPrefs.DeleteKey(GetKey(config));
            PlayerPrefs.DeleteKey(GetInventoryRepairKey(config));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Repairs saves produced before the first-use reward and persistent
        /// inventory became one transaction. The marker makes this migration
        /// idempotent: a legitimately consumed booster is never replenished on
        /// every level load.
        /// </summary>
        public static void RepairMissingInitialInventory(BoosterRewardConfig config)
        {
            if (config == null || GetState(config) == BoosterFirstUseTutorialState.NotStarted)
                return;

            string repairKey = GetInventoryRepairKey(config);
            if (PlayerPrefs.GetInt(repairKey, 0) == 1)
                return;

            if (BoosterInventoryRuntime.Current.GetCount(config.BoosterType) <= 0)
                BoosterInventoryRuntime.Current.Grant(config.BoosterType, 1);

            PlayerPrefs.SetInt(repairKey, 1);
            PlayerPrefs.Save();
        }

        private static string GetKey(BoosterRewardConfig config)
        {
            string id = string.IsNullOrWhiteSpace(config.TutorialId)
                ? config.BoosterType.ToString()
                : config.TutorialId;
            return KeyPrefix + id + "." + config.BoosterType;
        }

        private static string GetInventoryRepairKey(BoosterRewardConfig config)
        {
            string id = string.IsNullOrWhiteSpace(config.TutorialId)
                ? config.BoosterType.ToString()
                : config.TutorialId;
            return InventoryRepairKeyPrefix + id + "." + config.BoosterType;
        }
    }

    /// <summary>
    /// Coordinates the forced first use. It grants no gameplay effects itself:
    /// concrete boosters remain the sole mutation authority and report their
    /// successful effect through this gate.
    /// </summary>
    public sealed class BoosterFirstUseTutorialCoordinator : IBoosterFirstUseTutorialGate
    {
        private readonly PrototypeBoard board;
        private readonly Dictionary<BoosterRewardType, BoosterButton> buttons =
            new Dictionary<BoosterRewardType, BoosterButton>();

        private BoosterRewardConfig activeConfig;
        private BoosterFirstUseTutorialState state;

        public BoosterFirstUseTutorialCoordinator(PrototypeBoard board)
        {
            this.board = board;
        }

        public bool BlocksBoardDrag
        {
            get
            {
                // The countdown deliberately starts with the player's first
                // piece interaction. A Freeze Timer lesson can be presented
                // before that interaction, but blocking the board here would
                // make the highlighted timer button unavailable forever:
                // it only becomes usable after the countdown has started.
                if (state == BoosterFirstUseTutorialState.AwaitingBoosterTap &&
                    activeConfig != null &&
                    activeConfig.BoosterType == BoosterRewardType.FreezeTimer &&
                    (board == null || !board.IsTimerStarted))
                {
                    return false;
                }

                return state == BoosterFirstUseTutorialState.AwaitingBoosterTap ||
                       state == BoosterFirstUseTutorialState.AwaitingExactTarget ||
                       state == BoosterFirstUseTutorialState.ResolvingEffect;
            }
        }

        public bool IsBoosterHighlighted(BoosterRewardType boosterType)
        {
            return state == BoosterFirstUseTutorialState.AwaitingBoosterTap &&
                   activeConfig != null && activeConfig.BoosterType == boosterType;
        }

        public static bool Supports(BoosterRewardType boosterType)
        {
            return boosterType == BoosterRewardType.Rocket ||
                   boosterType == BoosterRewardType.Hammer ||
                   boosterType == BoosterRewardType.FreezeTimer;
        }

        public void RegisterButton(BoosterButton button)
        {
            if (button == null || !button.HasBoosterType)
                return;

            buttons[button.BoosterType] = button;
            if (state == BoosterFirstUseTutorialState.AwaitingBoosterTap &&
                activeConfig != null && activeConfig.BoosterType == button.BoosterType)
            {
                TutorialHandView.ShowForButton(button);
            }
        }

        public void BeginClaim(BoosterRewardConfig config)
        {
            if (config == null)
                return;

            if (!Supports(config.BoosterType))
            {
                Debug.LogWarning("[BoosterTutorial] Unsupported reward was claimed without a first-use lesson.");
                BoosterRewardUnlockState.MarkPresented(config);
                return;
            }

            BoosterFirstUseTutorialState persisted = BoosterFirstUseTutorialProgress.GetState(config);
            if (persisted == BoosterFirstUseTutorialState.Completed)
            {
                BoosterFirstUseTutorialProgress.RepairMissingInitialInventory(config);
                return;
            }

            if (persisted == BoosterFirstUseTutorialState.NotStarted)
            {
                BoosterInventoryRuntime.Current.Grant(config.BoosterType, config.GrantAmount);
                BoosterFirstUseTutorialProgress.SetState(config, BoosterFirstUseTutorialState.PendingRequiredUse);
            }

            StartPending(config);
        }

        public void ResumePending(BoosterRewardConfig[] configs)
        {
            if (configs == null)
                return;

            for (int index = 0; index < configs.Length; index++)
            {
                BoosterRewardConfig config = configs[index];
                if (config == null || !Supports(config.BoosterType))
                    continue;

                BoosterFirstUseTutorialProgress.RepairMissingInitialInventory(config);
                if (BoosterFirstUseTutorialProgress.GetState(config) !=
                    BoosterFirstUseTutorialState.PendingRequiredUse)
                    continue;

                StartPending(config);
                return;
            }
        }

        public bool AllowsBoosterActivation(BoosterRewardType boosterType)
        {
            return state != BoosterFirstUseTutorialState.AwaitingBoosterTap ||
                   (activeConfig != null && activeConfig.BoosterType == boosterType);
        }

        public void NotifyBoosterActivated(BoosterRewardType boosterType)
        {
            if (activeConfig == null || activeConfig.BoosterType != boosterType ||
                state != BoosterFirstUseTutorialState.AwaitingBoosterTap)
                return;

            state = boosterType == BoosterRewardType.FreezeTimer
                ? BoosterFirstUseTutorialState.ResolvingEffect
                : BoosterFirstUseTutorialState.AwaitingExactTarget;

            TutorialHandView.Hide();
            if (boosterType == BoosterRewardType.FreezeTimer)
                BoosterTargetingPresentation.Hide();
            if (state == BoosterFirstUseTutorialState.AwaitingExactTarget &&
                TryGetExpectedWorldPosition(out Vector2 position))
            {
                ShowRequiredBoardTarget(position);
                TutorialHandView.ShowForBoardTarget(position, PrototypeBootstrap.SceneCamera);
            }
        }

        public bool AllowsTarget(BoosterRewardType boosterType, BoardTargetResolver.Target target)
        {
            if (state != BoosterFirstUseTutorialState.AwaitingExactTarget)
                return true;

            if (activeConfig == null || activeConfig.BoosterType != boosterType ||
                !IsExpectedPiece(target.Piece))
                return false;

            if (boosterType != BoosterRewardType.Hammer)
                return true;

            return TryGetExpectedHammerCell(target.Piece, out GridCoordinate expectedCell) &&
                   target.Cell.Equals(expectedCell);
        }

        public void NotifyEffectApplied(BoosterRewardType boosterType)
        {
            if (activeConfig == null || activeConfig.BoosterType != boosterType ||
                state == BoosterFirstUseTutorialState.Completed)
                return;

            state = BoosterFirstUseTutorialState.Completed;
            BoosterFirstUseTutorialProgress.SetState(activeConfig, state);
            TutorialHandView.Hide();
            if (boosterType != BoosterRewardType.FreezeTimer)
                board?.ResumeTimer(this);
            activeConfig = null;
        }

        private void StartPending(BoosterRewardConfig config)
        {
            // A required-use tutorial must always have one consumable use.
            // This also repairs saves created by older builds where the
            // tutorial state was persisted before its inventory grant.
            if (BoosterInventoryRuntime.Current.GetCount(config.BoosterType) <= 0)
                BoosterInventoryRuntime.Current.Grant(config.BoosterType, 1);

            if (config.BoosterType != BoosterRewardType.FreezeTimer &&
                !HasResolvableTarget(config))
            {
                Debug.LogError(
                    "[BoosterTutorial] Required target is missing or invalid. Tutorial remains pending, but input was released to avoid a player soft-lock.");
                board?.ResumeTimer(this);
                activeConfig = null;
                state = BoosterFirstUseTutorialState.PendingRequiredUse;
                return;
            }

            activeConfig = config;
            state = BoosterFirstUseTutorialState.AwaitingBoosterTap;

            // Countdown ownership stays with the first accepted piece action.
            // A tutorial may highlight a booster before that first action, but
            // it must not silently start the level while the player is still
            // reading the board.
            if (config.BoosterType != BoosterRewardType.FreezeTimer)
                board?.TryPauseTimer(this);
            BoosterTargetingPresentation.ShowFirstUseBoosterFocus(config.BoosterType);

            if (buttons.TryGetValue(config.BoosterType, out BoosterButton button))
                TutorialHandView.ShowForButton(button);
        }

        private bool IsExpectedPiece(PuzzlePiece piece)
        {
            if (piece == null || activeConfig == null ||
                string.IsNullOrWhiteSpace(activeConfig.TutorialTargetPieceId))
                return false;

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null || level.pieces == null)
                return false;

            for (int index = 0; index < level.pieces.Count; index++)
            {
                PieceDefinition definition = level.pieces[index];
                if (definition != null && definition.tutorialTargetId == activeConfig.TutorialTargetPieceId)
                    return piece.SourcePieceId == index;
            }

            return false;
        }

        private bool HasResolvableTarget(BoosterRewardConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.TutorialTargetPieceId))
                return false;

            BoosterRewardConfig previousConfig = activeConfig;
            activeConfig = config;
            bool found = false;
            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < pieces.Count; index++)
            {
                if (IsExpectedPiece(pieces[index]))
                {
                    found = true;
                    break;
                }
            }

            activeConfig = previousConfig;
            return found;
        }

        private bool TryGetExpectedWorldPosition(out Vector2 position)
        {
            position = default;
            if (activeConfig == null)
                return false;

            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                if (!IsExpectedPiece(piece))
                    continue;

                if (activeConfig.BoosterType == BoosterRewardType.Rocket)
                {
                    position = piece.GetPreferredRocketAttachmentPoint();
                    return true;
                }

                if (!TryGetExpectedHammerCell(piece, out GridCoordinate targetCell))
                    return false;

                GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
                if (level == null)
                    return false;

                position = GravityLevelGridCoordinates.FineCellToWorld(
                    level,
                    targetCell);
                return true;
            }

            return false;
        }

        private void ShowRequiredBoardTarget(Vector2 worldPosition)
        {
            if (activeConfig == null || !TryGetExpectedPiece(out PuzzlePiece expectedPiece))
                return;

            if (activeConfig.BoosterType == BoosterRewardType.Hammer)
            {
                GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
                if (level == null)
                    return;

                float fineCellSize = 1f / level.subdivisions;
                BoosterTargetingPresentation.ShowFirstUseBoardTarget(
                    activeConfig.BoosterType,
                    expectedPiece,
                    worldPosition,
                    Vector2.one * fineCellSize);
                return;
            }

            BoosterTargetingPresentation.ShowFirstUseBoardTarget(
                activeConfig.BoosterType,
                expectedPiece,
                worldPosition,
                Vector2.zero);
        }

        private bool TryGetExpectedPiece(out PuzzlePiece expectedPiece)
        {
            expectedPiece = null;
            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                if (!IsExpectedPiece(piece))
                    continue;

                expectedPiece = piece;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves the authored Hammer cell through the live piece model. The
        /// authored coordinate identifies a local cell at spawn, while the
        /// model anchor follows gravity; using the model prevents the tutorial
        /// marker and input gate from remaining at the old board position.
        /// </summary>
        private bool TryGetExpectedHammerCell(PuzzlePiece expectedPiece, out GridCoordinate expectedCell)
        {
            expectedCell = default;
            if (activeConfig == null || expectedPiece == null || board == null ||
                !board.TryGetPieceModel(expectedPiece, out PieceModel model) ||
                !TryGetExpectedDefinition(expectedPiece, out PieceDefinition definition) ||
                definition.cells == null || definition.cells.Count == 0)
                return false;

            Vector2Int minimum = QuarterTurnUtility.Rotate(
                definition.cells[0].localCell,
                definition.quarterTurns);
            for (int index = 1; index < definition.cells.Count; index++)
            {
                Vector2Int rotated = QuarterTurnUtility.Rotate(
                    definition.cells[index].localCell,
                    definition.quarterTurns);
                minimum = new Vector2Int(
                    Mathf.Min(minimum.x, rotated.x),
                    Mathf.Min(minimum.y, rotated.y));
            }

            Vector2Int initialAnchor = definition.origin + minimum;
            Vector2Int requestedLocalCell = activeConfig.TutorialTargetFineCell - initialAnchor;
            GridCoordinate requestedLocal = new GridCoordinate(
                requestedLocalCell.x,
                requestedLocalCell.y);
            IReadOnlyList<GridCoordinate> localCells = model.LocalCells;
            for (int index = 0; index < localCells.Count; index++)
            {
                if (!localCells[index].Equals(requestedLocal))
                    continue;

                expectedCell = model.GetWorldCell(index);
                return true;
            }

            return false;
        }

        private bool TryGetExpectedDefinition(PuzzlePiece expectedPiece, out PieceDefinition definition)
        {
            definition = null;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            int sourcePieceId = expectedPiece != null ? expectedPiece.SourcePieceId : -1;
            if (level == null || level.pieces == null || sourcePieceId < 0 ||
                sourcePieceId >= level.pieces.Count)
                return false;

            definition = level.pieces[sourcePieceId];
            return definition != null;
        }
    }

    public static class BoosterFirstUseTutorialRuntime
    {
        private static BoosterFirstUseTutorialCoordinator current;
        private static readonly List<BoosterButton> registeredButtons = new List<BoosterButton>();

        public static IBoosterFirstUseTutorialGate Gate => current;

        public static void Configure(BoosterFirstUseTutorialCoordinator coordinator)
        {
            current = coordinator;
            if (current == null)
                return;

            for (int index = 0; index < registeredButtons.Count; index++)
                current.RegisterButton(registeredButtons[index]);
        }

        public static void RegisterButton(BoosterButton button)
        {
            if (button != null && !registeredButtons.Contains(button))
                registeredButtons.Add(button);
            current?.RegisterButton(button);
        }
    }
}
