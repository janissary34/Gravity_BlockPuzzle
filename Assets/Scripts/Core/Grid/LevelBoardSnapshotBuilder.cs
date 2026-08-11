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
            MarkInactiveCells(level, grid);
            MarkObstacles(level, grid);

            List<PieceModel> pieces = new List<PieceModel>(level.pieces.Count);
            List<LevelBoardSnapshotIssue> issues = new List<LevelBoardSnapshotIssue>();
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
            List<GridCoordinate> localCells = new List<GridCoordinate>(definition.cells.Count);
            for (int cellIndex = 0; cellIndex < definition.cells.Count; cellIndex++)
            {
                Vector2Int rotated = QuarterTurnUtility.Rotate(
                    definition.cells[cellIndex].localCell,
                    definition.quarterTurns);
                localCells.Add(new GridCoordinate(rotated.x, rotated.y));
            }

            return new PieceModel(
                pieceId,
                new GridCoordinate(definition.origin.x, definition.origin.y),
                localCells);
        }

        private static void MarkInactiveCells(GravityLevelDefinition level, GravityBoardGrid grid)
        {
            for (int index = 0; index < level.inactiveFineCells.Count; index++)
                grid.SetBlocked(ToCoordinate(level.inactiveFineCells[index]));

            for (int index = 0; index < level.inactiveBoardCells.Count; index++)
            {
                Vector2Int coarseCell = level.inactiveBoardCells[index];
                Vector2Int fineOrigin = coarseCell * level.subdivisions;
                for (int y = 0; y < level.subdivisions; y++)
                for (int x = 0; x < level.subdivisions; x++)
                    grid.SetBlocked(new GridCoordinate(fineOrigin.x + x, fineOrigin.y + y));
            }
        }

        private static void MarkObstacles(GravityLevelDefinition level, GravityBoardGrid grid)
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
                        grid,
                        obstacle.gridCell * level.subdivisions,
                        size * level.subdivisions);
                    continue;
                }

                Vector2Int legacySize = obstacle.quarterTurns % 2 == 0
                    ? obstacle.sizeInFineCells
                    : new Vector2Int(obstacle.sizeInFineCells.y, obstacle.sizeInFineCells.x);
                Vector2Int bottomLeft = obstacle.centreCell - new Vector2Int(
                    legacySize.x / 2,
                    legacySize.y / 2);
                MarkRectangle(grid, bottomLeft, legacySize);
            }
        }

        private static void MarkRectangle(GravityBoardGrid grid, Vector2Int bottomLeft, Vector2Int size)
        {
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
                grid.SetBlocked(new GridCoordinate(bottomLeft.x + x, bottomLeft.y + y));
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

        private static string Format(GridCoordinate coordinate)
        {
            return $"({coordinate.X}, {coordinate.Y})";
        }
    }
}
