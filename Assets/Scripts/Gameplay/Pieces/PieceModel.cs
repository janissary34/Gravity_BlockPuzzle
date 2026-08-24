using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Core.StateMachine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public sealed class PieceModel
    {
        private readonly List<GridCoordinate> localCells;
        private readonly StateMachine<PieceState> stateMachine;

        public int Id { get; }
        public GridCoordinate Anchor { get; private set; }
        public GridCoordinate PivotOffset { get; private set; }
        public bool IsOnBoard { get; private set; } = true;
        public PieceState State => stateMachine.Current;
        public IReadOnlyList<GridCoordinate> LocalCells => localCells;

        public PieceModel(
            int id,
            GridCoordinate anchor,
            GridCoordinate pivotOffset,
            List<GridCoordinate> localCellCoordinates)
        {
            Id = id;
            Anchor = anchor;
            PivotOffset = pivotOffset;
            localCells = localCellCoordinates;
            stateMachine = new StateMachine<PieceState>(
                PieceState.Placed,
                PieceStateTransitionRules.Create());
        }

        public GridCoordinate GetWorldCell(int localCellIndex)
        {
            return Anchor.Offset(localCells[localCellIndex]);
        }

        public void SetAnchor(GridCoordinate anchor)
        {
            Anchor = anchor;
            IsOnBoard = true;
        }

        /// <summary>
        /// Updates the shape owned by this stable piece id.  Hammer's interim
        /// behaviour removes cells without creating independent piece roots,
        /// so the grid identity must remain unchanged while its footprint is
        /// refreshed.
        /// </summary>
        public void ReplaceGeometry(
            GridCoordinate anchor,
            GridCoordinate pivotOffset,
            List<GridCoordinate> localCellCoordinates)
        {
            Anchor = anchor;
            PivotOffset = pivotOffset;
            localCells.Clear();
            localCells.AddRange(localCellCoordinates);
            IsOnBoard = true;
        }

        public void MarkOffBoard()
        {
            IsOnBoard = false;
        }

        public void MarkOnBoard()
        {
            IsOnBoard = true;
        }

        /// <summary>
        /// Applies a validated gameplay lifecycle transition. Repeating the
        /// current state is a successful no-op, which lets independent
        /// presentation callbacks converge on the same logical state safely.
        /// </summary>
        public bool TrySetState(PieceState state)
        {
            if (State == state)
                return true;

            return stateMachine.TryTransition(state);
        }
    }

    /// <summary>
    /// Defines the only legal logical lifecycle changes for a board piece.
    /// Presentation may animate the transition, but it must never bypass this
    /// rule set when changing the piece's gameplay state.
    /// </summary>
    public static class PieceStateTransitionRules
    {
        public static IEnumerable<StateTransition<PieceState>> Create()
        {
            yield return new StateTransition<PieceState>(PieceState.Spawned, PieceState.Placed);
            yield return new StateTransition<PieceState>(PieceState.Spawned, PieceState.Despawned);

            yield return new StateTransition<PieceState>(PieceState.Placed, PieceState.Dragging);
            yield return new StateTransition<PieceState>(PieceState.Placed, PieceState.Falling);
            yield return new StateTransition<PieceState>(PieceState.Placed, PieceState.HandoffToPhysics);
            yield return new StateTransition<PieceState>(PieceState.Placed, PieceState.Shredding);
            yield return new StateTransition<PieceState>(PieceState.Placed, PieceState.Despawned);

            yield return new StateTransition<PieceState>(PieceState.Dragging, PieceState.Placed);
            yield return new StateTransition<PieceState>(PieceState.Dragging, PieceState.Falling);
            yield return new StateTransition<PieceState>(PieceState.Dragging, PieceState.HandoffToPhysics);
            yield return new StateTransition<PieceState>(PieceState.Dragging, PieceState.Despawned);

            yield return new StateTransition<PieceState>(PieceState.Falling, PieceState.Placed);
            yield return new StateTransition<PieceState>(PieceState.Falling, PieceState.HandoffToPhysics);
            yield return new StateTransition<PieceState>(PieceState.Falling, PieceState.Shredding);
            yield return new StateTransition<PieceState>(PieceState.Falling, PieceState.Despawned);

            yield return new StateTransition<PieceState>(PieceState.HandoffToPhysics, PieceState.Shredding);
            yield return new StateTransition<PieceState>(PieceState.HandoffToPhysics, PieceState.Despawned);

            yield return new StateTransition<PieceState>(PieceState.Shredding, PieceState.Despawned);
        }
    }
}
