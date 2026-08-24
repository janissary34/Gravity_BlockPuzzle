using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Gravity
{
    public static class LevelBoardSnapshotRuntimeValidator
    {
        public static void Validate(
            GravityLevelDefinition level,
            LevelBoardSnapshot snapshot,
            IReadOnlyList<PuzzlePiece> runtimePieces,
            Object context)
        {
            if (level == null || snapshot == null || runtimePieces == null)
                return;

            int authoredPieceCount = CountAuthoredBreakablePieces(level);
            int authoredProgressUnits = CountAuthoredProgressUnits(level);
            int snapshotPieceCount = snapshot.Pieces.Count;
            int snapshotIssueCount = snapshot.Issues.Count;
            CountRuntimePieces(runtimePieces, out int runtimePieceCount, out int runtimeProgressUnits);

            if (snapshotPieceCount != authoredPieceCount)
            {
                Debug.LogWarning(
                    $"[LevelSnapshot] Piece count mismatch. Level='{level.levelName}', " +
                    $"authored={authoredPieceCount}, snapshot={snapshotPieceCount}.",
                    context);
            }

            if (runtimePieceCount != authoredPieceCount)
            {
                Debug.LogWarning(
                    $"[LevelSnapshot] Runtime piece count mismatch. Level='{level.levelName}', " +
                    $"authored={authoredPieceCount}, runtime={runtimePieceCount}.",
                    context);
            }

            if (runtimeProgressUnits != authoredProgressUnits)
            {
                Debug.LogWarning(
                    $"[LevelSnapshot] Runtime progress unit mismatch. Level='{level.levelName}', " +
                    $"authoredUnits={authoredProgressUnits}, runtimeUnits={runtimeProgressUnits}.",
                    context);
            }

            int anchorMismatchCount = ValidatePieceAnchors(level, snapshot, runtimePieces, context);

            if (snapshotIssueCount > 0)
            {
                Debug.LogWarning(
                    $"[LevelSnapshot] Built with {snapshotIssueCount} placement issue(s). " +
                    $"Level='{level.levelName}'. Check earlier Level snapshot warnings for details.",
                    context);
                return;
            }

            Debug.Log(
                $"[LevelSnapshot] Validated level='{level.levelName}', " +
                $"authoredPieces={authoredPieceCount}, runtimePieces={runtimePieceCount}, " +
                $"authoredProgressUnits={authoredProgressUnits}, runtimeProgressUnits={runtimeProgressUnits}, " +
                $"anchorMismatches={anchorMismatchCount}, issues=0.",
                context);
        }

        private static void CountRuntimePieces(
            IReadOnlyList<PuzzlePiece> runtimePieces,
            out int runtimePieceCount,
            out int runtimeProgressUnits)
        {
            runtimePieceCount = 0;
            runtimeProgressUnits = 0;
            for (int index = 0; index < runtimePieces.Count; index++)
            {
                PuzzlePiece piece = runtimePieces[index];
                if (piece == null)
                    continue;

                runtimePieceCount++;
                runtimeProgressUnits += piece.ProgressUnits;
            }
        }

        private static int ValidatePieceAnchors(
            GravityLevelDefinition level,
            LevelBoardSnapshot snapshot,
            IReadOnlyList<PuzzlePiece> runtimePieces,
            Object context)
        {
            int mismatchCount = 0;
            Dictionary<int, PuzzlePiece> runtimePiecesBySourceId =
                new Dictionary<int, PuzzlePiece>(runtimePieces.Count);
            for (int index = 0; index < runtimePieces.Count; index++)
            {
                PuzzlePiece runtimePiece = runtimePieces[index];
                if (runtimePiece == null || runtimePiece.SourcePieceId < 0)
                    continue;

                runtimePiecesBySourceId[runtimePiece.SourcePieceId] = runtimePiece;
            }

            for (int index = 0; index < snapshot.Pieces.Count; index++)
            {
                PieceModel model = snapshot.Pieces[index];
                if (!runtimePiecesBySourceId.TryGetValue(model.Id, out PuzzlePiece runtimePiece))
                {
                    mismatchCount++;
                    Debug.LogWarning(
                        $"[LevelSnapshot] Runtime piece is missing for snapshot id={model.Id}. " +
                        $"Level='{level.levelName}'.",
                        context);
                    continue;
                }

                GridCoordinate runtimePivot = GravityLevelGridCoordinates.WorldToFineCell(
                    level,
                    runtimePiece.transform.position);
                GridCoordinate runtimeAnchor = runtimePivot.Offset(model.PivotOffset);
                GridCoordinate snapshotAnchor = model.Anchor;
                if (runtimeAnchor.Equals(snapshotAnchor))
                    continue;

                mismatchCount++;
                string pieceName = model.Id >= 0 && model.Id < level.pieces.Count
                    ? level.pieces[model.Id].name
                    : runtimePiece.name;
                Debug.LogWarning(
                    $"[LevelSnapshot] Piece anchor mismatch. Level='{level.levelName}', " +
                    $"piece='{pieceName}', id={model.Id}, " +
                    $"snapshotAnchor={Format(snapshotAnchor)}, runtimeAnchor={Format(runtimeAnchor)}, " +
                    $"runtimePosition={runtimePiece.transform.position}.",
                    context);
            }

            return mismatchCount;
        }

        private static int CountAuthoredProgressUnits(GravityLevelDefinition level)
        {
            int total = 0;
            HashSet<Vector2Int> occupiedBoardCells = new HashSet<Vector2Int>();

            for (int pieceIndex = 0; pieceIndex < level.pieces.Count; pieceIndex++)
            {
                PieceDefinition piece = level.pieces[pieceIndex];
                if (!HasBlockCells(piece))
                    continue;

                occupiedBoardCells.Clear();

                for (int cellIndex = 0; cellIndex < piece.cells.Count; cellIndex++)
                {
                    PieceCellDefinition cell = piece.cells[cellIndex];
                    if (cell.type != PieceCellType.Block)
                        continue;

                    Vector2Int rotated = QuarterTurnUtility.Rotate(cell.localCell, piece.quarterTurns);
                    Vector2Int absolute = piece.origin + rotated;
                    Vector2Int boardCell = new Vector2Int(
                        Mathf.FloorToInt((float)absolute.x / level.subdivisions),
                        Mathf.FloorToInt((float)absolute.y / level.subdivisions));
                    occupiedBoardCells.Add(boardCell);
                }

                total += Mathf.Max(1, occupiedBoardCells.Count);
            }

            return total;
        }

        private static int CountAuthoredBreakablePieces(GravityLevelDefinition level)
        {
            int count = 0;
            for (int index = 0; index < level.pieces.Count; index++)
            {
                if (HasBlockCells(level.pieces[index]))
                    count++;
            }

            return count;
        }

        private static bool HasBlockCells(PieceDefinition piece)
        {
            if (piece == null || piece.cells == null)
                return false;

            for (int index = 0; index < piece.cells.Count; index++)
            {
                if (piece.cells[index].type == PieceCellType.Block)
                    return true;
            }

            return false;
        }

        private static string Format(GridCoordinate coordinate)
        {
            return $"({coordinate.X}, {coordinate.Y})";
        }
    }
}
