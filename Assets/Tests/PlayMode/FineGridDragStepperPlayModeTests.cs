using System.Collections;
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GravityPuzzle.Tests.PlayMode
{
    public sealed class FineGridDragStepperPlayModeTests
    {
        private static readonly Type CoordinateType = Type.GetType(
            "GravityPuzzle.Core.Grid.GridCoordinate, Assembly-CSharp");
        private static readonly MethodInfo TryGetAxisStepMethod = Type.GetType(
                "GravityPuzzle.Core.Grid.FineGridDragStepper, Assembly-CSharp")
            ?.GetMethod("TryGetAxisStep", BindingFlags.Public | BindingFlags.Static);
        private static readonly FieldInfo CoordinateX = CoordinateType?.GetField("X");
        private static readonly FieldInfo CoordinateY = CoordinateType?.GetField("Y");

        [UnityTest]
        public IEnumerator RealController_ConsumesAFastPointerTargetForNormalAndCombinedPieces()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create();
                yield return null;

                fixture.DragByFineCells(8, 0, false);
                Assert.That(fixture.AnchorX(false), Is.EqualTo(fixture.InitialAnchorX(false) + 8));
                Assert.That(fixture.BodyMatchesCommittedPivot(false), Is.True);

                fixture.Dispose();
                fixture = new DragRuntimeFixture();
                fixture.Create();
                yield return null;

                fixture.DragByFineCells(8, 0, true);
                Assert.That(fixture.AnchorX(true), Is.EqualTo(fixture.InitialAnchorX(true) + 8));
                Assert.That(fixture.CombinedDeltaIsPreserved(), Is.True);
                Assert.That(fixture.BodyMatchesCommittedPivot(true), Is.True);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_PreservesSafeFractionalDragForNormalAndCombinedPieces()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create();
                yield return null;

                fixture.DragByWorld(.1f, 0f, false);
                Assert.That(fixture.AnchorX(false), Is.EqualTo(fixture.InitialAnchorX(false)));
                Assert.That(fixture.BodyDelta(8).x, Is.EqualTo(.1f).Within(.0001f));
                Assert.That(fixture.BodyDelta(8).y, Is.EqualTo(0f).Within(.0001f));

                fixture.Dispose();
                fixture = new DragRuntimeFixture();
                fixture.Create();
                yield return null;

                fixture.DragByWorld(.1f, 0f, true);
                Assert.That(fixture.AnchorX(true), Is.EqualTo(fixture.InitialAnchorX(true)));
                Assert.That(fixture.BodyDelta(5).x, Is.EqualTo(.1f).Within(.0001f));
                Assert.That(fixture.BodyDelta(6).x, Is.EqualTo(.1f).Within(.0001f));
                Assert.That(fixture.CombinedDeltaIsPreserved(), Is.True);
                Assert.That(
                    Vector2.Distance(fixture.BodyDelta(5), fixture.BodyDelta(6)),
                    Is.LessThan(.0001f));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_SlidesAlongOpenAxisAndStopsAtExternalBlocker()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create();
                yield return null;

                fixture.BlockEveryVerticalCandidateForNormalPiece(8);
                fixture.DragByFineCells(8, 4, false);
                Assert.That(fixture.AnchorX(false), Is.EqualTo(fixture.InitialAnchorX(false) + 8));
                Assert.That(fixture.AnchorY(false), Is.EqualTo(fixture.InitialAnchorY(false)));

                fixture.Dispose();
                fixture = new DragRuntimeFixture();
                fixture.Create();
                yield return null;

                fixture.BlockNormalPieceAtHorizontalOffset(3);
                fixture.DragByFineCells(8, 0, false);
                Assert.That(fixture.AnchorX(false), Is.EqualTo(fixture.InitialAnchorX(false) + 2));
                Assert.That(fixture.BodyMatchesCommittedPivot(false), Is.True);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_StopsAtBoardBoundaryAndCombinedReleaseSnapsFromTheSameTraversal()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create();
                yield return null;

                fixture.DragByFineCells(-100, 0, false);
                Assert.That(fixture.AnchorX(false), Is.EqualTo(0));
                Assert.That(fixture.BodyMatchesCommittedPivot(false), Is.True);

                fixture.Dispose();
                fixture = new DragRuntimeFixture();
                fixture.Create();
                yield return null;

                fixture.DragByFineCells(6, 0, true);
                fixture.ReleaseCurrentDrag();
                Assert.That(fixture.AnchorX(true), Is.EqualTo(fixture.InitialAnchorX(true) + 8));
                Assert.That(fixture.CombinedDeltaIsPreserved(), Is.True);
                Assert.That(fixture.BodyMatchesCommittedPivot(true), Is.True);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_NormalDragStopsSmoothlyAtClosedBoxAndRecoversAwayFromIt()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create();
                yield return null;
                fixture.ConfigureBoxAdjacentToDraggedPiece(false, false, false);
                fixture.BeginDrag(false);

                for (int index = 0; index < 5; index++)
                {
                    fixture.MoveSelectedByWorld(.1f, 0f);
                    Assert.That(fixture.AnchorX(false), Is.EqualTo(fixture.InitialAnchorX(false)));
                    Assert.That(fixture.BodyMatchesCommittedPivot(false), Is.True,
                        "A Box rejection must not leave a fractional visual offset past its virtual boundary.");
                }

                fixture.MoveSelectedByWorld(-.1f, 0f);
                Assert.That(fixture.BodyDelta(8).x, Is.EqualTo(-.1f).Within(.0001f),
                    "Moving away from the Box must immediately restore continuous drag.");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_NormalDragStopsSmoothlyAtVerticalAndPartialBoxBoundaries()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create();
                yield return null;
                fixture.ConfigureBoxAdjacentToDraggedPiece(false, true, false);
                fixture.BeginDrag(false);
                fixture.MoveSelectedByWorld(0f, .1f);
                Assert.That(fixture.AnchorY(false), Is.EqualTo(fixture.InitialAnchorY(false)));
                Assert.That(fixture.BodyMatchesCommittedPivot(false), Is.True);

                fixture.Dispose();
                fixture = new DragRuntimeFixture();
                fixture.Create();
                yield return null;
                fixture.ConfigureBoxAdjacentToDraggedPiece(false, false, true);
                fixture.BeginDrag(false);
                fixture.MoveSelectedByWorld(.1f, 0f);
                Assert.That(fixture.BodyMatchesCommittedPivot(false), Is.True,
                    "External pieces must see a partial Box as the same solid boundary as a closed Box.");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_CombinedDragUsesTheSameVirtualBoxBoundary()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create();
                yield return null;
                fixture.ConfigureBoxAdjacentToDraggedPiece(true, false, false);
                fixture.BeginDrag(true);
                fixture.MoveSelectedByWorld(.1f, 0f);

                Assert.That(fixture.CombinedDeltaIsPreserved(), Is.True);
                Assert.That(fixture.BodyMatchesCommittedPivot(true), Is.True);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_HalfBlockReleaseAgainstBlockerAndBox_SnapsToCanonicalAnchor()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create();
                yield return null;

                // Stop +6 fine cells from InitialAnchorX (24 -> 30) against a blocker at +7.
                // Rounding +6 forward (+8 -> 32) is blocked; B+ must retract cleanly to canonical +4 (28)
                // instead of abandoning snap at 30.
                fixture.BlockNormalPieceAtHorizontalOffset(7);
                fixture.DragByFineCells(8, 0, false);
                Assert.That(fixture.AnchorX(false), Is.EqualTo(fixture.InitialAnchorX(false) + 6));
                fixture.ReleaseCurrentDrag();
                Assert.That(fixture.AnchorX(false), Is.EqualTo(fixture.InitialAnchorX(false) + 4));
                Assert.That(fixture.IsCanonicalAnchor(false), Is.True);

                fixture.Dispose();
                fixture = new DragRuntimeFixture();
                fixture.Create();
                yield return null;

                // Drag +2 fine cells and also push into an adjacent closed Box / Unlocking Elevator.
                fixture.ConfigureElevatorNextToCombinedGroup();
                fixture.BeginDrag(true);
                fixture.MoveSelectedByWorld(.5f, 0f);
                fixture.MoveSelectedByWorld(0f, .5f);
                fixture.ReleaseCurrentDrag();
                Assert.That(fixture.IsCanonicalAnchor(true), Is.True);
                Assert.That(fixture.CombinedDeltaIsPreserved(), Is.True);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_OffGridStartedPiece_SelfHealsAndDragEndCommitsOnInterrupt()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                // Start normal piece at an off-grid half-block anchor (22, 22).
                fixture.Create(normalAnchorOverride: new Vector2Int(22, 22));
                yield return null;

                fixture.BeginDrag(false);
                fixture.MoveSelectedByWorld(.25f, 0f);
                fixture.ReleaseCurrentDrag();
                Assert.That(fixture.IsCanonicalAnchor(false), Is.True,
                    "Canonical snap must self-heal a previously off-grid piece instead of locking its phase.");

                fixture.Dispose();
                fixture = new DragRuntimeFixture();
                fixture.Create();
                yield return null;

                // Drag +2 fine cells then interrupt drag cleanly (simulating lock / tutorial interruption).
                fixture.DragByFineCells(2, 2, false);
                fixture.InterruptCurrentDragQuietly();
                Assert.That(fixture.IsCanonicalAnchor(false), Is.True);
                Assert.That(fixture.IsPiecePlaced(false), Is.True,
                    "Interrupting an active drag must still commit canonical snap and restore Placed state.");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_RandomizedBoxAndElevatorFuzz_AlwaysMaintainsCanonicalRestingInvariant()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create();
                fixture.ConfigureBoxAdjacentToDraggedPiece(false, false, false);
                fixture.ConfigureElevatorNextToCombinedGroup();
                yield return null;

                System.Random rng = new System.Random(42069);
                for (int iteration = 0; iteration < 40; iteration++)
                {
                    bool combined = (iteration % 2) == 1;
                    fixture.BeginDrag(combined);
                    int stepCount = 1 + rng.Next(4);
                    for (int step = 0; step < stepCount; step++)
                    {
                        float dx = (rng.Next(9) - 4) * .25f + (rng.Next(3) - 1) * .1f;
                        float dy = (rng.Next(9) - 4) * .25f + (rng.Next(3) - 1) * .1f;
                        fixture.MoveSelectedByWorld(dx, dy);
                    }

                    fixture.ReleaseCurrentDrag();
                    Assert.That(fixture.IsCanonicalAnchor(combined), Is.True,
                        $"Fuzz iteration {iteration} left piece off canonical mod-4 grid.");
                    Assert.That(fixture.IsPiecePlaced(combined), Is.True,
                        $"Fuzz iteration {iteration} did not return piece to Placed.");
                    if (combined)
                        Assert.That(fixture.CombinedDeltaIsPreserved(), Is.True);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_CornerDragAndSubCellOverhang_NeverSnapWhileDragIsActive()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create();
                yield return null;

                // Block only the diagonal corner (x = 32, y = 28) relative to normal piece 8 (x=24..31, y=24..27).
                fixture.SetBlockedCell(32, 28);
                fixture.BeginDrag(false);

                // Move +0.10u (+0.40 fine) right into open X, then drag right with smaller upward wobble.
                // Must glide cleanly right along X without stalling, flipping diagonally, or wrapping at -0.5.
                fixture.MoveSelectedByWorld(.10f, 0f);
                Assert.That(fixture.BodyDelta(8).x, Is.EqualTo(.10f).Within(.0001f));
                Assert.That(fixture.BodyDelta(8).y, Is.EqualTo(0f).Within(.0001f));

                fixture.MoveSelectedByWorld(.05f, .01f);
                Assert.That(fixture.AnchorX(false), Is.EqualTo(fixture.InitialAnchorX(false)));
                Assert.That(fixture.BodyDelta(8).x, Is.EqualTo(.15f).Within(.0001f));
                Assert.That(fixture.BodyDelta(8).y, Is.EqualTo(0f).Within(.0001f));

                // Pushing up in small realistic frames smoothly bevels back around the corner with zero 1-frame snap.
                Vector2 previous = fixture.BodyWorldPosition(8);
                for (int step = 0; step < 10; step++)
                {
                    fixture.MoveSelectedByWorld(0f, .02f);
                    Vector2 current = fixture.BodyWorldPosition(8);
                    Assert.That(Vector2.Distance(previous, current), Is.LessThanOrEqualTo(.0201f),
                        "Corner bevel step must stay bounded by pointer delta without snapping.");
                    previous = current;
                }

                Assert.That(fixture.BodyDelta(8).x, Is.EqualTo(0f).Within(.0001f));
                Assert.That(fixture.BodyDelta(8).y, Is.EqualTo(.05f).Within(.0001f));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_CombinedCornerDrag_NeverFlipsAxesWhileDragIsActive()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create();
                yield return null;

                // Diagonal corner right-above combined upper L-piece 6's lower arm (x=4..11, y=8..11; upper arm x=4..7, y=12..15).
                fixture.SetBlockedCell(12, 12);
                fixture.BeginDrag(true);

                fixture.MoveSelectedByWorld(.10f, 0f);
                Assert.That(fixture.BodyDelta(5).x, Is.EqualTo(.10f).Within(.0001f));
                Assert.That(fixture.BodyDelta(6).x, Is.EqualTo(.10f).Within(.0001f));

                // Outward motion with smaller orthogonal wobble must stay smoothly on the active X axis.
                fixture.MoveSelectedByWorld(.02f, .01f);
                Assert.That(fixture.BodyDelta(5).x, Is.EqualTo(.12f).Within(.0001f));
                Assert.That(fixture.BodyDelta(5).y, Is.EqualTo(0f).Within(.0001f));

                // Incremental vertical push smoothly bevels the combined group around (12, 12) without jumps.
                Vector2 previous = fixture.BodyWorldPosition(5);
                for (int step = 0; step < 8; step++)
                {
                    fixture.MoveSelectedByWorld(0f, .02f);
                    Vector2 current = fixture.BodyWorldPosition(5);
                    Assert.That(Vector2.Distance(previous, current), Is.LessThanOrEqualTo(.0201f));
                    Assert.That(
                        Vector2.Distance(fixture.BodyDelta(5), fixture.BodyDelta(6)),
                        Is.LessThan(.0001f));
                    previous = current;
                }

                Assert.That(fixture.BodyDelta(5).x, Is.EqualTo(0f).Within(.0001f));
                Assert.That(fixture.BodyDelta(5).y, Is.EqualTo(.04f).Within(.0001f));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_OneByOnePiece_EntersExactOneBlockGapSmoothlyWithoutSnagOrSnap()
        {
            // Cover both Case B (within Anchor=24: +0.08u / -0.20u) and Case C (adjacent Anchor=23 at 23.20 / Anchor=25 at 24.80).
            (int startAnchorX, float setupMoveWorldX, string label)[] gapEntryCases =
            {
                (24, .08f, "CaseB-right-overhang"),
                (24, -.20f, "CaseB-left-overhang"),
                (20, .80f, "CaseC-approach-from-left-lip"),
                (28, -.80f, "CaseC-approach-from-right-lip")
            };

            for (int caseIndex = 0; caseIndex < gapEntryCases.Length; caseIndex++)
            {
                var testCase = gapEntryCases[caseIndex];
                DragRuntimeFixture fixture = new DragRuntimeFixture();
                try
                {
                    fixture.Create(
                        normalAnchorOverride: new Vector2Int(testCase.startAnchorX, 24),
                        singleBlockNormalPiece: true);
                    yield return null;

                    // Exact 4-fine-cell vertical gap at x=24..27, y=16..23 between left (20..23) and right (28..31) walls.
                    fixture.BlockVerticalGapWalls(gapMinX: 24, wallBottomY: 16, wallTopY: 23);
                    fixture.BeginDrag(false);
                    fixture.MoveSelectedByWorld(testCase.setupMoveWorldX, 0f);

                    Vector2 previous = fixture.BodyWorldPosition(8);
                    for (int step = 0; step < 32; step++)
                    {
                        fixture.MoveSelectedByWorld(0f, -.025f);
                        Vector2 current = fixture.BodyWorldPosition(8);
                        Assert.That(
                            Vector2.Distance(previous, current),
                            Is.LessThanOrEqualTo(.0251f),
                            $"{testCase.label} snapped visually during downward gap entry.");
                        previous = current;
                    }

                    Assert.That(
                        fixture.AnchorX(false),
                        Is.EqualTo(24),
                        $"{testCase.label} failed to center 1x1 block in exact 4-fine-cell gap.");
                    Assert.That(
                        fixture.AnchorY(false),
                        Is.LessThanOrEqualTo(22),
                        $"{testCase.label} snagged on gap lip instead of entering.");
                }
                finally
                {
                    fixture.Dispose();
                }
            }

            // Sweeping horizontally across the top of the gap with slight downward wobble must not snag or drop in.
            DragRuntimeFixture sweepFixture = new DragRuntimeFixture();
            try
            {
                sweepFixture.Create(singleBlockNormalPiece: true);
                yield return null;

                sweepFixture.BlockVerticalGapWalls(gapMinX: 24, wallBottomY: 16, wallTopY: 23);
                sweepFixture.BeginDrag(false);
                sweepFixture.MoveSelectedByWorld(.08f, 0f);
                sweepFixture.MoveSelectedByWorld(.05f, -.01f);
                Assert.That(sweepFixture.BodyDelta(8).x, Is.EqualTo(.13f).Within(.0001f));
                Assert.That(sweepFixture.BodyDelta(8).y, Is.EqualTo(0f).Within(.0001f));
            }
            finally
            {
                sweepFixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RealController_RegrabbingMidReleaseTween_PreservesVisualPositionWithoutSnap()
        {
            DragRuntimeFixture fixture = new DragRuntimeFixture();
            try
            {
                fixture.Create(withFallView: true);
                yield return null;

                // Drag +0.40u (+1.6 fine cells: commits +1 step, leaves +0.15u visual lead), then release to start snap tween.
                fixture.DragByWorld(.40f, 0f, false);
                Vector2 releasedStartPosition = fixture.BodyWorldPosition(8);
                fixture.ReleaseCurrentDrag();

                // Re-grab immediately while the release presentation tween is active back toward canonical x=24.
                Vector2 preGrabPosition = fixture.BodyWorldPosition(8);
                Assert.That(Vector2.Distance(preGrabPosition, releasedStartPosition), Is.LessThan(.05f));
                fixture.BeginDrag(false);
                Assert.That(
                    Vector2.Distance(fixture.BodyWorldPosition(8), preGrabPosition),
                    Is.LessThan(.001f),
                    "Re-grabbing a piece mid-release must not snap it to its release target.");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator PointerWithOneFineCellCrossOffset_SlidesAlongNarrowChannel()
        {
            // A one-cell-high corridor: the pointer is one fine cell above
            // it, so vertical attempts must fail while horizontal drag stays
            // responsive all the way through the channel.
            Assert.That(TryGetAxisStepMethod, Is.Not.Null);
            Assert.That(CoordinateType, Is.Not.Null);
            int pieceX = 1;
            int pieceY = 2;
            const int targetX = 8;
            const int targetY = 3;
            int committedSteps = 0;
            while (committedSteps < 64)
            {
                bool movedAnyAxis = false;
                if (TryGetAxisStep(
                        pieceX,
                        pieceY,
                        targetX,
                        targetY,
                        true,
                        out int horizontalX,
                        out int horizontalY) &&
                    IsInsideNarrowChannel(horizontalX, horizontalY))
                {
                    pieceX = horizontalX;
                    pieceY = horizontalY;
                    committedSteps++;
                    movedAnyAxis = true;
                }

                if (committedSteps >= 64)
                    break;

                if (TryGetAxisStep(
                        pieceX,
                        pieceY,
                        targetX,
                        targetY,
                        false,
                        out int verticalX,
                        out int verticalY) &&
                    IsInsideNarrowChannel(verticalX, verticalY))
                {
                    pieceX = verticalX;
                    pieceY = verticalY;
                    committedSteps++;
                    movedAnyAxis = true;
                }

                if (!movedAnyAxis)
                    break;
            }

            Assert.That(pieceX, Is.EqualTo(8));
            Assert.That(pieceY, Is.EqualTo(2));
            Assert.That(committedSteps, Is.EqualTo(7));
            yield return null;
        }

        private static bool IsInsideNarrowChannel(int x, int y)
        {
            return x >= 0 && x < 10 && y == 2;
        }

        private static bool TryGetAxisStep(
            int currentX,
            int currentY,
            int targetX,
            int targetY,
            bool horizontalAxis,
            out int nextX,
            out int nextY)
        {
            object[] arguments =
            {
                Activator.CreateInstance(CoordinateType, currentX, currentY),
                Activator.CreateInstance(CoordinateType, targetX, targetY),
                horizontalAxis,
                null
            };
            bool hasStep = (bool)TryGetAxisStepMethod.Invoke(null, arguments);
            if (!hasStep)
            {
                nextX = currentX;
                nextY = currentY;
                return false;
            }

            object coordinate = arguments[3];
            nextX = (int)CoordinateX.GetValue(coordinate);
            nextY = (int)CoordinateY.GetValue(coordinate);
            return true;
        }

        /// <summary>
        /// This fixture deliberately drives the real private controller entry
        /// points rather than duplicating its movement rules. Only the pointer
        /// sample is injected so PlayMode tests remain deterministic without
        /// depending on editor mouse state.
        /// </summary>
        private sealed class DragRuntimeFixture : IDisposable
        {
            private static readonly Type BootstrapType = Type.GetType("GravityPuzzle.PrototypeBootstrap, Assembly-CSharp");
            private static readonly Type LevelType = Type.GetType("GravityPuzzle.GravityLevelDefinition, Assembly-CSharp");
            private static readonly Type PieceDefinitionType = Type.GetType("GravityPuzzle.PieceDefinition, Assembly-CSharp");
            private static readonly Type PieceCellDefinitionType = Type.GetType("GravityPuzzle.PieceCellDefinition, Assembly-CSharp");
            private static readonly Type PieceCellType = Type.GetType("GravityPuzzle.PieceCellType, Assembly-CSharp");
            private static readonly Type JointDefinitionType = Type.GetType("GravityPuzzle.JointDefinition, Assembly-CSharp");
            private static readonly Type JointEndpointType = Type.GetType("GravityPuzzle.JointEndpointDefinition, Assembly-CSharp");
            private static readonly Type BoardType = Type.GetType("GravityPuzzle.PrototypeBoard, Assembly-CSharp");
            private static readonly Type ControllerType = Type.GetType("GravityPuzzle.PuzzleDragController, Assembly-CSharp");
            private static readonly Type PieceType = Type.GetType("GravityPuzzle.PuzzlePiece, Assembly-CSharp");
            private static readonly Type GridType = Type.GetType("GravityPuzzle.Core.Grid.GravityBoardGrid, Assembly-CSharp");
            private static readonly Type SnapshotType = Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshot, Assembly-CSharp");
            private static readonly Type SnapshotBuilderType = Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshotBuilder, Assembly-CSharp");
            private static readonly Type PieceModelType = Type.GetType("GravityPuzzle.Gameplay.Pieces.PieceModel, Assembly-CSharp");
            private static readonly Type RuntimeType = Type.GetType("GravityPuzzle.GravityLevelRuntime, Assembly-CSharp");
            private static readonly Type CoordinatorType = Type.GetType(
                "GravityPuzzle.Gameplay.Reveal.RevealAreaCoordinator, Assembly-CSharp");
            private static readonly Type BoxDefinitionType = Type.GetType(
                "GravityPuzzle.BoxRevealDefinition, Assembly-CSharp");
            private static readonly Type BoundsType = Type.GetType(
                "GravityPuzzle.RevealAreaBounds, Assembly-CSharp");
            private static readonly Type RevealAreaStateType = Type.GetType(
                "GravityPuzzle.RevealAreaState, Assembly-CSharp");
            private static readonly Type ElevatorDefinitionType = Type.GetType(
                "GravityPuzzle.ElevatorRevealDefinition, Assembly-CSharp");
            private static readonly Type PieceStateType = Type.GetType(
                "GravityPuzzle.Gameplay.Pieces.PieceState, Assembly-CSharp");
            private static readonly Type FallViewType = Type.GetType(
                "GravityPuzzle.Presentation.Views.PieceGridFallView, Assembly-CSharp");

            private readonly List<GameObject> objects = new List<GameObject>();
            private object level;
            private object board;
            private object controller;
            private object grid;
            private readonly Dictionary<int, object> models = new Dictionary<int, object>();
            private readonly Dictionary<int, Component> pieces = new Dictionary<int, Component>();
            private readonly Dictionary<int, Vector2> initialPositions = new Dictionary<int, Vector2>();
            private readonly Dictionary<int, Vector2Int> initialAnchors = new Dictionary<int, Vector2Int>();
            private Vector2 dragPointer;
            private ScriptableObject cachedTweenConfig;

            public void Create(
                Vector2Int? normalAnchorOverride = null,
                bool withFallView = false,
                bool singleBlockNormalPiece = false)
            {
                Assert.That(BoardType, Is.Not.Null);
                Assert.That(ControllerType, Is.Not.Null);
                Assert.That(PieceType, Is.Not.Null);
                Assert.That(GridType, Is.Not.Null);
                Assert.That(SnapshotType, Is.Not.Null);
                Assert.That(PieceModelType, Is.Not.Null);

                Assert.That(LevelType, Is.Not.Null);
                Assert.That(PieceDefinitionType, Is.Not.Null);
                Assert.That(PieceCellDefinitionType, Is.Not.Null);
                Assert.That(PieceCellType, Is.Not.Null);
                Assert.That(JointDefinitionType, Is.Not.Null);
                Assert.That(JointEndpointType, Is.Not.Null);

                level = CreateSyntheticCombinedDragLevel(singleBlockNormalPiece);
                ConfigureRuntimeLevel((UnityEngine.Object)level);

                int fineColumns = (int)level.GetType().GetProperty("FineColumns").GetValue(level);
                int fineRows = (int)level.GetType().GetProperty("FineRows").GetValue(level);
                grid = Activator.CreateInstance(GridType, fineColumns, fineRows);

                object authoredSnapshot = SnapshotBuilderType.GetMethod("Build").Invoke(null, new[] { level });
                IList runtimeModels = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(PieceModelType));
                AddModel(runtimeModels, authoredSnapshot, 5, new Vector2Int(4, 4));
                AddModel(runtimeModels, authoredSnapshot, 6, new Vector2Int(4, 8));
                AddModel(runtimeModels, authoredSnapshot, 8, normalAnchorOverride ?? new Vector2Int(24, 24));
                object issues = Activator.CreateInstance(typeof(List<>).MakeGenericType(
                    Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshotIssue, Assembly-CSharp")));
                object snapshot = Activator.CreateInstance(SnapshotType, grid, runtimeModels, issues);

                GameObject boardObject = new GameObject("Drag Test Board");
                objects.Add(boardObject);
                board = boardObject.AddComponent(BoardType);
                Invoke(board, "InitializeBoardSnapshot", snapshot);
                Invoke(board, "ConfigurePieceJoints", level);

                GameObject cameraObject = new GameObject("Drag Test Camera");
                objects.Add(cameraObject);
                Camera testCamera = cameraObject.AddComponent<Camera>();
                testCamera.orthographic = true;
                testCamera.transform.position = new Vector3(0f, 0f, -10f);
                ConfigureSceneCamera(testCamera);

                GameObject controllerObject = new GameObject("Drag Test Controller");
                objects.Add(controllerObject);
                controller = controllerObject.AddComponent(ControllerType);

                CreatePiece(5, withFallView);
                CreatePiece(6, withFallView);
                CreatePiece(8, withFallView);
                Physics2D.SyncTransforms();
            }

            public void DragByFineCells(int deltaX, int deltaY, bool combined)
            {
                DragByWorld(deltaX / 4f, deltaY / 4f, combined);
            }

            public void DragByWorld(float deltaX, float deltaY, bool combined)
            {
                BeginDrag(combined);
                MoveSelectedByWorld(deltaX, deltaY);
            }

            public void BeginDrag(bool combined)
            {
                int id = combined ? 5 : 8;
                Component piece = pieces[id];
                dragPointer = piece.GetComponent<Rigidbody2D>().position;
                Invoke(controller, "TrySelectPiece", dragPointer);
            }

            public void MoveSelectedByWorld(float deltaX, float deltaY)
            {
                dragPointer += new Vector2(deltaX, deltaY);
                Invoke(controller, "UpdateDragTarget", dragPointer);
                Invoke(controller, "TryMoveSelectedPieceOnGrid", SelectedPiece());
            }

            public void ConfigureBoxAdjacentToDraggedPiece(bool combined, bool vertical, bool partial)
            {
                Assert.That(CoordinatorType, Is.Not.Null);
                Assert.That(BoxDefinitionType, Is.Not.Null);
                Assert.That(BoundsType, Is.Not.Null);
                Assert.That(RevealAreaStateType, Is.Not.Null);

                int[] ids = combined ? new[] { 5, 6 } : new[] { 8 };
                int minX = int.MaxValue;
                int minY = int.MaxValue;
                int maxX = int.MinValue;
                int maxY = int.MinValue;
                for (int idIndex = 0; idIndex < ids.Length; idIndex++)
                {
                    object model = models[ids[idIndex]];
                    Vector2Int anchor = Anchor(ids[idIndex]);
                    foreach (object localCell in (IEnumerable)PieceModelType.GetProperty("LocalCells").GetValue(model))
                    {
                        Vector2Int local = ToVector2Int(localCell);
                        minX = Mathf.Min(minX, anchor.x + local.x);
                        minY = Mathf.Min(minY, anchor.y + local.y);
                        maxX = Mathf.Max(maxX, anchor.x + local.x);
                        maxY = Mathf.Max(maxY, anchor.y + local.y);
                    }
                }

                object box = Activator.CreateInstance(BoxDefinitionType);
                BoxDefinitionType.GetField("areaId").SetValue(box, Guid.NewGuid().ToString());
                BoxDefinitionType.GetField("targetShredCount").SetValue(box, int.MaxValue);
                object bounds = BoxDefinitionType.GetField("bounds").GetValue(box);
                BoundsType.GetField("origin").SetValue(
                    bounds,
                    vertical ? new Vector2Int(minX, maxY + 1) : new Vector2Int(maxX + 1, minY));
                BoundsType.GetField("size").SetValue(
                    bounds,
                    vertical ? new Vector2Int(maxX - minX + 1, 1) : new Vector2Int(1, maxY - minY + 1));
                BoxDefinitionType.GetField("bounds").SetValue(box, bounds);
                ((IList)level.GetType().GetField("boxes").GetValue(level)).Add(box);

                object coordinator = Activator.CreateInstance(CoordinatorType);
                Invoke(coordinator, "Initialize", board, level);
                Invoke(board, "ConfigureRevealAreas", coordinator);
                if (!partial)
                    return;

                IList runtimes = (IList)CoordinatorType.GetField("boxes", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(coordinator);
                object runtimeBox = runtimes[runtimes.Count - 1];
                runtimeBox.GetType().GetField("State").SetValue(
                    runtimeBox,
                    Enum.Parse(RevealAreaStateType, "PartiallyRevealed"));
            }

            public void ReleaseCurrentDrag()
            {
                Invoke(controller, "ReleasePiece");
            }

            public void InterruptCurrentDragQuietly()
            {
                Invoke(controller, "CommitDragEnd", false, false);
            }

            public bool IsCanonicalAnchor(bool combined)
            {
                Vector2Int anchor = Anchor(combined ? 5 : 8);
                bool primaryCanonical = ((anchor.x % 4) + 4) % 4 == 0 && ((anchor.y % 4) + 4) % 4 == 0;
                if (!combined)
                    return primaryCanonical;

                Vector2Int second = Anchor(6);
                return primaryCanonical && ((second.x % 4) + 4) % 4 == 0 && ((second.y % 4) + 4) % 4 == 0;
            }

            public bool IsPiecePlaced(bool combined)
            {
                object placed = Enum.Parse(PieceStateType, "Placed");
                bool primaryPlaced = Equals(PieceModelType.GetProperty("State").GetValue(models[combined ? 5 : 8]), placed);
                if (!combined)
                    return primaryPlaced;

                return primaryPlaced && Equals(PieceModelType.GetProperty("State").GetValue(models[6]), placed);
            }

            public void ConfigureElevatorNextToCombinedGroup()
            {
                Assert.That(CoordinatorType, Is.Not.Null);
                Assert.That(ElevatorDefinitionType, Is.Not.Null);
                Assert.That(BoundsType, Is.Not.Null);
                Assert.That(RevealAreaStateType, Is.Not.Null);

                Vector2Int anchor5 = Anchor(5);
                object elevator = Activator.CreateInstance(ElevatorDefinitionType);
                ElevatorDefinitionType.GetField("areaId").SetValue(elevator, Guid.NewGuid().ToString());
                object bounds = ElevatorDefinitionType.GetField("bounds").GetValue(elevator);
                BoundsType.GetField("origin").SetValue(bounds, new Vector2Int(anchor5.x + 8, anchor5.y));
                BoundsType.GetField("size").SetValue(bounds, new Vector2Int(4, 8));
                ElevatorDefinitionType.GetField("bounds").SetValue(elevator, bounds);
                ((IList)level.GetType().GetField("elevators").GetValue(level)).Add(elevator);

                object coordinator = Activator.CreateInstance(CoordinatorType);
                Invoke(coordinator, "Initialize", board, level);
                Invoke(board, "ConfigureRevealAreas", coordinator);

                IList runtimes = (IList)CoordinatorType.GetField("elevators", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(coordinator);
                object runtimeElevator = runtimes[runtimes.Count - 1];
                runtimeElevator.GetType().GetField("State").SetValue(
                    runtimeElevator,
                    Enum.Parse(RevealAreaStateType, "Unlocking"));
            }

            public int AnchorX(bool combined) => Anchor(combined ? 5 : 8).x;
            public int AnchorY(bool combined) => Anchor(combined ? 5 : 8).y;
            public int InitialAnchorX(bool combined) => initialAnchors[combined ? 5 : 8].x;
            public int InitialAnchorY(bool combined) => initialAnchors[combined ? 5 : 8].y;

            public bool CombinedDeltaIsPreserved()
            {
                Vector2Int first = Anchor(5);
                Vector2Int second = Anchor(6);
                return second - first == initialAnchors[6] - initialAnchors[5];
            }

            public bool BodyMatchesCommittedPivot(bool combined)
            {
                int id = combined ? 5 : 8;
                return Vector2.Distance(pieces[id].GetComponent<Rigidbody2D>().position, PivotWorld(id)) < .0001f;
            }

            public Vector2 BodyDelta(int id)
            {
                return pieces[id].GetComponent<Rigidbody2D>().position - initialPositions[id];
            }

            public Vector2 BodyWorldPosition(int id)
            {
                return pieces[id].GetComponent<Rigidbody2D>().position;
            }

            public void SetBlockedCell(int x, int y)
            {
                Invoke(grid, "SetBlocked", Coordinate(x, y));
            }

            public void BlockVerticalGapWalls(int gapMinX, int wallBottomY, int wallTopY)
            {
                for (int y = wallBottomY; y <= wallTopY; y++)
                {
                    for (int x = gapMinX - 4; x < gapMinX; x++)
                        SetBlockedCell(x, y);
                    for (int x = gapMinX + 4; x < gapMinX + 8; x++)
                        SetBlockedCell(x, y);
                }
            }

            public void BlockNormalPieceAtHorizontalOffset(int offset)
            {
                Vector2Int anchor = initialAnchors[8];
                Vector2Int rightEdgeCell = FirstLocalCell(8);
                foreach (object localCell in (IEnumerable)PieceModelType.GetProperty("LocalCells").GetValue(models[8]))
                {
                    Vector2Int cell = ToVector2Int(localCell);
                    if (cell.x > rightEdgeCell.x || (cell.x == rightEdgeCell.x && cell.y < rightEdgeCell.y))
                        rightEdgeCell = cell;
                }

                Invoke(grid, "SetBlocked", Coordinate(anchor.x + rightEdgeCell.x + offset, anchor.y + rightEdgeCell.y));
            }

            public void BlockEveryVerticalCandidateForNormalPiece(int horizontalDistance)
            {
                Vector2Int anchor = initialAnchors[8];
                int minLocalX = int.MaxValue;
                int maxLocalX = int.MinValue;
                int maxLocalY = int.MinValue;
                foreach (object localCell in (IEnumerable)PieceModelType.GetProperty("LocalCells").GetValue(models[8]))
                {
                    Vector2Int cell = ToVector2Int(localCell);
                    minLocalX = Mathf.Min(minLocalX, cell.x);
                    maxLocalX = Mathf.Max(maxLocalX, cell.x);
                    maxLocalY = Mathf.Max(maxLocalY, cell.y);
                }

                for (int x = minLocalX; x <= maxLocalX + horizontalDistance; x++)
                    Invoke(grid, "SetBlocked", Coordinate(anchor.x + x, anchor.y + maxLocalY + 1));
            }

            public void Dispose()
            {
                for (int index = objects.Count - 1; index >= 0; index--)
                {
                    if (objects[index] != null)
                        UnityEngine.Object.DestroyImmediate(objects[index]);
                }
                objects.Clear();
                ConfigureSceneCamera(null);
                ResetRuntimeLevel();
                if (level is UnityEngine.Object levelObject)
                    UnityEngine.Object.DestroyImmediate(levelObject);
                level = null;
            }

            private void AddModel(IList runtimeModels, object authoredSnapshot, int id, Vector2Int anchor)
            {
                object authored = GetSnapshotPiece(authoredSnapshot, id);
                Assert.That(authored, Is.Not.Null, $"Expected source model {id} in combineblock.asset.");
                object clone = Activator.CreateInstance(
                    PieceModelType,
                    id,
                    Coordinate(anchor.x, anchor.y),
                    PieceModelType.GetProperty("PivotOffset").GetValue(authored),
                    CopyLocalCells(authored));
                MethodInfo tryPlace = FindMethod(GridType, "TryPlace", 2);
                object[] placementArguments = { clone, null };
                bool placed = (bool)tryPlace.Invoke(grid, placementArguments);
                Assert.That(placed, Is.True, $"Could not place test model {id}.");
                runtimeModels.Add(clone);
                models[id] = clone;
                initialAnchors[id] = anchor;
            }

            private void CreatePiece(int id, bool withFallView = false)
            {
                GameObject root = new GameObject($"Drag Test Piece {id}");
                objects.Add(root);
                root.AddComponent<BoxCollider2D>().size = Vector2.one;
                if (withFallView && FallViewType != null)
                {
                    if (cachedTweenConfig == null)
                        cachedTweenConfig = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/TweenConfig.asset");
                    Component fallView = root.AddComponent(FallViewType);
                    FieldInfo configField = FallViewType.GetField("tweenConfig", BindingFlags.Instance | BindingFlags.NonPublic);
                    configField?.SetValue(fallView, cachedTweenConfig);
                }
                Component piece = root.AddComponent(PieceType);
                Invoke(piece, "ConfigureSourcePieceId", id);
                Rigidbody2D body = root.GetComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.position = PivotWorld(id);
                pieces[id] = piece;
                initialPositions[id] = body.position;
            }

            private Vector2 PivotWorld(int id)
            {
                Vector2Int anchor = Anchor(id);
                Vector2Int pivotOffset = ToVector2Int(PieceModelType.GetProperty("PivotOffset").GetValue(models[id]));
                int columns = (int)level.GetType().GetField("boardColumns").GetValue(level);
                int rows = (int)level.GetType().GetField("boardRows").GetValue(level);
                int subdivisions = (int)level.GetType().GetField("subdivisions").GetValue(level);
                return new Vector2(
                    -columns * .5f + (anchor.x - pivotOffset.x + .5f) / subdivisions,
                    -rows * .5f + (anchor.y - pivotOffset.y + .5f) / subdivisions);
            }

            private Vector2Int Anchor(int id) => ToVector2Int(PieceModelType.GetProperty("Anchor").GetValue(models[id]));

            private Component SelectedPiece()
            {
                FieldInfo selectedPiece = ControllerType.GetField(
                    "selectedPiece",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                return selectedPiece != null ? selectedPiece.GetValue(controller) as Component : null;
            }

            private Vector2Int FirstLocalCell(int id)
            {
                IEnumerable cells = (IEnumerable)PieceModelType.GetProperty("LocalCells").GetValue(models[id]);
                foreach (object cell in cells)
                    return ToVector2Int(cell);
                throw new AssertionException("A draggable model must have at least one local cell.");
            }

            private static object GetSnapshotPiece(object snapshot, int id)
            {
                MethodInfo method = SnapshotType.GetMethod("TryGetPiece");
                object[] arguments = { id, null };
                return (bool)method.Invoke(snapshot, arguments) ? arguments[1] : null;
            }

            private static IList CopyLocalCells(object model)
            {
                IList copied = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(CoordinateType));
                foreach (object cell in (IEnumerable)PieceModelType.GetProperty("LocalCells").GetValue(model))
                    copied.Add(cell);
                return copied;
            }

            private static object Coordinate(int x, int y) => Activator.CreateInstance(CoordinateType, x, y);

            private static Vector2Int ToVector2Int(object coordinate) => new Vector2Int(
                (int)CoordinateX.GetValue(coordinate),
                (int)CoordinateY.GetValue(coordinate));

            private static void Invoke(object target, string name, params object[] arguments)
            {
                MethodInfo method = FindMethod(target.GetType(), name, arguments.Length);
                Assert.That(method, Is.Not.Null, $"Missing runtime method {name}.");
                method.Invoke(target, arguments);
            }

            private static MethodInfo FindMethod(Type type, string name, int parameterCount)
            {
                MethodInfo[] methods = type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                for (int index = 0; index < methods.Length; index++)
                {
                    MethodInfo method = methods[index];
                    if (method.Name == name && method.GetParameters().Length == parameterCount)
                        return method;
                }

                return null;
            }

            private static void ConfigureSceneCamera(Camera camera)
            {
                Assert.That(BootstrapType, Is.Not.Null);
                MethodInfo configureCamera = BootstrapType.GetMethod(
                    "ConfigureSceneCamera",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                Assert.That(configureCamera, Is.Not.Null);
                configureCamera.Invoke(null, new object[] { camera });
            }

            private static void ConfigureRuntimeLevel(object activeLevel)
            {
                ResetRuntimeLevel();
                Array levels = Array.CreateInstance(activeLevel.GetType(), 1);
                levels.SetValue(activeLevel, 0);
                RuntimeType.GetField("levels", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, levels);
                RuntimeType.GetField("currentLevelIndex", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 0);
                RuntimeType.GetField("levelSequenceInitialized", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
            }

            private static void ResetRuntimeLevel()
            {
                RuntimeType.GetMethod("ResetLevelSequence", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, null);
            }

            private static ScriptableObject CreateSyntheticCombinedDragLevel(bool singleBlockNormalPiece)
            {
                ScriptableObject level = ScriptableObject.CreateInstance(LevelType);
                LevelType.GetField("boardColumns").SetValue(level, 10);
                LevelType.GetField("boardRows").SetValue(level, 10);
                LevelType.GetField("subdivisions").SetValue(level, 4);

                IList pieceDefs = (IList)LevelType.GetField("pieces").GetValue(level);
                for (int i = 0; i <= 8; i++)
                {
                    object def = Activator.CreateInstance(PieceDefinitionType);
                    PieceDefinitionType.GetField("pieceId").SetValue(def, $"synthetic-piece-{i}");
                    PieceDefinitionType.GetField("name").SetValue(def, $"Piece {i}");
                    pieceDefs.Add(def);
                }

                // Index 5: horizontal 8x4 block; index 6: L-shaped 3-module combined partner; index 8: normal block (4x4 or 8x4).
                PopulatePieceRect(pieceDefs[5], "combined-lower", new Vector2Int(32, 20), (0, 0, 8, 4));
                PopulatePieceRect(pieceDefs[6], "combined-upper", new Vector2Int(32, 24), (0, 0, 8, 4), (0, 4, 4, 4));
                PopulatePieceRect(
                    pieceDefs[8],
                    singleBlockNormalPiece ? "normal-single" : "normal-double",
                    new Vector2Int(32, 32),
                    singleBlockNormalPiece ? (0, 0, 4, 4) : (0, 0, 8, 4));

                IList jointDefs = (IList)LevelType.GetField("joints").GetValue(level);
                jointDefs.Add(CreateJoint("joint-0", "combined-lower", Vector2Int.zero, "combined-upper", Vector2Int.zero));
                jointDefs.Add(CreateJoint("joint-1", "combined-lower", new Vector2Int(4, 0), "combined-upper", new Vector2Int(4, 0)));
                return level;
            }

            private static void PopulatePieceRect(
                object pieceDefinition,
                string pieceId,
                Vector2Int origin,
                params (int x, int y, int w, int h)[] rects)
            {
                PieceDefinitionType.GetField("pieceId").SetValue(pieceDefinition, pieceId);
                PieceDefinitionType.GetField("origin").SetValue(pieceDefinition, origin);
                IList cells = (IList)PieceDefinitionType.GetField("cells").GetValue(pieceDefinition);
                object blockType = Enum.Parse(PieceCellType, "Block");
                for (int r = 0; r < rects.Length; r++)
                {
                    var rect = rects[r];
                    for (int y = rect.y; y < rect.y + rect.h; y++)
                    for (int x = rect.x; x < rect.x + rect.w; x++)
                        cells.Add(Activator.CreateInstance(PieceCellDefinitionType, new Vector2Int(x, y), blockType));
                }
            }

            private static object CreateJoint(
                string jointId,
                string pieceA,
                Vector2Int blockA,
                string pieceB,
                Vector2Int blockB)
            {
                object joint = Activator.CreateInstance(JointDefinitionType);
                JointDefinitionType.GetField("jointId").SetValue(joint, jointId);
                object endpointA = Activator.CreateInstance(JointEndpointType);
                JointEndpointType.GetField("pieceId").SetValue(endpointA, pieceA);
                JointEndpointType.GetField("localBlockOrigin").SetValue(endpointA, blockA);
                object endpointB = Activator.CreateInstance(JointEndpointType);
                JointEndpointType.GetField("pieceId").SetValue(endpointB, pieceB);
                JointEndpointType.GetField("localBlockOrigin").SetValue(endpointB, blockB);
                JointDefinitionType.GetField("endpointA").SetValue(joint, endpointA);
                JointDefinitionType.GetField("endpointB").SetValue(joint, endpointB);
                return joint;
            }
        }
    }
}
