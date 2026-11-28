using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GravityPuzzle.Tests.PlayMode
{
    public sealed class ConcurrentDragAndGravityPlayModeTests
    {
        private static readonly Type BootstrapType = Type.GetType("GravityPuzzle.PrototypeBootstrap, Assembly-CSharp");
        private static readonly Type LevelType = Type.GetType("GravityPuzzle.GravityLevelDefinition, Assembly-CSharp");
        private static readonly Type RuntimeType = Type.GetType("GravityPuzzle.GravityLevelRuntime, Assembly-CSharp");
        private static readonly Type BoardType = Type.GetType("GravityPuzzle.PrototypeBoard, Assembly-CSharp");
        private static readonly Type DragControllerType = Type.GetType("GravityPuzzle.PuzzleDragController, Assembly-CSharp");
        private static readonly Type PieceType = Type.GetType("GravityPuzzle.PuzzlePiece, Assembly-CSharp");
        private static readonly Type PieceModelType = Type.GetType("GravityPuzzle.Gameplay.Pieces.PieceModel, Assembly-CSharp");
        private static readonly Type PieceStateType = Type.GetType("GravityPuzzle.Gameplay.Pieces.PieceState, Assembly-CSharp");
        private static readonly Type GridType = Type.GetType("GravityPuzzle.Core.Grid.GravityBoardGrid, Assembly-CSharp");
        private static readonly Type SnapshotType = Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshot, Assembly-CSharp");
        private static readonly Type SnapshotIssueType = Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshotIssue, Assembly-CSharp");
        private static readonly Type CoordinateType = Type.GetType("GravityPuzzle.Core.Grid.GridCoordinate, Assembly-CSharp");
        private static readonly Type CoordsType = Type.GetType("GravityPuzzle.Core.Grid.GravityLevelGridCoordinates, Assembly-CSharp");
        private static readonly Type FallViewType = Type.GetType("GravityPuzzle.Presentation.Views.PieceGridFallView, Assembly-CSharp");
        private static readonly FieldInfo CoordinateXField = CoordinateType?.GetField("X");
        private static readonly FieldInfo CoordinateYField = CoordinateType?.GetField("Y");

        [UnityTest]
        public IEnumerator PullingLowerPieceHorizontallyOutFromUnderUpper_StartsUpperFallImmediatelyDuringDrag()
        {
            ConcurrentDragFixture fixture = new ConcurrentDragFixture();
            try
            {
                fixture.Create(columns: 6, rows: 6, subdivisions: 4);
                var bottom = fixture.AddBlock(1, anchorX: 4, anchorY: 0, widthCells: 4, heightCells: 4);
                var top = fixture.AddBlock(2, anchorX: 4, anchorY: 4, widthCells: 4, heightCells: 4);
                fixture.InitializeBoard();

                yield return null;

                fixture.BeginDrag(bottom.piece);

                // Move 3 fine cells right: 1 cell still supports top.
                fixture.MoveSelectedAndRunUpdate(0.75f, 0f);
                Assert.That(fixture.GetAnchor(top.model).y, Is.EqualTo(4));
                Assert.That(fixture.GetStateName(top.model), Is.EqualTo("Placed"));

                // Move 1 more fine cell right to X=8: support is gone, top must fall immediately without releasing drag.
                fixture.MoveSelectedAndRunUpdate(0.25f, 0f);

                Assert.That(fixture.SelectedPiece, Is.EqualTo(bottom.piece), "Lower piece must stay actively dragged.");
                Assert.That(fixture.GetStateName(bottom.model), Is.EqualTo("Dragging"));
                Assert.That(fixture.GetAnchor(top.model), Is.EqualTo(new Vector2Int(4, 0)),
                    "Upper piece must immediately commit gravity target without waiting for drag release.");
                Assert.That(fixture.GetStateName(top.model), Is.EqualTo("Falling"));
                Assert.That(fixture.IsFallAnimating(top.piece), Is.True,
                    "Upper piece fall tween must start immediately while lower piece drag is still active.");

                yield return new WaitForSeconds(0.25f);
                Assert.That(fixture.GetStateName(top.model), Is.EqualTo("Placed"));
                Assert.That(fixture.SelectedPiece, Is.EqualTo(bottom.piece), "Drag must still be active after upper piece lands.");

                fixture.ReleaseDrag();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator PullingLowerPieceDownward_KeepsUpperCanonicalUntilFullCoarseRowOpens()
        {
            ConcurrentDragFixture fixture = new ConcurrentDragFixture();
            try
            {
                fixture.Create(columns: 4, rows: 6, subdivisions: 4);
                var bottom = fixture.AddBlock(1, anchorX: 4, anchorY: 4, widthCells: 4, heightCells: 4);
                var top = fixture.AddBlock(2, anchorX: 4, anchorY: 8, widthCells: 4, heightCells: 4);
                fixture.InitializeBoard();

                // Begin drag immediately before initial FixedUpdate drops the stack so bottom is held at Y=4.
                fixture.BeginDrag(bottom.piece);
                yield return new WaitForFixedUpdate();
                Assert.That(fixture.GetAnchor(top.model).y, Is.EqualTo(8));

                // Pull down 2 fine cells (half block): top must remain canonical at Y=8.
                fixture.MoveSelectedAndRunUpdate(0f, -0.50f);
                Assert.That(fixture.GetAnchor(bottom.model).y, Is.EqualTo(2));
                Assert.That(fixture.GetAnchor(top.model).y, Is.EqualTo(8),
                    "Upper piece must stay aligned to canonical mod-4 rows while held support is at half-block Y.");

                // Pull down 2 more fine cells to Y=0: full coarse row opens, top immediately falls to Y=4.
                fixture.MoveSelectedAndRunUpdate(0f, -0.50f);
                Assert.That(fixture.GetAnchor(bottom.model).y, Is.EqualTo(0));
                Assert.That(fixture.GetAnchor(top.model).y, Is.EqualTo(4),
                    "Upper piece must fall to canonical Y=4 as soon as held piece vacates full coarse row.");
                Assert.That(fixture.GetStateName(top.model), Is.EqualTo("Falling"));

                fixture.ReleaseDrag();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator DraggingPieceBackIntoActivelyFallingSweep_BlocksPassThroughUntilFallingPieceClearsRow()
        {
            ConcurrentDragFixture fixture = new ConcurrentDragFixture();
            try
            {
                fixture.Create(columns: 6, rows: 8, subdivisions: 4);
                var bottom = fixture.AddBlock(1, anchorX: 4, anchorY: 12, widthCells: 4, heightCells: 4);
                var top = fixture.AddBlock(2, anchorX: 4, anchorY: 16, widthCells: 4, heightCells: 4);
                fixture.InitializeBoard();

                // Hold bottom at Y=12 and pull out to X=8 so top falls 16 -> 0 (4.0 world units).
                fixture.BeginDrag(bottom.piece);
                fixture.MoveSelectedAndRunUpdate(1.0f, 0f);
                Assert.That(fixture.GetAnchor(bottom.model), Is.EqualTo(new Vector2Int(8, 12)));
                Assert.That(fixture.GetAnchor(top.model), Is.EqualTo(new Vector2Int(4, 0)));
                Assert.That(fixture.IsFallAnimating(top.piece), Is.True);

                // While top is still visually above Y=12, trying to drag bottom back into X=4, Y=12 must be blocked.
                yield return null;
                Assert.That(((Component)top.piece).transform.position.y, Is.GreaterThan(fixture.FineRowWorldY(8)));
                fixture.MoveSelectedAndRunUpdate(-1.0f, 0f);
                Assert.That(fixture.GetAnchor(bottom.model).x, Is.EqualTo(8),
                    "Dragged piece must not enter cells still in an actively falling piece's visual sweep.");

                // Wait until top has visually fallen clear of Y=12..15 (landed at Y=0).
                yield return new WaitForSeconds(0.35f);
                Assert.That(fixture.GetStateName(top.model), Is.EqualTo("Placed"));

                // Now X=4, Y=12 is clear above top: bottom must slide cleanly back to (4, 12).
                fixture.MoveSelectedAndRunUpdate(-1.0f, 0f);
                Assert.That(fixture.GetAnchor(bottom.model), Is.EqualTo(new Vector2Int(4, 12)));

                fixture.ReleaseDrag();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator DisjointColumnPieceFallsImmediately_EvenWhileAnotherColumnIsFalling()
        {
            ConcurrentDragFixture fixture = new ConcurrentDragFixture();
            try
            {
                fixture.Create(columns: 6, rows: 8, subdivisions: 4);
                var leftFalling = fixture.AddBlock(1, anchorX: 0, anchorY: 20, widthCells: 4, heightCells: 4);
                var wideSupport = fixture.AddBlock(2, anchorX: 8, anchorY: 0, widthCells: 4, heightCells: 4);
                var rightStacked = fixture.AddBlock(3, anchorX: 8, anchorY: 4, widthCells: 4, heightCells: 4);
                fixture.InitializeBoard();

                // Grab support first so leftFalling starts falling on FixedUpdate while support is held.
                fixture.BeginDrag(wideSupport.piece);
                yield return new WaitForFixedUpdate();
                Assert.That(fixture.GetStateName(leftFalling.model), Is.EqualTo("Falling"));
                Assert.That(fixture.IsFallAnimating(leftFalling.piece), Is.True);

                // Drag right support away while leftFalling is still mid-fall: rightStacked must start falling immediately!
                fixture.MoveSelectedAndRunUpdate(1.0f, 0f);
                Assert.That(fixture.GetAnchor(wideSupport.model), Is.EqualTo(new Vector2Int(12, 0)));
                Assert.That(fixture.GetAnchor(rightStacked.model), Is.EqualTo(new Vector2Int(8, 0)));
                Assert.That(fixture.IsFallAnimating(rightStacked.piece), Is.True,
                    "Disjoint column must start falling immediately without waiting for another column's fall to complete.");

                fixture.ReleaseDrag();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator SubCellDragRemainderLeaningIntoNewlyOccupiedCell_IsClampedOnGravityCommit()
        {
            ConcurrentDragFixture fixture = new ConcurrentDragFixture();
            try
            {
                // Orthogonal sub-cell lean into column 7 when top falls into (4, 0).
                fixture.Create(columns: 6, rows: 6, subdivisions: 4);
                var bottom = fixture.AddBlock(1, anchorX: 4, anchorY: 0, widthCells: 4, heightCells: 4);
                var top = fixture.AddBlock(2, anchorX: 4, anchorY: 4, widthCells: 4, heightCells: 4);
                fixture.InitializeBoard();

                fixture.BeginDrag(bottom.piece);
                // Move +4 fine cells right to X=8, then nudge -0.075u (-0.3 fine cells) back toward X=7
                // via MoveSelectedOnly (before Update flushes gravity) so FixedUpdate/Update sees negative remainder.
                fixture.MoveSelectedWithoutGravityFlush(1.0f, 0f);
                fixture.MoveSelectedWithoutGravityFlush(-0.075f, 0f);
                Assert.That(fixture.GetAnchor(bottom.model), Is.EqualTo(new Vector2Int(8, 0)));

                yield return new WaitForFixedUpdate();

                Assert.That(fixture.GetAnchor(top.model), Is.EqualTo(new Vector2Int(4, 0)));
                Assert.That(fixture.FineDragRemainder.x, Is.EqualTo(0f).Within(0.0001f),
                    "Sub-cell overhang leaning into newly-occupied adjacent cell must clamp to zero.");
                Assert.That(
                    Mathf.Abs(((Component)bottom.piece).transform.position.x - fixture.FineCellWorld(8, 0).x),
                    Is.LessThan(0.0001f));
                fixture.ReleaseDrag();
            }
            finally
            {
                fixture.Dispose();
            }

            // Diagonal-only corner lean from (4, 0) toward empty (3, 4) when fallingCorner drops from (0, 8) to (0, 4)
            // onto a narrower support at X=0..1, leaving orthogonal (3, 0..3) and (4..7, 4) clear.
            ConcurrentDragFixture cornerFixture = new ConcurrentDragFixture();
            try
            {
                cornerFixture.Create(columns: 6, rows: 6, subdivisions: 4);
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 2; x++)
                    cornerFixture.SetBlockedCell(x, y);

                var held = cornerFixture.AddBlock(1, anchorX: 4, anchorY: 0, widthCells: 4, heightCells: 4);
                var fallingCorner = cornerFixture.AddBlock(2, anchorX: 0, anchorY: 8, widthCells: 4, heightCells: 4);
                cornerFixture.InitializeBoard();

                cornerFixture.BeginDrag(held.piece);
                // Lean held diagonally (-0.2, +0.3) fine cells toward empty (3, 4) before FixedUpdate drops fallingCorner.
                cornerFixture.MoveSelectedWithoutGravityFlush(-0.05f, 0.075f);
                Assert.That(cornerFixture.FineDragRemainder.x, Is.EqualTo(-0.2f).Within(0.01f));
                Assert.That(cornerFixture.FineDragRemainder.y, Is.EqualTo(0.3f).Within(0.01f));

                yield return new WaitForFixedUpdate();

                // fallingCorner commits to (0, 4), occupying diagonal corner (3, 4) while (3, 0..3) and (4..7, 4) remain empty.
                Assert.That(cornerFixture.GetAnchor(fallingCorner.model), Is.EqualTo(new Vector2Int(0, 4)));
                Assert.That(cornerFixture.FineDragRemainder.x, Is.EqualTo(0f).Within(0.0001f),
                    "Smaller diagonal lean axis (x=-0.2) must clamp to zero on diagonal-only corner block.");
                Assert.That(cornerFixture.FineDragRemainder.y, Is.EqualTo(0.3f).Within(0.01f),
                    "Orthogonally clear axis (y=+0.3) must remain preserved.");

                cornerFixture.ReleaseDrag();
            }
            finally
            {
                cornerFixture.Dispose();
            }
        }

        private sealed class ConcurrentDragFixture : IDisposable
        {
            private readonly List<UnityEngine.Object> cleanupObjects = new List<UnityEngine.Object>();
            private readonly List<object> models = new List<object>();
            private ScriptableObject level;
            private object grid;
            private object board;
            private object controller;
            private ScriptableObject tweenConfig;
            private Vector2 dragPointer;
            private int subdivisions = 4;

            public Component SelectedPiece
            {
                get
                {
                    FieldInfo f = DragControllerType.GetField("selectedPiece", BindingFlags.Instance | BindingFlags.NonPublic);
                    return f != null ? f.GetValue(controller) as Component : null;
                }
            }

            public Vector2 FineDragRemainder
            {
                get
                {
                    FieldInfo f = DragControllerType.GetField("fineDragRemainder", BindingFlags.Instance | BindingFlags.NonPublic);
                    return f != null ? (Vector2)f.GetValue(controller) : Vector2.zero;
                }
            }

            public void Create(int columns, int rows, int subdivisions)
            {
                this.subdivisions = subdivisions;
                level = Track(ScriptableObject.CreateInstance(LevelType));
                LevelType.GetField("boardColumns").SetValue(level, columns);
                LevelType.GetField("boardRows").SetValue(level, rows);
                LevelType.GetField("subdivisions").SetValue(level, subdivisions);

                RuntimeType.GetMethod("ResetLevelSequence", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
                Array levels = Array.CreateInstance(LevelType, 1);
                levels.SetValue(level, 0);
                RuntimeType.GetField("levels", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, levels);
                RuntimeType.GetField("currentLevelIndex", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 0);
                RuntimeType.GetField("levelSequenceInitialized", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);

                tweenConfig = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/TweenConfig.asset");
                Assert.That(tweenConfig, Is.Not.Null, "Expected Assets/TweenConfig.asset to exist.");

                grid = Activator.CreateInstance(GridType, columns * subdivisions, rows * subdivisions);

                GameObject camGo = Track(new GameObject("ConcurrentDragTestCamera"));
                Camera cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.transform.position = new Vector3(0f, 0f, -10f);
                BootstrapType.GetMethod("ConfigureSceneCamera", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    ?.Invoke(null, new object[] { cam });

                GameObject boardGo = Track(new GameObject("ConcurrentDragTestBoard"));
                board = boardGo.AddComponent(BoardType);
                controller = boardGo.AddComponent(DragControllerType);
            }

            public void SetBlockedCell(int x, int y)
            {
                GridType.GetMethod("SetBlocked").Invoke(grid, new[] { Coord(x, y) });
            }

            public (object model, Component piece) AddBlock(int id, int anchorX, int anchorY, int widthCells, int heightCells)
            {
                IList cells = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(CoordinateType));
                for (int y = 0; y < heightCells; y++)
                for (int x = 0; x < widthCells; x++)
                    cells.Add(Coord(x, y));

                object model = Activator.CreateInstance(PieceModelType, id, Coord(anchorX, anchorY), Coord(0, 0), cells);
                Assert.That((bool)GridType.GetMethod("TryPlace", new[] { PieceModelType }).Invoke(grid, new[] { model }), Is.True);
                models.Add(model);

                GameObject go = Track(new GameObject($"ConcurrentBlock_{id}"));
                go.SetActive(false);
                float fineSize = 1f / subdivisions;
                List<BoxCollider2D> colliders = new List<BoxCollider2D>();
                List<SpriteRenderer> visuals = new List<SpriteRenderer>();
                for (int y = 0; y < heightCells; y += subdivisions)
                for (int x = 0; x < widthCells; x += subdivisions)
                {
                    GameObject cGo = Track(new GameObject($"Cell_{x}_{y}"));
                    cGo.transform.SetParent(go.transform, false);
                    cGo.transform.localPosition = new Vector3(
                        (x + subdivisions * 0.5f - 0.5f) * fineSize,
                        (y + subdivisions * 0.5f - 0.5f) * fineSize,
                        0f);
                    BoxCollider2D col = cGo.AddComponent<BoxCollider2D>();
                    col.size = new Vector2(subdivisions * fineSize, subdivisions * fineSize);
                    colliders.Add(col);
                }

                object fallView = go.AddComponent(FallViewType);
                FallViewType.GetField("tweenConfig", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(fallView, tweenConfig);

                Component piece = go.AddComponent(PieceType);
                PieceType.GetMethod("ConfigureSourcePieceId").Invoke(piece, new object[] { id });
                PieceType.GetMethod(
                    "ConfigureCollisionGeometry",
                    new[] { typeof(CompositeCollider2D), typeof(List<BoxCollider2D>), typeof(List<SpriteRenderer>) })
                    .Invoke(piece, new object[] { null, colliders, visuals });

                Vector2 startPos = FineCellWorld(anchorX, anchorY);
                go.transform.position = startPos;
                go.SetActive(true);
                PieceType.GetMethod("SetGridPresentationPosition").Invoke(piece, new object[] { startPos, true });
                return (model, piece);
            }

            public void InitializeBoard()
            {
                IList modelList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(PieceModelType));
                for (int i = 0; i < models.Count; i++)
                    modelList.Add(models[i]);
                IList issueList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(SnapshotIssueType));
                object snapshot = Activator.CreateInstance(SnapshotType, grid, modelList, issueList);
                BoardType.GetMethod("InitializeBoardSnapshot").Invoke(board, new[] { snapshot });
                BoardType.GetMethod("ConfigurePieceJoints").Invoke(board, new object[] { level });
                Physics2D.SyncTransforms();
            }

            public void BeginDrag(Component piece)
            {
                dragPointer = piece.GetComponent<Rigidbody2D>().position;
                Invoke(controller, "TrySelectPiece", dragPointer);
                Assert.That(SelectedPiece, Is.EqualTo(piece));
            }

            public void MoveSelectedAndRunUpdate(float deltaX, float deltaY)
            {
                dragPointer += new Vector2(deltaX, deltaY);
                Invoke(controller, "UpdateDragTarget", dragPointer);
                Invoke(controller, "Update");
            }

            public void MoveSelectedWithoutGravityFlush(float deltaX, float deltaY)
            {
                dragPointer += new Vector2(deltaX, deltaY);
                Invoke(controller, "UpdateDragTarget", dragPointer);
                Invoke(controller, "TryMoveSelectedPieceOnGrid", SelectedPiece);
            }

            public void ReleaseDrag()
            {
                Invoke(controller, "ReleasePiece");
            }

            public Vector2Int GetAnchor(object model)
            {
                object anchor = PieceModelType.GetProperty("Anchor").GetValue(model);
                return new Vector2Int((int)CoordinateXField.GetValue(anchor), (int)CoordinateYField.GetValue(anchor));
            }

            public string GetStateName(object model)
            {
                return PieceModelType.GetProperty("State").GetValue(model).ToString();
            }

            public bool IsFallAnimating(Component piece)
            {
                object fallView = PieceType.GetProperty("GridFallView").GetValue(piece);
                return fallView != null && (bool)FallViewType.GetProperty("IsAnimating").GetValue(fallView);
            }

            public Vector2 FineCellWorld(int x, int y)
            {
                return (Vector2)CoordsType.GetMethod("FineCellToWorld").Invoke(null, new object[] { level, Coord(x, y) });
            }

            public float FineRowWorldY(int y) => FineCellWorld(0, y).y;

            public void Dispose()
            {
                if (BootstrapType != null)
                {
                    BootstrapType.GetMethod("ConfigureSceneCamera", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                        ?.Invoke(null, new object[] { null });
                }

                for (int index = cleanupObjects.Count - 1; index >= 0; index--)
                {
                    if (cleanupObjects[index] != null)
                        UnityEngine.Object.DestroyImmediate(cleanupObjects[index]);
                }

                cleanupObjects.Clear();
                RuntimeType?.GetMethod("ResetLevelSequence", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            }

            private T Track<T>(T obj) where T : UnityEngine.Object
            {
                if (obj != null)
                    cleanupObjects.Add(obj);
                return obj;
            }

            private static object Coord(int x, int y) => Activator.CreateInstance(CoordinateType, x, y);

            private static void Invoke(object target, string methodName, params object[] args)
            {
                MethodInfo[] methods = target.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name == methodName && methods[i].GetParameters().Length == args.Length)
                    {
                        methods[i].Invoke(target, args);
                        return;
                    }
                }

                throw new AssertionException($"Missing method {methodName} with {args.Length} args on {target.GetType().Name}.");
            }
        }
    }
}
