using System;
using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Gameplay.Reveal;
using NUnit.Framework;
using UnityEngine;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class ElevatorGravityTests
    {
        private sealed class DeferredPresentation : IRevealAreaPresentation
        {
            private Action pendingCompletion;

            public void SetBoxRemaining(int remaining, int targetCount)
            {
            }

            public void PlayOpening(RevealAreaKind kind, Action onCompleted)
            {
                pendingCompletion = onCompleted;
            }

            public void FinishOpening()
            {
                Action callback = pendingCompletion;
                pendingCompletion = null;
                callback?.Invoke();
            }
        }

        private static GravityLevelDefinition CreateLevelWithElevator(
            string areaId,
            int originX,
            int originY,
            int sizeX,
            int sizeY)
        {
            GravityLevelDefinition level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
            level.boardColumns = 4;
            level.boardRows = 10;
            level.subdivisions = 1;
            level.elevators.Add(new ElevatorRevealDefinition
            {
                areaId = areaId,
                bounds = new RevealAreaBounds
                {
                    origin = new Vector2Int(originX, originY),
                    size = new Vector2Int(sizeX, sizeY)
                }
            });
            return level;
        }

        private static PieceModel CreatePiece(int id, int anchorX, int anchorY)
        {
            return new PieceModel(
                id,
                new GridCoordinate(anchorX, anchorY),
                new GridCoordinate(0, 0),
                new List<GridCoordinate> { new GridCoordinate(0, 0) });
        }

        [Test]
        public void UnlockedElevator_AllowsPieceAboveCornerToFallThroughClearedBounds()
        {
            const string elevatorId = "elev-lower";
            GravityLevelDefinition level = CreateLevelWithElevator(elevatorId, 0, 1, 4, 4);
            GameObject boardGo = new GameObject("ElevatorTestBoard");
            try
            {
                PrototypeBoard board = boardGo.AddComponent<PrototypeBoard>();
                GravityBoardGrid grid = new GravityBoardGrid(4, 10);
                PieceModel cornerPiece = CreatePiece(1, 1, 5);
                Assert.That(grid.TryPlace(cornerPiece), Is.True);

                LevelBoardSnapshot snapshot = new LevelBoardSnapshot(
                    grid,
                    new List<PieceModel> { cornerPiece },
                    new List<LevelBoardSnapshotIssue>());
                board.InitializeBoardSnapshot(snapshot);

                RevealAreaCoordinator coordinator = new RevealAreaCoordinator();
                board.ConfigureRevealAreas(coordinator);
                coordinator.Initialize(board, level);

                // With no top-layer occupants and no deferred presentation,
                // the elevator unlocks synchronously and clears the board space.
                Assert.That(coordinator.HasPendingRevealContent, Is.False);
                bool moved = grid.TryGetFallTarget(
                    cornerPiece,
                    out GridCoordinate targetAnchor,
                    board.CanMovePieceDelegate);

                Assert.That(moved, Is.True);
                Assert.That(targetAnchor.Y, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        [Test]
        public void UnlockingElevator_BlocksExternalEntryUntilDoorFinish_ThenAllowsFallThrough()
        {
            const string elevatorId = "elev-opening";
            GravityLevelDefinition level = CreateLevelWithElevator(elevatorId, 0, 1, 4, 4);
            GameObject boardGo = new GameObject("ElevatorOpeningTestBoard");
            try
            {
                PrototypeBoard board = boardGo.AddComponent<PrototypeBoard>();
                GravityBoardGrid grid = new GravityBoardGrid(4, 10);
                PieceModel cornerPiece = CreatePiece(1, 2, 5);
                Assert.That(grid.TryPlace(cornerPiece), Is.True);

                LevelBoardSnapshot snapshot = new LevelBoardSnapshot(
                    grid,
                    new List<PieceModel> { cornerPiece },
                    new List<LevelBoardSnapshotIssue>());
                board.InitializeBoardSnapshot(snapshot);

                RevealAreaCoordinator coordinator = new RevealAreaCoordinator();
                DeferredPresentation presentation = new DeferredPresentation();
                coordinator.RegisterPresentation(elevatorId, presentation);
                board.ConfigureRevealAreas(coordinator);
                coordinator.Initialize(board, level);

                // While doors are still animating open (Unlocking), external piece waits at Y=5.
                Assert.That(coordinator.HasPendingRevealContent, Is.True);
                Assert.That(coordinator.CanMovePiece(cornerPiece, new GridCoordinate(2, 4)), Is.False);

                // Interior piece starting inside the elevator can still exit downward.
                PieceModel interiorPiece = CreatePiece(2, 2, 1);
                Assert.That(coordinator.CanMovePiece(interiorPiece, new GridCoordinate(2, 0)), Is.True);

                // Once the door animation completes (Unlocked), external piece falls cleanly to Y=0.
                presentation.FinishOpening();
                Assert.That(coordinator.HasPendingRevealContent, Is.False);
                bool moved = grid.TryGetFallTarget(
                    cornerPiece,
                    out GridCoordinate targetAnchor,
                    board.CanMovePieceDelegate);

                Assert.That(moved, Is.True);
                Assert.That(targetAnchor.Y, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }
    }
}
