using System.Collections.Generic;
using GravityPuzzle.Config;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class CameraFramingTests
    {
        [Test]
        public void FramedCamera_KeepsBoardAndShredderInsideSafeBandAcrossShapesAndAspects()
        {
            const float topInset = .23f;
            const float bottomInset = .21f;
            const float frameTopAllowance = .55f;
            const float horizontalMargin = 1.1f;
            const float viewportWidth = GravityGridMetrics.BoardViewportWidth;

            (int cols, int rows, float drop, float radius)[] cases =
            {
                (4, 4, 2.5f, .5f),
                (4, 5, 2.5f, .5f),
                (6, 13, 3.5f, .5f),
                (7, 11, 2.5f, .5f),
                (7, 13, 3.5f, .5f),
                (10, 11, 3.5f, .5f),
                (12, 8, 3.5f, .5f),
                (12, 9, 3.5f, .5f),
                (12, 10, 3.5f, .5f)
            };

            float[] aspects = { 9f / 21f, 1284f / 2778f, 9f / 16f };

            foreach (float aspect in aspects)
            {
                foreach ((int cols, int rows, float drop, float radius) item in cases)
                {
                    float halfHeight = item.rows * .5f;
                    float framedTop = halfHeight + frameTopAllowance;
                    float framedBottom = -halfHeight - item.drop - item.radius * 2f;
                    float cameraSize = GravityGridMetrics.FramedCameraSize(
                        item.cols,
                        framedTop - framedBottom,
                        aspect,
                        1f,
                        1f,
                        viewportWidth,
                        horizontalMargin,
                        topInset,
                        bottomInset);
                    float cameraY = GravityGridMetrics.CenteredCameraVerticalPosition(
                        framedTop,
                        framedBottom,
                        cameraSize,
                        topInset,
                        bottomInset);

                    float viewBottom = cameraY - cameraSize;
                    float viewHeight = cameraSize * 2f;
                    float topNorm = (framedTop - viewBottom) / viewHeight;
                    float bottomNorm = (framedBottom - viewBottom) / viewHeight;
                    float midNorm = (topNorm + bottomNorm) * .5f;
                    float expectedMidNorm = (1f - topInset + bottomInset) * .5f;

                    Assert.That(topNorm, Is.LessThanOrEqualTo(1f - topInset + 1e-4f),
                        $"Top frame clipped HUD for {item.cols}x{item.rows} at aspect {aspect:F3}");
                    Assert.That(bottomNorm, Is.GreaterThanOrEqualTo(bottomInset - 1e-4f),
                        $"Shredder clipped booster bar for {item.cols}x{item.rows} at aspect {aspect:F3}");
                    Assert.That(midNorm, Is.EqualTo(expectedMidNorm).Within(1e-4f),
                        $"Board+Shredder block was not vertically centered for {item.cols}x{item.rows}");
                }
            }
        }

        [Test]
        public void EffectiveShredderDrop_TrimsEmptyBottomRowsWithoutDroppingBelowHandoffClearance()
        {
            GravityLevelDefinition level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
            try
            {
                level.boardColumns = 8;
                level.boardRows = 10;
                level.subdivisions = 4;

                // Empty board keeps full configured drop (no spurious trimming).
                Assert.That(GravityGridMetrics.CountOpenEmptyBottomRows(level), Is.EqualTo(0));
                Assert.That(
                    GravityGridMetrics.EffectiveShredderDropBelowBoard(level, 1.5f, 2f),
                    Is.EqualTo(3.5f).Within(1e-4f));

                // Lowest obstacle at row 2 => 2 open bottom rows => drop reduces from 3.5 to 1.5.
                level.obstacles.Add(new ObstacleDefinition
                {
                    usesGridCells = true,
                    gridCell = new Vector2Int(2, 2),
                    sizeInGridCells = new Vector2Int(2, 1)
                });
                Assert.That(GravityGridMetrics.CountOpenEmptyBottomRows(level), Is.EqualTo(2));
                Assert.That(
                    GravityGridMetrics.EffectiveShredderDropBelowBoard(level, 1.5f, 2f),
                    Is.EqualTo(1.5f).Within(1e-4f));

                // Even with 4 empty bottom rows, drop never goes below handoff clearance (1.5).
                level.obstacles[0].gridCell = new Vector2Int(2, 4);
                Assert.That(GravityGridMetrics.CountOpenEmptyBottomRows(level), Is.EqualTo(4));
                Assert.That(
                    GravityGridMetrics.EffectiveShredderDropBelowBoard(level, 1.5f, 2f),
                    Is.EqualTo(1.5f).Within(1e-4f));

                // Adding a rotated piece that reaches row 1 lowers open empty rows to 1 => drop = 2.5.
                level.pieces.Add(new PieceDefinition
                {
                    origin = new Vector2Int(8, 8), // row 2
                    quarterTurns = 3, // Rotate((1,0), 3) = (0, -1) -> fine Y = 7 -> row 1
                    cells = new List<PieceCellDefinition>
                    {
                        new PieceCellDefinition(new Vector2Int(0, 0), PieceCellType.Block),
                        new PieceCellDefinition(new Vector2Int(1, 0), PieceCellType.Block)
                    }
                });
                Assert.That(GravityGridMetrics.CountOpenEmptyBottomRows(level), Is.EqualTo(1));
                Assert.That(
                    GravityGridMetrics.EffectiveShredderDropBelowBoard(level, 1.5f, 2f),
                    Is.EqualTo(2.5f).Within(1e-4f));

                // Inactive map cut-out on row 0 counts as non-open => 0 open rows => full 3.5 drop.
                level.inactiveBoardCells.Add(new Vector2Int(0, 0));
                Assert.That(GravityGridMetrics.CountOpenEmptyBottomRows(level), Is.EqualTo(0));
                Assert.That(
                    GravityGridMetrics.EffectiveShredderDropBelowBoard(level, 1.5f, 2f),
                    Is.EqualTo(3.5f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        [Test]
        public void AuthoredBoardPresentationConfig_EnforcesTopAndShredderGridClearancesAndZoomsInSmallLevels()
        {
            BoardPresentationConfig config = AssetDatabase.LoadAssetAtPath<BoardPresentationConfig>(
                "Assets/Config/Presentation/BoardPresentation_Navy.asset");
            Assert.That(config, Is.Not.Null);
            Assert.That(config.TopHudGapInGridCells, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(config.ShredderToBoosterGapInGridCells, Is.InRange(1.5f, 2.0f));

            float[] aspects = { 9f / 21f, 1125f / 2436f, 9f / 16f };
            (int cols, int rows)[] boards = { (4, 4), (5, 5), (7, 9), (9, 12), (12, 9) };
            const float drop = 3.5f;
            const float wheelRadius = .5f;

            foreach (float aspect in aspects)
            {
                float previousSmallSize = 0f;
                foreach ((int cols, int rows) board in boards)
                {
                    float halfHeight = board.rows * .5f;
                    float boardFrameTop = halfHeight + config.FrameTopAllowance;
                    float shredderBottom = -halfHeight - drop - wheelRadius * 2f;
                    float framedTop = boardFrameTop + config.TopHudGapInGridCells;
                    float framedBottom = shredderBottom - config.ShredderToBoosterGapInGridCells;

                    float cameraSize = GravityGridMetrics.FramedCameraSize(
                        board.cols,
                        framedTop - framedBottom,
                        aspect,
                        1f,
                        1f,
                        GravityGridMetrics.BoardViewportWidth,
                        config.CameraSizeMultiplier,
                        config.BoardTopViewportInset,
                        config.BoardBottomViewportInset,
                        config.FrameSideAllowance);
                    float cameraY = GravityGridMetrics.CenteredCameraVerticalPosition(
                        framedTop,
                        framedBottom,
                        cameraSize,
                        config.BoardTopViewportInset,
                        config.BoardBottomViewportInset);

                    float viewHeight = cameraSize * 2f;
                    float topHudBottomY = cameraY + cameraSize - config.BoardTopViewportInset * viewHeight;
                    float bottomHudTopY = cameraY - cameraSize + config.BoardBottomViewportInset * viewHeight;
                    float actualTopGap = topHudBottomY - boardFrameTop;
                    float actualBottomGap = shredderBottom - bottomHudTopY;

                    Assert.That(actualTopGap, Is.GreaterThanOrEqualTo(config.TopHudGapInGridCells - 1e-3f),
                        $"Top Timer-to-map gap fell below {config.TopHudGapInGridCells} for {board.cols}x{board.rows} at aspect {aspect:F3}");
                    Assert.That(actualBottomGap, Is.GreaterThanOrEqualTo(config.ShredderToBoosterGapInGridCells - 1e-3f),
                        $"Shredder-to-booster gap fell below {config.ShredderToBoosterGapInGridCells} for {board.cols}x{board.rows} at aspect {aspect:F3}");

                    if (board.cols == 4 && board.rows == 4)
                        previousSmallSize = cameraSize;
                    else if (board.cols == 9 && board.rows == 12)
                        Assert.That(previousSmallSize, Is.LessThan(cameraSize),
                            "Smaller boards must zoom in closer than larger boards.");
                }
            }
        }
    }
}
