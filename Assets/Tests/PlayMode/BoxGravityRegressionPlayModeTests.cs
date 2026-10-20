using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GravityPuzzle.Tests.PlayMode
{
    public sealed class BoxGravityRegressionPlayModeTests
    {
        private static readonly Type GridType = Type.GetType(
            "GravityPuzzle.Core.Grid.GravityBoardGrid, Assembly-CSharp");
        private static readonly Type CoordinateType = Type.GetType(
            "GravityPuzzle.Core.Grid.GridCoordinate, Assembly-CSharp");
        private static readonly Type PieceModelType = Type.GetType(
            "GravityPuzzle.Gameplay.Pieces.PieceModel, Assembly-CSharp");
        private static readonly Type CoordinatorType = Type.GetType(
            "GravityPuzzle.Gameplay.Reveal.RevealAreaCoordinator, Assembly-CSharp");
        private static readonly Type BoardType = Type.GetType(
            "GravityPuzzle.PrototypeBoard, Assembly-CSharp");
        private static readonly Type LevelType = Type.GetType(
            "GravityPuzzle.GravityLevelDefinition, Assembly-CSharp");
        private static readonly Type BoxDefinitionType = Type.GetType(
            "GravityPuzzle.BoxRevealDefinition, Assembly-CSharp");
        private static readonly Type BoundsType = Type.GetType(
            "GravityPuzzle.RevealAreaBounds, Assembly-CSharp");

        private object CreateCoordinate(int x, int y)
        {
            return Activator.CreateInstance(CoordinateType, x, y);
        }

        private object CreateGrid(int columns, int rows)
        {
            return Activator.CreateInstance(GridType, columns, rows);
        }

        private object CreatePieceModel(int id, int anchorX, int anchorY, int width, int height)
        {
            object anchor = CreateCoordinate(anchorX, anchorY);
            object pivotOffset = CreateCoordinate(0, 0);

            Type coordListType = typeof(List<>).MakeGenericType(CoordinateType);
            IList localCells = (IList)Activator.CreateInstance(coordListType);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    localCells.Add(CreateCoordinate(x, y));
                }
            }

            return Activator.CreateInstance(PieceModelType, id, anchor, pivotOffset, localCells);
        }

        private object CreateLevelWithBox(int targetShredCount, int originX, int originY, int sizeX, int sizeY)
        {
            ScriptableObject level = ScriptableObject.CreateInstance(LevelType);
            FieldInfo boxesField = LevelType.GetField("boxes");
            IList boxList = (IList)boxesField.GetValue(level);

            object box = Activator.CreateInstance(BoxDefinitionType);
            BoxDefinitionType.GetField("areaId").SetValue(box, Guid.NewGuid().ToString());
            BoxDefinitionType.GetField("targetShredCount").SetValue(box, targetShredCount);

            object bounds = BoxDefinitionType.GetField("bounds").GetValue(box);
            BoundsType.GetField("origin").SetValue(bounds, new Vector2Int(originX, originY));
            BoundsType.GetField("size").SetValue(bounds, new Vector2Int(sizeX, sizeY));
            BoxDefinitionType.GetField("bounds").SetValue(box, bounds);

            boxList.Add(box);
            return level;
        }

        [Test]
        public void PrototypeBoard_CanMovePieceDelegate_IsCachedAndNonAllocating()
        {
            GameObject go = new GameObject("BoardTestObj");
            try
            {
                Component board = go.AddComponent(BoardType);
                PropertyInfo delegateProp = BoardType.GetProperty("CanMovePieceDelegate");
                Assert.That(delegateProp, Is.Not.Null);

                object first = delegateProp.GetValue(board);
                object second = delegateProp.GetValue(board);

                Assert.That(first, Is.Not.Null);
                Assert.That(ReferenceEquals(first, second), Is.True, "CanMovePieceDelegate must return the exact same cached delegate instance.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LockedBox_ActsAsSolidObstacle_PieceStopsOnTopOfBox()
        {
            // Grid 10x10. Box from X:[2..5], Y:[2..5]. Piece at X:2, Y:8 (size 2x2).
            // Below piece: box is at Y:[2..5]. Top of box is Y=6.
            // Piece height is 2, so its anchor should stop at Y=6.
            object grid = CreateGrid(10, 10);
            object piece = CreatePieceModel(1, 2, 8, 2, 2);

            GameObject boardGo = new GameObject("BoardGo");
            ScriptableObject level = (ScriptableObject)CreateLevelWithBox(2, 2, 2, 4, 4);
            try
            {
                Component board = boardGo.AddComponent(BoardType);
                object coordinator = Activator.CreateInstance(CoordinatorType);
                CoordinatorType.GetMethod("Initialize").Invoke(coordinator, new object[] { board, level });

                BoardType.GetMethod("ConfigureRevealAreas").Invoke(board, new object[] { coordinator });
                Delegate canMoveDelegate = (Delegate)BoardType.GetProperty("CanMovePieceDelegate").GetValue(board);

                MethodInfo tryGetFallTarget = GridType.GetMethod("TryGetFallTarget", new Type[] { PieceModelType, CoordinateType.MakeByRefType(), canMoveDelegate.GetType() });
                Assert.That(tryGetFallTarget, Is.Not.Null);

                object[] parameters = new object[] { piece, null, canMoveDelegate };
                bool moved = (bool)tryGetFallTarget.Invoke(grid, parameters);

                Assert.That(moved, Is.True);
                object targetAnchor = parameters[1];
                int targetY = (int)CoordinateType.GetProperty("Y").GetValue(targetAnchor);
                Assert.That(targetY, Is.EqualTo(6), "Piece must stop on top of the closed box (Y=6), not fall through it.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        [Test]
        public void MidAirClosedBox_PreventsTunnelingThroughEmptySpaceBelowBox()
        {
            // Closed box at Y:[3..5]. Space at Y:[0..2] is completely empty!
            // Piece at Y:7 (size 1x1).
            // Tunneling bug check: Piece must stop at Y=6 (on top of box), NOT tunnel through the box to Y=0.
            object grid = CreateGrid(8, 10);
            object piece = CreatePieceModel(1, 3, 7, 1, 1);

            GameObject boardGo = new GameObject("BoardGo");
            ScriptableObject level = (ScriptableObject)CreateLevelWithBox(2, 2, 3, 3, 3);
            try
            {
                Component board = boardGo.AddComponent(BoardType);
                object coordinator = Activator.CreateInstance(CoordinatorType);
                CoordinatorType.GetMethod("Initialize").Invoke(coordinator, new object[] { board, level });

                BoardType.GetMethod("ConfigureRevealAreas").Invoke(board, new object[] { coordinator });
                Delegate canMoveDelegate = (Delegate)BoardType.GetProperty("CanMovePieceDelegate").GetValue(board);

                MethodInfo tryGetFallTarget = GridType.GetMethod("TryGetFallTarget", new Type[] { PieceModelType, CoordinateType.MakeByRefType(), canMoveDelegate.GetType() });
                object[] parameters = new object[] { piece, null, canMoveDelegate };
                bool moved = (bool)tryGetFallTarget.Invoke(grid, parameters);

                Assert.That(moved, Is.True);
                object targetAnchor = parameters[1];
                int targetY = (int)CoordinateType.GetProperty("Y").GetValue(targetAnchor);
                Assert.That(targetY, Is.EqualTo(6), "Piece must stop at Y=6 and not tunnel through the mid-air closed box to Y=0.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        [Test]
        public void PiecesAuthoredInsideClosedBox_CannotMoveWhileLocked()
        {
            // Piece is at X:3, Y:3, completely inside box at X:[2..5], Y:[2..5].
            object grid = CreateGrid(10, 10);
            object piece = CreatePieceModel(1, 3, 3, 1, 1);

            GameObject boardGo = new GameObject("BoardGo");
            ScriptableObject level = (ScriptableObject)CreateLevelWithBox(2, 2, 2, 4, 4);
            try
            {
                Component board = boardGo.AddComponent(BoardType);
                object coordinator = Activator.CreateInstance(CoordinatorType);
                CoordinatorType.GetMethod("Initialize").Invoke(coordinator, new object[] { board, level });

                BoardType.GetMethod("ConfigureRevealAreas").Invoke(board, new object[] { coordinator });
                Delegate canMoveDelegate = (Delegate)BoardType.GetProperty("CanMovePieceDelegate").GetValue(board);

                MethodInfo tryGetFallTarget = GridType.GetMethod("TryGetFallTarget", new Type[] { PieceModelType, CoordinateType.MakeByRefType(), canMoveDelegate.GetType() });
                object[] parameters = new object[] { piece, null, canMoveDelegate };
                bool moved = (bool)tryGetFallTarget.Invoke(grid, parameters);

                Assert.That(moved, Is.False, "A piece covered by a locked box must not plan any gravity move.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        [Test]
        public void IndependentPieces_OneBlockedByBox_OtherCanFallFreely()
        {
            // Piece 1 is directly above the closed box at X:2, Y:8.
            // Piece 2 is in an open column at X:8, Y:8.
            object grid = CreateGrid(10, 10);
            object pieceAboveBox = CreatePieceModel(1, 2, 8, 2, 2);
            object pieceInFreeCol = CreatePieceModel(2, 8, 8, 1, 1);

            GameObject boardGo = new GameObject("BoardGo");
            ScriptableObject level = (ScriptableObject)CreateLevelWithBox(2, 2, 2, 4, 4);
            try
            {
                Component board = boardGo.AddComponent(BoardType);
                object coordinator = Activator.CreateInstance(CoordinatorType);
                CoordinatorType.GetMethod("Initialize").Invoke(coordinator, new object[] { board, level });

                BoardType.GetMethod("ConfigureRevealAreas").Invoke(board, new object[] { coordinator });
                Delegate canMoveDelegate = (Delegate)BoardType.GetProperty("CanMovePieceDelegate").GetValue(board);

                MethodInfo tryGetFallTarget = GridType.GetMethod("TryGetFallTarget", new Type[] { PieceModelType, CoordinateType.MakeByRefType(), canMoveDelegate.GetType() });

                // Piece above box stops at Y=6
                object[] p1Params = new object[] { pieceAboveBox, null, canMoveDelegate };
                bool p1Moved = (bool)tryGetFallTarget.Invoke(grid, p1Params);
                Assert.That(p1Moved, Is.True);
                int p1TargetY = (int)CoordinateType.GetProperty("Y").GetValue(p1Params[1]);
                Assert.That(p1TargetY, Is.EqualTo(6));

                // Piece in free column falls all the way to floor Y=0
                object[] p2Params = new object[] { pieceInFreeCol, null, canMoveDelegate };
                bool p2Moved = (bool)tryGetFallTarget.Invoke(grid, p2Params);
                Assert.That(p2Moved, Is.True);
                int p2TargetY = (int)CoordinateType.GetProperty("Y").GetValue(p2Params[1]);
                Assert.That(p2TargetY, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        [Test]
        public void JointGroup_StopsCleanlyAboveBoxBoundary()
        {
            // Group of 2 pieces joined: member 1 at X:2, Y:8, member 2 at X:3, Y:8.
            // Box is at X:[1..5], Y:[2..5].
            object grid = CreateGrid(10, 10);
            object piece1 = CreatePieceModel(1, 2, 8, 1, 1);
            object piece2 = CreatePieceModel(2, 3, 8, 1, 1);

            Type pieceListType = typeof(List<>).MakeGenericType(PieceModelType);
            IList pieces = (IList)Activator.CreateInstance(pieceListType);
            pieces.Add(piece1);
            pieces.Add(piece2);

            Type coordListType = typeof(List<>).MakeGenericType(CoordinateType);
            IList targets = (IList)Activator.CreateInstance(coordListType);

            GameObject boardGo = new GameObject("BoardGo");
            ScriptableObject level = (ScriptableObject)CreateLevelWithBox(2, 1, 2, 5, 4);
            try
            {
                Component board = boardGo.AddComponent(BoardType);
                object coordinator = Activator.CreateInstance(CoordinatorType);
                CoordinatorType.GetMethod("Initialize").Invoke(coordinator, new object[] { board, level });

                BoardType.GetMethod("ConfigureRevealAreas").Invoke(board, new object[] { coordinator });
                Delegate canMoveDelegate = (Delegate)BoardType.GetProperty("CanMovePieceDelegate").GetValue(board);

                MethodInfo tryGetGroupFallTarget = GridType.GetMethod("TryGetGroupFallTarget", new Type[] { typeof(IReadOnlyList<>).MakeGenericType(PieceModelType), typeof(List<>).MakeGenericType(CoordinateType), canMoveDelegate.GetType() });
                object[] parameters = new object[] { pieces, targets, canMoveDelegate };
                bool moved = (bool)tryGetGroupFallTarget.Invoke(grid, parameters);

                Assert.That(moved, Is.True);
                int t1Y = (int)CoordinateType.GetProperty("Y").GetValue(targets[0]);
                int t2Y = (int)CoordinateType.GetProperty("Y").GetValue(targets[1]);
                Assert.That(t1Y, Is.EqualTo(6), "Member 1 must stop on top of box (Y=6).");
                Assert.That(t2Y, Is.EqualTo(6), "Member 2 must stop on top of box (Y=6).");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        [Test]
        public void GridGravityPlanner_WhenLowerPieceBlockedByClosedBox_PlansHigherFreePieceFirst()
        {
            Type snapshotType = Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshot, Assembly-CSharp");
            Type plannerType = Type.GetType("GravityPuzzle.Gameplay.Gravity.GridGravityPlanner, Assembly-CSharp");
            Type moveType = Type.GetType("GravityPuzzle.Gameplay.Gravity.GridGravityMove, Assembly-CSharp");
            Type issueListType = typeof(List<>).MakeGenericType(Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshotIssue, Assembly-CSharp"));

            object grid = CreateGrid(10, 10);
            // Lower piece 1 at X:2, Y:6 directly on top of box at X:[2..5], Y:[2..5].
            // It cannot fall any further.
            object pieceBlocked = CreatePieceModel(1, 2, 6, 2, 2);
            // Higher piece 2 at X:8, Y:8 in free column. It can fall to Y=0.
            object pieceFree = CreatePieceModel(2, 8, 8, 1, 1);

            Type pieceListType = typeof(List<>).MakeGenericType(PieceModelType);
            IList pieces = (IList)Activator.CreateInstance(pieceListType);
            pieces.Add(pieceBlocked);
            pieces.Add(pieceFree);

            object issues = Activator.CreateInstance(issueListType);
            object snapshot = Activator.CreateInstance(snapshotType, grid, pieces, issues);

            GameObject boardGo = new GameObject("BoardGo");
            ScriptableObject level = (ScriptableObject)CreateLevelWithBox(2, 2, 2, 4, 4);
            try
            {
                Component board = boardGo.AddComponent(BoardType);
                object coordinator = Activator.CreateInstance(CoordinatorType);
                CoordinatorType.GetMethod("Initialize").Invoke(coordinator, new object[] { board, level });

                BoardType.GetMethod("ConfigureRevealAreas").Invoke(board, new object[] { coordinator });
                Delegate canMoveDelegate = (Delegate)BoardType.GetProperty("CanMovePieceDelegate").GetValue(board);

                MethodInfo tryPlan = plannerType.GetMethod("TryPlanNextMove", new Type[] { snapshotType, typeof(Predicate<>).MakeGenericType(PieceModelType), canMoveDelegate.GetType(), moveType.MakeByRefType() });
                Assert.That(tryPlan, Is.Not.Null);

                object[] parameters = new object[] { snapshot, null, canMoveDelegate, null };
                bool found = (bool)tryPlan.Invoke(null, parameters);

                Assert.That(found, Is.True, "Planner must find a legal move for the free piece.");
                object move = parameters[3];
                int pieceId = (int)moveType.GetProperty("PieceId").GetValue(move);
                Assert.That(pieceId, Is.EqualTo(2), "Planner must select the free piece (ID=2), skipping the piece blocked by the closed box.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }
    }
}
