using System;
using System.Collections.Generic;
using System.Reflection;
using GravityPuzzle.Config;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Gravity;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Presentation.Views;
using NUnit.Framework;
using UnityEngine;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class CombinedBlockGravityAndShredderTests
    {
        private readonly List<UnityEngine.Object> cleanupObjects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            BlockShredder shredder = BlockShredder.Instance;
            if (shredder != null)
            {
                DestroyImmediateTracked(shredder.gameObject);
            }

            for (int index = cleanupObjects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object obj = cleanupObjects[index];
                if (obj != null)
                    DestroyImmediateTracked(obj);
            }

            cleanupObjects.Clear();
        }

        private static void DestroyImmediateTracked(UnityEngine.Object obj)
        {
            if (obj != null)
                UnityEngine.Object.DestroyImmediate(obj);
        }

        private T Track<T>(T obj) where T : UnityEngine.Object
        {
            if (obj != null)
                cleanupObjects.Add(obj);
            return obj;
        }

        private static GravityLevelDefinition CreateLevel(int columns, int rows, int subdivisions)
        {
            GravityLevelDefinition level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
            level.boardColumns = columns;
            level.boardRows = rows;
            level.subdivisions = subdivisions;
            return level;
        }

        private static PieceModel CreatePieceModel(int id, int anchorX, int anchorY, int widthCells, int heightCells)
        {
            List<GridCoordinate> cells = new List<GridCoordinate>(widthCells * heightCells);
            for (int y = 0; y < heightCells; y++)
            for (int x = 0; x < widthCells; x++)
                cells.Add(new GridCoordinate(x, y));

            return new PieceModel(id, new GridCoordinate(anchorX, anchorY), new GridCoordinate(0, 0), cells);
        }

        private static void SetNonPublicField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static void InvokeNonPublicMethod(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, $"Missing method '{methodName}' on {target.GetType().Name}.");
            method.Invoke(target, args);
        }

        private void ConfigureRuntimeLevel(GravityLevelDefinition level)
        {
            Type runtimeType = typeof(GravityLevelRuntime);
            GravityLevelDefinition[] levels = { level };
            runtimeType.GetField("levels", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, levels);
            runtimeType.GetField("currentLevelIndex", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 0);
            runtimeType.GetField("levelSequenceInitialized", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
        }

        private PuzzlePiece CreateRuntimePieceStub(int sourceId, int worldCellX, int worldCellY, float width, float height, GravityLevelDefinition level)
        {
            GameObject go = Track(new GameObject($"Piece_{sourceId}"));
            go.SetActive(false);

            GameObject cellGo = Track(new GameObject("Cell"));
            cellGo.transform.SetParent(go.transform, false);
            cellGo.transform.localPosition = new Vector3(width * 0.5f - (0.5f / level.subdivisions), height * 0.5f - (0.5f / level.subdivisions), 0f);
            BoxCollider2D collider = cellGo.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(width, height);

            PuzzlePiece piece = go.AddComponent<PuzzlePiece>();
            InvokeNonPublicMethod(piece, "Awake");
            piece.ConfigureSourcePieceId(sourceId);
            piece.ConfigureProgressUnits(1);
            piece.ConfigureCollisionGeometry(null, new List<BoxCollider2D> { collider }, new List<SpriteRenderer>());

            Vector2 originWorld = GravityLevelGridCoordinates.FineCellToWorld(level, new GridCoordinate(worldCellX, worldCellY));
            go.transform.position = originWorld;
            if (piece.Body != null)
                piece.Body.position = originWorld;

            go.SetActive(true);
            return piece;
        }

        [Test]
        public void PieceModel_GetLowestWorldCellY_HandlesNegativeLocalCells()
        {
            List<GridCoordinate> cells = new List<GridCoordinate>
            {
                new GridCoordinate(0, -1),
                new GridCoordinate(0, -4),
                new GridCoordinate(1, -2),
            };
            PieceModel model = new PieceModel(42, new GridCoordinate(2, 12), new GridCoordinate(0, 0), cells);

            Assert.That(model.GetLowestWorldCellY(), Is.EqualTo(8));
        }

        [Test]
        public void TryPlanAndCommitJointGroupGravityRetarget_MovesFallingCombinedBlockAtomically()
        {
            GravityLevelDefinition level = Track(CreateLevel(4, 6, 4));
            ConfigureRuntimeLevel(level);
            GameObject boardGo = Track(new GameObject("Board"));
            PrototypeBoard board = boardGo.AddComponent<PrototypeBoard>();

            GravityBoardGrid grid = new GravityBoardGrid(16, 24);
            PieceModel lower = CreatePieceModel(1, 0, 8, 4, 4);
            PieceModel upper = CreatePieceModel(2, 0, 12, 4, 4);
            Assert.That(grid.TryPlace(lower), Is.True);
            Assert.That(grid.TryPlace(upper), Is.True);
            Assert.That(lower.TrySetState(PieceState.Falling), Is.True);
            Assert.That(upper.TrySetState(PieceState.Falling), Is.True);

            board.InitializeBoardSnapshot(new LevelBoardSnapshot(
                grid,
                new List<PieceModel> { lower, upper },
                new List<LevelBoardSnapshotIssue>()));

            PieceJointGraph jointGraph = (PieceJointGraph)typeof(PrototypeBoard)
                .GetField("pieceJointGraph", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(board);
            jointGraph.Configure(
                new[] { 1, 2 },
                new[]
                {
                    new PieceJointGraph.Joint(1, 1, 2, new GridCoordinate(0, 0), new GridCoordinate(0, 0))
                });

            List<GridGravityMove> moves = new List<GridGravityMove>();
            Assert.That(board.TryPlanJointGroupGravityRetarget(1, moves), Is.True);
            Assert.That(moves.Count, Is.EqualTo(2));

            Assert.That(board.TryCommitJointGroupGravityRetarget(moves, out GridPlacementResult result), Is.True);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(lower.Anchor, Is.EqualTo(new GridCoordinate(0, 0)));
            Assert.That(upper.Anchor, Is.EqualTo(new GridCoordinate(0, 4)));
            Assert.That(lower.State, Is.EqualTo(PieceState.Falling));
            Assert.That(upper.State, Is.EqualTo(PieceState.Falling));
        }

        [Test]
        public void CantileveredCombinedBlock_IsLandingSupportedByShredderReservation_ChecksAcrossJoints()
        {
            GravityLevelDefinition level = Track(CreateLevel(4, 6, 4));
            ConfigureRuntimeLevel(level);
            GameObject boardGo = Track(new GameObject("Board"));
            PrototypeBoard board = boardGo.AddComponent<PrototypeBoard>();

            GravityBoardGrid grid = new GravityBoardGrid(16, 24);
            PieceModel shredderPiece = CreatePieceModel(10, 0, 0, 4, 3);
            PieceModel anchoredPartner = CreatePieceModel(11, 0, 3, 4, 4);
            PieceModel cantileveredPartner = CreatePieceModel(12, 4, 3, 4, 4);

            Assert.That(grid.TryPlace(shredderPiece), Is.True);
            Assert.That(grid.TryReserve(shredderPiece), Is.True);
            Assert.That(shredderPiece.TrySetState(PieceState.Shredding), Is.True);
            Assert.That(grid.TryPlace(anchoredPartner), Is.True);
            Assert.That(grid.TryPlace(cantileveredPartner), Is.True);

            board.InitializeBoardSnapshot(new LevelBoardSnapshot(
                grid,
                new List<PieceModel> { shredderPiece, anchoredPartner, cantileveredPartner },
                new List<LevelBoardSnapshotIssue>()));

            PieceJointGraph jointGraph = (PieceJointGraph)typeof(PrototypeBoard)
                .GetField("pieceJointGraph", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(board);
            jointGraph.Configure(
                new[] { 11, 12 },
                new[]
                {
                    new PieceJointGraph.Joint(1, 11, 12, new GridCoordinate(0, 0), new GridCoordinate(0, 0))
                });

            Assert.That(
                board.IsLandingSupportedByShredderReservation(cantileveredPartner, cantileveredPartner.Anchor),
                Is.True,
                "Cantilevered member joined to a shredder-supported partner must remain canonical-exempt.");

            jointGraph.RemoveJointsForMember(11);
            Assert.That(
                board.IsLandingSupportedByShredderReservation(cantileveredPartner, cantileveredPartner.Anchor),
                Is.False,
                "Once the joint breaks, the unsupported cantilever over empty cells must lose shredder support.");
        }

        [Test]
        public void ShredderCatchZone_FallingPieceAdmissionRequiresArrivalNearBottom_AndClearCorridor()
        {
            GravityLevelDefinition level = Track(CreateLevel(4, 6, 4));
            ConfigureRuntimeLevel(level);

            ShredderConfig config = Track(ScriptableObject.CreateInstance<ShredderConfig>());
            SetNonPublicField(config, "physicsFreefallEntrySpeed", 12.5f);
            SetNonPublicField(config, "physicsFreefallMaxSpeed", 25f);
            SetNonPublicField(config, "feedSpeed", 2f);
            SetNonPublicField(config, "enablePhysicsIsland", false);

            GameObject shredderGo = Track(new GameObject("BlockShredder"));
            BlockShredder shredder = shredderGo.AddComponent<BlockShredder>();
            SetNonPublicField(shredder, "shredderConfig", config);
            InvokeNonPublicMethod(shredder, "Awake");

            GameObject zoneGo = Track(new GameObject("CatchZone"));
            zoneGo.AddComponent<BoxCollider2D>();
            ShredderCatchZone zone = zoneGo.AddComponent<ShredderCatchZone>();
            InvokeNonPublicMethod(zone, "Awake");
            InvokeNonPublicMethod(zone, "OnEnable");
            float boardBottomY = -level.boardRows * 0.5f;
            float shredY = boardBottomY - 1.0f;
            zone.Configure(new Vector2(0f, shredY - 5f), new Vector2(4f, 10f), shredY, 0.7f, 0.5f);

            GameObject boardGo = Track(new GameObject("Board"));
            PrototypeBoard board = boardGo.AddComponent<PrototypeBoard>();

            GravityBoardGrid grid = new GravityBoardGrid(16, 24);
            PieceModel fallingModel = CreatePieceModel(1, 0, 0, 4, 4);
            PieceModel blockingModel = CreatePieceModel(2, 0, 0, 4, 4);
            Assert.That(grid.TryPlace(fallingModel), Is.True);
            Assert.That(fallingModel.TrySetState(PieceState.Falling), Is.True);
            Assert.That(blockingModel.TrySetState(PieceState.HandoffToPhysics), Is.True);
            Assert.That(blockingModel.TrySetState(PieceState.OpenFreefall), Is.True);
            board.InitializeBoardSnapshot(new LevelBoardSnapshot(
                grid,
                new List<PieceModel> { fallingModel, blockingModel },
                new List<LevelBoardSnapshotIssue>()));

            PuzzlePiece fallingPiece = CreateRuntimePieceStub(1, 0, 0, 1f, 1f, level);

            // Place visuals 0.30u above boardBottomY (previously admitted by +0.5f window!).
            fallingPiece.transform.position += Vector3.up * 0.30f;
            Assert.That(
                zone.ContainsMapExitFootprint(fallingPiece),
                Is.False,
                "Falling piece 0.30u above boardBottomY must not be prematurely pulled into freefall.");

            // Move visuals right to boardBottomY + 0.04u: now admitted cleanly when corridor is clear.
            fallingPiece.transform.position -= Vector3.up * 0.26f;
            Assert.That(zone.ContainsMapExitFootprint(fallingPiece), Is.True);

            // Place an active OpenFreefall obstacle overlapping row Y=0 in the same column: admission & CanMove must block!
            PuzzlePiece blockingPiece = CreateRuntimePieceStub(2, 0, 0, 1f, 1f, level);
            blockingPiece.ApplyLifecyclePhysicsProfile(PieceState.OpenFreefall, config, 12.5f);

            object handoffTypeInstance = Activator.CreateInstance(
                typeof(BlockShredder).GetNestedType("ExitHandoff", BindingFlags.NonPublic),
                nonPublic: true);
            SetNonPublicField(handoffTypeInstance, "Piece", blockingPiece);
            SetNonPublicField(handoffTypeInstance, "Zone", zone);
            SetNonPublicField(handoffTypeInstance, "EntrySequence", 1L);
            System.Collections.IList activeList = (System.Collections.IList)typeof(BlockShredder)
                .GetField("activeFreefallPieces", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(shredder);
            activeList.Add(handoffTypeInstance);
            InvokeNonPublicMethod(shredder, "RefreshOffBoardBlockedRows", false);

            Assert.That(
                zone.ContainsMapExitFootprint(fallingPiece),
                Is.False,
                "Map-exit must reject entry while an active OpenFreefall piece still overlaps row Y=0.");

            PieceModel followerModel = CreatePieceModel(3, 0, 4, 4, 4);
            Assert.That(
                board.CanMovePiece(followerModel, new GridCoordinate(0, 0)),
                Is.False,
                "Grid movement must not move into cells physically occupied by an off-board shredder piece.");
        }

        [Test]
        public void BlockShredder_TopologicalSort_OrdersSupportingPieceBeforeLowerOverhangingPiece()
        {
            GravityLevelDefinition level = Track(CreateLevel(4, 6, 4));
            ConfigureRuntimeLevel(level);

            ShredderConfig config = Track(ScriptableObject.CreateInstance<ShredderConfig>());
            SetNonPublicField(config, "physicsFreefallEntrySpeed", 12.5f);
            SetNonPublicField(config, "physicsFreefallMaxSpeed", 25f);
            SetNonPublicField(config, "feedSpeed", 2f);
            SetNonPublicField(config, "enablePhysicsIsland", false);

            GameObject boardGo = Track(new GameObject("Board"));
            PrototypeBoard board = boardGo.AddComponent<PrototypeBoard>();
            GravityBoardGrid grid = new GravityBoardGrid(16, 24);
            PieceModel supportModel = CreatePieceModel(1, 0, 0, 4, 4);
            PieceModel overhangModel = CreatePieceModel(2, 0, 4, 4, 4);
            Assert.That(supportModel.TrySetState(PieceState.HandoffToPhysics), Is.True);
            Assert.That(supportModel.TrySetState(PieceState.OpenFreefall), Is.True);
            Assert.That(overhangModel.TrySetState(PieceState.HandoffToPhysics), Is.True);
            Assert.That(overhangModel.TrySetState(PieceState.OpenFreefall), Is.True);
            board.InitializeBoardSnapshot(new LevelBoardSnapshot(
                grid,
                new List<PieceModel> { supportModel, overhangModel },
                new List<LevelBoardSnapshotIssue>()));

            GameObject shredderGo = Track(new GameObject("BlockShredder"));
            BlockShredder shredder = shredderGo.AddComponent<BlockShredder>();
            SetNonPublicField(shredder, "shredderConfig", config);
            InvokeNonPublicMethod(shredder, "Awake");

            GameObject zoneGo = Track(new GameObject("CatchZone"));
            zoneGo.AddComponent<BoxCollider2D>();
            ShredderCatchZone zone = zoneGo.AddComponent<ShredderCatchZone>();
            InvokeNonPublicMethod(zone, "Awake");
            InvokeNonPublicMethod(zone, "OnEnable");
            zone.Configure(new Vector2(0f, -8f), new Vector2(6f, 10f), -3f, 0.7f, 0.5f);

            // Lower support P in column X=-0.5..0.5, Y=-3.2..-2.2 (CachedLowestY = -3.2)
            PuzzlePiece supportP = CreateRuntimePieceStub(1, 0, 0, 1f, 1f, level);
            supportP.transform.position = new Vector3(0f, -2.7f, 0f);
            BoxCollider2D pCell = ((List<BoxCollider2D>)typeof(PuzzlePiece).GetField("collisionCells", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(supportP))[0];
            pCell.transform.localPosition = Vector3.zero;
            pCell.size = new Vector2(1f, 1f);
            supportP.ApplyLifecyclePhysicsProfile(PieceState.OpenFreefall, config, 12.5f);

            // Overhanging piece Q rests ON support P in column X=0 (Y=-2.1..-1.1),
            // PLUS has an adjacent prong in column X=1 reaching LOWER (Y=-3.6..-2.6 -> CachedLowestY = -3.6 < -3.2!).
            GameObject qGo = Track(new GameObject("Piece_Q"));
            qGo.SetActive(false);
            qGo.transform.position = Vector3.zero;

            GameObject qTop = Track(new GameObject("QTop"));
            qTop.transform.SetParent(qGo.transform, false);
            qTop.transform.localPosition = new Vector3(0f, -1.6f, 0f);
            BoxCollider2D qTopCol = qTop.AddComponent<BoxCollider2D>();
            qTopCol.size = new Vector2(1f, 1f);

            GameObject qProng = Track(new GameObject("QProng"));
            qProng.transform.SetParent(qGo.transform, false);
            qProng.transform.localPosition = new Vector3(1.2f, -3.1f, 0f);
            BoxCollider2D qProngCol = qProng.AddComponent<BoxCollider2D>();
            qProngCol.size = new Vector2(1f, 1f);

            PuzzlePiece overhangQ = qGo.AddComponent<PuzzlePiece>();
            InvokeNonPublicMethod(overhangQ, "Awake");
            overhangQ.ConfigureSourcePieceId(2);
            overhangQ.ConfigureProgressUnits(2);
            overhangQ.ConfigureCollisionGeometry(null, new List<BoxCollider2D> { qTopCol, qProngCol }, new List<SpriteRenderer>());
            qGo.SetActive(true);
            overhangQ.ApplyLifecyclePhysicsProfile(PieceState.OpenFreefall, config, 12.5f);

            Type handoffType = typeof(BlockShredder).GetNestedType("ExitHandoff", BindingFlags.NonPublic);
            System.Collections.IList activeList = (System.Collections.IList)typeof(BlockShredder)
                .GetField("activeFreefallPieces", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(shredder);

            // Insert Q before P intentionally; scalar CachedLowestY would keep Q first and invert them!
            object qEntry = Activator.CreateInstance(handoffType, nonPublic: true);
            SetNonPublicField(qEntry, "Piece", overhangQ);
            SetNonPublicField(qEntry, "Zone", zone);
            SetNonPublicField(qEntry, "EntrySequence", 2L);
            activeList.Add(qEntry);

            object pEntry = Activator.CreateInstance(handoffType, nonPublic: true);
            SetNonPublicField(pEntry, "Piece", supportP);
            SetNonPublicField(pEntry, "Zone", zone);
            SetNonPublicField(pEntry, "EntrySequence", 1L);
            activeList.Add(pEntry);

            InvokeNonPublicMethod(shredder, "SortActiveFreefallPiecesTopologically");

            PuzzlePiece firstPiece = (PuzzlePiece)handoffType.GetField("Piece").GetValue(activeList[0]);
            PuzzlePiece secondPiece = (PuzzlePiece)handoffType.GetField("Piece").GetValue(activeList[1]);
            Assert.That(firstPiece, Is.EqualTo(supportP), "Supporting piece in shared column must run before overhanging piece above it.");
            Assert.That(secondPiece, Is.EqualTo(overhangQ));
        }
    }
}
