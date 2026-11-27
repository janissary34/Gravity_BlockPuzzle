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
    public sealed class StackedAndCombinedShredderPlayModeTests
    {
        private static readonly Type BootstrapType = Type.GetType("GravityPuzzle.PrototypeBootstrap, Assembly-CSharp");
        private static readonly Type LevelType = Type.GetType("GravityPuzzle.GravityLevelDefinition, Assembly-CSharp");
        private static readonly Type PieceDefinitionType = Type.GetType("GravityPuzzle.PieceDefinition, Assembly-CSharp");
        private static readonly Type PieceCellDefinitionType = Type.GetType("GravityPuzzle.PieceCellDefinition, Assembly-CSharp");
        private static readonly Type PieceCellType = Type.GetType("GravityPuzzle.PieceCellType, Assembly-CSharp");
        private static readonly Type JointDefinitionType = Type.GetType("GravityPuzzle.JointDefinition, Assembly-CSharp");
        private static readonly Type ShredderDefinitionType = Type.GetType("GravityPuzzle.ShredderDefinition, Assembly-CSharp");
        private static readonly Type RuntimeType = Type.GetType("GravityPuzzle.GravityLevelRuntime, Assembly-CSharp");
        private static readonly Type BoardType = Type.GetType("GravityPuzzle.PrototypeBoard, Assembly-CSharp");
        private static readonly Type DragControllerType = Type.GetType("GravityPuzzle.PuzzleDragController, Assembly-CSharp");
        private static readonly Type BlockShredderType = Type.GetType("GravityPuzzle.BlockShredder, Assembly-CSharp");
        private static readonly Type CatchZoneType = Type.GetType("GravityPuzzle.ShredderCatchZone, Assembly-CSharp");
        private static readonly Type PuzzlePieceType = Type.GetType("GravityPuzzle.PuzzlePiece, Assembly-CSharp");
        private static readonly Type PieceModelType = Type.GetType("GravityPuzzle.Gameplay.Pieces.PieceModel, Assembly-CSharp");
        private static readonly Type GridType = Type.GetType("GravityPuzzle.Core.Grid.GravityBoardGrid, Assembly-CSharp");
        private static readonly Type SnapshotType = Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshot, Assembly-CSharp");
        private static readonly Type SnapshotIssueType = Type.GetType("GravityPuzzle.Core.Grid.LevelBoardSnapshotIssue, Assembly-CSharp");
        private static readonly Type CoordinateType = Type.GetType("GravityPuzzle.Core.Grid.GridCoordinate, Assembly-CSharp");
        private static readonly Type CoordsType = Type.GetType("GravityPuzzle.Core.Grid.GravityLevelGridCoordinates, Assembly-CSharp");
        private static readonly Type FallViewType = Type.GetType("GravityPuzzle.Presentation.Views.PieceGridFallView, Assembly-CSharp");
        private static readonly Type ShredderConfigType = Type.GetType("GravityPuzzle.Config.ShredderConfig, Assembly-CSharp");
        private static readonly Type TweenConfigType = Type.GetType("GravityPuzzle.Config.TweenConfig, Assembly-CSharp");
        private static readonly Type JointGraphType = Type.GetType("GravityPuzzle.Core.Grid.PieceJointGraph, Assembly-CSharp");
        private static readonly Type JointStructType = Type.GetType("GravityPuzzle.Core.Grid.PieceJointGraph+Joint, Assembly-CSharp");

        private readonly List<UnityEngine.Object> cleanupObjects = new List<UnityEngine.Object>();
        private Sprite cachedUnitSprite;

        [TearDown]
        public void TearDown()
        {
            if (BootstrapType != null)
            {
                BootstrapType.GetMethod("ConfigureSceneCamera", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    ?.Invoke(null, new object[] { null });
            }

            if (DragControllerType != null)
            {
                object controller = DragControllerType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
                if (controller is Component c && c != null)
                    UnityEngine.Object.DestroyImmediate(c.gameObject);
            }

            if (BlockShredderType != null)
            {
                object shredder = BlockShredderType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
                if (shredder is Component c && c != null)
                    UnityEngine.Object.DestroyImmediate(c.gameObject);
            }

            for (int index = cleanupObjects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object obj = cleanupObjects[index];
                if (obj != null)
                    UnityEngine.Object.DestroyImmediate(obj);
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

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo f = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(f, Is.Not.Null, $"Missing field '{fieldName}' on {target.GetType().Name}.");
            f.SetValue(target, value);
        }

        private static object GetProp(object target, string propName)
        {
            PropertyInfo p = target.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(p, Is.Not.Null, $"Missing property '{propName}' on {target.GetType().Name}.");
            return p.GetValue(target);
        }

        private static bool TryGetCellExtents(object piece, out float minY, out float maxY)
        {
            object[] args = { 0f, 0f };
            bool ok = (bool)PuzzlePieceType.GetMethod("TryGetActiveCellVerticalExtents").Invoke(piece, args);
            minY = (float)args[0];
            maxY = (float)args[1];
            return ok;
        }

        private (object board, object shredder, object zone, object level, ScriptableObject shredderConfig, ScriptableObject tweenConfig)
            CreateMinimalSimulation(int cols, int rows, int subdivisions)
        {
            ScriptableObject level = Track(ScriptableObject.CreateInstance(LevelType));
            LevelType.GetField("boardColumns").SetValue(level, cols);
            LevelType.GetField("boardRows").SetValue(level, rows);
            LevelType.GetField("subdivisions").SetValue(level, subdivisions);

            RuntimeType.GetMethod("ResetLevelSequence", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Array levels = Array.CreateInstance(LevelType, 1);
            levels.SetValue(level, 0);
            RuntimeType.GetField("levels", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, levels);
            RuntimeType.GetField("currentLevelIndex", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 0);
            RuntimeType.GetField("levelSequenceInitialized", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);

            ScriptableObject shredderConfig = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/ShredderConfig.asset");
            if (shredderConfig == null)
            {
                shredderConfig = Track(ScriptableObject.CreateInstance(ShredderConfigType));
                SetField(shredderConfig, "physicsFreefallEntrySpeed", 12.5f);
                SetField(shredderConfig, "physicsFreefallMaxSpeed", 25f);
                SetField(shredderConfig, "feedSpeed", 2f);
                SetField(shredderConfig, "gearClippingRatio", 0.45f);
            }

            ScriptableObject tweenConfig = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/TweenConfig.asset");
            Assert.That(tweenConfig, Is.Not.Null, "Expected Assets/TweenConfig.asset to exist.");

            cachedUnitSprite = Track(Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 4f, 4f),
                new Vector2(0.5f, 0.5f),
                4f));

            GameObject camGo = Track(new GameObject("TestCamera"));
            Camera testCam = camGo.AddComponent<Camera>();
            BootstrapType.GetMethod("ConfigureSceneCamera", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(null, new object[] { testCam });

            GameObject boardGo = Track(new GameObject("TestBoard"));
            object board = boardGo.AddComponent(BoardType);
            boardGo.AddComponent(DragControllerType);

            GameObject shredderGo = Track(new GameObject("TestShredder"));
            shredderGo.SetActive(false);
            object shredder = shredderGo.AddComponent(BlockShredderType);
            SetField(shredder, "shredderConfig", shredderConfig);
            shredderGo.SetActive(true);

            GameObject zoneGo = Track(new GameObject("TestCatchZone"));
            zoneGo.AddComponent<BoxCollider2D>();
            object zone = zoneGo.AddComponent(CatchZoneType);
            float boardBottomY = -rows * 0.5f;
            float handoffBelow = (float)ShredderConfigType.GetProperty("PhysicsHandoffDistanceBelowMap").GetValue(shredderConfig);
            float shredY = boardBottomY - handoffBelow;
            float approach = (float)ShredderConfigType.GetProperty("CaptureApproachDistance").GetValue(shredderConfig);
            CatchZoneType.GetMethod("Configure").Invoke(zone, new object[]
            {
                new Vector2(0f, shredY - 5f),
                new Vector2(cols, 10f),
                shredY,
                approach,
                0.5f
            });

            return (board, shredder, zone, level, shredderConfig, tweenConfig);
        }

        private (object model, object piece) CreateSimulatedBlock(
            int id,
            int anchorX,
            int anchorY,
            int widthCells,
            int heightCells,
            object level,
            ScriptableObject tweenConfig)
        {
            IList cells = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(CoordinateType));
            for (int y = 0; y < heightCells; y++)
            for (int x = 0; x < widthCells; x++)
                cells.Add(Coord(x, y));

            object model = Activator.CreateInstance(PieceModelType, id, Coord(anchorX, anchorY), Coord(0, 0), cells);

            GameObject go = Track(new GameObject($"Block_{id}"));
            go.SetActive(false);
            int subdivisions = (int)LevelType.GetField("subdivisions").GetValue(level);
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

                SpriteRenderer sr = cGo.AddComponent<SpriteRenderer>();
                sr.sprite = cachedUnitSprite;
                visuals.Add(sr);
            }

            object fallView = go.AddComponent(FallViewType);
            SetField(fallView, "tweenConfig", tweenConfig);
            object piece = go.AddComponent(PuzzlePieceType);
            SetField(piece, "sourcePieceId", id);
            SetField(piece, "isSpawnedFromPool", true);
            SetField(piece, "collisionCells", colliders);
            SetField(piece, "collisionCellVisuals", visuals);
            SetField(piece, "progressUnits", Mathf.Max(1, visuals.Count));

            Vector2 startPos = (Vector2)CoordsType.GetMethod("FineCellToWorld").Invoke(null, new[] { level, Coord(anchorX, anchorY) });
            go.transform.position = startPos;
            Rigidbody2D body = (Rigidbody2D)GetProp(piece, "Body");
            if (body != null)
                body.position = startPos;

            go.SetActive(true);
            return (model, piece);
        }

        [UnityTest]
        public IEnumerator StackedVerticalPieces_IntoSlowShredder_NeverInterpenetrateOrInvert()
        {
            var sim = CreateMinimalSimulation(4, 6, 4);
            object grid = Activator.CreateInstance(GridType, 16, 24);

            // Bottom piece is 2 modules (8 fine cells = 2.0u) tall at Y=0..7 -> slower to clear board bottom!
            // Middle piece at Y=8..11, Top piece at Y=12..15.
            var bottom = CreateSimulatedBlock(1, 4, 0, 4, 8, sim.level, sim.tweenConfig);
            var middle = CreateSimulatedBlock(2, 4, 8, 4, 4, sim.level, sim.tweenConfig);
            var top = CreateSimulatedBlock(3, 4, 12, 4, 4, sim.level, sim.tweenConfig);

            Assert.That((bool)GridType.GetMethod("TryPlace").Invoke(grid, new[] { bottom.model }), Is.True);
            Assert.That((bool)GridType.GetMethod("TryPlace").Invoke(grid, new[] { middle.model }), Is.True);
            Assert.That((bool)GridType.GetMethod("TryPlace").Invoke(grid, new[] { top.model }), Is.True);

            IList modelList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(PieceModelType));
            modelList.Add(bottom.model);
            modelList.Add(middle.model);
            modelList.Add(top.model);
            IList issueList = (IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(SnapshotIssueType));
            object snapshot = Activator.CreateInstance(SnapshotType, grid, modelList, issueList);
            BoardType.GetMethod("InitializeBoardSnapshot").Invoke(sim.board, new[] { snapshot });
            BoardType.GetMethod("WakeUpGravity").Invoke(sim.board, null);

            float cutterSeamY = (float)CatchZoneType.GetProperty("ShredY").GetValue(sim.zone) -
                                (float)CatchZoneType.GetProperty("WheelRadius").GetValue(sim.zone) *
                                Mathf.Clamp((float)ShredderConfigType.GetProperty("GearClippingRatio").GetValue(sim.shredderConfig), 0.1f, 0.9f);

            float elapsed = 0f;
            while (elapsed < 1.8f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;

                float bottomMaxY = 0f;
                float middleMinY = 0f;
                float middleMaxY = 0f;
                float topMinY = 0f;
                bool hasBottom = bottom.piece != null && ((Component)bottom.piece).gameObject.activeInHierarchy &&
                                 TryGetCellExtents(bottom.piece, out _, out bottomMaxY) && bottomMaxY > cutterSeamY + 0.05f;
                bool hasMiddle = middle.piece != null && ((Component)middle.piece).gameObject.activeInHierarchy &&
                                 TryGetCellExtents(middle.piece, out middleMinY, out middleMaxY) && middleMaxY > cutterSeamY + 0.05f;
                bool hasTop = top.piece != null && ((Component)top.piece).gameObject.activeInHierarchy &&
                              TryGetCellExtents(top.piece, out topMinY, out _);

                if (hasBottom && hasMiddle)
                {
                    Assert.That(
                        middleMinY,
                        Is.GreaterThanOrEqualTo(bottomMaxY - 0.04f),
                        $"Middle block penetrated or swapped below Bottom block at t={elapsed:F2}s (middleMinY={middleMinY:F3}, bottomMaxY={bottomMaxY:F3}).");
                }

                if (hasMiddle && hasTop)
                {
                    Assert.That(
                        topMinY,
                        Is.GreaterThanOrEqualTo(middleMaxY - 0.04f),
                        $"Top block penetrated or swapped below Middle block at t={elapsed:F2}s (topMinY={topMinY:F3}, middleMaxY={middleMaxY:F3}).");
                }
            }
        }

        [UnityTest]
        public IEnumerator CombinedBlock_VerticalSplitAtShredder_NeverInterpenetratesPartner()
        {
            var sim = CreateMinimalSimulation(4, 6, 4);
            object grid = Activator.CreateInstance(GridType, 16, 24);

            // Combined block: Bottom 2-tall piece at Y=4..11 joined vertically to Top piece at Y=12..15.
            // Starts above shredder (air at Y=0..3), falls together, Bottom enters shredder & breaks joint!
            var lower = CreateSimulatedBlock(10, 4, 4, 4, 8, sim.level, sim.tweenConfig);
            var upper = CreateSimulatedBlock(11, 4, 12, 4, 4, sim.level, sim.tweenConfig);

            Assert.That((bool)GridType.GetMethod("TryPlace").Invoke(grid, new[] { lower.model }), Is.True);
            Assert.That((bool)GridType.GetMethod("TryPlace").Invoke(grid, new[] { upper.model }), Is.True);

            IList modelList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(PieceModelType));
            modelList.Add(lower.model);
            modelList.Add(upper.model);
            IList issueList = (IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(SnapshotIssueType));
            object snapshot = Activator.CreateInstance(SnapshotType, grid, modelList, issueList);
            BoardType.GetMethod("InitializeBoardSnapshot").Invoke(sim.board, new[] { snapshot });

            object jointGraph = BoardType.GetField("pieceJointGraph", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim.board);
            Array joints = Array.CreateInstance(JointStructType, 1);
            joints.SetValue(Activator.CreateInstance(JointStructType, 1, 10, 11, Coord(0, 0), Coord(0, 0)), 0);
            JointGraphType.GetMethod("Configure").Invoke(jointGraph, new object[] { new[] { 10, 11 }, joints });

            BoardType.GetMethod("WakeUpGravity").Invoke(sim.board, null);

            float cutterSeamY = (float)CatchZoneType.GetProperty("ShredY").GetValue(sim.zone) -
                                (float)CatchZoneType.GetProperty("WheelRadius").GetValue(sim.zone) *
                                Mathf.Clamp((float)ShredderConfigType.GetProperty("GearClippingRatio").GetValue(sim.shredderConfig), 0.1f, 0.9f);

            bool upperEnteredFreefall = false;
            float elapsed = 0f;
            while (elapsed < 2.0f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;

                if (upper.piece != null && (bool)GetProp(upper.piece, "IsInOpenFreefall"))
                    upperEnteredFreefall = true;

                float lowerMaxY = 0f;
                float upperMinY = 0f;
                bool hasLower = lower.piece != null && ((Component)lower.piece).gameObject.activeInHierarchy &&
                                TryGetCellExtents(lower.piece, out _, out lowerMaxY) && lowerMaxY > cutterSeamY + 0.05f;
                bool hasUpper = upper.piece != null && ((Component)upper.piece).gameObject.activeInHierarchy &&
                                TryGetCellExtents(upper.piece, out upperMinY, out _);

                if (hasLower && hasUpper)
                {
                    Assert.That(
                        upperMinY,
                        Is.GreaterThanOrEqualTo(lowerMaxY - 0.04f),
                        $"Combined partner overlapped lower member at t={elapsed:F2}s (upperMinY={upperMinY:F3}, lowerMaxY={lowerMaxY:F3}).");
                }
            }

            Assert.That(upperEnteredFreefall, Is.True, "Detached upper combined member must also follow smoothly into shredder without stalling mid-air.");
        }

        [UnityTest]
        public IEnumerator CombinedBlock_StackedAboveFallingPiece_EntersFallSynchronouslyWithoutOverlap()
        {
            var sim = CreateMinimalSimulation(4, 6, 4);
            object grid = Activator.CreateInstance(GridType, 16, 24);

            // Base piece at Y=4..7 (gap Y=0..3 below), with a horizontal combined block (20 <-> 21) resting on top at Y=8..11.
            var basePiece = CreateSimulatedBlock(19, 4, 4, 4, 4, sim.level, sim.tweenConfig);
            var combinedLeft = CreateSimulatedBlock(20, 4, 8, 4, 4, sim.level, sim.tweenConfig);
            var combinedRight = CreateSimulatedBlock(21, 8, 8, 4, 4, sim.level, sim.tweenConfig);

            Assert.That((bool)GridType.GetMethod("TryPlace").Invoke(grid, new[] { basePiece.model }), Is.True);
            Assert.That((bool)GridType.GetMethod("TryPlace").Invoke(grid, new[] { combinedLeft.model }), Is.True);
            Assert.That((bool)GridType.GetMethod("TryPlace").Invoke(grid, new[] { combinedRight.model }), Is.True);

            IList modelList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(PieceModelType));
            modelList.Add(basePiece.model);
            modelList.Add(combinedLeft.model);
            modelList.Add(combinedRight.model);
            IList issueList = (IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(SnapshotIssueType));
            object snapshot = Activator.CreateInstance(SnapshotType, grid, modelList, issueList);
            BoardType.GetMethod("InitializeBoardSnapshot").Invoke(sim.board, new[] { snapshot });

            object jointGraph = BoardType.GetField("pieceJointGraph", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim.board);
            Array joints = Array.CreateInstance(JointStructType, 1);
            joints.SetValue(Activator.CreateInstance(JointStructType, 1, 20, 21, Coord(0, 0), Coord(0, 0)), 0);
            JointGraphType.GetMethod("Configure").Invoke(jointGraph, new object[] { new[] { 20, 21 }, joints });

            BoardType.GetMethod("WakeUpGravity").Invoke(sim.board, null);

            // Combined block stacked above falling base must join the coordinated fall stack immediately.
            object fallViewBase = GetProp(basePiece.piece, "GridFallView");
            object fallViewLeft = GetProp(combinedLeft.piece, "GridFallView");
            object fallViewRight = GetProp(combinedRight.piece, "GridFallView");
            bool anyBaseMoving = (bool)FallViewType.GetProperty("IsAnimating").GetValue(fallViewBase) ||
                                 (bool)GetProp(basePiece.piece, "IsInOpenFreefall");
            Assert.That(anyBaseMoving, Is.True);
            bool bothCombinedMoving = ((bool)FallViewType.GetProperty("IsAnimating").GetValue(fallViewLeft) &&
                                       (bool)FallViewType.GetProperty("IsAnimating").GetValue(fallViewRight)) ||
                                      ((bool)GetProp(combinedLeft.piece, "IsInOpenFreefall") &&
                                       (bool)GetProp(combinedRight.piece, "IsInOpenFreefall"));
            Assert.That(bothCombinedMoving, Is.True, "Combined block stacked on a falling piece must start falling in lockstep.");

            yield return new WaitForFixedUpdate();

            float cutterSeamY = (float)CatchZoneType.GetProperty("ShredY").GetValue(sim.zone) -
                                (float)CatchZoneType.GetProperty("WheelRadius").GetValue(sim.zone) *
                                Mathf.Clamp((float)ShredderConfigType.GetProperty("GearClippingRatio").GetValue(sim.shredderConfig), 0.1f, 0.9f);

            float elapsed = 0f;
            while (elapsed < 1.5f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;

                float baseMaxY = 0f;
                float leftMinY = 0f;
                bool hasBase = basePiece.piece != null && ((Component)basePiece.piece).gameObject.activeInHierarchy &&
                               TryGetCellExtents(basePiece.piece, out _, out baseMaxY) && baseMaxY > cutterSeamY + 0.05f;
                bool hasLeft = combinedLeft.piece != null && ((Component)combinedLeft.piece).gameObject.activeInHierarchy &&
                               TryGetCellExtents(combinedLeft.piece, out leftMinY, out _);

                if (hasBase && hasLeft)
                {
                    Assert.That(
                        leftMinY,
                        Is.GreaterThanOrEqualTo(baseMaxY - 0.04f),
                        $"Combined left partner penetrated base block at t={elapsed:F2}s (leftMinY={leftMinY:F3}, baseMaxY={baseMaxY:F3}).");
                }
            }
        }
    }
}
