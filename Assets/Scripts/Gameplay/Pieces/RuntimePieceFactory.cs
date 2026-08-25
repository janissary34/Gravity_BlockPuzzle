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
        private static bool useVoxelShardGrid = false;
        private static readonly HashSet<string> warnedMissingVisualIds = new HashSet<string>();

        public static void SetRootProvider(IRuntimePieceRootProvider provider)
        {
            rootProvider = provider ?? throw new System.ArgumentNullException(nameof(provider));
        }

        public static void SetVisualConfig(PieceVisualConfig config)
        {
            pieceVisualConfig = config;
        }

        public static void SetPresentationMode(bool useVoxelGrid)
        {
            useVoxelShardGrid = useVoxelGrid;
        }


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRootProvider()
        {
            rootProvider = null;
            sharedOutlineMaterial = null;
            pieceVisualConfig = null;
            warnedMissingVisualIds.Clear();
        }

        public static PuzzlePiece Create(
            GravityLevelDefinition level,
            PieceDefinition definition,
            int sourcePieceId)
        {
            if (rootProvider == null)
                throw new System.InvalidOperationException(
                    "[PiecePool] RuntimePieceFactory has not been configured by RuntimePieceFactoryBootstrap.");

            if (definition == null)
                throw new System.ArgumentNullException(nameof(definition));

            // The level editor can retain an empty entry after its final cell is
            // removed. It has no geometry to shred, so renting a BlockPiece for
            // it would create an unreachable progress unit.
            if (!HasBlockCells(definition))
            {
                Debug.LogWarning($"[PiecePool] Ignoring empty authored piece '{definition.name}' (id: {sourcePieceId}).");
                return null;
            }

            RuntimePieceRoot root = rootProvider.Create(definition.name);
            GameObject piece = root.GameObject;
            ResolveVisual(definition, out Color visualColor, out Sprite voxelSprite);
            PrepareRoot(root.Piece, level, definition);
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
            ClearGeneratedContent(root.Piece);
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

        public static void ResetPiecePartSlot(PuzzlePiece piece, PiecePartSlot slot)
        {
            if (slot == null)
                return;

            piece?.RemoveVoxelPresentation(slot.VoxelShards);
            slot.ReturnVoxels();
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
            ClearGeneratedContent(piece);

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
            PuzzlePiece piece,
            GravityLevelDefinition level,
            PieceDefinition definition)
        {
            Transform pieceTransform = piece.transform;
            pieceTransform.position = CellWorldPosition(level, definition.origin);
            pieceTransform.rotation = Quaternion.identity;
            pieceTransform.localScale = Vector3.one;
            ClearGeneratedContent(piece);
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

            ClearGeneratedContent(piece);
            ConfigureBody(body, level);
            ConfigureComposite(composite);

            List<BoxCollider2D> collisionCells = new List<BoxCollider2D>(cells.Count);
            List<SpriteRenderer> cellVisuals = new List<SpriteRenderer>(cells.Count);
            List<VoxelShard> voxelShards = new List<VoxelShard>(
                cells.Count * VoxelBlockBuilder.Subdivisions * VoxelBlockBuilder.Subdivisions);
            for (int index = 0; index < cells.Count; index++)
            {
                RuntimePieceFragmentCell fragmentCell = cells[index];
                PiecePartSlot slot = piece.GetPartSlot(index);
                BoxCollider2D collider = ConfigurePiecePartSlot(
                    slot,
                    new PiecePartGeometry(BlockCellName, fragmentCell.LocalPosition, fragmentCell.Size),
                    color,
                    null,
                    out SpriteRenderer visual,
                    voxelShards);
                collisionCells.Add(collider);
                cellVisuals.Add(visual);
            }

            composite.GenerateGeometry();
            piece.ConfigureProgressUnits(Mathf.Max(1, Mathf.CeilToInt(remainingProgress)));
            piece.ConfigureVisualColor(color);
            piece.ConfigureCollisionGeometry(composite, collisionCells, cellVisuals);
            piece.ConfigureVoxelPresentation(voxelShards);
            piece.ConfigureRemainingProgress(remainingProgress);
            ConfigureOutline(outline, composite);
            ConfigureOutlinePresentation(piece);
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
            if (content.VoxelShards != null && content.VoxelShards.Count > 0)
                puzzlePiece.ConfigureVoxelPresentation(content.VoxelShards);
            else if (content.CollisionCellVisuals != null)
                puzzlePiece.ConfigureSolidCellPresentation(content.CollisionCellVisuals);

            ConfigureOutlinePresentation(puzzlePiece);
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
            List<VoxelShard> voxelShards = new List<VoxelShard>(
                parts.Count * VoxelBlockBuilder.Subdivisions * VoxelBlockBuilder.Subdivisions);
            for (int index = 0; index < parts.Count; index++)
            {
                PiecePartSlot slot = puzzlePiece.GetPartSlot(index);
                BoxCollider2D collider = ConfigurePiecePartSlot(
                    slot,
                    parts[index],
                    visualColor,
                    voxelSprite,
                    out SpriteRenderer cellVisual,
                    voxelShards);
                collisionCells.Add(collider);
                collisionCellVisuals.Add(cellVisual);
            }

            return new PieceRuntimeContent(
                progressUnits,
                collisionCells,
                collisionCellVisuals,
                voxelShards);
        }

        private static void ClearGeneratedContent(PuzzlePiece piece)
        {
            if (piece == null)
                return;

            IReadOnlyList<PiecePartSlot> partSlots = piece.PartSlots;
            for (int index = 0; index < partSlots.Count; index++)
            {
                PiecePartSlot slot = partSlots[index];
                if (slot == null)
                    continue;

                slot.ReturnVoxels();
                slot.ResetSlot();
            }

            piece.ClearVoxelPresentation();
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

        private static bool HasBlockCells(PieceDefinition definition)
        {
            if (definition.cells == null)
                return false;

            for (int index = 0; index < definition.cells.Count; index++)
            {
                if (definition.cells[index].type == PieceCellType.Block)
                    return true;
            }

            return false;
        }

        private static void ConfigureOutline(LineRenderer outline, CompositeCollider2D pieceComposite)
        {
            outline.enabled = true;
            outline.useWorldSpace = false;
            outline.loop = true;
            outline.positionCount = 0;
            outline.startWidth = GetRestingOutlineWidth();
            outline.endWidth = GetRestingOutlineWidth();
            outline.numCornerVertices = GetOutlineCornerVertices();
            outline.numCapVertices = GetOutlineCapVertices();
            outline.sortingOrder = GetRestingOutlineSortingOrder();

            if (sharedOutlineMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    sharedOutlineMaterial = new Material(shader) { name = "Shared Outline Material" };
            }

            if (sharedOutlineMaterial != null)
                outline.sharedMaterial = sharedOutlineMaterial;

            outline.startColor = GetRestingOutlineColor();
            outline.endColor = GetRestingOutlineColor();

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

        private static void ConfigureOutlinePresentation(PuzzlePiece piece)
        {
            if (piece == null)
                return;

            piece.ConfigureOutlinePresentation(
                GetRestingOutlineWidth(),
                GetSelectedOutlineWidth(),
                GetRestingOutlineColor(),
                GetSelectedOutlineColor(),
                GetRestingOutlineSortingOrder(),
                GetSelectedOutlineSortingOrder());
        }

        private static float GetRestingOutlineWidth() => pieceVisualConfig != null ? pieceVisualConfig.RestingOutlineWidth : 0.05f;
        private static float GetSelectedOutlineWidth() => pieceVisualConfig != null ? pieceVisualConfig.SelectedOutlineWidth : 0.08f;
        private static Color GetRestingOutlineColor() => pieceVisualConfig != null ? pieceVisualConfig.RestingOutlineColor : Color.black;
        private static Color GetSelectedOutlineColor() => pieceVisualConfig != null ? pieceVisualConfig.SelectedOutlineColor : Color.white;
        private static int GetRestingOutlineSortingOrder() => pieceVisualConfig != null ? pieceVisualConfig.RestingOutlineSortingOrder : 10;
        private static int GetSelectedOutlineSortingOrder() => pieceVisualConfig != null ? pieceVisualConfig.SelectedOutlineSortingOrder : 20;
        private static int GetOutlineCornerVertices() => pieceVisualConfig != null ? pieceVisualConfig.OutlineCornerVertices : 4;
        private static int GetOutlineCapVertices() => pieceVisualConfig != null ? pieceVisualConfig.OutlineCapVertices : 4;

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
            out SpriteRenderer cellVisual,
            List<VoxelShard> voxelShards = null)
        {
            cellVisual = slot.Visual;
            slot.transform.localPosition = part.LocalPosition;
            slot.transform.localScale = Vector3.one;
            cellVisual.sprite = voxelSprite != null ? voxelSprite : PrototypeBootstrap.GetSquareSprite();
            cellVisual.color = color;
            cellVisual.sortingOrder = 5;
            cellVisual.transform.localScale = new Vector3(part.Size.x, part.Size.y, 1f);

            if (useVoxelShardGrid)
            {
                cellVisual.enabled = false;
                VoxelBlockBuilder.BuildVoxelGrid(slot.transform, part.Name, part.Size, color, voxelSprite, voxelShards, slot);
            }
            else
            {
                cellVisual.enabled = true;
            }

            BoxCollider2D partCollider = slot.Collision;
            // The authored collider lives on the slot itself (or beneath it).
            // The slot already owns the part offset, so applying that offset to
            // the collider as well moves its hit/physics geometry twice as far
            // as the rendered voxel grid. Besides making collisions incorrect,
            // that left booster taps testing an invisible, displaced shape.
            if (partCollider.transform == slot.transform ||
                partCollider.transform.IsChildOf(slot.transform))
            {
                partCollider.transform.localPosition = Vector3.zero;
            }
            else
            {
                partCollider.transform.localPosition = part.LocalPosition;
            }
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
            if (string.IsNullOrWhiteSpace(definition.visualId))
                return;

            if (pieceVisualConfig == null ||
                !pieceVisualConfig.TryGet(definition.visualId, out PieceVisualDefinition visual))
            {
                WarnMissingVisualDefinition(definition.visualId);
                return;
            }

            color = visual.Tint;
            voxelSprite = visual.Sprite;
        }

        private static void WarnMissingVisualDefinition(string visualId)
        {
            if (!warnedMissingVisualIds.Add(visualId))
                return;

            string source = pieceVisualConfig == null
                ? "no PieceVisualConfig is assigned"
                : $"'{pieceVisualConfig.name}' has no matching definition";
            Debug.LogWarning(
                $"[PieceVisualConfig] visualId '{visualId}' cannot be resolved because {source}. " +
                "The authored level color is being used as the safe fallback.");
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
                List<SpriteRenderer> collisionCellVisuals,
                List<VoxelShard> voxelShards)
            {
                ProgressUnits = progressUnits;
                CollisionCells = collisionCells;
                CollisionCellVisuals = collisionCellVisuals;
                VoxelShards = voxelShards;
            }

            public int ProgressUnits { get; }
            public List<BoxCollider2D> CollisionCells { get; }
            public List<SpriteRenderer> CollisionCellVisuals { get; }
            public List<VoxelShard> VoxelShards { get; }
        }
    }
}
