using System.Collections.Generic;
using DG.Tweening;
using GravityPuzzle.Config;
using GravityPuzzle.Presentation.VFX;
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
    //arda
    public static class RuntimePieceFactory
    {
        private const string GridBlockName = "Grid Block";
        private const string BlockCellName = "Block Cell";
        private const string HookCellName = "Hook Cell";

        private static Material sharedOutlineMaterial;
        private static IRuntimePieceRootProvider rootProvider;
        private static PieceVisualConfig pieceVisualConfig;
        private static IIceBlockParticleVfx iceParticleVfx;
        private static bool useVoxelShardGrid = false;
        private static readonly HashSet<string> warnedMissingVisualIds = new HashSet<string>();
        private static readonly HashSet<string> warnedMissingPaletteIds = new HashSet<string>();

        public static void SetRootProvider(IRuntimePieceRootProvider provider)
        {
            rootProvider = provider ?? throw new System.ArgumentNullException(nameof(provider));
        }

        public static void SetVisualConfig(PieceVisualConfig config)
        {
            pieceVisualConfig = config;
        }

        public static void SetIceParticleVfx(IIceBlockParticleVfx particleVfx)
        {
            iceParticleVfx = particleVfx;
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
            iceParticleVfx = null;
            warnedMissingVisualIds.Clear();
            warnedMissingPaletteIds.Clear();
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
            ResolveVisual(
                definition,
                out Color visualColor,
                out Sprite voxelSprite,
                out Material presentationMaterial);
            PrepareRoot(root.Piece, level, definition);
            ConfigureBody(root.Body, level);
            ConfigureComposite(root.CompositeCollider);

            PieceRuntimeContent content = BuildRuntimeContent(
                root.Piece,
                level,
                definition,
                visualColor,
                voxelSprite,
                presentationMaterial);

            root.CompositeCollider.GenerateGeometry();
            ConfigureOutline(root.Outline, root.CompositeCollider);

            PuzzlePiece puzzlePiece = root.Piece;
            ApplyPieceSetup(
                puzzlePiece,
                sourcePieceId,
                definition,
                visualColor,
                presentationMaterial,
                root.CompositeCollider,
                content);
            ConfigureWholePiecePresentation(puzzlePiece, level, definition);

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
            Material presentationMaterial,
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
            root.Piece.ConfigurePresentationMaterials(
                presentationMaterial,
                pieceVisualConfig != null ? pieceVisualConfig.IceMaterial : null);
            ConfigureFragment(root.Piece, root.Body, root.CompositeCollider, root.Outline,
                level, cells, color, presentationMaterial, remainingProgress);
            // A pooled Rigidbody2D can retain its previous physics pose until
            // Unity's next transform sync. Fragment grid registration happens
            // immediately below its creation, so explicitly align the physics
            // body with the root before that model is derived.
            root.Body.position = position;
            root.Body.rotation = rotation.eulerAngles.z;
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
                level, cells, color, piece.PresentationMaterial, remainingProgress);
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
            // This root is pooled. PrepareRoot has already assigned the new
            // authored transform position, but a Rigidbody2D can still retain
            // the physics pose from its previous owner until it is explicitly
            // updated. Grid-fall tweens drive the Rigidbody2D, so leaving that
            // stale pose in place makes a newly spawned piece travel sideways
            // towards its grid target and visibly pass through neighbours.
            body.position = body.transform.position;
            body.simulated = true;
            body.gravityScale = level.gravityScale;
            body.mass = 1f;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = false;
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
            Material presentationMaterial,
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
                    GetCellPresentationSprite(GetFallbackSprite(false), fragmentCell.Size),
                    presentationMaterial,
                    out SpriteRenderer visual,
                    voxelShards);
                collisionCells.Add(collider);
                cellVisuals.Add(visual);
            }

            composite.GenerateGeometry();
            // Build the presentation outline while adjoining modular cells
            // still share their full edges. ConfigureCollisionGeometry applies
            // a small collision skin afterwards; generating the outline from
            // that shrunken composite turns each cell into a separate path and
            // leaves a selected fragment outlined only around one cell.
            ConfigureOutline(outline, composite);
            piece.ConfigureProgressUnits(Mathf.Max(1, Mathf.CeilToInt(remainingProgress)));
            piece.ConfigureVisualColor(color);
            piece.ConfigurePresentationMaterials(
                presentationMaterial,
                pieceVisualConfig != null ? pieceVisualConfig.IceMaterial : null);
            piece.ConfigureCollisionGeometry(composite, collisionCells, cellVisuals);
            if (voxelShards.Count > 0)
                piece.ConfigureVoxelPresentation(voxelShards);
            else
                piece.ConfigureSolidCellPresentation(cellVisuals);
            piece.ConfigureRemainingProgress(remainingProgress);
            ConfigureOutlinePresentation(piece);
            // A hammer split can create an arbitrary partial topology. Even
            // when that topology happens to resemble an atlas silhouette, the
            // original art's baked shadow/pivot no longer belongs to the
            // retained cells. Keep every fragment on the modular artist-brick
            // path; this makes its geometry, outline and shading deterministic.
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
            Material presentationMaterial,
            CompositeCollider2D pieceComposite,
            PieceRuntimeContent content)
        {
            puzzlePiece.ConfigureIceParticleVfx(iceParticleVfx);
            puzzlePiece.ConfigurePresentationMaterials(
                presentationMaterial,
                pieceVisualConfig != null ? pieceVisualConfig.IceMaterial : null);
            puzzlePiece.Configure(new PieceRuntimeSetup(
                sourcePieceId,
                Mathf.Max(1, content.ProgressUnits),
                visualColor,
                pieceComposite,
                content.CollisionCells,
                content.CollisionCellVisuals,
                pieceVisualConfig != null ? pieceVisualConfig.IceOverlaySprite : null,
                pieceVisualConfig != null ? pieceVisualConfig.IceOverlayTint : new Color(1f, 1f, 1f, .42f),
                pieceVisualConfig != null ? pieceVisualConfig.IceFrostTint : new Color(1f, 1f, 1f, .18f),
                pieceVisualConfig != null ? pieceVisualConfig.BombOverlaySprite : null,
                pieceVisualConfig != null ? pieceVisualConfig.BombOverlayFill : .82f,
                definition.frozenMoveCount,
                definition.iceCounterFontSize,
                definition.iceCounterTextColor,
                definition.iceCounterOutlineColor,
                definition.iceCounterOutlineWidth,
                definition.iceCounterOffset,
                definition.specialBlockType,
                definition.bombTimerSeconds,
                definition.bombCounterFontSize,
                definition.bombCounterTextColor,
                definition.bombCounterOutlineColor,
                definition.bombCounterOutlineWidth,
                definition.bombCounterOffset));
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
            Sprite voxelSprite,
            Material presentationMaterial)
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
                    GetCellPresentationSprite(voxelSprite, parts[index].Size),
                    presentationMaterial,
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

            // A pooled root can previously have carried a complete atlas
            // silhouette. Clear it before restoring modular part slots so a
            // hammer fragment or a new level piece never inherits a stale
            // whole-piece renderer from its former owner.
            piece.ClearWholePiecePresentation();

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
        private static float GetSelectedOutlineWidth() => pieceVisualConfig != null ? pieceVisualConfig.SelectedOutlineWidth : 0.04f;
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
            Material presentationMaterial,
            out SpriteRenderer cellVisual,
            List<VoxelShard> voxelShards = null)
        {
            cellVisual = slot.Visual;
            bool visualUsesSlotTransform = cellVisual.transform == slot.transform;
            slot.transform.localPosition = part.LocalPosition;
            slot.transform.localRotation = Quaternion.identity;
            slot.transform.localScale = Vector3.one;
            if (!visualUsesSlotTransform)
            {
                cellVisual.transform.localPosition = Vector3.zero;
                cellVisual.transform.localRotation = Quaternion.identity;
            }

            Sprite presentationSprite = voxelSprite != null
                ? voxelSprite
                : PrototypeBootstrap.GetSquareSprite();
            cellVisual.sprite = presentationSprite;
            cellVisual.color = presentationMaterial != null ? Color.white : color;
            cellVisual.sharedMaterial = presentationMaterial;
            cellVisual.flipX = false;
            cellVisual.flipY = false;
            cellVisual.sortingOrder = 5;

            // VoxelShard rendering is retained only for legacy content that
            // has no artist-authored fallback sprite. Rendering a brick atlas
            // through an 8x8 voxel grid produces the old tiny/default voxel
            // appearance. Solid slots are already pooled on BlockPiece and
            // remain the authoritative presentation for all new art.
            bool useLegacyVoxelFallback = useVoxelShardGrid && voxelSprite == null;
            if (useLegacyVoxelFallback)
            {
                // The slot is the VoxelShard parent. Keep it unscaled so
                // each shard's part-size geometry remains in board space.
                if (!visualUsesSlotTransform)
                    cellVisual.transform.localScale = Vector3.one;
                cellVisual.enabled = false;
                VoxelBlockBuilder.BuildVoxelGrid(slot.transform, part.Name, part.Size, color, voxelSprite, voxelShards, slot);
            }
            else
            {
                // Scene_Tuna sprites are authored at their own pixels-per-unit.
                // Scale the presentation to the physical cell's bounds rather
                // than assuming a one-unit sprite. This keeps visual and
                // collider geometry aligned for standard, fragment and ice
                // fallback paths.
                Vector2 spriteBounds = presentationSprite != null
                    ? presentationSprite.bounds.size
                    : Vector2.one;
                Vector3 presentationScale = new Vector3(
                    spriteBounds.x > 0f ? part.Size.x / spriteBounds.x : part.Size.x,
                    spriteBounds.y > 0f ? part.Size.y / spriteBounds.y : part.Size.y,
                    1f);
                if (visualUsesSlotTransform)
                    slot.transform.localScale = presentationScale;
                else
                    cellVisual.transform.localScale = presentationScale;
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
            out Sprite voxelSprite,
            out Material presentationMaterial)
        {
            bool isFrozenIce = definition.specialBlockType == PieceSpecialBlockType.Ice &&
                               definition.frozenMoveCount > 0;
            color = definition.color;
            presentationMaterial = ResolvePaletteMaterial(definition.paletteId);
            if (presentationMaterial != null)
                color = presentationMaterial.color;
            voxelSprite = GetFallbackSprite(isFrozenIce);
            if (string.IsNullOrWhiteSpace(definition.visualId))
                return;

            if (pieceVisualConfig == null ||
                !pieceVisualConfig.TryGet(definition.visualId, out PieceVisualDefinition visual))
            {
                WarnMissingVisualDefinition(definition.visualId);
                return;
            }

            if (presentationMaterial == null)
                color = visual.Tint;
            // ice_block was authored for the retired ice presentation. The
            // new atlas is only valid for an exact shape; all other frozen
            // shapes use the current 1x1 brick plus the generic frost layer.
            // Never reintroduce the old sprite in that fallback path.
            if (!isFrozenIce)
                voxelSprite = visual.Sprite;
        }

        private static Material ResolvePaletteMaterial(string paletteId)
        {
            if (string.IsNullOrWhiteSpace(paletteId))
                return null;

            if (pieceVisualConfig != null &&
                pieceVisualConfig.TryGetPaletteMaterial(paletteId, out Material material))
                return material;

            if (warnedMissingPaletteIds.Add(paletteId))
            {
                string source = pieceVisualConfig == null
                    ? "no PieceVisualConfig is assigned"
                    : $"'{pieceVisualConfig.name}' has no matching palette";
                Debug.LogWarning(
                    $"[PieceVisualConfig] paletteId '{paletteId}' cannot be resolved because {source}. " +
                    "The piece will use its legacy colour until the palette is fixed.");
            }

            return null;
        }

        private static Sprite GetFallbackSprite(bool isIce)
        {
            if (pieceVisualConfig == null)
                return null;

            if (!isIce)
                return pieceVisualConfig.NormalFallbackSprite;

            // The artist supplied atlas silhouettes for the supported ice
            // shapes, not a standalone 1x1 ice brick. Unsupported ice
            // fragments still need to stay on the new modular presentation;
            // their normal brick is covered by the pooled ice overlay later
            // in the piece setup. Falling back to null here would reactivate
            // the legacy 8x8 voxel path.
            return pieceVisualConfig.IceFallbackSprite != null
                ? pieceVisualConfig.IceFallbackSprite
                : pieceVisualConfig.NormalFallbackSprite;
        }

        private static Sprite GetCellPresentationSprite(Sprite fallbackSprite, Vector2 partSize)
        {
            // A partial, broken or newly authored shape has no whole-piece
            // silhouette. Every pooled slot therefore renders the artist's
            // 1x1 brick at its own board-cell size. This keeps the fallback
            // visually consistent without letting VoxelShard subdivide the
            // brick image again.
            return fallbackSprite;
        }

        private static void ConfigureWholePiecePresentation(
            PuzzlePiece piece,
            GravityLevelDefinition level,
            PieceDefinition definition)
        {
            if (piece == null || pieceVisualConfig == null ||
                !TryGetAtlasShapeKey(level, definition, out string shapeKey) ||
                !pieceVisualConfig.TryGetShapePresentation(
                    shapeKey,
                    out PieceShapeVisualDefinition visual,
                    out PieceShapeVisualTransform transform))
                return;

            bool isFrozenIce = definition.specialBlockType == PieceSpecialBlockType.Ice &&
                               definition.frozenMoveCount > 0;
            // An atlas normal sprite is not an ice fallback. If the artist has
            // not supplied this exact ice silhouette, preserve its geometry by
            // keeping the modular brick presentation and applying the generic
            // frost layer instead of exposing normal or legacy ice art.
            if (isFrozenIce && visual.IceSprite == null)
                return;

            // A normal sprite is retained underneath the ice art so the ice
            // release returns to the same authored silhouette.
            piece.ConfigureWholePiecePresentation(
                visual.NormalSprite,
                visual.IceSprite,
                transform,
                visual.PreferModularIce);
        }

        /// <summary>
        /// Matches the level's authoritative fine-cell geometry to an atlas
        /// silhouette. Atlas art is authored in module units while levels may
        /// describe the exact same silhouette in four-by-four fine cells.
        /// A candidate is valid only when every atlas module maps to a fully
        /// occupied square of the same scale; irregular damage safely falls
        /// back to the modular presentation.
        /// </summary>
        private static bool TryGetAtlasShapeKey(
            GravityLevelDefinition level,
            PieceDefinition definition,
            out string shapeKey)
        {
            shapeKey = null;
            if (level == null || definition == null || definition.cells == null ||
                definition.cells.Count == 0)
                return false;

            HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
            int minimumX = int.MaxValue;
            int minimumY = int.MaxValue;
            int maximumX = int.MinValue;
            int maximumY = int.MinValue;
            for (int index = 0; index < definition.cells.Count; index++)
            {
                PieceCellDefinition cell = definition.cells[index];
                // Hooks and other special cells do not have a compatible
                // brick-atlas silhouette and must retain their own fallback.
                if (cell.type != PieceCellType.Block)
                    return false;

                Vector2Int localCell = QuarterTurnUtility.Rotate(
                    cell.localCell,
                    definition.quarterTurns);
                if (!occupiedCells.Add(localCell))
                    continue;

                minimumX = Mathf.Min(minimumX, localCell.x);
                minimumY = Mathf.Min(minimumY, localCell.y);
                maximumX = Mathf.Max(maximumX, localCell.x);
                maximumY = Mathf.Max(maximumY, localCell.y);
            }

            if (occupiedCells.Count == 0)
                return false;

            int width = maximumX - minimumX + 1;
            int height = maximumY - minimumY + 1;
            int largestScale = Mathf.Min(width, height);
            for (int scale = largestScale; scale >= 1; scale--)
            {
                if (width % scale != 0 || height % scale != 0 ||
                    occupiedCells.Count % (scale * scale) != 0)
                    continue;

                if (!TryBuildUniformScaledShapeKey(
                        occupiedCells,
                        minimumX,
                        minimumY,
                        width,
                        height,
                        scale,
                        out string candidateKey))
                    continue;

                if (pieceVisualConfig.TryGetShapePresentation(
                        candidateKey,
                        out _,
                        out _))
                {
                    shapeKey = candidateKey;
                    return true;
                }
            }

            return false;
        }

        private static bool TryBuildUniformScaledShapeKey(
            HashSet<Vector2Int> occupiedCells,
            int minimumX,
            int minimumY,
            int width,
            int height,
            int scale,
            out string shapeKey)
        {
            List<string> modules = new List<string>();
            for (int moduleY = 0; moduleY < height / scale; moduleY++)
            {
                for (int moduleX = 0; moduleX < width / scale; moduleX++)
                {
                    int occupiedCount = 0;
                    int startX = minimumX + moduleX * scale;
                    int startY = minimumY + moduleY * scale;
                    for (int y = 0; y < scale; y++)
                    {
                        for (int x = 0; x < scale; x++)
                        {
                            if (occupiedCells.Contains(new Vector2Int(startX + x, startY + y)))
                                occupiedCount++;
                        }
                    }

                    if (occupiedCount == 0)
                        continue;
                    if (occupiedCount != scale * scale)
                    {
                        shapeKey = null;
                        return false;
                    }

                    modules.Add(moduleX + "," + moduleY);
                }
            }

            // BuildShapeKey in the atlas authoring tool records cells in
            // bottom-to-top row order. Preserve that canonical order here;
            // lexicographic string sorting would place "0,1" before "1,0"
            // and make valid L/T silhouettes miss their authored sprite.
            shapeKey = modules.Count > 0 ? string.Join(";", modules) : null;
            return !string.IsNullOrEmpty(shapeKey);
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
