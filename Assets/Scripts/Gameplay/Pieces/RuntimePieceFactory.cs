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

    /// <summary>
    /// Renderer-only description of one part of an intact piece. Reveal-area
    /// presentations use this to mirror a future pooled piece without renting
    /// a gameplay root or creating colliders.
    /// </summary>
    public readonly struct RuntimePiecePreviewVisual
    {
        public RuntimePiecePreviewVisual(
            Sprite sprite,
            Material material,
            Color color,
            Vector2 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            SpriteDrawMode drawMode,
            Vector2 size,
            bool flipX)
        {
            Sprite = sprite;
            Material = material;
            Color = color;
            LocalPosition = localPosition;
            LocalScale = localScale;
            LocalRotation = localRotation;
            DrawMode = drawMode;
            Size = size;
            FlipX = flipX;
        }

        public Sprite Sprite { get; }
        public Material Material { get; }
        public Color Color { get; }
        public Vector2 LocalPosition { get; }
        public Vector3 LocalScale { get; }
        public Quaternion LocalRotation { get; }
        public SpriteDrawMode DrawMode { get; }
        public Vector2 Size { get; }
        public bool FlipX { get; }
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
        private static readonly HashSet<string> warnedUnsupportedPaletteIds = new HashSet<string>();

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

        /// <summary>
        /// Appends the same base sprite composition that an unmodified pooled
        /// piece will receive. This intentionally excludes counters, colliders
        /// and runtime effects because callers use it only as a transient
        /// presentation preview before the real piece is activated.
        /// </summary>
        public static void AppendPreviewVisuals(
            GravityLevelDefinition level,
            PieceDefinition definition,
            List<RuntimePiecePreviewVisual> results)
        {
            if (level == null || definition == null || results == null || !HasBlockCells(definition))
                return;

            ResolveVisual(
                definition,
                out Color visualColor,
                out Sprite modularSprite,
                out Material presentationMaterial);

            bool isFrozenIce = definition.specialBlockType == PieceSpecialBlockType.Ice &&
                               definition.frozenMoveCount > 0;
            List<PiecePartGeometry> parts = BuildPartGeometry(
                level,
                definition,
                1f / Mathf.Max(1, level.subdivisions),
                out _);

            if (TryAppendWholePreviewVisual(
                    level,
                    definition,
                    parts,
                    visualColor,
                    presentationMaterial,
                    isFrozenIce,
                    results))
            {
                return;
            }

            Sprite sprite = modularSprite != null ? modularSprite : PrototypeBootstrap.GetSquareSprite();
            if (sprite == null || sprite.bounds.size.x <= 0f || sprite.bounds.size.y <= 0f)
                return;

            Material material = isFrozenIce && pieceVisualConfig != null
                ? pieceVisualConfig.IceMaterial
                : presentationMaterial != null ? presentationMaterial : GetFallbackSpriteMaterial();
            Color color = isFrozenIce || presentationMaterial != null ? Color.white : visualColor;
            float iceScale = isFrozenIce && pieceVisualConfig != null
                ? pieceVisualConfig.ModularIceCellScale
                : 1f;
            for (int index = 0; index < parts.Count; index++)
            {
                PiecePartGeometry part = parts[index];
                results.Add(new RuntimePiecePreviewVisual(
                    sprite,
                    material,
                    color,
                    part.LocalPosition,
                    new Vector3(
                        part.Size.x / sprite.bounds.size.x * iceScale,
                        part.Size.y / sprite.bounds.size.y * iceScale,
                        1f),
                    Quaternion.identity,
                    SpriteDrawMode.Simple,
                    Vector2.one,
                    false));
            }
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
            warnedUnsupportedPaletteIds.Clear();
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
                pieceVisualConfig != null ? pieceVisualConfig.IceMaterial : null,
                GetFallbackSpriteMaterial());
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
                    GetFallbackSpriteMaterial(),
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
                pieceVisualConfig != null ? pieceVisualConfig.IceMaterial : null,
                GetFallbackSpriteMaterial());
            piece.ConfigureCollisionGeometry(composite, collisionCells, cellVisuals);
            if (voxelShards.Count > 0)
                piece.ConfigureVoxelPresentation(voxelShards);
            else
                piece.ConfigureSolidCellPresentation(cellVisuals);
            piece.ConfigureRemainingProgress(remainingProgress);
            ConfigureOutlinePresentation(piece);
            ConfigureFragmentJoinedPresentation(piece, cells);
        }

        /// <summary>
        /// Rebuilds the joined modular skin after a hammer edit. Fragment
        /// colliders remain independent grid cells; only their presentation is
        /// replaced by pooled horizontal/vertical bars so internal rounded caps
        /// cannot create a visible seam between retained neighbours.
        /// </summary>
        private static void ConfigureFragmentJoinedPresentation(
            PuzzlePiece piece,
            IReadOnlyList<RuntimePieceFragmentCell> cells)
        {
            if (piece == null || pieceVisualConfig == null || cells == null || cells.Count < 2)
                return;

            Vector2 moduleSize = cells[0].Size;
            if (moduleSize.x <= Mathf.Epsilon || moduleSize.y <= Mathf.Epsilon)
                return;

            Vector2 minimumEdge = cells[0].LocalPosition - moduleSize * .5f;
            for (int index = 1; index < cells.Count; index++)
            {
                RuntimePieceFragmentCell cell = cells[index];
                if (!Mathf.Approximately(cell.Size.x, moduleSize.x) ||
                    !Mathf.Approximately(cell.Size.y, moduleSize.y))
                    return;

                minimumEdge = Vector2.Min(minimumEdge, cell.LocalPosition - moduleSize * .5f);
            }

            List<Vector2Int> moduleCells = new List<Vector2Int>(cells.Count);
            HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();
            const float alignmentTolerance = .01f;
            for (int index = 0; index < cells.Count; index++)
            {
                Vector2 relativeCenter = cells[index].LocalPosition - minimumEdge;
                float moduleX = relativeCenter.x / moduleSize.x - .5f;
                float moduleY = relativeCenter.y / moduleSize.y - .5f;
                int x = Mathf.RoundToInt(moduleX);
                int y = Mathf.RoundToInt(moduleY);
                if (Mathf.Abs(moduleX - x) > alignmentTolerance ||
                    Mathf.Abs(moduleY - y) > alignmentTolerance)
                    return;

                Vector2Int coordinate = new Vector2Int(x, y);
                if (!occupied.Add(coordinate))
                    return;

                moduleCells.Add(coordinate);
            }

            string shapeKey = BuildNormalizedModuleShapeKey(occupied);
            if (pieceVisualConfig.TryGetShapePresentation(
                    shapeKey,
                    out PieceShapeVisualDefinition visual,
                    out PieceShapeVisualTransform transform) &&
                visual.NormalSprite != null)
            {
                // Prefer the exact atlas silhouette when one exists. In
                // particular, the authored 1x2/2x1 sprites preserve the right
                // stud count; resizing the generated 1x3/3x1 bar would squeeze
                // three modules of artwork into a two-module fragment.
                piece.ConfigureWholePiecePresentation(
                    visual.NormalSprite,
                    visual.IceSprite,
                    transform,
                    false,
                    false,
                    visual.TileNormalSpriteToBounds);
                return;
            }

            piece.ConfigureJoinedModulePresentation(
                moduleCells,
                pieceVisualConfig.NormalFallbackSprite,
                pieceVisualConfig.HorizontalJoinedFallbackSprite,
                pieceVisualConfig.VerticalJoinedFallbackSprite);
        }

        private static string BuildNormalizedModuleShapeKey(HashSet<Vector2Int> occupied)
        {
            int maximumX = 0;
            int maximumY = 0;
            foreach (Vector2Int cell in occupied)
            {
                maximumX = Mathf.Max(maximumX, cell.x);
                maximumY = Mathf.Max(maximumY, cell.y);
            }

            List<string> keys = new List<string>(occupied.Count);
            for (int y = 0; y <= maximumY; y++)
            {
                for (int x = 0; x <= maximumX; x++)
                {
                    if (occupied.Contains(new Vector2Int(x, y)))
                        keys.Add(x + "," + y);
                }
            }

            return string.Join(";", keys);
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
                pieceVisualConfig != null ? pieceVisualConfig.IceMaterial : null,
                GetFallbackSpriteMaterial());

            float baseIceFontSize = pieceVisualConfig != null ? pieceVisualConfig.IceCounterFontSize : .55f;
            float resolvedIceFontSize = definition.iceCounterFontSize <= 0f
                ? baseIceFontSize
                : baseIceFontSize * Mathf.Clamp(definition.iceCounterFontSize / 36f, 0.2f, 3.0f);

            float baseBombFontSize = pieceVisualConfig != null ? pieceVisualConfig.BombCounterFontSize : .65f;
            float resolvedBombFontSize = definition.bombCounterFontSize <= 0f
                ? baseBombFontSize
                : baseBombFontSize * Mathf.Clamp(definition.bombCounterFontSize / 36f, 0.2f, 3.0f);

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
                pieceVisualConfig != null ? pieceVisualConfig.BombOverlayFill : .936f,
                definition.frozenMoveCount,
                pieceVisualConfig != null ? pieceVisualConfig.IceCounterFont : null,
                resolvedIceFontSize,
                pieceVisualConfig != null ? pieceVisualConfig.IceCounterTextColor : definition.iceCounterTextColor,
                pieceVisualConfig != null ? pieceVisualConfig.IceCounterOutlineColor : definition.iceCounterOutlineColor,
                pieceVisualConfig != null ? pieceVisualConfig.IceCounterOutlineWidth : definition.iceCounterOutlineWidth,
                pieceVisualConfig != null && pieceVisualConfig.IceCounterOffset != Vector2.zero ? pieceVisualConfig.IceCounterOffset : definition.iceCounterOffset,
                pieceVisualConfig != null ? pieceVisualConfig.IceCounterSortingOrder : 50,
                definition.specialBlockType,
                definition.bombTimerSeconds,
                pieceVisualConfig != null ? pieceVisualConfig.BombCounterFont : null,
                resolvedBombFontSize,
                pieceVisualConfig != null ? pieceVisualConfig.BombCounterTextColor : definition.bombCounterTextColor,
                pieceVisualConfig != null ? pieceVisualConfig.BombCounterOutlineColor : definition.bombCounterOutlineColor,
                pieceVisualConfig != null ? pieceVisualConfig.BombCounterOutlineWidth : definition.bombCounterOutlineWidth,
                pieceVisualConfig != null && pieceVisualConfig.BombCounterOffset != Vector2.zero ? pieceVisualConfig.BombCounterOffset : definition.bombCounterOffset,
                pieceVisualConfig != null ? pieceVisualConfig.BombCounterSortingOrder : 50));
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
            bool usesModularIce = definition.specialBlockType == PieceSpecialBlockType.Ice &&
                                  definition.frozenMoveCount > 0;
            float presentationScaleMultiplier = usesModularIce && pieceVisualConfig != null
                ? pieceVisualConfig.ModularIceCellScale
                : 1f;
            for (int index = 0; index < parts.Count; index++)
            {
                PiecePartSlot slot = puzzlePiece.GetPartSlot(index);
                BoxCollider2D collider = ConfigurePiecePartSlot(
                    slot,
                    parts[index],
                    visualColor,
                    GetCellPresentationSprite(voxelSprite, parts[index].Size),
                    presentationMaterial,
                    GetFallbackSpriteMaterial(),
                    out SpriteRenderer cellVisual,
                    voxelShards,
                    presentationScaleMultiplier);
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
            // The LineRenderer join only rounds within the line width. That
            // leaves a square contour on the rounded Scene Tuna piece art and
            // lets a selected glow cover the artwork at exterior corners.
            // Build the rounded, offset contour explicitly instead.
            outline.alignment = LineAlignment.TransformZ;
            outline.numCornerVertices = 0;
            outline.numCapVertices = 0;
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
            Vector2[] path = new Vector2[pointCount];
            pieceComposite.GetPath(pathIndex, path);
            List<Vector2> contour = BuildOutlineContour(
                path,
                GetOutlineCornerRadius(),
                GetSelectedOutlineWidth() * .5f +
                GetOutlineOuterPadding() -
                GetOutlineVisualInset(),
                GetOutlineCornerVertices());
            outline.positionCount = contour.Count;
            for (int index = 0; index < contour.Count; index++)
                outline.SetPosition(index, new Vector3(contour[index].x, contour[index].y, 0f));
        }

        /// <summary>
        /// Produces a presentation-only contour outside an orthogonal composite
        /// path. Convex corners are intentionally rounded at the same scale as
        /// the piece art; concave joins remain mitered in the empty space so an
        /// L/T/C silhouette never loses part of its footprint.
        /// </summary>
        private static List<Vector2> BuildOutlineContour(
            Vector2[] path,
            float requestedRadius,
            float outsideClearance,
            int cornerSegments)
        {
            List<Vector2> contour = new List<Vector2>();
            if (path == null || path.Length < 3)
                return contour;

            float signedArea = 0f;
            for (int index = 0; index < path.Length; index++)
            {
                Vector2 current = path[index];
                Vector2 next = path[(index + 1) % path.Length];
                signedArea += current.x * next.y - next.x * current.y;
            }

            float winding = signedArea >= 0f ? 1f : -1f;
            // A negative clearance intentionally consumes transparent atlas
            // gutter. It changes only the presentation path; collider and
            // authoritative grid bounds remain untouched.
            float clearance = outsideClearance;
            int segments = Mathf.Max(1, cornerSegments);
            const float minimumEdgeLength = .0001f;
            const float cornerEpsilon = .0001f;

            for (int index = 0; index < path.Length; index++)
            {
                Vector2 previous = path[(index + path.Length - 1) % path.Length];
                Vector2 current = path[index];
                Vector2 next = path[(index + 1) % path.Length];
                Vector2 incoming = current - previous;
                Vector2 outgoing = next - current;
                float incomingLength = incoming.magnitude;
                float outgoingLength = outgoing.magnitude;
                if (incomingLength < minimumEdgeLength || outgoingLength < minimumEdgeLength)
                    continue;

                incoming /= incomingLength;
                outgoing /= outgoingLength;
                Vector2 incomingOutside = GetOutsideNormal(incoming, winding);
                Vector2 outgoingOutside = GetOutsideNormal(outgoing, winding);
                float turn = incoming.x * outgoing.y - incoming.y * outgoing.x;

                if (Mathf.Abs(turn) < cornerEpsilon)
                {
                    AddContourPoint(contour, current + incomingOutside * clearance);
                    continue;
                }

                bool isConvex = turn * winding > 0f;
                if (!isConvex || requestedRadius <= cornerEpsilon)
                {
                    AddContourPoint(
                        contour,
                        IntersectOffsetEdges(
                            current,
                            incoming,
                            incomingOutside,
                            outgoing,
                            outgoingOutside,
                            clearance));
                    continue;
                }

                float radius = Mathf.Min(
                    requestedRadius,
                    Mathf.Max(0f, Mathf.Min(incomingLength, outgoingLength) * .5f - cornerEpsilon));
                if (radius <= cornerEpsilon)
                {
                    AddContourPoint(
                        contour,
                        IntersectOffsetEdges(
                            current,
                            incoming,
                            incomingOutside,
                            outgoing,
                            outgoingOutside,
                            clearance));
                    continue;
                }

                Vector2 start = current - incoming * radius + incomingOutside * clearance;
                Vector2 end = current + outgoing * radius + outgoingOutside * clearance;
                Vector2 incomingInside = -incomingOutside;
                Vector2 outgoingInside = -outgoingOutside;
                Vector2 bisector = (incomingInside + outgoingInside).normalized;
                float bisectorProjection = Mathf.Abs(Vector2.Dot(bisector, incomingInside));
                if (bisectorProjection < cornerEpsilon)
                {
                    AddContourPoint(contour, start);
                    AddContourPoint(contour, end);
                    continue;
                }

                Vector2 center = current + bisector * (radius / bisectorProjection);
                float arcRadius = Vector2.Distance(center, start);
                float startAngle = Mathf.Atan2(start.y - center.y, start.x - center.x);
                float endAngle = Mathf.Atan2(end.y - center.y, end.x - center.x);
                float arc = winding > 0f
                    ? Mathf.Repeat(endAngle - startAngle, Mathf.PI * 2f)
                    : Mathf.Repeat(startAngle - endAngle, Mathf.PI * 2f);

                for (int segment = 0; segment <= segments; segment++)
                {
                    float angle = startAngle + winding * arc * segment / segments;
                    AddContourPoint(contour, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * arcRadius);
                }
            }

            return contour;
        }

        private static Vector2 GetOutsideNormal(Vector2 direction, float winding)
        {
            return winding > 0f
                ? new Vector2(direction.y, -direction.x)
                : new Vector2(-direction.y, direction.x);
        }

        private static Vector2 IntersectOffsetEdges(
            Vector2 corner,
            Vector2 incoming,
            Vector2 incomingOutside,
            Vector2 outgoing,
            Vector2 outgoingOutside,
            float clearance)
        {
            Vector2 first = corner + incomingOutside * clearance;
            Vector2 second = corner + outgoingOutside * clearance;
            float denominator = incoming.x * outgoing.y - incoming.y * outgoing.x;
            if (Mathf.Abs(denominator) < .0001f)
                return (first + second) * .5f;

            Vector2 delta = second - first;
            float distance = (delta.x * outgoing.y - delta.y * outgoing.x) / denominator;
            return first + incoming * distance;
        }

        private static void AddContourPoint(List<Vector2> contour, Vector2 point)
        {
            if (contour.Count == 0 || (contour[contour.Count - 1] - point).sqrMagnitude > .00000001f)
                contour.Add(point);
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
        private static float GetOutlineCornerRadius() => pieceVisualConfig != null ? pieceVisualConfig.OutlineCornerRadius : .12f;
        private static float GetOutlineOuterPadding() => pieceVisualConfig != null ? pieceVisualConfig.OutlineOuterPadding : 0f;
        private static float GetOutlineVisualInset() => pieceVisualConfig != null ? pieceVisualConfig.OutlineVisualInset : .075f;
        private static int GetOutlineCornerVertices() => pieceVisualConfig != null ? pieceVisualConfig.OutlineCornerVertices : 4;

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
            Material fallbackSpriteMaterial,
            out SpriteRenderer cellVisual,
            List<VoxelShard> voxelShards = null,
            float presentationScaleMultiplier = 1f)
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
            cellVisual.sharedMaterial = presentationMaterial != null
                ? presentationMaterial
                : fallbackSpriteMaterial;
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
                    (spriteBounds.x > 0f ? part.Size.x / spriteBounds.x : part.Size.x) * presentationScaleMultiplier,
                    (spriteBounds.y > 0f ? part.Size.y / spriteBounds.y : part.Size.y) * presentationScaleMultiplier,
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
            if (presentationMaterial != null && IsSupportedSpriteMaterial(presentationMaterial))
            {
                color = presentationMaterial.color;
            }
            else if (presentationMaterial != null)
            {
                WarnUnsupportedPaletteMaterial(definition.paletteId, presentationMaterial);
                // An unresolved Shader Graph renders Unity's solid magenta
                // error colour. Keep the authored palette tint and allow the
                // SpriteRenderer default material to render the brick sprite.
                presentationMaterial = null;
            }
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

        private static bool IsSupportedSpriteMaterial(Material material)
        {
            return material != null &&
                   material.shader != null &&
                   material.shader.isSupported &&
                   material.shader.name != "Hidden/InternalErrorShader";
        }

        private static Material GetFallbackSpriteMaterial()
        {
            Material fallback = pieceVisualConfig != null
                ? pieceVisualConfig.FallbackSpriteMaterial
                : null;
            return IsSupportedSpriteMaterial(fallback) ? fallback : null;
        }

        private static void WarnUnsupportedPaletteMaterial(string paletteId, Material material)
        {
            string warningKey = string.IsNullOrWhiteSpace(paletteId)
                ? material.name
                : paletteId;
            if (!warnedUnsupportedPaletteIds.Add(warningKey))
                return;

            Debug.LogWarning(
                $"[PieceVisualConfig] Palette '{warningKey}' uses unsupported material " +
                $"'{material.name}'. Rendering with the SpriteRenderer default material and the authored colour instead.");
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

        private static bool TryAppendWholePreviewVisual(
            GravityLevelDefinition level,
            PieceDefinition definition,
            List<PiecePartGeometry> parts,
            Color visualColor,
            Material presentationMaterial,
            bool isFrozenIce,
            List<RuntimePiecePreviewVisual> results)
        {
            if (pieceVisualConfig == null || parts == null || parts.Count == 0)
                return false;

            List<Vector2Int> moduleCells = new List<Vector2Int>();
            if (!TryGetUniformModuleShape(level, definition, moduleCells, out string shapeKey) ||
                !pieceVisualConfig.TryGetShapePresentation(
                    shapeKey,
                    out PieceShapeVisualDefinition visual,
                    out PieceShapeVisualTransform transform))
            {
                return false;
            }

            bool useModularIce = isFrozenIce &&
                                 (pieceVisualConfig.UseUnifiedModularIce || visual.PreferModularIce);
            Sprite sprite = isFrozenIce && visual.IceSprite != null && !useModularIce
                ? visual.IceSprite
                : visual.NormalSprite;
            if (sprite == null || visual.PreferModularNormal || useModularIce ||
                sprite.bounds.size.x <= 0f || sprite.bounds.size.y <= 0f)
            {
                return false;
            }

            GetPiecePartBounds(parts, out Vector2 minimum, out Vector2 maximum);
            Vector2 size = maximum - minimum;
            Vector2 center = (minimum + maximum) * .5f;
            bool swapsDimensions = transform.QuarterTurns % 2 != 0;
            bool useTiledSprite = sprite == visual.NormalSprite &&
                                  visual.TileNormalSpriteToBounds &&
                                  transform.QuarterTurns == 0 &&
                                  !transform.FlipX;
            Vector3 scale;
            Vector2 tiledSize = Vector2.one;
            SpriteDrawMode drawMode = SpriteDrawMode.Simple;
            if (useTiledSprite)
            {
                float moduleScale = size.x >= size.y
                    ? size.y / sprite.bounds.size.y
                    : size.x / sprite.bounds.size.x;
                if (moduleScale <= 0f)
                    return false;

                scale = new Vector3(moduleScale, moduleScale, 1f);
                tiledSize = size / moduleScale;
                drawMode = SpriteDrawMode.Tiled;
            }
            else
            {
                scale = new Vector3(
                    (swapsDimensions ? size.y : size.x) / sprite.bounds.size.x,
                    (swapsDimensions ? size.x : size.y) / sprite.bounds.size.y,
                    1f);
            }

            Material material = isFrozenIce && visual.IceSprite != null
                ? pieceVisualConfig.IceMaterial
                : presentationMaterial != null ? presentationMaterial : GetFallbackSpriteMaterial();
            Color color = isFrozenIce || presentationMaterial != null ? Color.white : visualColor;
            results.Add(new RuntimePiecePreviewVisual(
                sprite,
                material,
                color,
                center,
                scale,
                Quaternion.Euler(0f, 0f, transform.QuarterTurns * 90f),
                drawMode,
                tiledSize,
                transform.FlipX));
            return true;
        }

        private static void ConfigureWholePiecePresentation(
            PuzzlePiece piece,
            GravityLevelDefinition level,
            PieceDefinition definition)
        {
            if (piece == null || pieceVisualConfig == null)
                return;

            List<Vector2Int> moduleCells = new List<Vector2Int>();
            if (!TryGetUniformModuleShape(level, definition, moduleCells, out string shapeKey))
                return;

            if (!pieceVisualConfig.TryGetShapePresentation(
                    shapeKey,
                    out PieceShapeVisualDefinition visual,
                    out PieceShapeVisualTransform transform))
            {
                piece.ConfigureJoinedModulePresentation(
                    moduleCells,
                    pieceVisualConfig.NormalFallbackSprite,
                    pieceVisualConfig.HorizontalJoinedFallbackSprite,
                    pieceVisualConfig.VerticalJoinedFallbackSprite);
                return;
            }

            bool isFrozenIce = definition.specialBlockType == PieceSpecialBlockType.Ice &&
                               definition.frozenMoveCount > 0;
            bool useModularIce = isFrozenIce &&
                                 (pieceVisualConfig.UseUnifiedModularIce ||
                                  visual.PreferModularIce);
            piece.ConfigureWholePiecePresentation(
                visual.NormalSprite,
                visual.IceSprite,
                transform,
                useModularIce,
                visual.PreferModularNormal,
                visual.TileNormalSpriteToBounds);
        }

        /// <summary>
        /// Matches the level's authoritative fine-cell geometry to an atlas
        /// silhouette. Atlas art is authored in module units while levels may
        /// describe the exact same silhouette in four-by-four fine cells.
        /// A candidate is valid only when every atlas module maps to a fully
        /// occupied square of the same scale; irregular damage safely falls
        /// back to the modular presentation.
        /// </summary>
        private static bool TryGetUniformModuleShape(
            GravityLevelDefinition level,
            PieceDefinition definition,
            List<Vector2Int> moduleCells,
            out string shapeKey)
        {
            shapeKey = null;
            moduleCells.Clear();
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
            // Atlas modules represent authored board cells. Trying arbitrary
            // larger divisors makes a complete 2x2 piece look like one giant
            // 1x1 module and stretches BB_1X1 over all four cells. Use the
            // level's authoritative fine-cell subdivision so visual grouping
            // cannot silently change the piece's structural composition.
            int moduleScale = Mathf.Max(1, level.subdivisions);
            if (width % moduleScale != 0 || height % moduleScale != 0 ||
                occupiedCells.Count % (moduleScale * moduleScale) != 0)
                return false;

            if (!TryBuildUniformScaledShapeKey(
                    occupiedCells,
                    minimumX,
                    minimumY,
                    width,
                    height,
                    moduleScale,
                    moduleCells,
                    out string candidateKey))
                return false;

            shapeKey = candidateKey;
            return true;
        }

        private static bool TryBuildUniformScaledShapeKey(
            HashSet<Vector2Int> occupiedCells,
            int minimumX,
            int minimumY,
            int width,
            int height,
            int scale,
            List<Vector2Int> modules,
            out string shapeKey)
        {
            modules.Clear();
            List<string> moduleKeys = new List<string>();
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

                    modules.Add(new Vector2Int(moduleX, moduleY));
                    moduleKeys.Add(moduleX + "," + moduleY);
                }
            }

            // BuildShapeKey in the atlas authoring tool records cells in
            // bottom-to-top row order. Preserve that canonical order here;
            // lexicographic string sorting would place "0,1" before "1,0"
            // and make valid L/T silhouettes miss their authored sprite.
            shapeKey = moduleKeys.Count > 0 ? string.Join(";", moduleKeys) : null;
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
