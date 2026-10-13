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
            private const string CombinedLevelPath = "Assets/combineblock.asset";
            private static readonly Type BoardType = Type.GetType("GravityPuzzle.PrototypeBoard, Assembly-CSharp");
            private static readonly Type ControllerType = Type.GetType("GravityPuzzle.PuzzleDragController, Assembly-CSharp");
            private static readonly Type PieceType = Type.GetType("GravityPuzzle.PuzzlePiece, Assembly-CSharp");
            private static readonly Type GridType = Type.GetType("GravityPuzzle.Core.Grid.GravityBoardGrid, Assembly-CSharp");
            private static readonly Type SnapshotType = Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshot, Assembly-CSharp");
            private static readonly Type SnapshotBuilderType = Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshotBuilder, Assembly-CSharp");
            private static readonly Type PieceModelType = Type.GetType("GravityPuzzle.Gameplay.Pieces.PieceModel, Assembly-CSharp");
            private static readonly Type RuntimeType = Type.GetType("GravityPuzzle.GravityLevelRuntime, Assembly-CSharp");

            private readonly List<GameObject> objects = new List<GameObject>();
            private object level;
            private object board;
            private object controller;
            private object grid;
            private readonly Dictionary<int, object> models = new Dictionary<int, object>();
            private readonly Dictionary<int, Component> pieces = new Dictionary<int, Component>();
            private readonly Dictionary<int, Vector2> initialPositions = new Dictionary<int, Vector2>();
            private readonly Dictionary<int, Vector2Int> initialAnchors = new Dictionary<int, Vector2Int>();

            public void Create()
            {
                Assert.That(BoardType, Is.Not.Null);
                Assert.That(ControllerType, Is.Not.Null);
                Assert.That(PieceType, Is.Not.Null);
                Assert.That(GridType, Is.Not.Null);
                Assert.That(SnapshotType, Is.Not.Null);
                Assert.That(PieceModelType, Is.Not.Null);

                level = AssetDatabase.LoadAssetAtPath<ScriptableObject>(CombinedLevelPath);
                Assert.That(level, Is.Not.Null, "The regression fixture must use the authored combined-block level.");
                ConfigureRuntimeLevel(level);

                int fineColumns = (int)level.GetType().GetProperty("FineColumns").GetValue(level);
                int fineRows = (int)level.GetType().GetProperty("FineRows").GetValue(level);
                grid = Activator.CreateInstance(GridType, fineColumns, fineRows);

                object authoredSnapshot = SnapshotBuilderType.GetMethod("Build").Invoke(null, new[] { level });
                IList runtimeModels = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(PieceModelType));
                AddModel(runtimeModels, authoredSnapshot, 5, new Vector2Int(4, 4));
                AddModel(runtimeModels, authoredSnapshot, 6, new Vector2Int(4, 8));
                AddModel(runtimeModels, authoredSnapshot, 8, new Vector2Int(24, 24));
                object issues = Activator.CreateInstance(typeof(List<>).MakeGenericType(
                    Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshotIssue, Assembly-CSharp")));
                object snapshot = Activator.CreateInstance(SnapshotType, grid, runtimeModels, issues);

                GameObject boardObject = new GameObject("Drag Test Board");
                objects.Add(boardObject);
                board = boardObject.AddComponent(BoardType);
                Invoke(board, "InitializeBoardSnapshot", snapshot);
                Invoke(board, "ConfigurePieceJoints", level);

                GameObject controllerObject = new GameObject("Drag Test Controller");
                objects.Add(controllerObject);
                controller = controllerObject.AddComponent(ControllerType);

                CreatePiece(5);
                CreatePiece(6);
                CreatePiece(8);
                Physics2D.SyncTransforms();
            }

            public void DragByFineCells(int deltaX, int deltaY, bool combined)
            {
                DragByWorld(deltaX / 4f, deltaY / 4f, combined);
            }

            public void DragByWorld(float deltaX, float deltaY, bool combined)
            {
                int id = combined ? 5 : 8;
                Component piece = pieces[id];
                Rigidbody2D body = piece.GetComponent<Rigidbody2D>();
                Vector2 pointer = body.position;
                Invoke(controller, "TrySelectPiece", pointer, .01f);
                Invoke(controller, "UpdateDragTarget", pointer + new Vector2(deltaX, deltaY));
                Invoke(controller, "TryMoveSelectedPieceOnGrid", piece);
            }

            public void ReleaseCurrentDrag()
            {
                Invoke(controller, "ReleasePiece");
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

            public void BlockNormalPieceAtHorizontalOffset(int offset)
            {
                Vector2Int anchor = initialAnchors[8];
                Vector2Int local = FirstLocalCell(8);
                Invoke(grid, "SetBlocked", Coordinate(anchor.x + offset + local.x, anchor.y + local.y));
            }

            public void BlockEveryVerticalCandidateForNormalPiece(int horizontalDistance)
            {
                Vector2Int anchor = initialAnchors[8];
                Vector2Int local = FirstLocalCell(8);
                for (int x = 0; x <= horizontalDistance; x++)
                    Invoke(grid, "SetBlocked", Coordinate(anchor.x + x + local.x, anchor.y + local.y + 1));
            }

            public void Dispose()
            {
                for (int index = objects.Count - 1; index >= 0; index--)
                {
                    if (objects[index] != null)
                        UnityEngine.Object.DestroyImmediate(objects[index]);
                }
                objects.Clear();
                ResetRuntimeLevel();
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

            private void CreatePiece(int id)
            {
                GameObject root = new GameObject($"Drag Test Piece {id}");
                objects.Add(root);
                root.AddComponent<BoxCollider2D>().size = Vector2.one;
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
        }
    }
}
