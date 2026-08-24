using System.Collections.Generic;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;

namespace GravityPuzzle.Core.Grid
{
    public static class LevelBoardSnapshotBuilder
    {
        public static LevelBoardSnapshot Build(GravityLevelDefinition level)
        {
            int fineColumns = level.FineColumns;
            int fineRows = level.FineRows;
            GravityBoardGrid grid = new GravityBoardGrid(fineColumns, fineRows);
            List<LevelBoardSnapshotIssue> issues = new List<LevelBoardSnapshotIssue>();
            MarkInactiveCells(level, grid, issues);
            MarkObstacles(level, grid, issues);

            List<PieceModel> pieces = new List<PieceModel>(level.pieces.Count);
            for (int pieceIndex = 0; pieceIndex < level.pieces.Count; pieceIndex++)
            {
                PieceDefinition definition = level.pieces[pieceIndex];
                PieceModel model = CreatePieceModel(pieceIndex, definition);
                if (!grid.TryPlace(model, out GridPlacementResult placementResult))
                {
                    issues.Add(new LevelBoardSnapshotIssue(model.Id, definition.name, placementResult));
                    Debug.LogWarning(
                        FormatPlacementWarning(level, definition, model, placementResult),
                        level);
                }
                pieces.Add(model);
            }

            return new LevelBoardSnapshot(grid, pieces, issues);
        }

        private static PieceModel CreatePieceModel(int pieceId, PieceDefinition definition)
        {
            List<Vector2Int> rotatedCells = new List<Vector2Int>(definition.cells.Count);
            Vector2Int minimum = Vector2Int.zero;
            for (int cellIndex = 0; cellIndex < definition.cells.Count; cellIndex++)
            {
                Vector2Int rotated = QuarterTurnUtility.Rotate(
                    definition.cells[cellIndex].localCell,
                    definition.quarterTurns);
                rotatedCells.Add(rotated);
                if (cellIndex == 0)
                {
                    minimum = rotated;
                    continue;
                }

                minimum = new Vector2Int(
                    System.Math.Min(minimum.x, rotated.x),
                    System.Math.Min(minimum.y, rotated.y));
            }

            List<GridCoordinate> localCells = new List<GridCoordinate>(rotatedCells.Count);
            for (int cellIndex = 0; cellIndex < rotatedCells.Count; cellIndex++)
            {
                Vector2Int normalized = rotatedCells[cellIndex] - minimum;
                localCells.Add(new GridCoordinate(normalized.x, normalized.y));
            }

            Vector2Int anchor = definition.origin + minimum;
            return new PieceModel(
                pieceId,
                new GridCoordinate(anchor.x, anchor.y),
                new GridCoordinate(minimum.x, minimum.y),
                localCells);
        }

        private static void MarkInactiveCells(
            GravityLevelDefinition level,
            GravityBoardGrid grid,
            List<LevelBoardSnapshotIssue> issues)
        {
            for (int index = 0; index < level.inactiveFineCells.Count; index++)
                MarkBlockedCell(
                    level,
                    grid,
                    issues,
                    ToCoordinate(level.inactiveFineCells[index]),
                    $"inactive fine cell #{index}");

            for (int index = 0; index < level.inactiveBoardCells.Count; index++)
            {
                Vector2Int coarseCell = level.inactiveBoardCells[index];
                Vector2Int fineOrigin = coarseCell * level.subdivisions;
                for (int y = 0; y < level.subdivisions; y++)
                for (int x = 0; x < level.subdivisions; x++)
                    MarkBlockedCell(
                        level,
                        grid,
                        issues,
                        new GridCoordinate(fineOrigin.x + x, fineOrigin.y + y),
                        $"inactive board cell #{index}");
            }
        }

        private static void MarkObstacles(
            GravityLevelDefinition level,
            GravityBoardGrid grid,
            List<LevelBoardSnapshotIssue> issues)
        {
            for (int index = 0; index < level.obstacles.Count; index++)
            {
                ObstacleDefinition obstacle = level.obstacles[index];
                if (obstacle.usesGridCells)
                {
                    Vector2Int size = obstacle.quarterTurns % 2 == 0
                        ? obstacle.sizeInGridCells
                        : new Vector2Int(obstacle.sizeInGridCells.y, obstacle.sizeInGridCells.x);
                    MarkRectangle(
                        level,
                        grid,
                        issues,
                        obstacle.gridCell * level.subdivisions,
                        size * level.subdivisions,
                        $"obstacle '{obstacle.name}'");
                    continue;
                }

                Vector2Int legacySize = obstacle.quarterTurns % 2 == 0
                    ? obstacle.sizeInFineCells
                    : new Vector2Int(obstacle.sizeInFineCells.y, obstacle.sizeInFineCells.x);
                Vector2Int bottomLeft = obstacle.centreCell - new Vector2Int(
                    legacySize.x / 2,
                    legacySize.y / 2);
                MarkRectangle(
                    level,
                    grid,
                    issues,
                    bottomLeft,
                    legacySize,
                    $"obstacle '{obstacle.name}'");
            }
        }

        private static void MarkRectangle(
            GravityLevelDefinition level,
            GravityBoardGrid grid,
            List<LevelBoardSnapshotIssue> issues,
            Vector2Int bottomLeft,
            Vector2Int size,
            string source)
        {
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
                MarkBlockedCell(
                    level,
                    grid,
                    issues,
                    new GridCoordinate(bottomLeft.x + x, bottomLeft.y + y),
                    source);
        }

        private static GridCoordinate ToCoordinate(Vector2Int coordinate)
        {
            return new GridCoordinate(coordinate.x, coordinate.y);
        }

        private static string FormatPlacementWarning(
            GravityLevelDefinition level,
            PieceDefinition definition,
            PieceModel model,
            GridPlacementResult result)
        {
            return $"Level snapshot could not place piece '{definition.name}' " +
                   $"(id: {model.Id}, level: {level.levelName}, anchor: {Format(model.Anchor)}). " +
                   $"Reason: {result.Reason}, cell: {Format(result.Coordinate)}, " +
                   $"cellState: {result.CellState}, occupantId: {result.OccupantId}.";
        }

        private static void MarkBlockedCell(
            GravityLevelDefinition level,
            GravityBoardGrid grid,
            List<LevelBoardSnapshotIssue> issues,
            GridCoordinate coordinate,
            string source)
        {
            if (grid.TrySetBlocked(coordinate, out GridPlacementResult result))
                return;

            issues.Add(new LevelBoardSnapshotIssue(
                LevelBoardSnapshotIssueKind.BlockedCellMarking,
                source,
                result));
            Debug.LogWarning(
                FormatBlockedCellWarning(level, source, result),
                level);
        }

        private static string FormatBlockedCellWarning(
            GravityLevelDefinition level,
            string source,
            GridPlacementResult result)
        {
            return $"Level snapshot could not mark blocked cell from {source} " +
                   $"(level: {level.levelName}). Reason: {result.Reason}, " +
                   $"cell: {Format(result.Coordinate)}, cellState: {result.CellState}, " +
                   $"occupantId: {result.OccupantId}.";
        }

        private static string Format(GridCoordinate coordinate)
        {
            return $"({coordinate.X}, {coordinate.Y})";
        }
    }
}
