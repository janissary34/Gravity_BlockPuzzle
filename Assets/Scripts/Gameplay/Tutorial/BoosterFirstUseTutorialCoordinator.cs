using System;
using System.Collections.Generic;
using GravityPuzzle.Config;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Input;
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
        bool AllowsBoosterActivation(BoosterRewardType boosterType);
        bool AllowsTarget(BoosterRewardType boosterType, BoardTargetResolver.Target target);
        void NotifyBoosterActivated(BoosterRewardType boosterType);
        void NotifyEffectApplied(BoosterRewardType boosterType);
    }

    /// <summary>Persisted state is intentionally separate from game lifecycle.</summary>
    public static class BoosterFirstUseTutorialProgress
    {
        private const string KeyPrefix = "GravityPuzzle.BoosterFirstUse.";

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

        private static string GetKey(BoosterRewardConfig config)
        {
            string id = string.IsNullOrWhiteSpace(config.TutorialId)
                ? config.BoosterType.ToString()
                : config.TutorialId;
            return KeyPrefix + id + "." + config.BoosterType;
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

        public bool BlocksBoardDrag => state == BoosterFirstUseTutorialState.AwaitingBoosterTap ||
                                       state == BoosterFirstUseTutorialState.AwaitingExactTarget ||
                                       state == BoosterFirstUseTutorialState.ResolvingEffect;

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
                return;

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
                if (config == null || !Supports(config.BoosterType) ||
                    BoosterFirstUseTutorialProgress.GetState(config) != BoosterFirstUseTutorialState.PendingRequiredUse)
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

            Vector2Int expectedCell = activeConfig.TutorialTargetFineCell;
            return target.Cell.Equals(new GridCoordinate(expectedCell.x, expectedCell.y));
        }

        public void NotifyEffectApplied(BoosterRewardType boosterType)
        {
            if (activeConfig == null || activeConfig.BoosterType != boosterType ||
                state == BoosterFirstUseTutorialState.Completed)
                return;

            state = BoosterFirstUseTutorialState.Completed;
            BoosterFirstUseTutorialProgress.SetState(activeConfig, state);
            TutorialHandView.Hide();
            board?.ResumeTimer(this);
            activeConfig = null;
        }

        private void StartPending(BoosterRewardConfig config)
        {
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

            // Timer booster normally requires a running clock. Starting and
            // immediately owner-pausing it keeps the first-use lesson fair.
            if (config.BoosterType == BoosterRewardType.FreezeTimer)
                board?.StartTimer();
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

                GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
                if (level == null)
                    return false;

                Vector2Int targetCell = activeConfig.TutorialTargetFineCell;
                position = GravityLevelGridCoordinates.FineCellToWorld(
                    level,
                    new GridCoordinate(targetCell.x, targetCell.y));
                return true;
            }

            return false;
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
