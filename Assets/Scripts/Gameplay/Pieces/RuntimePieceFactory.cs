using System.Collections.Generic;
using DG.Tweening;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public readonly struct RuntimePieceFragmentCell
    {
        public RuntimePieceFragmentCell(Vector2 localPosition, Vector2 size)
        {
            LocalPosition = localPosition;
            Size = size;
        }

        public Vector2 LocalPosition { get; }
        public Vector2 Size { get; }
    }

    public static class RuntimePieceFactory
    {
        private const string GridBlockName = "Grid Block";
        private const string BlockCellName = "Block Cell";
        private const string HookCellName = "Hook Cell";

        private static Material sharedOutlineMaterial;
        private static IRuntimePieceRootProvider rootProvider;
        private static PieceVisualConfig pieceVisualConfig;

        public static void SetRootProvider(IRuntimePieceRootProvider provider)
        {
            rootProvider = provider ?? throw new System.ArgumentNullException(nameof(provider));
        }

        public static void SetVisualConfig(PieceVisualConfig config)
        {
            pieceVisualConfig = config;
        }


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRootProvider()
        {
            rootProvider = null;
            sharedOutlineMaterial = null;
            pieceVisualConfig = null;
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
            ResolveVisual(definition, out Color visualColor, out Sprite voxelSprite);
            PrepareRoot(piece.transform, level, definition);
            ConfigureBody(root.Body, level);
            ConfigureComposite(root.CompositeCollider);

            PieceRuntimeContent content = BuildRuntimeContent(
                root.Piece,
                level,
                definition,
                visualColor,
                voxelSprite);

            root.CompositeCollider.GenerateGeometry();
            ConfigureOutline(root.Outline, root.CompositeCollider);

            PuzzlePiece puzzlePiece = root.Piece;
            ApplyPieceSetup(
                puzzlePiece,
                sourcePieceId,
                definition,
                visualColor,
                root.CompositeCollider,
                content);

            return puzzlePiece;
        }

        public static RuntimePieceRoot RentSplitRoot(
            string pieceName,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale)
        {
            if (rootProvider == null)
                throw new System.InvalidOperationException(
                    "[PiecePool] RuntimePieceFactory has not been configured by RuntimePieceFactoryBootstrap.");

            RuntimePieceRoot root = rootProvider.Create(pieceName);
            root.Transform.SetParent(null, true);
            root.Transform.position = position;
            root.Transform.rotation = rotation;
            root.Transform.localScale = scale;
            ClearGeneratedContent(root.Transform);
            ConfigureComposite(root.CompositeCollider);
            return root;
        }

        /// <summary>
        /// Creates a hammer fragment from the same authored BlockPiece prefab
        /// used by normal level pieces. No live visual or collider hierarchy is
        /// moved between roots.
        /// </summary>
        public static PuzzlePiece CreateFragment(
            string pieceName,
            GravityLevelDefinition level,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            IReadOnlyList<RuntimePieceFragmentCell> cells,
            Color color,
            float remainingProgress)
        {
            if (rootProvider == null)
                throw new System.InvalidOperationException(
                    "[PiecePool] RuntimePieceFactory has not been configured by RuntimePieceFactoryBootstrap.");

            RuntimePieceRoot root = rootProvider.Create(pieceName);
            root.Transform.SetParent(null, true);
            root.Transform.position = position;
            root.Transform.rotation = rotation;
            root.Transform.localScale = scale;
            ConfigureFragment(root.Piece, root.Body, root.CompositeCollider, root.Outline,
                level, cells, color, remainingProgress);
            return root.Piece;
        }

        /// <summary>
        /// Reuses an existing prefab root for the retained portion of a hammer
        /// hit. Clearing slots first also returns their VoxelShards to the pool.
        /// </summary>
        public static void RebuildFragment(
            PuzzlePiece piece,
            GravityLevelDefinition level,
            IReadOnlyList<RuntimePieceFragmentCell> cells,
            Color color,
            float remainingProgress)
        {
            if (piece == null)
                return;

            ConfigureFragment(piece, piece.Body, piece.CompositeCollider, piece.Outline,
                level, cells, color, remainingProgress);
        }

        public static void ResetPiecePartSlot(PiecePartSlot slot)
        {
            if (slot == null)
                return;

            VoxelShard[] voxels = slot.GetComponentsInChildren<VoxelShard>(true);
            for (int index = 0; index < voxels.Length; index++)
            {
                if (voxels[index] != null)
                    VoxelBlockBuilder.ReturnVoxel(voxels[index]);
            }

            slot.ResetSlot();
        }

        public static void RefreshOutline(PuzzlePiece piece)
        {
            if (piece == null || piece.CompositeCollider == null || piece.Outline == null)
                return;

            ConfigureOutline(piece.Outline, piece.CompositeCollider);
        }

        /// <summary>
        /// Restores a pooled BlockPiece root to an inert prefab-ready state.
        /// This is the one cleanup point for content generated into authored
        /// slots, presentation tweens, colliders and rigidbody state.
        /// </summary>
        public static void ResetPooledPiece(PuzzlePiece piece)
        {
            if (piece == null)
                return;

            DOTween.Kill(piece.gameObject);
            ClearGeneratedContent(piece.transform);

            piece.ResetToPooledPhysics();

            if (piece.CompositeCollider != null)
                piece.CompositeCollider.enabled = false;
            if (piece.Outline != null)
            {
                piece.Outline.positionCount = 0;
                piece.Outline.enabled = false;
            }
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
            body.angularDrag = 0f;
        }

        private static void ConfigureFragment(
            PuzzlePiece piece,
            Rigidbody2D body,
            CompositeCollider2D composite,
            LineRenderer outline,
            GravityLevelDefinition level,
            IReadOnlyList<RuntimePieceFragmentCell> cells,
            Color color,
            float remainingProgress)
        {
            if (piece == null || body == null || composite == null || outline == null ||
                level == null || cells == null || cells.Count == 0)
                throw new System.ArgumentException("A hammer fragment requires a configured BlockPiece prefab and at least one cell.");

            if (cells.Count > piece.PartSlotCount)
                throw new System.InvalidOperationException(
                    $"[PiecePool] BlockPiece prefab has {piece.PartSlotCount} part slots but hammer fragment needs {cells.Count}.");

            ClearGeneratedContent(piece.transform);
            ConfigureBody(body, level);
            ConfigureComposite(composite);

            List<BoxCollider2D> collisionCells = new List<BoxCollider2D>(cells.Count);
            List<SpriteRenderer> cellVisuals = new List<SpriteRenderer>(cells.Count);
            for (int index = 0; index < cells.Count; index++)
            {
                RuntimePieceFragmentCell fragmentCell = cells[index];
                PiecePartSlot slot = piece.GetPartSlot(index);
                BoxCollider2D collider = ConfigurePiecePartSlot(
                    slot,
                    new PiecePartGeometry(BlockCellName, fragmentCell.LocalPosition, fragmentCell.Size),
                    color,
                    null,
                    out SpriteRenderer visual);
                collisionCells.Add(collider);
                cellVisuals.Add(visual);
            }

            composite.GenerateGeometry();
            piece.ConfigureProgressUnits(Mathf.Max(1, Mathf.CeilToInt(remainingProgress)));
            piece.ConfigureVisualColor(color);
            piece.ConfigureCollisionGeometry(composite, collisionCells, cellVisuals);
            piece.ConfigureRemainingProgress(remainingProgress);
            ConfigureOutline(outline, composite);
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
            Color visualColor,
            CompositeCollider2D pieceComposite,
            PieceRuntimeContent content)
        {
            puzzlePiece.Configure(new PieceRuntimeSetup(
                sourcePieceId,
                Mathf.Max(1, content.ProgressUnits),
                visualColor,
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
            PuzzlePiece puzzlePiece,
            GravityLevelDefinition level,
            PieceDefinition definition,
            Color visualColor,
            Sprite voxelSprite)
        {
            float fineCellSize = 1f / level.subdivisions;
            List<PiecePartGeometry> parts = BuildPartGeometry(level, definition, fineCellSize, out int progressUnits);
            GetPiecePartBounds(parts, out Vector2 minimum, out Vector2 maximum);
            if (parts.Count > puzzlePiece.PartSlotCount)
            {
                throw new System.InvalidOperationException(
                    $"[PiecePool] BlockPiece prefab has {puzzlePiece.PartSlotCount} part slots but '{definition.name}' needs {parts.Count}. Add more authored slots before Play.");
            }

            List<BoxCollider2D> collisionCells = new List<BoxCollider2D>(parts.Count);
            List<SpriteRenderer> collisionCellVisuals = new List<SpriteRenderer>(parts.Count);
            for (int index = 0; index < parts.Count; index++)
            {
                PiecePartSlot slot = puzzlePiece.GetPartSlot(index);
                BoxCollider2D collider = ConfigurePiecePartSlot(
                    slot,
                    parts[index],
                    visualColor,
                    voxelSprite,
                    out SpriteRenderer cellVisual);
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
            VoxelShard[] attachedVoxels = pieceTransform.GetComponentsInChildren<VoxelShard>(true);
            for (int index = 0; index < attachedVoxels.Length; index++)
            {
                VoxelShard voxel = attachedVoxels[index];
                if (voxel != null)
                    VoxelBlockBuilder.ReturnVoxel(voxel);
            }

            PiecePartSlot[] partSlots = pieceTransform.GetComponentsInChildren<PiecePartSlot>(true);
            for (int index = 0; index < partSlots.Length; index++)
                partSlots[index].ResetSlot();

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

            int pathIndex = FindOuterPathIndex(pieceComposite);
            int pointCount = pieceComposite.GetPathPointCount(pathIndex);
            outline.positionCount = pointCount;
            Vector2[] path = new Vector2[pointCount];
            pieceComposite.GetPath(pathIndex, path);
            for (int index = 0; index < pointCount; index++)
                outline.SetPosition(index, new Vector3(path[index].x, path[index].y, 0f));
        }

        private static int FindOuterPathIndex(CompositeCollider2D pieceComposite)
        {
            int outerPathIndex = 0;
            float largestArea = 0f;
            for (int pathIndex = 0; pathIndex < pieceComposite.pathCount; pathIndex++)
            {
                int count = pieceComposite.GetPathPointCount(pathIndex);
                if (count < 3)
                    continue;

                Vector2[] path = new Vector2[count];
                pieceComposite.GetPath(pathIndex, path);
                float signedArea = 0f;
                for (int index = 0; index < count; index++)
                {
                    Vector2 current = path[index];
                    Vector2 next = path[(index + 1) % count];
                    signedArea += current.x * next.y - next.x * current.y;
                }

                float area = Mathf.Abs(signedArea);
                if (area <= largestArea)
                    continue;

                largestArea = area;
                outerPathIndex = pathIndex;
            }

            return outerPathIndex;
        }

        private static BoxCollider2D ConfigurePiecePartSlot(
            PiecePartSlot slot,
            PiecePartGeometry part,
            Color color,
            Sprite voxelSprite,
            out SpriteRenderer cellVisual)
        {
            cellVisual = slot.Visual;
            slot.transform.localPosition = part.LocalPosition;
            slot.transform.localScale = Vector3.one;
            cellVisual.sprite = PrototypeBootstrap.GetSquareSprite();
            cellVisual.color = color;
            cellVisual.enabled = false;

            VoxelBlockBuilder.BuildVoxelGrid(slot.transform, part.Name, part.Size, color, voxelSprite);

            BoxCollider2D partCollider = slot.Collision;
            partCollider.transform.localPosition = part.LocalPosition;
            partCollider.transform.localScale = Vector3.one;
            partCollider.size = part.Size;
            partCollider.edgeRadius = 0f;
            partCollider.usedByComposite = true;
            partCollider.enabled = true;
            return partCollider;
        }

        private static void ResolveVisual(
            PieceDefinition definition,
            out Color color,
            out Sprite voxelSprite)
        {
            color = definition.color;
            voxelSprite = null;
            if (pieceVisualConfig == null || string.IsNullOrWhiteSpace(definition.visualId) ||
                !pieceVisualConfig.TryGet(definition.visualId, out PieceVisualDefinition visual))
                return;

            color = visual.Tint;
            voxelSprite = visual.Sprite;
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
