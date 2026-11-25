using System;
using System.Collections.Generic;
using System.Reflection;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Gravity;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Gameplay.Reveal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class CanonicalGridSnapTests
    {
        private static readonly MethodInfo IsModelFullyWithinExposedBoxRowsMethod =
            typeof(RevealAreaCoordinator).GetMethod(
                "IsModelFullyWithinExposedBoxRows",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private sealed class SilentDeferredPresentation : IRevealAreaPresentation
        {
            public void SetBoxRemaining(int remaining, int targetCount)
            {
            }

            public void PlayOpening(RevealAreaKind kind, Action onCompleted)
            {
            }
        }

        [Test]
        public void Planner_OddCoarseOriginMidpoint_PreservesEvenStepParityAndSelfHealsOffGridStart()
        {
            List<GridCoordinate> candidates = new List<GridCoordinate>(36);

            // Odd coarse origin X=4 dragged +6 fine cells to X=10 must try 12 first
            // so FineGridDragStepper midpoint behavior remains unchanged.
            GridReleaseSnapPlanner.CollectCanonicalCandidates(
                visualAnchor: new GridCoordinate(10, 4),
                committedAnchor: new GridCoordinate(10, 4),
                hasLastCanonicalAnchor: true,
                lastCanonicalAnchor: new GridCoordinate(8, 4),
                hasStartAnchor: true,
                startAnchor: new GridCoordinate(4, 4),
                alignmentStride: 4,
                candidates);

            Assert.That(candidates.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(candidates[0], Is.EqualTo(new GridCoordinate(12, 4)));
            Assert.That(candidates[1], Is.EqualTo(new GridCoordinate(8, 4)));
            for (int index = 0; index < candidates.Count; index++)
                Assert.That(GridReleaseSnapPlanner.IsCanonicalAnchor(candidates[index], 4), Is.True);

            // Starting from a legacy +2 phase-locked anchor (6, 10) must NOT remain
            // phase-locked; all candidates are canonical multiples of 4.
            GridReleaseSnapPlanner.CollectCanonicalCandidates(
                visualAnchor: new GridCoordinate(6, 10),
                committedAnchor: new GridCoordinate(6, 10),
                hasLastCanonicalAnchor: false,
                lastCanonicalAnchor: default,
                hasStartAnchor: true,
                startAnchor: new GridCoordinate(6, 10),
                alignmentStride: 4,
                candidates);

            Assert.That(candidates.Count, Is.GreaterThan(0));
            for (int index = 0; index < candidates.Count; index++)
                Assert.That(GridReleaseSnapPlanner.IsCanonicalAnchor(candidates[index], 4), Is.True);
        }

        [Test]
        public void Planner_EuclideanFloorAndNegativeModulo_AreCanonical()
        {
            Assert.That(GridReleaseSnapPlanner.FloorToMultiple(-2, 4), Is.EqualTo(-4));
            Assert.That(GridReleaseSnapPlanner.CeilToMultiple(-2, 4), Is.EqualTo(0));
            Assert.That(GridReleaseSnapPlanner.IsCanonicalCoordinate(-4, 4), Is.True);
            Assert.That(GridReleaseSnapPlanner.IsCanonicalCoordinate(-2, 4), Is.False);
            Assert.That(GridReleaseSnapPlanner.FloorToMultiple(7, 4), Is.EqualTo(4));
            Assert.That(GridReleaseSnapPlanner.CeilToMultiple(7, 4), Is.EqualTo(8));
        }

        [Test]
        public void BoxPartialExposure_QuantizesCoarseRowsAndHandlesNonDivisibleShredCounts()
        {
            Assert.That(IsModelFullyWithinExposedBoxRowsMethod, Is.Not.Null);

            GravityLevelDefinition level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
            level.boardColumns = 5;
            level.boardRows = 8;
            level.subdivisions = 4;
            BoxRevealDefinition boxDef = new BoxRevealDefinition
            {
                areaId = "quantized-box",
                name = "Quantized Box",
                targetShredCount = 4,
                bounds = new RevealAreaBounds
                {
                    origin = new Vector2Int(0, 16),
                    size = new Vector2Int(12, 12)
                }
            };
            level.boxes.Add(boxDef);

            GameObject boardGo = new GameObject("BoxQuantizeBoard");
            try
            {
                PrototypeBoard board = boardGo.AddComponent<PrototypeBoard>();
                GravityBoardGrid grid = new GravityBoardGrid(20, 32);
                board.InitializeBoardSnapshot(new LevelBoardSnapshot(
                    grid,
                    new List<PieceModel>(),
                    new List<LevelBoardSnapshotIssue>()));

                RevealAreaCoordinator coordinator = new RevealAreaCoordinator();
                coordinator.RegisterPresentation("quantized-box", new SilentDeferredPresentation());
                board.ConfigureRevealAreas(coordinator);
                coordinator.Initialize(board, level);

                FieldInfo boxesField = typeof(RevealAreaCoordinator).GetField(
                    "boxes",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                object runtimeBox = ((System.Collections.IList)boxesField.GetValue(coordinator))[0];
                FieldInfo exposureField = runtimeBox.GetType().GetField("Exposure");
                FieldInfo stateField = runtimeBox.GetType().GetField("State");
                stateField.SetValue(runtimeBox, RevealAreaState.PartiallyRevealed);

                PieceModel pieceAtBottomOfBox = new PieceModel(
                    1,
                    new GridCoordinate(0, 16),
                    new GridCoordinate(0, 0),
                    CreateBoxCells(4, 4));
                PieceModel halfBlockCell = new PieceModel(
                    2,
                    new GridCoordinate(0, 17),
                    new GridCoordinate(0, 0),
                    new List<GridCoordinate> { new GridCoordinate(0, 0) });

                // 0.25 of 12 rows = 3 raw rows -> quantized down to 0 coarse rows (Y=16 stays closed).
                exposureField.SetValue(runtimeBox, 0.25f);
                Assert.That(
                    (bool)IsModelFullyWithinExposedBoxRowsMethod.Invoke(
                        coordinator,
                        new object[] { halfBlockCell, new GridCoordinate(0, 17), runtimeBox }),
                    Is.False);

                // 1f / 3f of 12 rows has float product ~3.999999f; must expose 4 rows (Y=16..19), not 0 or 2.
                exposureField.SetValue(runtimeBox, 1f / 3f);
                Assert.That(
                    (bool)IsModelFullyWithinExposedBoxRowsMethod.Invoke(
                        coordinator,
                        new object[] { pieceAtBottomOfBox, new GridCoordinate(0, 16), runtimeBox }),
                    Is.True);

                // 0.50 of 12 rows = 6 raw rows -> quantized to 4 coarse rows (Y=20..21 still blocked).
                exposureField.SetValue(runtimeBox, 0.50f);
                Assert.That(
                    (bool)IsModelFullyWithinExposedBoxRowsMethod.Invoke(
                        coordinator,
                        new object[] { halfBlockCell, new GridCoordinate(0, 21), runtimeBox }),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        [Test]
        public void GravityTrim_StopsCanonicalAboveHeldHalfBlockDrag_ButFollowsTransitiveShredderSupport()
        {
            GravityLevelDefinition level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
            level.boardColumns = 4;
            level.boardRows = 6;
            level.subdivisions = 4;

            GameObject boardGo = new GameObject("GravityTrimBoard");
            try
            {
                ConfigureActiveLevelForTest(level);
                PrototypeBoard board = boardGo.AddComponent<PrototypeBoard>();
                GravityBoardGrid grid = new GravityBoardGrid(16, 24);

                // Lower piece held by player at half-block Y=2..5 (Dragging -> NOT shredder-backed).
                PieceModel heldHalfBlock = new PieceModel(1, new GridCoordinate(0, 2), default, CreateBoxCells(4, 4));
                heldHalfBlock.TrySetState(PieceState.Dragging);
                Assert.That(grid.TryPlace(heldHalfBlock), Is.True);

                // Upper piece starts at canonical Y=12 -> raw fall stops on held piece at Y=6 (mod 4 == 2),
                // so planner trims target to Y=8 (mod 4 == 0).
                PieceModel upper = new PieceModel(2, new GridCoordinate(0, 12), default, CreateBoxCells(4, 4));
                Assert.That(grid.TryPlace(upper), Is.True);

                // Independent column: piece 3 shredding (Reserved at Y=0..3), piece 4 Placed at Y=4..7 (still
                // settled on Reserved) and piece 5 falling from Y=12 -> must keep raw target Y=8; wait if
                // Reserved ends at Y=2 (top = 2), piece 4 at Y=2..5, piece 5 at Y=12 drops onto Y=6!
                PieceModel shredding = new PieceModel(3, new GridCoordinate(8, -2), default, new List<GridCoordinate>
                {
                    new GridCoordinate(0, 2), new GridCoordinate(1, 2), new GridCoordinate(2, 2), new GridCoordinate(3, 2),
                    new GridCoordinate(0, 3), new GridCoordinate(1, 3), new GridCoordinate(2, 3), new GridCoordinate(3, 3)
                });
                shredding.TrySetState(PieceState.HandoffToPhysics);
                shredding.TrySetState(PieceState.Shredding);
                Assert.That(grid.TryPlace(shredding), Is.True);
                Assert.That(grid.TryReserve(shredding), Is.True);

                PieceModel middleTracker = new PieceModel(4, new GridCoordinate(8, 2), default, CreateBoxCells(4, 4));
                Assert.That(grid.TryPlace(middleTracker), Is.True);

                PieceModel topStackMember = new PieceModel(5, new GridCoordinate(8, 12), default, CreateBoxCells(4, 4));
                Assert.That(grid.TryPlace(topStackMember), Is.True);

                LevelBoardSnapshot snapshot = new LevelBoardSnapshot(
                    grid,
                    new List<PieceModel> { heldHalfBlock, upper, shredding, middleTracker, topStackMember },
                    new List<LevelBoardSnapshotIssue>());
                board.InitializeBoardSnapshot(snapshot);
                board.ConfigurePieceJoints(level);

                List<GridGravityMove> moves = new List<GridGravityMove>();
                Assert.That(board.TryPlanJointGroupGravityMove(2, moves), Is.True);
                Assert.That(moves.Count, Is.EqualTo(1));
                Assert.That(moves[0].ToAnchor.Y, Is.EqualTo(8),
                    "Gravity must trim above a mid-drag half-block piece instead of landing at Y=6.");

                moves.Clear();
                Assert.That(board.TryPlanJointGroupGravityMove(5, moves), Is.True);
                Assert.That(moves.Count, Is.EqualTo(1));
                Assert.That(moves[0].ToAnchor.Y, Is.EqualTo(6),
                    "Gravity must preserve smooth fine-cell following above a transitively Reserved shredder stack.");
            }
            finally
            {
                ResetActiveLevelForTest();
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        [Test]
        public void RuntimeValidator_WarnsOnOffGridPieceAnchorAndOffGridRevealBounds()
        {
            GravityLevelDefinition level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
            level.levelName = "OffGridValidatorTest";
            level.boardColumns = 5;
            level.boardRows = 7;
            level.subdivisions = 4;
            level.boxes.Add(new BoxRevealDefinition
            {
                areaId = "bad-box",
                name = "OffGridBox",
                bounds = new RevealAreaBounds
                {
                    origin = new Vector2Int(2, 4),
                    size = new Vector2Int(8, 8)
                }
            });

            GameObject boardGo = new GameObject("ValidatorBoard");
            try
            {
                GravityBoardGrid grid = new GravityBoardGrid(20, 28);
                PieceModel offGridPiece = new PieceModel(
                    0,
                    new GridCoordinate(6, 4),
                    new GridCoordinate(0, 0),
                    CreateBoxCells(4, 4));
                grid.TryPlace(offGridPiece);

                LevelBoardSnapshot snapshot = new LevelBoardSnapshot(
                    grid,
                    new List<PieceModel> { offGridPiece },
                    new List<LevelBoardSnapshotIssue>());

                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[LevelSnapshot\] Off-grid piece anchor detected"));
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[LevelSnapshot\] Off-grid Box reveal bounds detected"));

                LevelBoardSnapshotRuntimeValidator.Validate(level, snapshot, new List<PuzzlePiece>(), boardGo);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        private static List<GridCoordinate> CreateBoxCells(int width, int height)
        {
            List<GridCoordinate> cells = new List<GridCoordinate>(width * height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                cells.Add(new GridCoordinate(x, y));
            return cells;
        }

        private static void ConfigureActiveLevelForTest(GravityLevelDefinition activeLevel)
        {
            ResetActiveLevelForTest();
            Array levels = Array.CreateInstance(typeof(GravityLevelDefinition), 1);
            levels.SetValue(activeLevel, 0);
            typeof(GravityLevelRuntime).GetField("levels", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, levels);
            typeof(GravityLevelRuntime).GetField("currentLevelIndex", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, 0);
            typeof(GravityLevelRuntime).GetField("levelSequenceInitialized", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, true);
        }

        private static void ResetActiveLevelForTest()
        {
            typeof(GravityLevelRuntime).GetMethod("ResetLevelSequence", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, null);
        }
    }
}
