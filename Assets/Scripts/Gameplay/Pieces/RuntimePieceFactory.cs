using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public static class RuntimePieceFactory
    {
        private const string CollisionGeometryRootName = "Collision Geometry";
        private const string GridBlockName = "Grid Block";
        private const string BlockCellName = "Block Cell";
        private const string HookCellName = "Hook Cell";

        private static Material sharedOutlineMaterial;
        private static IRuntimePieceRootProvider rootProvider;

        public static void SetRootProvider(IRuntimePieceRootProvider provider)
        {
            rootProvider = provider ?? throw new System.ArgumentNullException(nameof(provider));
        }


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRootProvider()
        {
            rootProvider = null;
            sharedOutlineMaterial = null;
        }

        public static PuzzlePiece Create(
            GravityLevelDefinition level,
            PieceDefinition definition,
            int sourcePieceId)
        {
            if (rootProvider == null)
                throw new System.InvalidOperationException(
                    "[PiecePool] RuntimePieceFactory has not been configured by RuntimePieceFactoryBootstrap.");

            RuntimePieceRoot root = rootProvider.Create(definition.name);
            GameObject piece = root.GameObject;
            PrepareRoot(piece.transform, level, definition);
            ConfigureBody(root.Body, level);
            ConfigureComposite(root.CompositeCollider);

            PieceRuntimeContent content = BuildRuntimeContent(
                piece.transform,
                level,
                definition);

            root.CompositeCollider.GenerateGeometry();
            ConfigureOutline(root.Outline, root.CompositeCollider);

            PuzzlePiece puzzlePiece = root.Piece;
            ApplyPieceSetup(
                puzzlePiece,
                sourcePieceId,
                definition,
                root.CompositeCollider,
                content);

            return puzzlePiece;
        }

        private static void PrepareRoot(
            Transform pieceTransform,
            GravityLevelDefinition level,
            PieceDefinition definition)
        {
            pieceTransform.position = CellWorldPosition(level, definition.origin);
            pieceTransform.rotation = Quaternion.identity;
            pieceTransform.localScale = Vector3.one;
            ClearGeneratedContent(pieceTransform);
        }

        private static void ConfigureBody(Rigidbody2D body, GravityLevelDefinition level)
        {
            body.simulated = true;
            body.gravityScale = level.gravityScale;
            body.mass = 1f;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.rotation = 0f;
        }

        private static void ConfigureComposite(CompositeCollider2D pieceComposite)
        {
            pieceComposite.enabled = true;
            pieceComposite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            pieceComposite.generationType = CompositeCollider2D.GenerationType.Synchronous;
            pieceComposite.edgeRadius = 0f;
        }

        private static void ApplyPieceSetup(
            PuzzlePiece puzzlePiece,
            int sourcePieceId,
            PieceDefinition definition,
            CompositeCollider2D pieceComposite,
            PieceRuntimeContent content)
        {
            puzzlePiece.Configure(new PieceRuntimeSetup(
                sourcePieceId,
                Mathf.Max(1, content.ProgressUnits),
                definition.color,
                pieceComposite,
                content.CollisionCells,
                content.CollisionCellVisuals,
                definition.frozenMoveCount,
                definition.iceCounterFontSize,
                definition.iceCounterTextColor,
                definition.iceCounterOutlineColor,
                definition.iceCounterOutlineWidth,
                definition.iceCounterOffset));
        }

        private static PieceRuntimeContent BuildRuntimeContent(
            Transform pieceTransform,
            GravityLevelDefinition level,
            PieceDefinition definition)
        {
            float fineCellSize = 1f / level.subdivisions;
            List<PiecePartGeometry> parts = BuildPartGeometry(level, definition, fineCellSize, out int progressUnits);
            GetPiecePartBounds(parts, out Vector2 minimum, out Vector2 maximum);
            Vector2 collisionCentre = (minimum + maximum) * .5f;
            GameObject collisionRootObject = new GameObject(CollisionGeometryRootName);
            collisionRootObject.transform.SetParent(pieceTransform, false);
            collisionRootObject.transform.localPosition = collisionCentre;
            List<BoxCollider2D> collisionCells = new List<BoxCollider2D>(parts.Count);
            List<SpriteRenderer> collisionCellVisuals = new List<SpriteRenderer>(parts.Count);
            for (int index = 0; index < parts.Count; index++)
            {
                BoxCollider2D collider = CreatePiecePart(pieceTransform, collisionRootObject.transform, collisionCentre, parts[index], definition.color, out SpriteRenderer cellVisual);
                collisionCells.Add(collider);
                collisionCellVisuals.Add(cellVisual);
            }

            return new PieceRuntimeContent(
                progressUnits,
                collisionCells,
                collisionCellVisuals);
        }

        private static void ClearGeneratedContent(Transform pieceTransform)
        {
            for (int index = pieceTransform.childCount - 1; index >= 0; index--)
            {
                Transform child = pieceTransform.GetChild(index);
                if (child.name == CollisionGeometryRootName || child.name == GridBlockName || child.name == BlockCellName || child.name == HookCellName)
                    Object.Destroy(child.gameObject);
            }
        }


        private static List<PiecePartGeometry> BuildPartGeometry(
            GravityLevelDefinition level,
            PieceDefinition definition,
            float fineCellSize,
            out int progressUnits)
        {
            List<PiecePartGeometry> parts = new List<PiecePartGeometry>();
            Dictionary<Vector2Int, int> blockCounts = new Dictionary<Vector2Int, int>();

            for (int index = 0; index < definition.cells.Count; index++)
            {
                PieceCellDefinition cell = definition.cells[index];
                Vector2Int rotated = QuarterTurnUtility.Rotate(cell.localCell, definition.quarterTurns);
                if (cell.type != PieceCellType.Block)
                    continue;

                Vector2Int absolute = definition.origin + rotated;
                Vector2Int gridCell = new Vector2Int(
                    Mathf.FloorToInt((float)absolute.x / level.subdivisions),
                    Mathf.FloorToInt((float)absolute.y / level.subdivisions));
                blockCounts.TryGetValue(gridCell, out int count);
                blockCounts[gridCell] = count + 1;
            }

            HashSet<Vector2Int> completeModules = new HashSet<Vector2Int>();
            int cellsPerModule = level.subdivisions * level.subdivisions;
            foreach (KeyValuePair<Vector2Int, int> blockCount in blockCounts)
            {
                if (blockCount.Value != cellsPerModule)
                    continue;

                completeModules.Add(blockCount.Key);
                Vector2 localPosition =
                    GridCellWorldPosition(level, blockCount.Key) -
                    CellWorldPosition(level, definition.origin);
                parts.Add(new PiecePartGeometry(GridBlockName, localPosition, Vector2.one));
            }

            for (int index = 0; index < definition.cells.Count; index++)
            {
                PieceCellDefinition cell = definition.cells[index];
                Vector2Int rotated = QuarterTurnUtility.Rotate(cell.localCell, definition.quarterTurns);
                Vector2Int absolute = definition.origin + rotated;
                Vector2Int gridCell = new Vector2Int(
                    Mathf.FloorToInt((float)absolute.x / level.subdivisions),
                    Mathf.FloorToInt((float)absolute.y / level.subdivisions));
                if (cell.type == PieceCellType.Block && completeModules.Contains(gridCell))
                    continue;

                Vector2 localPosition = (Vector2)rotated * fineCellSize;
                string partName = cell.type == PieceCellType.Hook ? HookCellName : BlockCellName;
                parts.Add(new PiecePartGeometry(
                    partName,
                    localPosition,
                    Vector2.one * fineCellSize));
            }

            progressUnits = blockCounts.Count;
            return parts;
        }

        private static void ConfigureOutline(LineRenderer outline, CompositeCollider2D pieceComposite)
        {
            outline.enabled = true;
            outline.useWorldSpace = false;
            outline.loop = true;
            outline.positionCount = 0;
            outline.startWidth = 0.05f;
            outline.endWidth = 0.05f;
            outline.numCornerVertices = 4;
            outline.numCapVertices = 4;
            outline.sortingOrder = 10;

            if (sharedOutlineMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    sharedOutlineMaterial = new Material(shader) { name = "Shared Outline Material" };
            }

            if (sharedOutlineMaterial != null)
                outline.sharedMaterial = sharedOutlineMaterial;

            outline.startColor = Color.black;
            outline.endColor = Color.black;

            if (pieceComposite.pathCount <= 0)
                return;

            int pointCount = pieceComposite.GetPathPointCount(0);
            outline.positionCount = pointCount;
            Vector2[] path = new Vector2[pointCount];
            pieceComposite.GetPath(0, path);
            for (int index = 0; index < pointCount; index++)
                outline.SetPosition(index, new Vector3(path[index].x, path[index].y, 0f));
        }

        private static BoxCollider2D CreatePiecePart(
            Transform visualParent,
            Transform collisionRoot,
            Vector2 collisionCentre,
            PiecePartGeometry part,
            Color color,
            out SpriteRenderer cellVisual)
        {
            GameObject visual = new GameObject(part.Name);
            visual.transform.SetParent(visualParent, false);
            visual.transform.localPosition = part.LocalPosition;

            cellVisual = visual.AddComponent<SpriteRenderer>();
            cellVisual.sprite = PrototypeBootstrap.GetSquareSprite();
            cellVisual.color = color;
            cellVisual.enabled = false;

            VoxelBlockBuilder.BuildVoxelGrid(visual.transform, part.Name, part.Size, color);

            GameObject colliderObject = new GameObject($"{part.Name} Collider");
            colliderObject.transform.SetParent(collisionRoot, false);
            colliderObject.transform.localPosition = part.LocalPosition - collisionCentre;

            BoxCollider2D partCollider = colliderObject.AddComponent<BoxCollider2D>();
            partCollider.size = part.Size;
            partCollider.edgeRadius = 0f;
            partCollider.usedByComposite = true;
            return partCollider;
        }

        private static void GetPiecePartBounds(
            List<PiecePartGeometry> parts,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int index = 0; index < parts.Count; index++)
            {
                PiecePartGeometry part = parts[index];
                Vector2 half = part.Size * .5f;
                minimum = Vector2.Min(minimum, part.LocalPosition - half);
                maximum = Vector2.Max(maximum, part.LocalPosition + half);
            }

            if (parts.Count == 0)
            {
                minimum = Vector2.zero;
                maximum = Vector2.one * .01f;
            }
        }

        private static Vector2 CellWorldPosition(GravityLevelDefinition level, Vector2Int cell)
        {
            float fineCellSize = 1f / level.subdivisions;
            return new Vector2(
                -level.boardColumns * .5f + (cell.x + .5f) * fineCellSize,
                -level.boardRows * .5f + (cell.y + .5f) * fineCellSize);
        }

        private static Vector2 GridCellWorldPosition(GravityLevelDefinition level, Vector2Int cell)
        {
            return new Vector2(
                -level.boardColumns * .5f + cell.x + .5f,
                -level.boardRows * .5f + cell.y + .5f);
        }

        private readonly struct PiecePartGeometry
        {
            public PiecePartGeometry(string name, Vector2 localPosition, Vector2 size)
            {
                Name = name;
                LocalPosition = localPosition;
                Size = size;
            }

            public string Name { get; }
            public Vector2 LocalPosition { get; }
            public Vector2 Size { get; }
        }

        private readonly struct PieceRuntimeContent
        {
            public PieceRuntimeContent(
                int progressUnits,
                List<BoxCollider2D> collisionCells,
                List<SpriteRenderer> collisionCellVisuals)
            {
                ProgressUnits = progressUnits;
                CollisionCells = collisionCells;
                CollisionCellVisuals = collisionCellVisuals;
            }

            public int ProgressUnits { get; }
            public List<BoxCollider2D> CollisionCells { get; }
            public List<SpriteRenderer> CollisionCellVisuals { get; }
        }
    }
}
