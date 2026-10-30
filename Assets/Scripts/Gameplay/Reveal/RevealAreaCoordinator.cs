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
        void SetBoxRemaining(int remaining, int target);
        void PlayOpening(RevealAreaKind kind, Action onCompleted);
    }

    /// <summary>
    /// Optional presentation extension for Boxes that can display the content
    /// which will be activated when their existing opening contract completes.
    /// It carries authored data only and never changes reveal authority.
    /// </summary>
    public interface IBoxRevealPreviewPresentation
    {
        void ConfigureBoxPreview(GravityLevelDefinition level, BoxRevealDefinition definition);
    }

    /// <summary>
    /// Explicit one-way shutter boundary. The view reports visual aperture
    /// progress only; the coordinator remains the gameplay authority.
    /// </summary>
    public interface IBoxRevealExposurePresentation : IBoxRevealPreviewPresentation
    {
        void SetBoxExposureChangedHandler(Action<float> handler);
        bool CanPresentContent(int contentIndex);
        void SetContentPreviewVisible(int contentIndex, bool visible);
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
            // A Box requirement is met once per fully removed runtime piece,
            // whether its removal was committed by the shredder or by a
            // hammer. The set keeps split/removal paths from double counting
            // the same authoritative piece id.
            public readonly HashSet<int> CountedRemovedPieceIds = new HashSet<int>();
            public readonly List<BoxContentRuntime> Contents = new List<BoxContentRuntime>();
            public float Exposure;
        }

        private sealed class BoxContentRuntime
        {
            public int AuthoredIndex;
            public PieceDefinition Definition;
            public bool IsActivated;
            public bool HasLeftBox;
            public int RuntimePieceId = -1;
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
        private readonly Dictionary<int, BoxContentRuntime> activatedBoxContentByPieceId =
            new Dictionary<int, BoxContentRuntime>();
        private readonly Dictionary<int, BoxRuntime> activatedBoxByPieceId =
            new Dictionary<int, BoxRuntime>();
        private readonly List<PieceDefinition> remainingBoxContent = new List<PieceDefinition>();
        private PrototypeBoard board;
        private GravityLevelDefinition level;

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
            this.level = level;
            board = owner ?? throw new ArgumentNullException(nameof(owner));
            boxes.Clear();
            elevators.Clear();
            activatedBoxContentByPieceId.Clear();
            activatedBoxByPieceId.Clear();

            if (level == null)
                return;

            int boxCount = level.boxes != null ? level.boxes.Count : 0;
            for (int index = 0; index < boxCount; index++)
            {
                BoxRevealDefinition definition = level.boxes[index];
                if (ValidateDefinition(definition, RevealAreaKind.Box, index))
                {
                    BoxRuntime box = new BoxRuntime { Definition = definition };
                    CreateBoxContentRecords(box);
                    boxes.Add(box);
                }
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
                {
                    ConfigureBoxPresentation(box, presentation);
                }
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
                {
                    ConfigureBoxPresentation(box, presentation);
                }
            }
        }

        /// <summary>Called only for a shredder-committed piece destruction.</summary>
        public void NotifyPieceShredded(int pieceId)
        {
            NotifyPieceRemoved(pieceId);
        }

        /// <summary>
        /// Called after a hammer has fully removed an authoritative runtime
        /// piece. Partial hammer edits keep the piece active and do not count.
        /// </summary>
        public void NotifyPieceHammerDestroyed(int pieceId)
        {
            NotifyPieceRemoved(pieceId);
        }

        private void NotifyPieceRemoved(int pieceId)
        {
            for (int index = 0; index < boxes.Count; index++)
            {
                BoxRuntime box = boxes[index];
                if ((box.State != RevealAreaState.Locked && box.State != RevealAreaState.PartiallyRevealed) ||
                    !box.CountedRemovedPieceIds.Add(pieceId))
                    continue;

                if (presentations.TryGetValue(box.Definition.areaId, out IRevealAreaPresentation presentation))
                    presentation.SetBoxRemaining(box.Definition.targetShredCount - box.CountedRemovedPieceIds.Count, box.Definition.targetShredCount);

                if (box.CountedRemovedPieceIds.Count >= box.Definition.targetShredCount)
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

            if (boxes.Count == 0 && elevators.Count == 0)
                return true;

            GridBounds current = GridBounds.From(model, model.Anchor);
            GridBounds target = GridBounds.From(model, targetAnchor);
            bool isBoxContent = TryGetActivatedBoxContent(model.Id, out BoxRuntime ownerBox, out BoxContentRuntime ownerContent);
            for (int index = 0; index < boxes.Count; index++)
            {
                BoxRuntime box = boxes[index];
                if (box.State == RevealAreaState.Unlocked)
                    continue;

                GridBounds boxBounds = GridBounds.From(box.Definition.bounds);
                if (isBoxContent && ReferenceEquals(box, ownerBox))
                {
                    if (ownerContent.HasLeftBox)
                    {
                        if (target.Intersects(boxBounds))
                            return false;
                        continue;
                    }

                    if (!CanContentTraversePartialBox(model, targetAnchor, box))
                        return false;
                    continue;
                }

                // Legacy-authored levels can already contain a normal piece in
                // a Box footprint. Treat that piece as covered until the Box
                // opens as well; otherwise it could move out before its shred
                // requirement is met.
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

        /// <summary>
        /// Tunnel content cannot be selected until it has left its partially
        /// opened Tunnel. Exposure releases it to gravity, never to dragging.
        /// </summary>
        public bool CanBeginInteraction(PieceModel model)
        {
            if (model == null)
                return false;

            GridBounds current = GridBounds.From(model, model.Anchor);
            if (TryGetActivatedBoxContent(model.Id, out BoxRuntime ownerBox, out BoxContentRuntime ownerContent))
            {
                if (ownerBox.State == RevealAreaState.Unlocked || ownerContent.HasLeftBox)
                    return true;

                return false;
            }

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
            RefreshBoxContentPresentation();
            EvaluateElevators();
        }

        /// <summary>
        /// A fully exposed Tunnel root is released to the normal grid-gravity
        /// planner. The planner still owns whether a legal downward path exists.
        /// </summary>
        public bool CanParticipateInGravity(PieceModel model)
        {
            if (model == null || !TryGetActivatedBoxContent(model.Id, out BoxRuntime box, out BoxContentRuntime content))
                return true;

            return box.State == RevealAreaState.Unlocked || content.HasLeftBox ||
                   (content.IsActivated &&
                    IsModelFullyWithinExposedBoxRows(model, model.Anchor, box));
        }

        /// <summary>Inside Tunnel roots cannot receive targeted boosters.</summary>
        public bool CanUseTargetedBooster(PieceModel model)
        {
            if (model == null ||
                !TryGetActivatedBoxContent(model.Id, out BoxRuntime box, out BoxContentRuntime content))
            {
                return true;
            }

            return box.State == RevealAreaState.Unlocked || content.HasLeftBox;
        }

        private void StartBoxUnlock(BoxRuntime box)
        {
            if (box.State != RevealAreaState.Locked && box.State != RevealAreaState.PartiallyRevealed)
                return;

            box.State = RevealAreaState.Unlocking;
            BeginOpening(box.Definition, RevealAreaKind.Box, () => CompleteBoxUnlock(box));
        }

        private void CompleteBoxUnlock(BoxRuntime box)
        {
            if (box.State != RevealAreaState.Unlocking)
                return;

            remainingBoxContent.Clear();
            for (int index = 0; index < box.Contents.Count; index++)
            {
                BoxContentRuntime content = box.Contents[index];
                if (!content.IsActivated && content.Definition != null)
                    remainingBoxContent.Add(content.Definition);
            }

            if (remainingBoxContent.Count > 0 && !ActivateContent(remainingBoxContent))
                return;

            for (int index = 0; index < box.Contents.Count; index++)
                box.Contents[index].IsActivated = true;

            box.State = RevealAreaState.Unlocked;
            ReleaseBoxContentPresentation(box);
            AreaUnlocked?.Invoke(new RevealAreaEvent(box.Definition.areaId, RevealAreaKind.Box));
            EvaluateElevators();
            board?.WakeUpGravity();
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
            // Fixed interior content remains absent from the board until every
            // top-layer piece has left and the opening presentation completes.
            if (elevator.State != RevealAreaState.Unlocking ||
                !ActivateContent(elevator.Definition.hiddenPieces ?? EmptyPieces))
                return;

            elevator.State = RevealAreaState.Unlocked;
            AreaUnlocked?.Invoke(new RevealAreaEvent(elevator.Definition.areaId, RevealAreaKind.Elevator));
            board?.WakeUpGravity();
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

        private void ConfigureBoxPresentation(BoxRuntime box, IRevealAreaPresentation presentation)
        {
            if (presentation is IBoxRevealPreviewPresentation previewPresentation && level != null)
                previewPresentation.ConfigureBoxPreview(level, box.Definition);

            if (presentation is IBoxRevealExposurePresentation exposurePresentation)
                exposurePresentation.SetBoxExposureChangedHandler(opening => HandleBoxExposure(box, opening));

            presentation.SetBoxRemaining(
                box.Definition.targetShredCount - box.CountedRemovedPieceIds.Count,
                box.Definition.targetShredCount);
        }

        private void CreateBoxContentRecords(BoxRuntime box)
        {
            if (box.Definition.hiddenPieces == null)
                return;

            for (int index = 0; index < box.Definition.hiddenPieces.Count; index++)
            {
                box.Contents.Add(new BoxContentRuntime
                {
                    AuthoredIndex = index,
                    Definition = box.Definition.hiddenPieces[index]
                });
            }
        }

        private void HandleBoxExposure(BoxRuntime box, float opening)
        {
            if (box == null || box.State == RevealAreaState.Unlocked)
                return;

            box.Exposure = Mathf.Clamp01(opening);
            if (box.State == RevealAreaState.Locked && box.Exposure > 0f)
                box.State = RevealAreaState.PartiallyRevealed;

            if (!presentations.TryGetValue(box.Definition.areaId, out IRevealAreaPresentation presentation) ||
                !(presentation is IBoxRevealExposurePresentation exposurePresentation))
            {
                return;
            }

            bool activatedAnyContent = false;
            for (int index = 0; index < box.Contents.Count; index++)
            {
                BoxContentRuntime content = box.Contents[index];
                if (content.IsActivated || content.Definition == null ||
                    !exposurePresentation.CanPresentContent(content.AuthoredIndex) ||
                    !IsDefinitionFullyExposed(content.Definition, box, box.Exposure))
                {
                    continue;
                }

                if (!board.TryActivateRevealPiece(content.Definition, out PuzzlePiece piece))
                    continue;

                content.IsActivated = true;
                content.RuntimePieceId = piece.SourcePieceId;
                activatedBoxContentByPieceId[content.RuntimePieceId] = content;
                activatedBoxByPieceId[content.RuntimePieceId] = box;
                RestoreInsideBoxPresentation(box, content, piece);
                activatedAnyContent = true;
            }

            // Activation commits an authoritative root, but its initial grid
            // position may already have a clear exit. Wake the owned gravity
            // path once after the batch so it can plan that fall immediately.
            if (activatedAnyContent)
                board?.WakeUpGravity();
        }

        private void RefreshBoxContentPresentation()
        {
            for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
            {
                BoxRuntime box = boxes[boxIndex];
                if (box.State == RevealAreaState.Unlocked)
                    continue;

                for (int contentIndex = 0; contentIndex < box.Contents.Count; contentIndex++)
                {
                    BoxContentRuntime content = box.Contents[contentIndex];
                    if (!content.IsActivated || content.RuntimePieceId < 0 || board == null ||
                        !board.BoardSnapshot.TryGetPiece(content.RuntimePieceId, out PieceModel model))
                    {
                        continue;
                    }

                    PuzzlePiece piece = board.FindActivePiece(content.RuntimePieceId);
                    if (piece == null || model.State == PieceState.Despawned)
                        continue;

                    if (!GridBounds.From(model, model.Anchor).Intersects(GridBounds.From(box.Definition.bounds)))
                    {
                        content.HasLeftBox = true;
                        ReleaseContentPresentation(box, content, piece);
                        continue;
                    }

                    if (box.State == RevealAreaState.Unlocking && model.State != PieceState.Dragging)
                    {
                        ReleaseContentPresentation(box, content, piece);
                    }
                    else if (!content.HasLeftBox && model.State != PieceState.Dragging)
                        RestoreInsideBoxPresentation(box, content, piece);
                }
            }
        }

        private void RestoreInsideBoxPresentation(
            BoxRuntime box,
            BoxContentRuntime content,
            PuzzlePiece piece)
        {
            if (piece == null)
                return;

            if (presentations.TryGetValue(box.Definition.areaId, out IRevealAreaPresentation presentation) &&
                presentation is IBoxRevealExposurePresentation exposurePresentation)
            {
                exposurePresentation.SetContentPreviewVisible(content.AuthoredIndex, true);
                piece.HideBoxRevealContentVisual();
            }
        }

        private void ReleaseContentPresentation(
            BoxRuntime box,
            BoxContentRuntime content,
            PuzzlePiece piece)
        {
            if (presentations.TryGetValue(box.Definition.areaId, out IRevealAreaPresentation presentation) &&
                presentation is IBoxRevealExposurePresentation exposurePresentation)
            {
                exposurePresentation.SetContentPreviewVisible(content.AuthoredIndex, false);
            }

            piece?.RestoreBoxRevealContentVisual();
        }

        private void ReleaseBoxContentPresentation(BoxRuntime box)
        {
            for (int index = 0; index < box.Contents.Count; index++)
            {
                BoxContentRuntime content = box.Contents[index];
                if (content.RuntimePieceId >= 0 && board != null)
                {
                    // The Box view returns to its pool before the opening
                    // completion callback runs. ClearPreviewVisuals already
                    // owns its slots, so only release the live root here.
                    // Touching the pooled view could otherwise alter a new
                    // Box that rented the same visual in the same frame.
                    board.FindActivePiece(content.RuntimePieceId)?.RestoreBoxRevealContentVisual();
                }
            }
        }

        private bool TryGetActivatedBoxContent(
            int pieceId,
            out BoxRuntime box,
            out BoxContentRuntime content)
        {
            // Short-circuiting the two lookups can leave the second out
            // parameter untouched when the Box mapping is absent. Initialize
            // both so callers always receive a valid failure result.
            box = null;
            content = null;
            return activatedBoxByPieceId.TryGetValue(pieceId, out box) &&
                   activatedBoxContentByPieceId.TryGetValue(pieceId, out content);
        }

        private static bool IsDefinitionFullyExposed(
            PieceDefinition definition,
            BoxRuntime box,
            float exposure)
        {
            if (definition == null || definition.cells == null)
                return false;

            int height = Mathf.Max(1, box.Definition.bounds.size.y);
            bool hasBlock = false;
            for (int index = 0; index < definition.cells.Count; index++)
            {
                PieceCellDefinition cell = definition.cells[index];
                if (cell.type != PieceCellType.Block)
                    continue;

                hasBlock = true;
                Vector2Int absolute = definition.origin + QuarterTurnUtility.Rotate(
                    cell.localCell,
                    definition.quarterTurns);
                float requiredExposure = (absolute.y + 1 - box.Definition.bounds.YMin) / (float)height;
                if (exposure + .0001f < requiredExposure)
                    return false;
            }

            return hasBlock;
        }

        private static bool CanContentTraversePartialBox(
            PieceModel model,
            GridCoordinate targetAnchor,
            BoxRuntime box)
        {
            return IsModelFullyWithinExposedBoxRows(model, targetAnchor, box);
        }

        private static bool IsModelFullyWithinExposedBoxRows(
            PieceModel model,
            GridCoordinate anchor,
            BoxRuntime box)
        {
            RevealAreaBounds bounds = box.Definition.bounds;
            int exposedYExclusive = bounds.YMin + Mathf.FloorToInt(
                Mathf.Clamp01(box.Exposure) * Mathf.Max(1, bounds.size.y) + .0001f);
            for (int index = 0; index < model.LocalCells.Count; index++)
            {
                GridCoordinate cell = anchor.Offset(model.LocalCells[index]);
                bool insideBox = cell.X >= bounds.XMin && cell.X < bounds.XMaxExclusive &&
                                 cell.Y >= bounds.YMin && cell.Y < bounds.YMaxExclusive;
                if (insideBox && cell.Y >= exposedYExclusive)
                    return false;
            }

            return true;
        }

        private bool RefreshElevatorOccupancy(ElevatorRuntime elevator)
        {
            GridBounds bounds = GridBounds.From(elevator.Definition.bounds);
            elevator.NextOccupantIds.Clear();
            // Only live board pieces are top-layer occupants. Fixed interior
            // pieces are deliberately not instantiated before the unlock.
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

                for (int pieceIndex = 0; pieceIndex < box.Contents.Count; pieceIndex++)
                {
                    BoxContentRuntime content = box.Contents[pieceIndex];
                    if (!content.IsActivated && content.Definition != null &&
                        GridBounds.From(content.Definition).Intersects(bounds))
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
