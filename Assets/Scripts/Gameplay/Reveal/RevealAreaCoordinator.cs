using System;
using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Reveal
{
    public enum RevealAreaKind
    {
        Box,
        Elevator
    }

    public readonly struct RevealAreaEvent
    {
        public RevealAreaEvent(string areaId, RevealAreaKind kind)
        {
            AreaId = areaId;
            Kind = kind;
        }

        public string AreaId { get; }
        public RevealAreaKind Kind { get; }
    }

    /// <summary>Explicit presentation boundary. Gameplay safely completes immediately when no view is authored.</summary>
    public interface IRevealAreaPresentation
    {
        void SetBoxRemaining(int remaining);
        void PlayOpening(RevealAreaKind kind, Action onCompleted);
    }

    /// <summary>
    /// Owns reveal-area state for one level. It consumes board commits rather
    /// than transforms, colliders, or Update polling.
    /// </summary>
    public sealed class RevealAreaCoordinator
    {
        private static readonly IReadOnlyList<PieceDefinition> EmptyPieces = new PieceDefinition[0];
        private sealed class BoxRuntime
        {
            public BoxRevealDefinition Definition;
            public RevealAreaState State = RevealAreaState.Locked;
            public readonly HashSet<int> CountedShredPieceIds = new HashSet<int>();
        }

        private sealed class ElevatorRuntime
        {
            public ElevatorRevealDefinition Definition;
            public RevealAreaState State = RevealAreaState.Locked;
            public readonly HashSet<int> CurrentOccupantIds = new HashSet<int>();
            public readonly HashSet<int> NextOccupantIds = new HashSet<int>();
            public readonly List<int> EnteredIds = new List<int>();
            public readonly List<int> ExitedIds = new List<int>();
        }

        private readonly List<BoxRuntime> boxes = new List<BoxRuntime>();
        private readonly List<ElevatorRuntime> elevators = new List<ElevatorRuntime>();
        private readonly Dictionary<string, IRevealAreaPresentation> presentations =
            new Dictionary<string, IRevealAreaPresentation>();
        private PrototypeBoard board;

        public event Action<RevealAreaEvent> AreaUnlockStarted;
        public event Action<RevealAreaEvent> AreaUnlocked;
        public event Action<string, int> PieceEnteredArea;
        public event Action<string, int> PieceExitedArea;

        public bool HasPendingRevealContent
        {
            get
            {
                for (int index = 0; index < boxes.Count; index++)
                    if (boxes[index].State != RevealAreaState.Unlocked)
                        return true;

                for (int index = 0; index < elevators.Count; index++)
                    if (elevators[index].State != RevealAreaState.Unlocked)
                        return true;

                return false;
            }
        }

        public void Initialize(PrototypeBoard owner, GravityLevelDefinition level)
        {
            board = owner ?? throw new ArgumentNullException(nameof(owner));
            boxes.Clear();
            elevators.Clear();

            if (level == null)
                return;

            int boxCount = level.boxes != null ? level.boxes.Count : 0;
            for (int index = 0; index < boxCount; index++)
            {
                BoxRevealDefinition definition = level.boxes[index];
                if (ValidateDefinition(definition, RevealAreaKind.Box, index))
                    boxes.Add(new BoxRuntime { Definition = definition });
            }

            int elevatorCount = level.elevators != null ? level.elevators.Count : 0;
            for (int index = 0; index < elevatorCount; index++)
            {
                ElevatorRevealDefinition definition = level.elevators[index];
                if (ValidateDefinition(definition, RevealAreaKind.Elevator, index))
                    elevators.Add(new ElevatorRuntime { Definition = definition });
            }

            for (int index = 0; index < boxes.Count; index++)
            {
                BoxRuntime box = boxes[index];
                if (presentations.TryGetValue(box.Definition.areaId, out IRevealAreaPresentation presentation))
                    presentation.SetBoxRemaining(box.Definition.targetShredCount);
            }

            // A zero-target box is deliberately opened from the initialized
            // board state, after all regular pieces have been registered.
            for (int index = 0; index < boxes.Count; index++)
            {
                if (boxes[index].Definition.targetShredCount == 0)
                    StartBoxUnlock(boxes[index]);
            }

            EvaluateElevators();
        }

        public void RegisterPresentation(string areaId, IRevealAreaPresentation presentation)
        {
            if (string.IsNullOrWhiteSpace(areaId) || presentation == null)
                return;

            presentations[areaId] = presentation;
            for (int index = 0; index < boxes.Count; index++)
            {
                BoxRuntime box = boxes[index];
                if (box.Definition.areaId == areaId)
                    presentation.SetBoxRemaining(box.Definition.targetShredCount - box.CountedShredPieceIds.Count);
            }
        }

        /// <summary>Called only for a shredder-committed piece destruction.</summary>
        public void NotifyPieceShredded(int pieceId)
        {
            for (int index = 0; index < boxes.Count; index++)
            {
                BoxRuntime box = boxes[index];
                if (box.State != RevealAreaState.Locked || !box.CountedShredPieceIds.Add(pieceId))
                    continue;

                if (presentations.TryGetValue(box.Definition.areaId, out IRevealAreaPresentation presentation))
                    presentation.SetBoxRemaining(box.Definition.targetShredCount - box.CountedShredPieceIds.Count);

                if (box.CountedShredPieceIds.Count >= box.Definition.targetShredCount)
                    StartBoxUnlock(box);
            }
        }

        /// <summary>
        /// Applies closed-box and permanently-opened-elevator re-entry rules
        /// before the board commits a candidate grid movement.
        /// </summary>
        public bool CanMovePiece(PieceModel model, GridCoordinate targetAnchor)
        {
            if (model == null)
                return false;

            GridBounds current = GridBounds.From(model, model.Anchor);
            GridBounds target = GridBounds.From(model, targetAnchor);
            for (int index = 0; index < boxes.Count; index++)
            {
                BoxRuntime box = boxes[index];
                if (box.State == RevealAreaState.Unlocked)
                    continue;

                GridBounds boxBounds = GridBounds.From(box.Definition.bounds);
                // Legacy-authored levels can already contain a normal piece in
                // a Box footprint. Treat that piece as covered until the Box
                // opens as well; otherwise it could be dragged out before its
                // shred requirement is met.
                if (current.Intersects(boxBounds) || target.Intersects(boxBounds))
                    return false;
            }

            for (int index = 0; index < elevators.Count; index++)
            {
                ElevatorRuntime elevator = elevators[index];
                if (elevator.State == RevealAreaState.Locked)
                    continue;

                if (target.Intersects(GridBounds.From(elevator.Definition.bounds)) &&
                    !current.Intersects(GridBounds.From(elevator.Definition.bounds)))
                    return false;
            }

            return true;
        }

        /// <summary>Returns false while a live piece is still concealed by a closed Box.</summary>
        public bool CanBeginInteraction(PieceModel model)
        {
            if (model == null)
                return false;

            GridBounds current = GridBounds.From(model, model.Anchor);
            for (int index = 0; index < boxes.Count; index++)
            {
                BoxRuntime box = boxes[index];
                if (box.State != RevealAreaState.Unlocked &&
                    current.Intersects(GridBounds.From(box.Definition.bounds)))
                    return false;
            }

            return true;
        }

        /// <summary>Called after every authoritative occupancy or lifecycle commit.</summary>
        public void NotifyBoardChanged()
        {
            EvaluateElevators();
        }

        private void StartBoxUnlock(BoxRuntime box)
        {
            if (box.State != RevealAreaState.Locked)
                return;

            box.State = RevealAreaState.Unlocking;
            BeginOpening(box.Definition, RevealAreaKind.Box, () => CompleteBoxUnlock(box));
        }

        private void CompleteBoxUnlock(BoxRuntime box)
        {
            if (box.State != RevealAreaState.Unlocking || !ActivateContent(box.Definition.hiddenPieces ?? EmptyPieces))
                return;

            box.State = RevealAreaState.Unlocked;
            AreaUnlocked?.Invoke(new RevealAreaEvent(box.Definition.areaId, RevealAreaKind.Box));
            EvaluateElevators();
        }

        private void EvaluateElevators()
        {
            if (board == null || board.BoardSnapshot == null)
                return;

            for (int index = 0; index < elevators.Count; index++)
            {
                ElevatorRuntime elevator = elevators[index];
                if (elevator.State != RevealAreaState.Locked ||
                    RefreshElevatorOccupancy(elevator))
                    continue;

                elevator.State = RevealAreaState.Unlocking;
                BeginOpening(
                    elevator.Definition,
                    RevealAreaKind.Elevator,
                    () => CompleteElevatorUnlock(elevator));
            }
        }

        private void CompleteElevatorUnlock(ElevatorRuntime elevator)
        {
            if (elevator.State != RevealAreaState.Unlocking ||
                !ActivateContent(elevator.Definition.hiddenPieces ?? EmptyPieces))
                return;

            elevator.State = RevealAreaState.Unlocked;
            AreaUnlocked?.Invoke(new RevealAreaEvent(elevator.Definition.areaId, RevealAreaKind.Elevator));
        }

        private void BeginOpening(
            RevealAreaDefinition definition,
            RevealAreaKind kind,
            Action completed)
        {
            AreaUnlockStarted?.Invoke(new RevealAreaEvent(definition.areaId, kind));
            if (presentations.TryGetValue(definition.areaId, out IRevealAreaPresentation presentation))
            {
                presentation.PlayOpening(kind, completed);
                return;
            }

            completed?.Invoke();
        }

        private bool ActivateContent(IReadOnlyList<PieceDefinition> pieces)
        {
            return board != null && board.TryActivateRevealPieces(pieces);
        }

        private bool RefreshElevatorOccupancy(ElevatorRuntime elevator)
        {
            GridBounds bounds = GridBounds.From(elevator.Definition.bounds);
            elevator.NextOccupantIds.Clear();
            IReadOnlyList<PieceModel> models = board.BoardSnapshot.Pieces;
            for (int index = 0; index < models.Count; index++)
            {
                PieceModel model = models[index];
                if (model != null && model.State != PieceState.Despawned &&
                    GridBounds.From(model, model.Anchor).Intersects(bounds))
                    elevator.NextOccupantIds.Add(model.Id);
            }

            PublishElevatorOccupancyChanges(elevator);
            if (elevator.NextOccupantIds.Count > 0)
                return true;

            // A closed box's pieces have no runtime roots yet. Their authored
            // footprint is nevertheless an elevator occupant, so an elevator
            // behind that box cannot reveal prematurely.
            for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
            {
                BoxRuntime box = boxes[boxIndex];
                if (box.State == RevealAreaState.Unlocked)
                    continue;

                if (box.Definition.hiddenPieces == null)
                    continue;

                for (int pieceIndex = 0; pieceIndex < box.Definition.hiddenPieces.Count; pieceIndex++)
                {
                    PieceDefinition piece = box.Definition.hiddenPieces[pieceIndex];
                    if (piece != null && GridBounds.From(piece).Intersects(bounds))
                        return true;
                }
            }

            return false;
        }

        private void PublishElevatorOccupancyChanges(ElevatorRuntime elevator)
        {
            elevator.EnteredIds.Clear();
            elevator.ExitedIds.Clear();
            foreach (int pieceId in elevator.NextOccupantIds)
            {
                if (!elevator.CurrentOccupantIds.Contains(pieceId))
                    elevator.EnteredIds.Add(pieceId);
            }

            foreach (int pieceId in elevator.CurrentOccupantIds)
            {
                if (!elevator.NextOccupantIds.Contains(pieceId))
                    elevator.ExitedIds.Add(pieceId);
            }

            for (int index = 0; index < elevator.EnteredIds.Count; index++)
                PieceEnteredArea?.Invoke(elevator.Definition.areaId, elevator.EnteredIds[index]);
            for (int index = 0; index < elevator.ExitedIds.Count; index++)
                PieceExitedArea?.Invoke(elevator.Definition.areaId, elevator.ExitedIds[index]);

            elevator.CurrentOccupantIds.Clear();
            foreach (int pieceId in elevator.NextOccupantIds)
                elevator.CurrentOccupantIds.Add(pieceId);
        }

        private static bool ValidateDefinition(
            RevealAreaDefinition definition,
            RevealAreaKind kind,
            int index)
        {
            if (definition == null || definition.bounds.size.x <= 0 || definition.bounds.size.y <= 0)
            {
                Debug.LogWarning($"[Reveal] Ignoring invalid {kind} definition at index {index}.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.areaId) || definition.areaId == Guid.Empty.ToString())
            {
                definition.areaId = Guid.NewGuid().ToString();
                Debug.LogWarning($"[Reveal] Generated missing Area Id for {kind} '{definition.name}'. Save the level asset.");
            }

            return true;
        }

        private readonly struct GridBounds
        {
            private GridBounds(int minX, int minY, int maxXExclusive, int maxYExclusive)
            {
                MinX = minX;
                MinY = minY;
                MaxXExclusive = maxXExclusive;
                MaxYExclusive = maxYExclusive;
            }

            private int MinX { get; }
            private int MinY { get; }
            private int MaxXExclusive { get; }
            private int MaxYExclusive { get; }

            public bool Intersects(GridBounds other)
            {
                return MinX < other.MaxXExclusive && MaxXExclusive > other.MinX &&
                       MinY < other.MaxYExclusive && MaxYExclusive > other.MinY;
            }

            public static GridBounds From(RevealAreaBounds bounds)
            {
                return new GridBounds(
                    bounds.XMin,
                    bounds.YMin,
                    bounds.XMaxExclusive,
                    bounds.YMaxExclusive);
            }

            public static GridBounds From(PieceModel model, GridCoordinate anchor)
            {
                int minX = int.MaxValue;
                int minY = int.MaxValue;
                int maxX = int.MinValue;
                int maxY = int.MinValue;
                for (int index = 0; index < model.LocalCells.Count; index++)
                {
                    GridCoordinate cell = anchor.Offset(model.LocalCells[index]);
                    minX = Math.Min(minX, cell.X);
                    minY = Math.Min(minY, cell.Y);
                    maxX = Math.Max(maxX, cell.X + 1);
                    maxY = Math.Max(maxY, cell.Y + 1);
                }

                return new GridBounds(minX, minY, maxX, maxY);
            }

            public static GridBounds From(PieceDefinition definition)
            {
                int minX = int.MaxValue;
                int minY = int.MaxValue;
                int maxX = int.MinValue;
                int maxY = int.MinValue;
                for (int index = 0; index < definition.cells.Count; index++)
                {
                    PieceCellDefinition cell = definition.cells[index];
                    if (cell.type != PieceCellType.Block)
                        continue;

                    Vector2Int rotated = QuarterTurnUtility.Rotate(cell.localCell, definition.quarterTurns);
                    Vector2Int absolute = definition.origin + rotated;
                    minX = Math.Min(minX, absolute.x);
                    minY = Math.Min(minY, absolute.y);
                    maxX = Math.Max(maxX, absolute.x + 1);
                    maxY = Math.Max(maxY, absolute.y + 1);
                }

                return new GridBounds(minX, minY, maxX, maxY);
            }
        }
    }
}
