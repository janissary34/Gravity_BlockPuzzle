using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
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

            int authoredPieceCount = level.pieces.Count;
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
            int comparableCount = Mathf.Min(
                Mathf.Min(snapshot.Pieces.Count, runtimePieces.Count),
                level.pieces.Count);
            for (int index = 0; index < comparableCount; index++)
            {
                PuzzlePiece runtimePiece = runtimePieces[index];
                if (runtimePiece == null)
                    continue;

                GridCoordinate runtimeAnchor = WorldToFineCell(level, runtimePiece.transform.position);
                GridCoordinate snapshotAnchor = snapshot.Pieces[index].Anchor;
                if (runtimeAnchor.Equals(snapshotAnchor))
                    continue;

                mismatchCount++;
                string pieceName = level.pieces[index].name;
                Debug.LogWarning(
                    $"[LevelSnapshot] Piece anchor mismatch. Level='{level.levelName}', " +
                    $"piece='{pieceName}', id={snapshot.Pieces[index].Id}, " +
                    $"snapshotAnchor={Format(snapshotAnchor)}, runtimeAnchor={Format(runtimeAnchor)}, " +
                    $"runtimePosition={runtimePiece.transform.position}.",
                    context);
            }

            return mismatchCount;
        }

        private static GridCoordinate WorldToFineCell(GravityLevelDefinition level, Vector2 worldPosition)
        {
            int x = Mathf.FloorToInt((worldPosition.x + level.boardColumns * .5f) * level.subdivisions);
            int y = Mathf.FloorToInt((worldPosition.y + level.boardRows * .5f) * level.subdivisions);
            return new GridCoordinate(x, y);
        }

        private static int CountAuthoredProgressUnits(GravityLevelDefinition level)
        {
            int total = 0;
            HashSet<Vector2Int> occupiedBoardCells = new HashSet<Vector2Int>();

            for (int pieceIndex = 0; pieceIndex < level.pieces.Count; pieceIndex++)
            {
                PieceDefinition piece = level.pieces[pieceIndex];
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

        private static string Format(GridCoordinate coordinate)
        {
            return $"({coordinate.X}, {coordinate.Y})";
        }
    }
}
