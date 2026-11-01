using UnityEngine;
using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;
using GravityPuzzle.Presentation.VFX;

namespace GravityPuzzle
{
    /// <summary>
    /// Identifies the root of one complete movable puzzle piece.
    /// Its child colliders form the body and the smaller hook geometry.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(CompositeCollider2D), typeof(LineRenderer))]
    public sealed class PuzzlePiece : MonoBehaviour, IPoolable, IPoolReturnReceiver<PuzzlePiece>
    {
        public readonly struct TargetableCell
        {
            public Vector2 Center { get; }
            public Vector2 Size { get; }

            public TargetableCell(Vector2 center, Vector2 size)
            {
                Center = center;
                Size = size;
            }
        }

        public readonly struct RemovedCell
        {
            public readonly Vector2 worldPosition;
            public readonly Vector2 size;
            public readonly Color color;
            public readonly float progressUnits;
            public readonly int renderedVoxelCount;

            public RemovedCell(
                Vector2 worldPosition,
                Vector2 size,
                Color color,
                float progressUnits,
                int renderedVoxelCount)
            {
                this.worldPosition = worldPosition;
                this.size = size;
                this.color = color;
                this.progressUnits = progressUnits;
                this.renderedVoxelCount = renderedVoxelCount;
            }
        }

        private const float RuntimeIceCounterFontScale = .32f;
        private const float MinimumBombCounterFontScale = .18f;
        private const float MaximumBombCounterFontScale = .32f;
        private const string IceCounterPresentationName = "Ice Counter Presentation";
        private const string BombCounterPresentationName = "Bomb Counter Presentation";
        private static readonly int BombDefuseFadeId = Shader.PropertyToID("_DefuseFade");
        private static readonly int BombFragmentClipMinLocalId = Shader.PropertyToID("_ClipMinLocal");
        private static readonly int BombFragmentClipMaxLocalId = Shader.PropertyToID("_ClipMaxLocal");
        private static readonly List<PuzzlePiece> activePieces = new List<PuzzlePiece>();

        public static IReadOnlyList<PuzzlePiece> ActivePieces => activePieces;
        public Rigidbody2D Body { get; private set; }
        public PieceGridFallView GridFallView { get; private set; }
        public CompositeCollider2D CompositeCollider => compositeCollider;
        public LineRenderer Outline => rootOutline;
        internal RuntimePieceOutlineGrid OutlineGrid => outlineGrid;
        internal int OutlineRendererCount => outlineRenderers.Count;
        public bool IsSelected => isSelected;
        /// <summary>
        /// True only after this piece has won a physical shredder-mouth feed.
        /// A support piece inside a temporary physics island is deliberately
        /// not yet shredding: it remains visible, collidable and has not
        /// reported gameplay destruction.
        /// </summary>
        public bool IsBeingShredded => shredderHandoffMode == ShredderHandoffMode.Feeding;
        public bool IsInShredderPhysicsHandoff =>
            shredderHandoffMode == ShredderHandoffMode.PhysicsIsland;
        public bool IsShredderInteractionLocked =>
            shredderHandoffMode != ShredderHandoffMode.None;
        public bool IsFrozen { get; private set; }
        public bool IsBomb { get; private set; }
        public bool IsBombDefused { get; private set; }
        public float BombTimerSeconds { get; private set; }
        public int SourcePieceId { get; private set; } = -1;
        /// <summary>Authored board-block units represented by this draggable piece.</summary>
        public int ProgressUnits { get; private set; } = 1;
        /// <summary>Number of visible voxel shards currently represented by this root.</summary>
        public int ActiveVoxelPresentationCount => Mathf.Max(
            1,
            (collisionCellVisuals != null ? collisionCellVisuals.Count : 0) *
            VoxelBlockBuilder.Subdivisions * VoxelBlockBuilder.Subdivisions);
        /// <summary>
        /// True only when this pooled root represents at least one authored
        /// board block. Empty level-editor entries must never contribute to a
        /// level's progress target.
        /// </summary>
        public bool HasRuntimeBlockCells => collisionCellVisuals != null && collisionCellVisuals.Count > 0;
        /// <summary>Unclaimed progress remaining after cell-level booster hits.</summary>
        public float RemainingProgressUnits { get; private set; } = 1f;
        /// <summary>Source colour from the authored piece definition.</summary>
        public Color VisualColor { get; private set; } = Color.white;
        /// <summary>Shared artist material used by the normal brick presentation.</summary>
        public Material PresentationMaterial { get; private set; }
        private Material fallbackPresentationMaterial;
        public Bounds CollisionBounds
        {
            get
            {
                if (compositeCollider != null && compositeCollider.enabled)
                    return compositeCollider.bounds;

                if (solidColliders == null)
                    CacheSolidColliders();

                Bounds bounds = new Bounds(transform.position, Vector3.zero);
                bool found = false;
                foreach (Collider2D collider in solidColliders)
                {
                    if (collider == null || !collider.enabled || collider.isTrigger)
                        continue;

                    if (found)
                        bounds.Encapsulate(collider.bounds);
                    else
                    {
                        bounds = collider.bounds;
                        found = true;
                    }
                }

                return bounds;
            }
        }

        public float CurrentCollisionInset => collisionCells == null || useFullCollisionGeometry
            ? 0f
            : GravityGridMetrics.RestingPieceCollisionSkinInCells;

        public int CollisionCellCount => collisionCells != null ? collisionCells.Count : 0;
        public IReadOnlyList<BoxCollider2D> CollisionCells => collisionCells;
        public IReadOnlyList<SpriteRenderer> CollisionCellVisuals => collisionCellVisuals;

        // These are presentation renderers only; joint gameplay remains grid-owned.
        public SpriteRenderer WholePieceVisual => wholePieceRenderer;
        public IReadOnlyList<SpriteRenderer> JoinedPieceVisuals => joinedPieceRenderers;

        public int PrimarySortingLayerID =>
            wholePieceRenderer != null && wholePieceRenderer.enabled
                ? wholePieceRenderer.sortingLayerID
                : (collisionCellVisuals != null && collisionCellVisuals.Count > 0 && collisionCellVisuals[0] != null
                    ? collisionCellVisuals[0].sortingLayerID
                    : 0);

        private readonly List<SpriteRenderer> iceRenderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> iceSlots = new List<SpriteRenderer>();
        private readonly List<JoinedPieceSegment> joinedPieceSegments = new List<JoinedPieceSegment>();
        private readonly List<SpriteRenderer> joinedPieceRenderers = new List<SpriteRenderer>();
        private SpriteRenderer wholePieceRenderer;
        private Sprite wholePieceNormalSprite;
        private Sprite wholePieceIceSprite;
        private bool tileWholePieceNormalPresentation;
        private bool preferModularNormalPresentation;
        private bool preferModularIcePresentation;
        private PieceShapeVisualTransform wholePieceTransform = PieceShapeVisualTransform.Identity;
        private Transform iceSlotsRoot;
        private readonly List<PiecePartSlot> partSlots = new List<PiecePartSlot>();
        private CompositeCollider2D compositeCollider;
        private LineRenderer rootOutline;
        // The prefab owns these renderers. They are cached once because a grid
        // silhouette can have an outer loop plus interior or island loops.
        private readonly List<LineRenderer> outlineRenderers = new List<LineRenderer>(8);
        private RuntimePieceOutlineGrid outlineGrid;
        private SpriteRenderer[] boxRevealRenderers;
        private bool[] boxRevealRendererEnabledStates;
        private int[] boxRevealRendererSortingOrders;
        private SpriteMaskInteraction[] boxRevealRendererMaskInteractions;
        private bool boxRevealPresentationCaptured;
        private bool[] boxRevealOutlineEnabledStates;
        private int[] boxRevealOutlineSortingOrders;
        private bool boxRevealIceCounterEnabled;
        private bool boxRevealBombCounterEnabled;
        private List<BoxCollider2D> collisionCells;
        private List<Vector2> fullCollisionCellSizes;
        private List<SpriteRenderer> collisionCellVisuals;
        private Sprite iceOverlaySprite;
        private Color iceOverlayTint = new Color(1f, 1f, 1f, .42f);
        private Color iceFrostTint = new Color(1f, 1f, 1f, .18f);
        private Sprite bombOverlaySprite;
        private float bombOverlayFill = .936f;
        private Material bombDefuseSplitMaterial;
        private int bombShredderVoxelSubdivisions = 4;
        private Material icePresentationMaterial;
        private IIceBlockParticleVfx iceParticleVfx;
        private IIceBlockParticleVfxHandle iceParticleVfxHandle;
        private readonly List<ShredderReservationCell> shredderReservationCells =
            new List<ShredderReservationCell>();
        private readonly List<GridCoordinate> shredderCellsReadyToRelease =
            new List<GridCoordinate>();
        private float shredderReservationStartBodyY;
        // A contact-connected stack that enters the shredder together becomes
        // a short-lived physics island. Its original grid cells stay reserved
        // until every island member despawns, so normal grid gravity cannot
        // enter the same space while physics owns the contact response.
        private bool keepShredderGridReservationUntilDespawn;
        private readonly List<VoxelShard> configuredVoxelShards = new List<VoxelShard>();
        private SpriteRenderer[] configuredShredderRenderers;
        private Collider2D[] solidColliders;
        private PhysicsMaterial2D[] defaultSolidColliderMaterials;
        private int[] defaultSolidColliderLayers;
        private int defaultPhysicsLayer;
        private SpriteRenderer[] shredderPresentationRenderers;
        private ShredderDissolvePresentation shredderDissolvePresentation;
        private float restingOutlineWidth = 0.05f;
        private float selectedOutlineWidth = 0.08f;
        private Color restingOutlineColor = Color.black;
        private Color selectedOutlineColor = Color.white;
        private int restingOutlineSortingOrder = 10;
        private int selectedOutlineSortingOrder = 20;
        private bool[] shredderPresentationEnabledStates;
        private SpriteMaskInteraction[] shredderPresentationMaskStates;
        private int[] shredderPresentationSortingOrders;
        private int shredderPresentationOutlineSortingOrder;
        private Vector2 currentVisualJitterOffset;
        private enum ShredderHandoffMode
        {
            None,
            PhysicsIsland,
            Feeding
        }

        private ShredderHandoffMode shredderHandoffMode;
        private bool gridDespawned;
        private bool useFullCollisionGeometry;
        private bool isSelected;
        // Presentation-only state for an armed Rocket or Hammer booster.
        // It never participates in board occupancy or input ownership.
        private bool isBoosterTargeted;
        // A forced tutorial target must render over its dimmer without
        // changing piece state or normal booster targeting presentation.
        private bool isTutorialTargeted;
        private int tutorialTargetSortingOrder;
        // Normal Hammer and Rocket targeting use the same presentation lease
        // so every board piece remains readable above the world dimmer.
        private bool isBoosterTargetingForeground;
        private int boosterTargetingForegroundSortingOrder;
        private SpriteRenderer[] targetingForegroundRenderers;
        private int[] targetingForegroundOriginalSortingOrders;
        private TextMeshPro[] targetingForegroundTexts;
        private int[] targetingForegroundTextOriginalSortingOrders;
        private bool destructionReported;
        private bool iceReleaseAnimating;
        private int frozenUntilDestroyedCount;
        private int previousFrozenRemaining = -1;
        private TextMeshPro iceCounterText;
        private TextMeshPro bombCounterText;
        private TMP_FontAsset iceCounterFont;
        private TMP_FontAsset bombCounterFont;
        private TMP_FontAsset defaultCounterFont;
        private Action<PuzzlePiece> returnToPool;
        private float iceCounterFontSize = .55f;
        private int iceCounterSortingOrder = 50;
        private Color iceCounterTextColor = Color.black;
        private Color iceCounterOutlineColor = Color.white;
        private float iceCounterOutlineWidth = .18f;
        private Vector2 iceCounterOffset;
        private float bombCounterFontSize = .65f;
        private int bombCounterSortingOrder = 50;
        private Color bombCounterTextColor = new Color(.95f, .18f, .12f);
        private Color bombCounterOutlineColor = Color.white;
        private float bombCounterOutlineWidth = .18f;
        private Vector2 bombCounterOffset;
        private SpriteRenderer bombOverlayRenderer;
        private SpriteRenderer bombDefuseLeftRenderer;
        private SpriteRenderer bombDefuseRightRenderer;
        private readonly List<SpriteRenderer> bombShredderFragmentRenderers =
            new List<SpriteRenderer>(16);
        private MaterialPropertyBlock bombDefusePropertyBlock;
        private int previousBombRemaining = -1;

        private readonly struct JoinedPieceSegment
        {
            public Sprite Sprite { get; }
            public Vector2 Offset { get; }
            public Vector2 Size { get; }
            public bool Tiled { get; }

            public JoinedPieceSegment(Sprite sprite, Vector2 offset, Vector2 size, bool tiled)
            {
                Sprite = sprite;
                Offset = offset;
                Size = size;
                Tiled = tiled;
            }
        }

        private struct ShredderReservationCell
        {
            public GridCoordinate localCoordinate;
            public float initialWorldCenterY;
            public bool released;

            public ShredderReservationCell(
                GridCoordinate localCoordinate,
                float initialWorldCenterY)
            {
                this.localCoordinate = localCoordinate;
                this.initialWorldCenterY = initialWorldCenterY;
                released = false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActivePieces()
        {
            activePieces.Clear();
        }

        private void Awake()
        {
            bombDefusePropertyBlock = new MaterialPropertyBlock();
            defaultPhysicsLayer = gameObject.layer;
            Body = GetComponent<Rigidbody2D>();
            GridFallView = GetComponent<PieceGridFallView>();
            shredderDissolvePresentation = GetComponent<ShredderDissolvePresentation>();
            compositeCollider = GetComponent<CompositeCollider2D>();
            rootOutline = GetComponent<LineRenderer>();
            if (rootOutline != null)
                outlineRenderers.Add(rootOutline);
            LineRenderer[] cachedOutlineRenderers = GetComponentsInChildren<LineRenderer>(true);
            for (int index = 0; index < cachedOutlineRenderers.Length; index++)
            {
                LineRenderer outline = cachedOutlineRenderers[index];
                if (outline != null && outline != rootOutline)
                    outlineRenderers.Add(outline);
            }
            PiecePartSlot[] cachedPartSlots = GetComponentsInChildren<PiecePartSlot>(true);
            for (int index = 0; index < cachedPartSlots.Length; index++)
                partSlots.Add(cachedPartSlots[index]);
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            boxRevealRenderers = renderers;
            for (int index = 0; index < renderers.Length; index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer.transform.parent != null &&
                    renderer.transform.parent.name == "Ice Presentation Slots")
                {
                    iceSlotsRoot = renderer.transform.parent;
                    iceSlots.Add(renderer);
                }
            }

            TextMeshPro[] textComponents = GetComponentsInChildren<TextMeshPro>(true);
            for (int index = 0; index < textComponents.Length; index++)
            {
                if (textComponents[index].name == IceCounterPresentationName)
                {
                    iceCounterText = textComponents[index];
                    defaultCounterFont = iceCounterText.font;
                }
                else if (textComponents[index].name == BombCounterPresentationName)
                {
                    bombCounterText = textComponents[index];
                }
            }

            if (bombCounterText == null)
                bombCounterText = iceCounterText;

            if (iceSlots.Count > 0)
                wholePieceRenderer = iceSlots[0];
        }

        public int PartSlotCount => partSlots.Count;

        public PiecePartSlot GetPartSlot(int index)
        {
            return index >= 0 && index < partSlots.Count ? partSlots[index] : null;
        }

        internal LineRenderer GetOutlineRenderer(int index)
        {
            return index >= 0 && index < outlineRenderers.Count ? outlineRenderers[index] : null;
        }

        internal void ConfigureOutlineGrid(RuntimePieceOutlineGrid grid)
        {
            outlineGrid = grid;
        }

        internal void ClearOutlineRenderers(int activeRendererCount = 0)
        {
            for (int index = 0; index < outlineRenderers.Count; index++)
            {
                LineRenderer outline = outlineRenderers[index];
                if (outline == null)
                    continue;

                outline.positionCount = 0;
                outline.enabled = index < activeRendererCount;
            }
        }

        private void SetOutlineRenderersEnabled(bool enabled)
        {
            for (int index = 0; index < outlineRenderers.Count; index++)
            {
                LineRenderer outline = outlineRenderers[index];
                if (outline != null)
                    outline.enabled = enabled && outline.positionCount >= 3;
            }
        }

        private void SetOutlineRenderersSortingOrder(int sortingOrder)
        {
            for (int index = 0; index < outlineRenderers.Count; index++)
            {
                LineRenderer outline = outlineRenderers[index];
                if (outline != null)
                    outline.sortingOrder = sortingOrder;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            for (int outlineIndex = 0; outlineIndex < outlineRenderers.Count; outlineIndex++)
            {
                LineRenderer outline = outlineRenderers[outlineIndex];
                if (outline == null || outline.positionCount < 2)
                    continue;

                for (int index = 0; index < outline.positionCount; index++)
                {
                    Vector3 point = outline.transform.TransformPoint(outline.GetPosition(index));
                    Vector3 next = outline.transform.TransformPoint(
                        outline.GetPosition((index + 1) % outline.positionCount));
                    Gizmos.DrawSphere(point, .015f);
                    Gizmos.DrawLine(point, next);
                }
            }
        }
#endif

        private PiecePartSlot FindPartSlot(BoxCollider2D collision, SpriteRenderer visual)
        {
            for (int index = 0; index < partSlots.Count; index++)
            {
                PiecePartSlot slot = partSlots[index];
                if (slot != null && (slot.Collision == collision || slot.Visual == visual))
                    return slot;
            }

            return null;
        }

        private List<RuntimePieceFragmentCell> BuildFragmentCells(IReadOnlyList<int> indices)
        {
            List<RuntimePieceFragmentCell> cells = new List<RuntimePieceFragmentCell>(indices.Count);
            for (int index = 0; index < indices.Count; index++)
            {
                int sourceIndex = indices[index];
                BoxCollider2D cell = collisionCells[sourceIndex];
                Vector2 localPosition = transform.InverseTransformPoint(cell.bounds.center);
                cells.Add(new RuntimePieceFragmentCell(
                    localPosition,
                    fullCollisionCellSizes[sourceIndex]));
            }

            return cells;
        }

        private void OnEnable()
        {
            if (!activePieces.Contains(this))
                activePieces.Add(this);
        }

        private void OnDisable()
        {
            activePieces.Remove(this);
        }

        private void OnDestroy()
        {
            MarkDespawnedInBoard();
        }

        public void OnSpawn()
        {
            RestoreBoxRevealContentVisual();
            RestoreShredderPresentation();
            RestoreDefaultPhysicsLayers();
            shredderHandoffMode = ShredderHandoffMode.None;
            keepShredderGridReservationUntilDespawn = false;
            gridDespawned = false;
            destructionReported = false;
            iceReleaseAnimating = false;
            previousFrozenRemaining = -1;
            useFullCollisionGeometry = false;
            isSelected = false;
            isBoosterTargeted = false;
            isTutorialTargeted = false;
            isBoosterTargetingForeground = false;
            RestoreTargetingForegroundSorting();
            IsFrozen = false;
            IsBombDefused = false;
            SourcePieceId = -1;
            SetOutlineRenderersEnabled(true);
            ApplyOutlinePresentation();
            if (compositeCollider != null)
                compositeCollider.enabled = true;
            RestoreDefaultCollisionMaterials();
            InvalidateVoxelCache();
        }

        public void OnDespawn()
        {
            // Pool instances must not expose their previous shredder
            // presentation state between lifetimes. Gameplay lifecycle is
            // owned by the board, never by this presentation flag.
            shredderHandoffMode = ShredderHandoffMode.None;
            keepShredderGridReservationUntilDespawn = false;
            isSelected = false;
            isBoosterTargeted = false;
            isTutorialTargeted = false;
            isBoosterTargetingForeground = false;
            RestoreBoxRevealContentVisual();
            RestoreTargetingForegroundSorting();
            ApplyOutlinePresentation();
            returnToPool = null;
            ClearIceVisuals();
            ClearBombVisuals();
            ClearWholePiecePresentation();
            RestoreShredderPresentation();
            RestoreDefaultPhysicsLayers();
            RestoreDefaultCollisionMaterials();
            RuntimePieceFactory.ResetPooledPiece(this);
            InvalidateVoxelCache();
        }

        /// <summary>
        /// Keeps a partially revealed root in the authoritative grid while its
        /// Box preview owns the recessed visual. It never changes materials,
        /// colours, colliders, or gameplay state.
        /// </summary>
        public void HideBoxRevealContentVisual()
        {
            CaptureBoxRevealPresentation();
            if (boxRevealRenderers != null)
            {
                for (int index = 0; index < boxRevealRenderers.Length; index++)
                {
                    if (boxRevealRenderers[index] != null)
                        boxRevealRenderers[index].enabled = false;
                }
            }

            SetOutlineRenderersEnabled(false);
            if (iceCounterText != null)
                iceCounterText.enabled = false;
            if (bombCounterText != null)
                bombCounterText.enabled = false;
        }

        /// <summary>Releases only the temporary Box visual lease.</summary>
        public void RestoreBoxRevealContentVisual()
        {
            if (!boxRevealPresentationCaptured)
                return;

            if (boxRevealRenderers != null)
            {
                for (int index = 0; index < boxRevealRenderers.Length; index++)
                {
                    SpriteRenderer renderer = boxRevealRenderers[index];
                    if (renderer == null)
                        continue;

                    renderer.enabled = boxRevealRendererEnabledStates[index];
                    renderer.sortingOrder = boxRevealRendererSortingOrders[index];
                    renderer.maskInteraction = boxRevealRendererMaskInteractions[index];
                }
            }

            RestoreBoxRevealOutlines();
            if (iceCounterText != null)
                iceCounterText.enabled = boxRevealIceCounterEnabled;
            if (bombCounterText != null)
                bombCounterText.enabled = boxRevealBombCounterEnabled;

            boxRevealPresentationCaptured = false;
            boxRevealRendererEnabledStates = null;
            boxRevealRendererSortingOrders = null;
            boxRevealRendererMaskInteractions = null;
            boxRevealOutlineEnabledStates = null;
            boxRevealOutlineSortingOrders = null;
        }

        private void CaptureBoxRevealPresentation()
        {
            if (boxRevealPresentationCaptured)
                return;

            int rendererCount = boxRevealRenderers != null ? boxRevealRenderers.Length : 0;
            boxRevealRendererEnabledStates = new bool[rendererCount];
            boxRevealRendererSortingOrders = new int[rendererCount];
            boxRevealRendererMaskInteractions = new SpriteMaskInteraction[rendererCount];
            for (int index = 0; index < rendererCount; index++)
            {
                SpriteRenderer renderer = boxRevealRenderers[index];
                if (renderer == null)
                    continue;

                boxRevealRendererEnabledStates[index] = renderer.enabled;
                boxRevealRendererSortingOrders[index] = renderer.sortingOrder;
                boxRevealRendererMaskInteractions[index] = renderer.maskInteraction;
            }

            int outlineCount = outlineRenderers.Count;
            boxRevealOutlineEnabledStates = new bool[outlineCount];
            boxRevealOutlineSortingOrders = new int[outlineCount];
            for (int index = 0; index < outlineCount; index++)
            {
                LineRenderer outline = outlineRenderers[index];
                if (outline == null)
                    continue;

                boxRevealOutlineEnabledStates[index] = outline.enabled;
                boxRevealOutlineSortingOrders[index] = outline.sortingOrder;
            }
            boxRevealIceCounterEnabled = iceCounterText != null && iceCounterText.enabled;
            boxRevealBombCounterEnabled = bombCounterText != null && bombCounterText.enabled;
            boxRevealPresentationCaptured = true;
        }

        private void RestoreBoxRevealOutlines()
        {
            if (boxRevealOutlineEnabledStates == null || boxRevealOutlineSortingOrders == null)
                return;

            int count = Mathf.Min(
                outlineRenderers.Count,
                Mathf.Min(boxRevealOutlineEnabledStates.Length, boxRevealOutlineSortingOrders.Length));
            for (int index = 0; index < count; index++)
            {
                LineRenderer outline = outlineRenderers[index];
                if (outline == null)
                    continue;

                outline.enabled = boxRevealOutlineEnabledStates[index];
                outline.sortingOrder = boxRevealOutlineSortingOrders[index];
            }
        }

        /// <summary>
        /// Captures the pooled voxel presentation generated for this root.
        /// Shredder feeds consume this cache rather than discovering children
        /// after the piece enters its handoff state.
        /// </summary>
        public void ConfigureVoxelPresentation(IReadOnlyList<VoxelShard> voxels)
        {
            configuredVoxelShards.Clear();
            if (voxels == null || voxels.Count == 0)
            {
                configuredShredderRenderers = null;
                return;
            }

            for (int index = 0; index < voxels.Count; index++)
            {
                VoxelShard voxel = voxels[index];
                if (voxel != null)
                    configuredVoxelShards.Add(voxel);
            }

            configuredShredderRenderers = new SpriteRenderer[configuredVoxelShards.Count];
            for (int index = 0; index < configuredVoxelShards.Count; index++)
                configuredShredderRenderers[index] = configuredVoxelShards[index].Renderer;
        }

        public void ConfigureSolidCellPresentation(IReadOnlyList<SpriteRenderer> visuals)
        {
            configuredVoxelShards.Clear();
            if (visuals == null || visuals.Count == 0)
            {
                configuredShredderRenderers = null;
                return;
            }

            configuredShredderRenderers = new SpriteRenderer[visuals.Count];
            for (int index = 0; index < visuals.Count; index++)
                configuredShredderRenderers[index] = visuals[index];
        }

        /// <summary>
        /// Shows a single artist-authored sprite for an intact, module-aligned
        /// shape. Colliders and cell visuals remain the gameplay authority.
        /// Hammer edits explicitly return to the modular presentation.
        /// </summary>
        public void ConfigureWholePiecePresentation(
            Sprite normalSprite,
            Sprite iceSprite,
            PieceShapeVisualTransform transform,
            bool preferModularIce = false,
            bool preferModularNormal = false,
            bool tileNormalSpriteToBounds = false)
        {
            ClearIceVisuals();
            ClearBombVisuals();
            ClearJoinedPiecePresentation();
            wholePieceNormalSprite = normalSprite;
            wholePieceIceSprite = iceSprite;
            preferModularNormalPresentation = preferModularNormal;
            preferModularIcePresentation = preferModularIce;
            tileWholePieceNormalPresentation = tileNormalSpriteToBounds;
            wholePieceTransform = transform;
            RefreshWholePiecePresentation();
            PrototypeBoard board = PrototypeBoard.Active;
            RefreshFreezeState(board != null ? board.DestroyedPieceCount : 0);
            RefreshBombState();
        }

        /// <summary>
        /// Builds an intact presentation for a uniform module shape that has no
        /// authored silhouette. Maximal horizontal and vertical bars overlap at
        /// corners, hiding internal module caps without changing colliders or
        /// authoritative board occupancy.
        /// </summary>
        public void ConfigureJoinedModulePresentation(
            IReadOnlyList<Vector2Int> moduleCells,
            Sprite singleModuleSprite,
            Sprite horizontalBarSprite,
            Sprite verticalBarSprite)
        {
            ClearIceVisuals();
            ClearBombVisuals();
            ClearWholePiecePresentation();
            if (moduleCells == null || moduleCells.Count == 0 ||
                singleModuleSprite == null || horizontalBarSprite == null ||
                verticalBarSprite == null)
                return;

            HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();
            int minimumX = int.MaxValue;
            int minimumY = int.MaxValue;
            int maximumX = int.MinValue;
            int maximumY = int.MinValue;
            for (int index = 0; index < moduleCells.Count; index++)
            {
                Vector2Int cell = moduleCells[index];
                if (!occupied.Add(cell))
                    continue;

                minimumX = Mathf.Min(minimumX, cell.x);
                minimumY = Mathf.Min(minimumY, cell.y);
                maximumX = Mathf.Max(maximumX, cell.x);
                maximumY = Mathf.Max(maximumY, cell.y);
            }

            if (occupied.Count == 0)
                return;

            Vector2 shapeCenter = new Vector2(
                (minimumX + maximumX + 1f) * .5f,
                (minimumY + maximumY + 1f) * .5f);
            HashSet<Vector2Int> covered = new HashSet<Vector2Int>();
            HashSet<Vector2Int> horizontalCovered = new HashSet<Vector2Int>();
            HashSet<Vector2Int> verticalCovered = new HashSet<Vector2Int>();

            for (int y = minimumY; y <= maximumY; y++)
            {
                int x = minimumX;
                while (x <= maximumX)
                {
                    if (!occupied.Contains(new Vector2Int(x, y)))
                    {
                        x++;
                        continue;
                    }

                    int start = x;
                    while (x + 1 <= maximumX && occupied.Contains(new Vector2Int(x + 1, y)))
                        x++;

                    int length = x - start + 1;
                    if (length >= 2)
                    {
                        Vector2 runCenter = new Vector2(start + length * .5f, y + .5f);
                        joinedPieceSegments.Add(new JoinedPieceSegment(
                            horizontalBarSprite,
                            runCenter - shapeCenter,
                            new Vector2(length, 1f),
                            length != 3));
                        for (int coveredX = start; coveredX <= x; coveredX++)
                        {
                            Vector2Int coveredCell = new Vector2Int(coveredX, y);
                            covered.Add(coveredCell);
                            horizontalCovered.Add(coveredCell);
                        }
                    }

                    x++;
                }
            }

            for (int x = minimumX; x <= maximumX; x++)
            {
                int y = minimumY;
                while (y <= maximumY)
                {
                    if (!occupied.Contains(new Vector2Int(x, y)))
                    {
                        y++;
                        continue;
                    }

                    int start = y;
                    while (y + 1 <= maximumY && occupied.Contains(new Vector2Int(x, y + 1)))
                        y++;

                    int length = y - start + 1;
                    if (length >= 2)
                    {
                        Vector2 runCenter = new Vector2(x + .5f, start + length * .5f);
                        joinedPieceSegments.Add(new JoinedPieceSegment(
                            verticalBarSprite,
                            runCenter - shapeCenter,
                            new Vector2(1f, length),
                            length != 3));
                        for (int coveredY = start; coveredY <= y; coveredY++)
                        {
                            Vector2Int coveredCell = new Vector2Int(x, coveredY);
                            covered.Add(coveredCell);
                            verticalCovered.Add(coveredCell);
                        }
                    }

                    y++;
                }
            }

            // A one-module cap at every bar intersection masks the rounded
            // endpoints of both bars. The bars remain visible beneath it, so
            // the outer silhouette stays continuous through L/T/C corners.
            foreach (Vector2Int cell in horizontalCovered)
            {
                if (!verticalCovered.Contains(cell))
                    continue;

                joinedPieceSegments.Add(new JoinedPieceSegment(
                    singleModuleSprite,
                    new Vector2(cell.x + .5f, cell.y + .5f) - shapeCenter,
                    Vector2.one,
                    false));
            }

            foreach (Vector2Int cell in occupied)
            {
                if (covered.Contains(cell))
                    continue;

                joinedPieceSegments.Add(new JoinedPieceSegment(
                    singleModuleSprite,
                    new Vector2(cell.x + .5f, cell.y + .5f) - shapeCenter,
                    Vector2.one,
                    false));
            }

            RefreshWholePiecePresentation();
            PrototypeBoard board = PrototypeBoard.Active;
            RefreshFreezeState(board != null ? board.DestroyedPieceCount : 0);
            RefreshBombState();
        }

        public void ClearWholePiecePresentation()
        {
            ClearJoinedPiecePresentation();
            wholePieceNormalSprite = null;
            wholePieceIceSprite = null;
            preferModularNormalPresentation = false;
            preferModularIcePresentation = false;
            tileWholePieceNormalPresentation = false;
            wholePieceTransform = PieceShapeVisualTransform.Identity;
            ResetWholePieceRenderer(wholePieceRenderer);
            wholePieceRenderer = iceSlots.Count > 0 ? iceSlots[0] : null;

            SetModularCellPresentationVisible(true);
        }

        private void ClearJoinedPiecePresentation()
        {
            for (int index = 0; index < joinedPieceRenderers.Count; index++)
                ResetWholePieceRenderer(joinedPieceRenderers[index]);

            joinedPieceRenderers.Clear();
            joinedPieceSegments.Clear();
        }

        private void ResetWholePieceRenderer(SpriteRenderer renderer)
        {
            if (renderer == null)
                return;

            renderer.enabled = false;
            renderer.sprite = null;
            renderer.color = Color.white;
            renderer.sharedMaterial = null;
            renderer.flipX = false;
            renderer.flipY = false;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.transform.SetParent(iceSlotsRoot, false);
            renderer.transform.localPosition = Vector3.zero;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = Vector3.one;
        }

        public IReadOnlyList<VoxelShard> ConfiguredVoxelShards => configuredVoxelShards;
        public SpriteRenderer[] ConfiguredShredderRenderers => configuredShredderRenderers;
        public IReadOnlyList<PiecePartSlot> PartSlots => partSlots;

        /// <summary>
        /// Replaces an intact atlas silhouette with its already-authored modular
        /// cells before the piece enters the cutter. This is presentation-only:
        /// colliders and board ownership remain untouched. Having a real cell
        /// surface lets the shredder consume a row at a time instead of hiding
        /// one large rectangle behind the wheel art.
        /// </summary>
        public void PrepareModularShredderSurface()
        {
            // Keep a complete authored silhouette intact for the full feed.
            // The feed mask occludes it inside the teeth, avoiding the visual
            // gaps that appeared when vertically stacked modules were exposed
            // one at a time. Modular cells remain the fallback for pieces that
            // do not have an authored whole-piece surface.
            if (wholePieceRenderer != null && wholePieceRenderer.enabled)
            {
                SetModularCellPresentationVisible(false);
                configuredShredderRenderers = joinedPieceRenderers.Count > 0
                    ? joinedPieceRenderers.ToArray()
                    : new[] { wholePieceRenderer };
                AppendBombOverlayToShredderPresentation();
                return;
            }

            if (collisionCellVisuals == null || collisionCellVisuals.Count == 0)
                return;

            // Most current pieces are already composed from modular cells and
            // therefore do not have an enabled atlas renderer to swap out.
            // They still need the same explicit renderer list as atlas-backed
            // pieces; without it the shredder has no contact surface to sample
            // and consequently emits no debris at all.
            SetModularCellPresentationVisible(true);

            if (configuredShredderRenderers == null ||
                configuredShredderRenderers.Length != collisionCellVisuals.Count)
            {
                configuredShredderRenderers = new SpriteRenderer[collisionCellVisuals.Count];
            }

            for (int index = 0; index < collisionCellVisuals.Count; index++)
                configuredShredderRenderers[index] = collisionCellVisuals[index];

            AppendBombOverlayToShredderPresentation();
        }

        private void AppendBombOverlayToShredderPresentation()
        {
            if (!IsBomb || IsBombDefused || bombOverlayRenderer == null ||
                !bombOverlayRenderer.enabled || configuredShredderRenderers == null)
            {
                return;
            }

            if (TryBuildBombShredderFragments())
            {
                int originalCount = configuredShredderRenderers.Length;
                SpriteRenderer[] renderersWithFragments = new SpriteRenderer[
                    originalCount + bombShredderFragmentRenderers.Count];
                Array.Copy(configuredShredderRenderers, renderersWithFragments, originalCount);
                for (int index = 0; index < bombShredderFragmentRenderers.Count; index++)
                    renderersWithFragments[originalCount + index] = bombShredderFragmentRenderers[index];
                configuredShredderRenderers = renderersWithFragments;
                return;
            }

            SpriteRenderer[] renderersWithBomb = new SpriteRenderer[
                configuredShredderRenderers.Length + 1];
            Array.Copy(
                configuredShredderRenderers,
                renderersWithBomb,
                configuredShredderRenderers.Length);
            renderersWithBomb[renderersWithBomb.Length - 1] = bombOverlayRenderer;
            configuredShredderRenderers = renderersWithBomb;
        }

        private bool TryBuildBombShredderFragments()
        {
            if (bombDefuseSplitMaterial == null || bombOverlayRenderer == null ||
                bombOverlayRenderer.sprite == null || bombShredderFragmentRenderers.Count > 0)
                return false;

            int subdivisions = bombShredderVoxelSubdivisions;
            int fragmentCount = subdivisions * subdivisions;
            for (int index = 0; index < fragmentCount; index++)
            {
                if (!TryGetAvailableIceSlot(out SpriteRenderer fragment))
                {
                    ClearBombShredderFragments();
                    return false;
                }

                // Add immediately so the next allocation cannot lease the
                // same pooled renderer.
                bombShredderFragmentRenderers.Add(fragment);
            }

            SpriteRenderer source = bombOverlayRenderer;
            Bounds spriteBounds = source.sprite.bounds;
            Vector2 minimum = spriteBounds.min;
            Vector2 fragmentSize = spriteBounds.size / subdivisions;
            for (int y = 0; y < subdivisions; y++)
            {
                for (int x = 0; x < subdivisions; x++)
                {
                    int index = y * subdivisions + x;
                    Vector2 clipMinimum = minimum + Vector2.Scale(
                        fragmentSize,
                        new Vector2(x, y));
                    ConfigureBombFragmentRenderer(
                        bombShredderFragmentRenderers[index],
                        source,
                        clipMinimum,
                        clipMinimum + fragmentSize,
                        $"Bomb Shred Voxel {x}_{y}");
                }
            }

            source.enabled = false;
            return true;
        }

        public void ClearVoxelPresentation()
        {
            configuredVoxelShards.Clear();
            configuredShredderRenderers = null;
        }

        /// <summary>
        /// Populates the buffer with world-space bounding boxes of each active block cell.
        /// This ensures cutter contact and voxel emission respect the true piece geometry,
        /// avoiding false bounds on empty notches in L, U, or irregular pieces.
        /// </summary>
        public int GetActiveCellBounds(List<Bounds> resultsBuffer)
        {
            if (resultsBuffer == null)
                return 0;

            resultsBuffer.Clear();

            // 1. Authoritative physical collision cells: each 1x1 block has its own
            // BoxCollider2D on its part slot with true local/world transform.
            if (collisionCells != null && collisionCells.Count > 0)
            {
                for (int i = 0; i < collisionCells.Count; i++)
                {
                    BoxCollider2D cell = collisionCells[i];
                    if (cell == null)
                        continue;

                    Vector3 center = cell.transform.TransformPoint(cell.offset);
                    resultsBuffer.Add(CreateWorldCellBounds(
                        cell.transform,
                        center,
                        cell.size));
                }

                if (resultsBuffer.Count > 0)
                    return resultsBuffer.Count;
            }

            // 2. Modular cell visuals fallback
            if (collisionCellVisuals != null && collisionCellVisuals.Count > 0)
            {
                for (int i = 0; i < collisionCellVisuals.Count; i++)
                {
                    SpriteRenderer visual = collisionCellVisuals[i];
                    if (visual == null)
                        continue;

                    Vector3 center = visual.transform.position;
                    Vector2 cellSize = (fullCollisionCellSizes != null && i < fullCollisionCellSizes.Count)
                        ? fullCollisionCellSizes[i]
                        : Vector2.one;

                    resultsBuffer.Add(CreateWorldCellBounds(
                        visual.transform,
                        center,
                        cellSize));
                }

                if (resultsBuffer.Count > 0)
                    return resultsBuffer.Count;
            }

            // 3. Last fallback: root transform / renderer bounds
            if (wholePieceRenderer != null && wholePieceRenderer.enabled)
            {
                resultsBuffer.Add(wholePieceRenderer.bounds);
                return 1;
            }

            resultsBuffer.Add(new Bounds(transform.position, Vector3.one));
            return 1;
        }

        /// <summary>
        /// Returns the true vertical footprint from the cached cell geometry.
        /// This deliberately does not read Collider2D.bounds: exit handoff
        /// runs while normal piece colliders are disabled.
        /// </summary>
        public bool TryGetActiveCellVerticalExtents(
            out float lowestCellY,
            out float highestCellY)
        {
            lowestCellY = float.PositiveInfinity;
            highestCellY = float.NegativeInfinity;

            if (collisionCells != null && collisionCells.Count > 0)
            {
                for (int index = 0; index < collisionCells.Count; index++)
                {
                    BoxCollider2D cell = collisionCells[index];
                    if (cell == null)
                        continue;

                    Vector3 center = cell.transform.TransformPoint(cell.offset);
                    Vector2 size = fullCollisionCellSizes != null && index < fullCollisionCellSizes.Count
                        ? fullCollisionCellSizes[index]
                        : cell.size;
                    Bounds bounds = CreateWorldCellBounds(cell.transform, center, size);
                    lowestCellY = Mathf.Min(lowestCellY, bounds.min.y);
                    highestCellY = Mathf.Max(highestCellY, bounds.max.y);
                }
            }

            if (lowestCellY != float.PositiveInfinity)
                return true;

            if (collisionCellVisuals != null && collisionCellVisuals.Count > 0)
            {
                for (int index = 0; index < collisionCellVisuals.Count; index++)
                {
                    SpriteRenderer visual = collisionCellVisuals[index];
                    if (visual == null)
                        continue;

                    Vector2 size = fullCollisionCellSizes != null && index < fullCollisionCellSizes.Count
                        ? fullCollisionCellSizes[index]
                        : Vector2.one;
                    Bounds bounds = CreateWorldCellBounds(
                        visual.transform,
                        visual.transform.position,
                        size);
                    lowestCellY = Mathf.Min(lowestCellY, bounds.min.y);
                    highestCellY = Mathf.Max(highestCellY, bounds.max.y);
                }
            }

            if (lowestCellY != float.PositiveInfinity)
                return true;

            lowestCellY = transform.position.y - .5f;
            highestCellY = transform.position.y + .5f;
            return true;
        }

        private static Bounds CreateWorldCellBounds(
            Transform cellTransform,
            Vector3 center,
            Vector2 localSize)
        {
            // TransformVector includes both the hierarchy scale and rotation.
            // Summing the projected local axes yields the world-space AABB of
            // this actual cell instead of treating a rotated cell as axis-aligned.
            Vector3 worldWidth = cellTransform.TransformVector(new Vector3(localSize.x, 0f, 0f));
            Vector3 worldHeight = cellTransform.TransformVector(new Vector3(0f, localSize.y, 0f));
            float width = Mathf.Max(.2f, Mathf.Abs(worldWidth.x) + Mathf.Abs(worldHeight.x));
            float height = Mathf.Max(.2f, Mathf.Abs(worldWidth.y) + Mathf.Abs(worldHeight.y));
            return new Bounds(center, new Vector3(width, height, 1f));
        }

        public void RemoveVoxelPresentation(IReadOnlyList<VoxelShard> voxels)
        {
            if (voxels == null || voxels.Count == 0 || configuredVoxelShards.Count == 0)
                return;

            for (int voxelIndex = 0; voxelIndex < voxels.Count; voxelIndex++)
            {
                VoxelShard voxel = voxels[voxelIndex];
                for (int configuredIndex = configuredVoxelShards.Count - 1; configuredIndex >= 0; configuredIndex--)
                {
                    if (configuredVoxelShards[configuredIndex] == voxel)
                        configuredVoxelShards.RemoveAt(configuredIndex);
                }
            }

            configuredShredderRenderers = new SpriteRenderer[configuredVoxelShards.Count];
            for (int index = 0; index < configuredVoxelShards.Count; index++)
                configuredShredderRenderers[index] = configuredVoxelShards[index].Renderer;
        }

        public void SetPoolReturnHandler(Action<PuzzlePiece> returnHandler)
        {
            returnToPool = returnHandler;
        }

        public void ReleaseInstance()
        {
            MarkDespawnedInBoard();

            if (returnToPool != null)
            {
                returnToPool(this);
                return;
            }

            Debug.LogError("[PiecePool] A PuzzlePiece was released without a pool return handler. The instance has been disabled instead of destroyed.", this);
            gameObject.SetActive(false);
        }

        private void MarkDespawnedInBoard()
        {
            if (gridDespawned)
                return;

            PrototypeBoard board = PrototypeBoard.Active;
            if (board == null)
                return;

            bool releasedShredderReservation = IsBeingShredded;
            if (!board.TryDespawnPieceFromGrid(this))
                return;

            gridDespawned = true;
            if (releasedShredderReservation)
                PuzzleDragController.WakeUpGravity();
        }

        private List<Vector2Int> cachedActiveVoxelOffsets;
        private Transform cachedFirstActiveVoxel;

        public void InvalidateVoxelCache()
        {
            cachedActiveVoxelOffsets = null;
            cachedFirstActiveVoxel = null;
        }

        public List<Vector2Int> GetActiveVoxelOffsets()
        {
            if (cachedActiveVoxelOffsets != null)
                return cachedActiveVoxelOffsets;

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            float size = 1f;
            if (level != null && level.subdivisions > 0)
                size = 1f / level.subdivisions;

            cachedActiveVoxelOffsets = new List<Vector2Int>();
            foreach (Transform child in transform)
            {
                if (child.gameObject.activeSelf)
                {
                    string n = child.name;
                    if (n.Contains("Overlay") || 
                        n.Contains("Selection") || 
                        n.Contains("Collision Geometry") ||
                        n.Contains("Outline") ||
                        n.Contains("Ice") ||
                        n.Contains("Border") ||
                        n.Contains("Count")) 
                        continue;

                    int localX = Mathf.RoundToInt(child.localPosition.x / size);
                    int localY = Mathf.RoundToInt(child.localPosition.y / size);
                    Vector2Int cell = new Vector2Int(localX, localY);
                    if (!cachedActiveVoxelOffsets.Contains(cell))
                        cachedActiveVoxelOffsets.Add(cell);
                }
            }

            if (cachedActiveVoxelOffsets.Count == 0)
            {
                if (solidColliders == null) CacheSolidColliders();
                if (solidColliders != null && solidColliders.Length > 0)
                {
                    for (int i = 0; i < solidColliders.Length; i++)
                    {
                        if (solidColliders[i] != null && solidColliders[i].enabled && solidColliders[i].gameObject.activeInHierarchy)
                        {
                            Vector2 localPos = solidColliders[i].transform.localPosition;
                            int x = Mathf.RoundToInt(localPos.x / size);
                            int y = Mathf.RoundToInt(localPos.y / size);
                            Vector2Int cell = new Vector2Int(x, y);
                            if (!cachedActiveVoxelOffsets.Contains(cell))
                                cachedActiveVoxelOffsets.Add(cell);
                        }
                    }
                }
            }

            if (cachedActiveVoxelOffsets.Count == 0)
                cachedActiveVoxelOffsets.Add(Vector2Int.zero);

            return cachedActiveVoxelOffsets;
        }

        public Transform GetFirstActiveVoxelTransform()
        {
            if (cachedFirstActiveVoxel != null && cachedFirstActiveVoxel.gameObject.activeSelf)
                return cachedFirstActiveVoxel;

            foreach (Transform child in transform)
            {
                if (child.gameObject.activeSelf)
                {
                    string n = child.name;
                    if (n.Contains("Overlay") || 
                        n.Contains("Selection") || 
                        n.Contains("Collision Geometry") ||
                        n.Contains("Outline") ||
                        n.Contains("Ice") ||
                        n.Contains("Border") ||
                        n.Contains("Count")) 
                        continue;

                    cachedFirstActiveVoxel = child;
                    return child;
                }
            }
            return transform;
        }

        private void Start()
        {
            CacheSolidColliders();
        }

        public void SetSelected(bool isSelected)
        {
            // A piece is committed to the shredder as soon as it enters the
            // catch zone. It must not regain drag selection during the feed.
            if (isSelected && (IsFrozen || IsShredderInteractionLocked))
                return;

            this.isSelected = isSelected;

            ApplyOutlinePresentation();
        }

        /// <summary>
        /// Enables the existing selected-outline styling for booster targeting
        /// without changing gameplay state or drag selection.
        /// </summary>
        public void SetBoosterTargeted(bool isTargeted)
        {
            isBoosterTargeted = isTargeted;
            ApplyOutlinePresentation();
        }

        /// <summary>
        /// Raises the already-authored contour of one piece above a tutorial
        /// dimmer. This preserves the real, possibly irregular piece outline
        /// instead of drawing an approximate filled rectangle over it.
        /// Also elevates child SpriteRenderers above the dimmer backdrop so the
        /// piece appears colorful and unobstructed by the dark backdrop.
        /// </summary>
        public void SetTutorialTargeted(bool isTargeted, int sortingOrder = 0)
        {
            if (isTutorialTargeted == isTargeted && tutorialTargetSortingOrder == sortingOrder)
                return;

            isTutorialTargeted = isTargeted;
            tutorialTargetSortingOrder = sortingOrder;
            RefreshTargetingForegroundSorting();
            ApplyOutlinePresentation();
        }

        /// <summary>
        /// Keeps a piece's authored visuals in front of the world-space
        /// targeting dimmer. This is presentation-only: it neither marks the
        /// piece as a target nor affects board, input, or lifecycle state.
        /// </summary>
        public void SetBoosterTargetingForeground(bool isForeground, int sortingOrder = 0)
        {
            if (isBoosterTargetingForeground == isForeground &&
                boosterTargetingForegroundSortingOrder == sortingOrder)
            {
                return;
            }

            isBoosterTargetingForeground = isForeground;
            boosterTargetingForegroundSortingOrder = sortingOrder;
            RefreshTargetingForegroundSorting();
            ApplyOutlinePresentation();
        }

        private void RefreshTargetingForegroundSorting()
        {
            bool shouldRenderInForeground = isTutorialTargeted || isBoosterTargetingForeground;
            if (!shouldRenderInForeground)
            {
                RestoreTargetingForegroundSorting();
                return;
            }

            int sortingOrder = isTutorialTargeted
                ? tutorialTargetSortingOrder
                : boosterTargetingForegroundSortingOrder;
            ApplyTargetingForegroundSorting(sortingOrder);
        }

        private void ApplyTargetingForegroundSorting(int sortingOrder)
        {
            if (targetingForegroundRenderers == null)
            {
                SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
                if (renderers != null && renderers.Length > 0)
                {
                    targetingForegroundRenderers = renderers;
                    targetingForegroundOriginalSortingOrders = new int[renderers.Length];
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        SpriteRenderer renderer = renderers[i];
                        if (renderer != null)
                            targetingForegroundOriginalSortingOrders[i] = renderer.sortingOrder;
                    }
                }

                TextMeshPro[] texts = GetComponentsInChildren<TextMeshPro>(true);
                if (texts != null && texts.Length > 0)
                {
                    targetingForegroundTexts = texts;
                    targetingForegroundTextOriginalSortingOrders = new int[texts.Length];
                    for (int i = 0; i < texts.Length; i++)
                    {
                        TextMeshPro text = texts[i];
                        if (text != null && text.renderer != null)
                            targetingForegroundTextOriginalSortingOrders[i] = text.renderer.sortingOrder;
                    }
                }
            }

            int targetOrder = Mathf.Max(0, sortingOrder - 1);
            if (targetingForegroundRenderers != null)
            {
                for (int i = 0; i < targetingForegroundRenderers.Length; i++)
                {
                    SpriteRenderer renderer = targetingForegroundRenderers[i];
                    if (renderer != null && renderer.transform.IsChildOf(transform))
                        renderer.sortingOrder = targetOrder;
                }
            }

            if (targetingForegroundTexts != null)
            {
                for (int i = 0; i < targetingForegroundTexts.Length; i++)
                {
                    TextMeshPro text = targetingForegroundTexts[i];
                    if (text != null && text.renderer != null && text.transform.IsChildOf(transform))
                        text.renderer.sortingOrder = targetOrder;
                }
            }
        }

        private void RestoreTargetingForegroundSorting()
        {
            if (targetingForegroundRenderers != null && targetingForegroundOriginalSortingOrders != null)
            {
                for (int i = 0; i < targetingForegroundRenderers.Length; i++)
                {
                    SpriteRenderer renderer = targetingForegroundRenderers[i];
                    if (renderer != null && renderer.transform.IsChildOf(transform))
                    {
                        renderer.sortingOrder = targetingForegroundOriginalSortingOrders[i];
                    }
                }
                targetingForegroundRenderers = null;
                targetingForegroundOriginalSortingOrders = null;
            }

            if (targetingForegroundTexts != null && targetingForegroundTextOriginalSortingOrders != null)
            {
                for (int i = 0; i < targetingForegroundTexts.Length; i++)
                {
                    TextMeshPro text = targetingForegroundTexts[i];
                    if (text != null && text.renderer != null && text.transform.IsChildOf(transform))
                    {
                        text.renderer.sortingOrder = targetingForegroundTextOriginalSortingOrders[i];
                    }
                }
                targetingForegroundTexts = null;
                targetingForegroundTextOriginalSortingOrders = null;
            }
        }

        /// <summary>
        /// Appends the current world-space footprints of each runtime cell
        /// without allocating. Hammer targeting uses these only as visual
        /// overlays; the board remains the action authority.
        /// </summary>
        public void CollectTargetableCells(List<TargetableCell> results)
        {
            if (results == null || IsFrozen || IsShredderInteractionLocked || collisionCells == null ||
                fullCollisionCellSizes == null)
                return;

            int cellCount = Mathf.Min(collisionCells.Count, fullCollisionCellSizes.Count);
            for (int index = 0; index < cellCount; index++)
            {
                BoxCollider2D cell = collisionCells[index];
                if (cell == null || !cell.enabled)
                    continue;

                Vector3 scale = cell.transform.lossyScale;
                Vector2 localSize = fullCollisionCellSizes[index];
                Vector2 worldSize = new Vector2(
                    localSize.x * Mathf.Abs(scale.x),
                    localSize.y * Mathf.Abs(scale.y));
                results.Add(new TargetableCell(cell.bounds.center, worldSize));
            }
        }

        /// <summary>
        /// Resolves a point inside the rendered modular block and returns that
        /// block's complete world footprint. Tutorial presentation uses this to
        /// frame one authored block rather than one fine-grid subdivision.
        /// </summary>
        public bool TryGetTargetableCellAt(Vector2 worldPosition, out TargetableCell targetableCell)
        {
            targetableCell = default;
            if (!TryGetCellIndexAt(worldPosition, out int targetIndex) ||
                collisionCells == null || fullCollisionCellSizes == null ||
                targetIndex < 0 || targetIndex >= collisionCells.Count ||
                targetIndex >= fullCollisionCellSizes.Count)
                return false;

            BoxCollider2D cell = collisionCells[targetIndex];
            if (cell == null)
                return false;

            Vector3 scale = cell.transform.lossyScale;
            Vector2 localSize = fullCollisionCellSizes[targetIndex];
            targetableCell = new TargetableCell(
                cell.bounds.center,
                new Vector2(
                    localSize.x * Mathf.Abs(scale.x),
                    localSize.y * Mathf.Abs(scale.y)));
            return true;
        }

        private void ApplyOutlinePresentation()
        {
            bool useTargetOutline = isSelected || isBoosterTargeted || isTutorialTargeted;
            float outlineWidth = useTargetOutline ? selectedOutlineWidth : restingOutlineWidth;
            Color outlineColor = useTargetOutline ? selectedOutlineColor : restingOutlineColor;
            int sortingOrder = isTutorialTargeted
                ? tutorialTargetSortingOrder
                : isBoosterTargetingForeground
                    ? boosterTargetingForegroundSortingOrder
                    : useTargetOutline ? selectedOutlineSortingOrder : restingOutlineSortingOrder;
            for (int index = 0; index < outlineRenderers.Count; index++)
            {
                LineRenderer outline = outlineRenderers[index];
                // A pooled piece is reset before RuntimePieceFactory rebuilds
                // its contour. LineRenderer rejects presentation updates while
                // it has no valid contour points.
                if (outline == null || outline.positionCount < 3)
                    continue;

                outline.startWidth = outlineWidth;
                outline.endWidth = outlineWidth;
                outline.startColor = outlineColor;
                outline.endColor = outlineColor;
                outline.sortingOrder = sortingOrder;
            }
        }

        public void ConfigureOutlinePresentation(
            float restingWidth,
            float selectedWidth,
            Color restingColor,
            Color selectedColor,
            int restingSortingOrder,
            int selectedSortingOrder)
        {
            restingOutlineWidth = Mathf.Max(0.001f, restingWidth);
            selectedOutlineWidth = Mathf.Max(0.001f, selectedWidth);
            restingOutlineColor = restingColor;
            selectedOutlineColor = selectedColor;
            restingOutlineSortingOrder = restingSortingOrder;
            selectedOutlineSortingOrder = selectedSortingOrder;
            ApplyOutlinePresentation();
        }

        public void ConfigureCollisionGeometry(
            CompositeCollider2D composite,
            List<BoxCollider2D> cells,
            List<SpriteRenderer> cellVisuals)
        {
            List<Vector2> cellSizes = new List<Vector2>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
                cellSizes.Add(cells[i].size);

            ConfigureCollisionGeometry(composite, cells, cellVisuals, cellSizes);
        }

        public void Configure(PieceRuntimeSetup setup)
        {
            PrepareForRuntimeSetup();
            ConfigureSourcePieceId(setup.SourcePieceId);
            ConfigureProgressUnits(setup.ProgressUnits);
            ConfigureVisualColor(setup.VisualColor);
            ConfigureCollisionGeometry(
                setup.CompositeCollider,
                setup.CollisionCells,
                setup.CollisionCellVisuals);
            iceOverlaySprite = setup.IceOverlaySprite;
            iceOverlayTint = setup.IceOverlayTint;
            iceFrostTint = setup.IceFrostTint;
            bombOverlaySprite = setup.BombOverlaySprite;
            bombOverlayFill = setup.BombOverlayFill;
            bombDefuseSplitMaterial = setup.BombDefuseSplitMaterial;
            bombShredderVoxelSubdivisions = Mathf.Clamp(setup.BombShredderVoxelSubdivisions, 2, 8);
            ConfigureFreeze(
                setup.SpecialBlockType == PieceSpecialBlockType.Bomb ? 0 : setup.FrozenMoveCount,
                setup.IceCounterFont,
                setup.IceCounterFontSize,
                setup.IceCounterTextColor,
                setup.IceCounterOutlineColor,
                setup.IceCounterOutlineWidth,
                setup.IceCounterOffset,
                setup.IceCounterSortingOrder);
            iceParticleVfxHandle = setup.SpecialBlockType != PieceSpecialBlockType.Bomb && setup.FrozenMoveCount > 0
                ? iceParticleVfx?.RentPair()
                : null;
            ConfigureBomb(
                setup.SpecialBlockType == PieceSpecialBlockType.Bomb,
                setup.BombTimerSeconds,
                setup.BombCounterFontSize,
                setup.BombCounterTextColor,
                setup.BombCounterOutlineColor,
                setup.BombCounterOutlineWidth,
                setup.BombCounterOffset,
                setup.BombCounterFont,
                setup.BombCounterSortingOrder);
        }

        public void ConfigureIceParticleVfx(IIceBlockParticleVfx particleVfx)
        {
            iceParticleVfx = particleVfx;
        }

        private void PrepareForRuntimeSetup()
        {
            ClearIceVisuals();
            ClearBombVisuals();
            ClearWholePiecePresentation();
            iceOverlaySprite = null;
            iceOverlayTint = new Color(1f, 1f, 1f, .42f);
            iceFrostTint = new Color(1f, 1f, 1f, .18f);
            bombOverlaySprite = null;
            bombOverlayFill = .936f;
            bombDefuseSplitMaterial = null;
            bombShredderVoxelSubdivisions = 4;
            shredderReservationCells.Clear();
            shredderCellsReadyToRelease.Clear();
            shredderReservationStartBodyY = 0f;
            shredderHandoffMode = ShredderHandoffMode.None;
            keepShredderGridReservationUntilDespawn = false;
            RestoreDefaultPhysicsLayers();
            destructionReported = false;
            useFullCollisionGeometry = false;
            isSelected = false;
            isBoosterTargeted = false;
            isTutorialTargeted = false;
            isBoosterTargetingForeground = false;
            RestoreTargetingForegroundSorting();
            IsFrozen = false;
            IsBomb = false;
            IsBombDefused = false;
            BombTimerSeconds = 0f;
            previousBombRemaining = -1;
            InvalidateVoxelCache();
        }

        public void ConfigureProgressUnits(int units)
        {
            ProgressUnits = Mathf.Max(1, units);
            RemainingProgressUnits = ProgressUnits;
        }

        public void ConfigureSourcePieceId(int sourcePieceId)
        {
            SourcePieceId = sourcePieceId;
        }

        /// <summary>
        /// Builds the authoritative fine-grid shape from this fragment's live
        /// collider cells. Hammer splitting changes topology at runtime, so the
        /// original level definition can no longer describe this particular
        /// fragment.
        /// </summary>
        public bool TryCreateGridModel(
            GravityLevelDefinition level,
            int modelId,
            out PieceModel model)
        {
            model = null;
            if (level == null || Body == null || collisionCells == null || collisionCells.Count == 0)
                return false;

            // A runtime collider is not always one fine grid cell.  The piece
            // factory collapses complete modules into a single 1x1 collider,
            // which represents subdivisions * subdivisions fine cells.  Using
            // just collider.bounds.center here made a hammer fragment look
            // almost empty to the grid and allowed other pieces to overlap its
            // visible area.  Expand every collider back into its full fine-cell
            // footprint before constructing the authoritative model.
            List<GridCoordinate> worldCells = new List<GridCoordinate>(collisionCells.Count);
            GridCoordinate minimum = default;
            bool hasCell = false;
            for (int index = 0; index < collisionCells.Count; index++)
            {
                BoxCollider2D cell = collisionCells[index];
                if (cell == null || !cell.enabled)
                    continue;

                // The resting collision profile deliberately shrinks colliders
                // by a small skin. That profile is presentation/physics
                // support only; deriving the board footprint from bounds made
                // a hammer fragment's grid cells depend on that shrink and
                // could move it into a neighbouring obstacle or piece.
                Vector2 fullLocalSize = fullCollisionCellSizes != null && index < fullCollisionCellSizes.Count
                    ? fullCollisionCellSizes[index]
                    : cell.size;
                Vector3 lossyScale = cell.transform.lossyScale;
                Vector2 fullWorldSize = Vector2.Scale(
                    fullLocalSize,
                    new Vector2(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y)));
                Vector2 fullWorldCenter = cell.transform.TransformPoint(cell.offset);
                int fineWidth = Mathf.Max(
                    1,
                    Mathf.RoundToInt(fullWorldSize.x * level.subdivisions));
                int fineHeight = Mathf.Max(
                    1,
                    Mathf.RoundToInt(fullWorldSize.y * level.subdivisions));
                float fineCellSize = 1f / level.subdivisions;
                GridCoordinate bottomLeft = GravityLevelGridCoordinates.WorldToFineCell(
                    level,
                    new Vector2(
                        fullWorldCenter.x - fullWorldSize.x * .5f + fineCellSize * .5f,
                        fullWorldCenter.y - fullWorldSize.y * .5f + fineCellSize * .5f));

                for (int y = 0; y < fineHeight; y++)
                {
                    for (int x = 0; x < fineWidth; x++)
                    {
                        GridCoordinate coordinate = new GridCoordinate(
                            bottomLeft.X + x,
                            bottomLeft.Y + y);
                        if (!hasCell)
                        {
                            minimum = coordinate;
                            hasCell = true;
                        }
                        else
                        {
                            minimum = new GridCoordinate(
                                Mathf.Min(minimum.X, coordinate.X),
                                Mathf.Min(minimum.Y, coordinate.Y));
                        }

                        worldCells.Add(coordinate);
                    }
                }
            }

            if (!hasCell)
                return false;

            List<GridCoordinate> localCells = new List<GridCoordinate>(worldCells.Count);
            for (int index = 0; index < worldCells.Count; index++)
            {
                GridCoordinate worldCell = worldCells[index];
                localCells.Add(new GridCoordinate(
                    worldCell.X - minimum.X,
                    worldCell.Y - minimum.Y));
            }

            // Fragment topology is generated from this root's rendered slot
            // layout. Use the root position for its pivot as well: a pooled
            // Rigidbody may still expose its previous physics pose until the
            // next physics sync, which would shift the grid model away from
            // the newly built collider and visual geometry.
            GridCoordinate pivot = GravityLevelGridCoordinates.WorldToFineCell(
                level,
                transform.position);
            GridCoordinate pivotOffset = new GridCoordinate(
                minimum.X - pivot.X,
                minimum.Y - pivot.Y);
            model = new PieceModel(modelId, minimum, pivotOffset, localCells);
            return true;
        }

        public void ConfigureRemainingProgress(float units)
        {
            RemainingProgressUnits = Mathf.Max(0f, units);
        }

        public void ConfigureVisualColor(Color color)
        {
            VisualColor = new Color(color.r, color.g, color.b, 1f);
        }

        /// <summary>
        /// Supplies shared materials for a pooled root. This is presentation
        /// data only: board occupancy and collider geometry remain unchanged.
        /// </summary>
        public void ConfigurePresentationMaterials(
            Material normalMaterial,
            Material iceMaterial,
            Material fallbackMaterial)
        {
            PresentationMaterial = normalMaterial;
            icePresentationMaterial = iceMaterial;
            fallbackPresentationMaterial = fallbackMaterial;
        }

        public void PrepareForShredderPhysics()
        {
            useFullCollisionGeometry = true;
            ApplyCollisionProfile();
        }

        /// <summary>
        /// Keeps a piece visually available for a presentation-only removal
        /// such as the rocket booster. Board physics and colliders must not
        /// participate while the transform is carried by the presentation.
        /// </summary>
        public void PrepareForPresentationRemoval()
        {
            SetSelected(false);

            if (Body == null)
                return;

            Body.velocity = Vector2.zero;
            Body.angularVelocity = 0f;
            Body.simulated = false;
        }

        /// <summary>
        /// Returns this root to an inert pool state. This clears every setting
        /// that the shredder handoff is allowed to mutate, so a later renter
        /// cannot inherit angular motion or an unlocked rigidbody profile.
        /// </summary>
        public void ResetToPooledPhysics()
        {
            RestoreDefaultPhysicsLayers();
            if (Body == null)
                return;

            Body.velocity = Vector2.zero;
            Body.angularVelocity = 0f;
            Body.rotation = 0f;
            Body.angularDrag = 0f;
            Body.gravityScale = 0f;
            Body.bodyType = RigidbodyType2D.Kinematic;
            Body.constraints = RigidbodyConstraints2D.FreezeAll;
            Body.interpolation = RigidbodyInterpolation2D.None;
            Body.useFullKinematicContacts = false;
            Body.sleepMode = RigidbodySleepMode2D.StartAsleep;
            Body.simulated = false;
        }

        /// <summary>
        /// Applies the only runtime physics profile allowed for a board piece:
        /// the captured shredder feed. Normal drag and grid gravity retain
        /// their deterministic kinematic presentation ownership.
        /// </summary>
        public void EnterShredderPhysics(ShredderConfig config)
        {
            if (config == null)
                return;

            PrepareForShredderPhysics();
            ApplyShredderPhysicsLayer();
            if (Body == null)
                return;

            Body.simulated = true;
            Body.bodyType = RigidbodyType2D.Kinematic;
            Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Body.constraints = RigidbodyConstraints2D.None;
            Body.gravityScale = 0f;
            // The kinematic feed remains a real contact surface for a
            // temporary dynamic support chain. This flag also makes its
            // collision policy explicit instead of relying on a prefab default.
            Body.useFullKinematicContacts = true;
            Body.velocity = Vector2.zero;
            Body.angularDrag = config.FeedAngularDrag;
            // Preserve the pose reached during the dynamic support handoff,
            // but stop rotation once this piece becomes a kinematic feed.
            // A wide active feed must not sweep into another simultaneous feed.
            Body.angularVelocity = 0f;
            // Once the coordinate gate accepts this piece, the feed owns its
            // transform. Letting its kinematic collider hit later freefall
            // bodies would make an infinite-mass pinball surface.
            SetPiecePhysicsCollidersEnabled(false);
        }

        /// <summary>
        /// Enables the temporary Dynamic profile used only for pieces that are
        /// physically supported by a root already entering the shredder. The
        /// board has reserved these pieces first; this method owns only the
        /// Unity contact response inside that isolated handoff corridor.
        /// </summary>
        public void EnterShredderSupportPhysics(ShredderConfig config)
        {
            if (config == null)
                return;

            PrepareForShredderPhysics();
            ApplyShredderPhysicsLayer();
            if (Body == null)
                return;

            Body.simulated = true;
            Body.bodyType = RigidbodyType2D.Dynamic;
            Body.gravityScale = config.PhysicsIslandGravityScale;
            Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            // A planner-approved support member already fits the authored
            // mouth in its grid pose. Let it fall and collide vertically, but
            // never rotate or drift sideways into a guide and become a shelf
            // queue before reaching its own coordinate capture plane.
            Body.constraints = RigidbodyConstraints2D.FreezePositionX |
                               RigidbodyConstraints2D.FreezeRotation;
            Body.useFullKinematicContacts = false;
            Body.velocity = Vector2.zero;
            Body.angularVelocity = 0f;
            Body.angularDrag = config.PhysicsIslandAngularDrag;
            Body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            ApplyShredderCollisionMaterial(config.FeedPhysicsMaterial);
            Body.WakeUp();
        }

        public void EnterShredderFreefallPhysics(ShredderConfig config, float downwardSpeed)
        {
            EnterShredderSupportPhysics(config);
            if (Body == null)
                return;

            // The exit phase moves the transform while this body is inert.
            // Synchronise the Rigidbody pose before switching it to Dynamic;
            // otherwise Unity can solve one step from a stale pooled pose.
            Body.position = transform.position;
            Body.rotation = transform.eulerAngles.z;
            Body.constraints = RigidbodyConstraints2D.None;
            Body.angularDrag = config.PhysicsFreefallAngularDrag;
            Body.velocity = Vector2.down * Mathf.Max(
                config.PhysicsFreefallEntrySpeed,
                downwardSpeed);
            Body.angularVelocity = 0f;
            Body.WakeUp();
        }

        private void SetPiecePhysicsCollidersEnabled(bool enabled)
        {
            if (solidColliders == null)
                CacheSolidColliders();

            if (solidColliders == null)
                return;

            for (int index = 0; index < solidColliders.Length; index++)
            {
                Collider2D collider = solidColliders[index];
                if (collider != null && !collider.isTrigger)
                    collider.enabled = enabled;
            }
        }

        /// <summary>
        /// Takes a prevalidated support member through the controlled approach
        /// corridor. This is intentionally kinematic: Unity contacts may still
        /// render the final feed, but no unowned Dynamic body may wedge itself
        /// between the cutter guides and leave the gameplay lifecycle locked.
        /// </summary>
        public void EnterShredderApproachPhysics(ShredderConfig config)
        {
            if (config == null)
                return;

            PrepareForShredderPhysics();
            ApplyShredderPhysicsLayer();
            if (Body == null)
                return;

            Body.simulated = true;
            Body.bodyType = RigidbodyType2D.Kinematic;
            Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Body.constraints = RigidbodyConstraints2D.FreezePositionX |
                               RigidbodyConstraints2D.FreezeRotation;
            Body.gravityScale = 0f;
            Body.useFullKinematicContacts = true;
            Body.velocity = Vector2.zero;
            Body.angularVelocity = 0f;
            Body.angularDrag = config.FeedAngularDrag;
        }

        /// <summary>
        /// Applies the authored shredder-feed material to colliders already
        /// cached by this piece. No hierarchy search occurs during feed.
        /// </summary>
        public void ApplyShredderCollisionMaterial(PhysicsMaterial2D material)
        {
            if (material == null || solidColliders == null)
                return;

            for (int index = 0; index < solidColliders.Length; index++)
            {
                if (solidColliders[index] != null)
                    solidColliders[index].sharedMaterial = material;
            }
        }

        /// <summary>
        /// Captures the authored renderer state immediately before a shredder
        /// feed changes clipping or visibility. Pool return restores this state
        /// before the next piece rents the prefab root.
        /// </summary>
        public void BeginShredderPresentation(
            SpriteRenderer[] renderers,
            float shredderY,
            ShredderConfig shredderConfig)
        {
            RestoreShredderPresentation();
            // The collider footprint shrinks one board cell at a time during
            // a shredder feed. Its outline would therefore draw artificial
            // horizontal seams through the still-visible voxel presentation.
            // The next pool spawn restores the authored root outline.
            SetOutlineRenderersEnabled(false);

            if (renderers == null || renderers.Length == 0)
                return;

            shredderPresentationRenderers = renderers;
            shredderPresentationEnabledStates = new bool[renderers.Length];
            shredderPresentationMaskStates = new SpriteMaskInteraction[renderers.Length];
            shredderPresentationSortingOrders = new int[renderers.Length];
            shredderPresentationOutlineSortingOrder = rootOutline != null
                ? rootOutline.sortingOrder
                : 0;
            for (int index = 0; index < renderers.Length; index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer == null)
                    continue;

                shredderPresentationEnabledStates[index] = renderer.enabled;
                shredderPresentationMaskStates[index] = renderer.maskInteraction;
                shredderPresentationSortingOrders[index] = renderer.sortingOrder;
            }

            if (shredderConfig != null && shredderConfig.EnableAdvancedShreddingPresentation)
                shredderDissolvePresentation?.Begin(
                    renderers,
                    shredderY,
                    shredderConfig,
                    VisualColor);
        }

        /// <summary>
        /// Keeps later pieces visually behind the active shredder feed. This
        /// affects presentation only; board occupancy remains owned by the
        /// grid reservation captured at handoff.
        /// </summary>
        public void SetShredderPresentationDepth(int sortingOrderOffset)
        {
            if (shredderPresentationRenderers == null ||
                shredderPresentationSortingOrders == null)
                return;

            for (int index = 0; index < shredderPresentationRenderers.Length; index++)
            {
                SpriteRenderer renderer = shredderPresentationRenderers[index];
                if (renderer != null && renderer.transform.IsChildOf(transform))
                    renderer.sortingOrder = shredderPresentationSortingOrders[index] + sortingOrderOffset;
            }

            SetOutlineRenderersSortingOrder(shredderPresentationOutlineSortingOrder + sortingOrderOffset);
        }

        public void ApplyShredderPresentationClipping()
        {
            if (shredderPresentationRenderers == null)
                return;

            // The dissolve shader clips against the same world-space cutter
            // line but has a softly irregular edge. Keep the legacy mask as a
            // compatibility fallback for prefabs that do not carry it.
            if (shredderDissolvePresentation != null && shredderDissolvePresentation.IsActive)
                return;

            for (int index = 0; index < shredderPresentationRenderers.Length; index++)
            {
                SpriteRenderer renderer = shredderPresentationRenderers[index];
                if (renderer != null && renderer.transform.IsChildOf(transform))
                    renderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            }
        }

        public void ApplyShredderVisualJitter(Vector2 jitterOffset)
        {
            Vector2 delta = jitterOffset - currentVisualJitterOffset;
            if (delta == Vector2.zero)
                return;

            currentVisualJitterOffset = jitterOffset;
            if (shredderPresentationRenderers != null)
            {
                for (int i = 0; i < shredderPresentationRenderers.Length; i++)
                {
                    SpriteRenderer r = shredderPresentationRenderers[i];
                    if (r != null && r.transform.IsChildOf(transform))
                        r.transform.localPosition += (Vector3)delta;
                }
            }

            if (configuredVoxelShards != null)
            {
                for (int i = 0; i < configuredVoxelShards.Count; i++)
                {
                    VoxelShard s = configuredVoxelShards[i];
                    if (s != null && s.transform.IsChildOf(transform))
                        s.transform.localPosition += (Vector3)delta;
                }
            }
        }

        public void ResetShredderVisualJitter()
        {
            if (currentVisualJitterOffset == Vector2.zero)
                return;

            Vector2 undoDelta = -currentVisualJitterOffset;
            currentVisualJitterOffset = Vector2.zero;

            if (shredderPresentationRenderers != null)
            {
                for (int i = 0; i < shredderPresentationRenderers.Length; i++)
                {
                    SpriteRenderer r = shredderPresentationRenderers[i];
                    if (r != null && r.transform.IsChildOf(transform))
                        r.transform.localPosition += (Vector3)undoDelta;
                }
            }

            if (configuredVoxelShards != null)
            {
                for (int i = 0; i < configuredVoxelShards.Count; i++)
                {
                    VoxelShard s = configuredVoxelShards[i];
                    if (s != null && s.transform.IsChildOf(transform))
                        s.transform.localPosition += (Vector3)undoDelta;
                }
            }
        }

        public void HideShredderRenderer(SpriteRenderer renderer)
        {
            if (renderer == null || !renderer.transform.IsChildOf(transform))
                return;

            renderer.enabled = false;
            if (renderer == bombOverlayRenderer || bombShredderFragmentRenderers.Contains(renderer))
            {
                HideBombCounterAfterShredderPass();
            }
        }

        private void HideBombCounterAfterShredderPass()
        {
            if (bombCounterText == null)
                return;

            for (int index = 0; index < bombShredderFragmentRenderers.Count; index++)
            {
                SpriteRenderer fragment = bombShredderFragmentRenderers[index];
                if (fragment != null && fragment.enabled)
                    return;
            }

            bombCounterText.enabled = false;
            bombCounterText.gameObject.SetActive(false);
        }

        private void RestoreDefaultCollisionMaterials()
        {
            if (solidColliders == null || defaultSolidColliderMaterials == null)
                return;

            int count = Mathf.Min(solidColliders.Length, defaultSolidColliderMaterials.Length);
            for (int index = 0; index < count; index++)
            {
                if (solidColliders[index] != null)
                    solidColliders[index].sharedMaterial = defaultSolidColliderMaterials[index];
            }
        }

        private void RestoreShredderPresentation()
        {
            ResetShredderVisualJitter();
            shredderDissolvePresentation?.Restore();
            if (shredderPresentationRenderers == null)
                return;

            for (int index = 0; index < shredderPresentationRenderers.Length; index++)
            {
                SpriteRenderer renderer = shredderPresentationRenderers[index];
                if (renderer == null || !renderer.transform.IsChildOf(transform))
                    continue;

                renderer.enabled = shredderPresentationEnabledStates[index];
                renderer.maskInteraction = shredderPresentationMaskStates[index];
                renderer.sortingOrder = shredderPresentationSortingOrders[index];
                renderer.SetPropertyBlock(null);
            }

            shredderPresentationRenderers = null;
            shredderPresentationEnabledStates = null;
            shredderPresentationMaskStates = null;
            shredderPresentationSortingOrders = null;
            SetOutlineRenderersSortingOrder(shredderPresentationOutlineSortingOrder);
        }

        public void ReleaseCollisionCellsAtOrBelow(float worldY, List<BoxCollider2D> releasedBuffer = null)
        {
            AdvanceShredderGridReleaseFrontier(worldY);

            if (collisionCells == null)
                return;

            bool geometryChanged = false;
            for (int i = 0; i < collisionCells.Count; i++)
            {
                BoxCollider2D cell = collisionCells[i];
                if (cell == null || !cell.enabled || cell.bounds.min.y > worldY)
                    continue;

                // This physical cell has entered the grinder. Remove only this
                // lower section; the remaining upper cells stay solid until they
                // reach the same line on a later physics step.
                if (releasedBuffer != null)
                    releasedBuffer.Add(cell);

                cell.enabled = false;
                geometryChanged = true;
            }

            if (!geometryChanged)
                return;

            if (compositeCollider != null)
                compositeCollider.GenerateGeometry();
            CacheSolidColliders();
            // The shredder releases collision cells before their individual
            // voxel visuals finish crossing the cutter. Do not rebuild the
            // root outline from that intermediate collision geometry.
            if (!IsBeingShredded)
                RuntimePieceFactory.RefreshOutline(this);
            InvalidateVoxelCache();
            Physics2D.SyncTransforms();
        }

        private void CaptureShredderReservationCells(PieceModel model)
        {
            shredderReservationCells.Clear();
            shredderCellsReadyToRelease.Clear();

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (model == null || level == null)
                return;

            for (int index = 0; index < model.LocalCells.Count; index++)
            {
                GridCoordinate localCoordinate = model.LocalCells[index];
                Vector2 worldCenter = GravityLevelGridCoordinates.FineCellToWorld(
                    level,
                    model.Anchor.Offset(localCoordinate));
                shredderReservationCells.Add(new ShredderReservationCell(
                    localCoordinate,
                    worldCenter.y));
            }

            shredderReservationStartBodyY = Body != null ? Body.position.y : transform.position.y;
        }

        /// <summary>
        /// Advances the logical cutter frontier one or more fine rows. Feed
        /// tremor and rotation are presentation-only, so the authoritative
        /// release boundary uses the kinematic feed's vertical displacement.
        /// All cells on a crossed row are released in one board transaction.
        /// </summary>
        private void AdvanceShredderGridReleaseFrontier(float shredderY)
        {
            if (keepShredderGridReservationUntilDespawn)
                return;

            if (shredderReservationCells.Count == 0)
                return;

            float currentBodyY = Body != null ? Body.position.y : transform.position.y;
            float verticalFeedDistance = currentBodyY - shredderReservationStartBodyY;
            float initialSpaceFrontierY = shredderY - verticalFeedDistance;
            shredderCellsReadyToRelease.Clear();
            for (int index = 0; index < shredderReservationCells.Count; index++)
            {
                ShredderReservationCell cell = shredderReservationCells[index];
                if (cell.released || cell.initialWorldCenterY > initialSpaceFrontierY)
                    continue;

                cell.released = true;
                shredderReservationCells[index] = cell;
                shredderCellsReadyToRelease.Add(cell.localCoordinate);
            }

            if (shredderCellsReadyToRelease.Count > 0)
            {
                // Releasing a reserved row wakes the grid gravity owner. The
                // grid then selects and commits the next legal fall before
                // presentation moves any intact piece.
                PrototypeBoard.Active?.TryReleaseShredderGridCells(
                    this,
                    shredderCellsReadyToRelease);
            }
        }

        private void ConfigureCollisionGeometry(
            CompositeCollider2D composite,
            List<BoxCollider2D> cells,
            List<SpriteRenderer> cellVisuals,
            List<Vector2> cellSizes)
        {
            compositeCollider = composite;
            collisionCells = new List<BoxCollider2D>(cells);
            collisionCellVisuals = new List<SpriteRenderer>(cellVisuals);
            fullCollisionCellSizes = new List<Vector2>(cellSizes);

            ApplyCollisionProfile();
            CacheSolidColliders(true);
        }

        public void ConfigureFreeze(
            int requiredDestroyedPieces,
            TMP_FontAsset counterFont,
            float counterFontSize,
            Color counterTextColor,
            Color counterOutlineColor,
            float counterOutlineWidth,
            Vector2 counterOffset,
            int counterSortingOrder)
        {
            frozenUntilDestroyedCount = Mathf.Max(0, requiredDestroyedPieces);
            iceCounterFont = counterFont;
            iceCounterFontSize = counterFontSize > 5f
                ? Mathf.Clamp((counterFontSize / 36f) * .55f, .1f, 3f)
                : Mathf.Clamp(counterFontSize, .1f, 3f);
            iceCounterTextColor = counterTextColor;
            iceCounterOutlineColor = counterOutlineColor;
            iceCounterOutlineWidth = Mathf.Clamp01(counterOutlineWidth);
            iceCounterOffset = counterOffset;
            iceCounterSortingOrder = counterSortingOrder;
            PrototypeBoard board = PrototypeBoard.Active;
            RefreshFreezeState(board != null ? board.DestroyedPieceCount : 0);
        }

        public void ConfigureBomb(
            bool isBomb,
            float timerSeconds,
            float counterFontSize,
            Color counterTextColor,
            Color counterOutlineColor,
            float counterOutlineWidth,
            Vector2 counterOffset,
            TMP_FontAsset counterFont = null,
            int counterSortingOrder = 50)
        {
            IsBomb = isBomb;
            IsBombDefused = false;
            BombTimerSeconds = isBomb ? Mathf.Max(1f, timerSeconds) : 0f;
            bombCounterFontSize = counterFontSize > 5f
                ? Mathf.Clamp((counterFontSize / 36f) * .65f, .1f, 3f)
                : Mathf.Clamp(counterFontSize, .1f, 3f);
            bombCounterTextColor = counterTextColor;
            bombCounterOutlineColor = counterOutlineColor;
            bombCounterOutlineWidth = Mathf.Clamp01(counterOutlineWidth);
            bombCounterOffset = counterOffset;
            bombCounterFont = counterFont;
            bombCounterSortingOrder = counterSortingOrder;
            previousBombRemaining = -1;

            if (IsBomb)
            {
                RefreshBombState();
            }
        }

        public void RefreshBombState()
        {
            if (!IsBomb || IsShredderInteractionLocked || IsBombDefused)
                return;

            if (bombOverlayRenderer == null && bombOverlaySprite != null)
                BuildBombVisuals();

            previousBombRemaining = -1;
            UpdateBombCounter(Mathf.CeilToInt(BombTimerSeconds));
        }

        public void RefreshBombTimer(float secondsRemaining)
        {
            if (!IsBomb || IsShredderInteractionLocked || IsBombDefused)
                return;

            int remainingSeconds = Mathf.CeilToInt(Mathf.Max(0f, secondsRemaining));
            TextMeshPro counter = bombCounterText != null ? bombCounterText : iceCounterText;
            if (remainingSeconds == previousBombRemaining && counter != null && counter.enabled)
                return;

            previousBombRemaining = remainingSeconds;
            UpdateBombCounter(remainingSeconds);
        }

        /// <summary>
        /// Returns the visible Bomb icon centre for a Hammer-only presentation
        /// strike. Gameplay targeting remains owned by the selected grid cell.
        /// </summary>
        public bool TryGetBombVisualImpactPoint(out Vector2 impactPoint)
        {
            impactPoint = default;
            if (!IsBomb || IsBombDefused || bombOverlayRenderer == null ||
                !bombOverlayRenderer.enabled)
            {
                return false;
            }

            impactPoint = bombOverlayRenderer.bounds.center;
            return true;
        }

        public void RefreshFreezeState(int destroyedPieceCount)
        {
            bool shouldBeFrozen =
                frozenUntilDestroyedCount > 0 &&
                destroyedPieceCount < frozenUntilDestroyedCount &&
                !IsShredderInteractionLocked;

            bool wasFrozen = IsFrozen;
            if (wasFrozen && !shouldBeFrozen)
                CaptureWholePieceIceReleaseLayer();

            IsFrozen = shouldBeFrozen;
            RefreshWholePiecePresentation();
            if (shouldBeFrozen)
            {
                if (iceRenderers.Count == 0 &&
                    (wholePieceIceSprite == null || preferModularIcePresentation))
                    BuildIceVisuals();

                int remainingCount = frozenUntilDestroyedCount - destroyedPieceCount;
                if (previousFrozenRemaining >= 0 && remainingCount < previousFrozenRemaining)
                    PlayIceCrackFeedback();

                iceReleaseAnimating = false;
                previousFrozenRemaining = remainingCount;
                UpdateIceCounter(remainingCount);
            }
            else
            {
                previousFrozenRemaining = -1;
                if ((wasFrozen || iceRenderers.Count > 0) && !iceReleaseAnimating)
                    PlayIceReleaseAnimation();
                else if (!iceReleaseAnimating)
                    ClearIceVisuals();

                if (Body != null)
                    Body.WakeUp();

            }
        }

        /// <summary>
        /// Removes this piece's ice presentation for a rocket action. The
        /// rocket owns the already-reserved piece, so the frozen state is
        /// cleared before the carrier takes transform ownership.
        /// </summary>
        public void BreakIceForRocket(
            int? effectSortingLayerId = null,
            int? effectSortingOrder = null)
        {
            if (!IsFrozen)
                return;

            CaptureWholePieceIceReleaseLayer();
            IsFrozen = false;
            previousFrozenRemaining = -1;
            RefreshWholePiecePresentation();
            PlayIceReleaseAnimation(effectSortingLayerId, effectSortingOrder);
        }

        public void ReportDestroyed()
        {
            if (destructionReported)
                return;

            destructionReported = true;
            PrototypeBoard board = PrototypeBoard.Active;
            if (board != null)
                board.NotifyPieceDestroyed(this);
        }

        /// <summary>
        /// Removes the modular cell whose visible square contains worldPosition.
        /// Returns false when the tap did not land on this piece.
        /// </summary>
        public bool TryRemoveCellAt(Vector2 worldPosition)
        {
            return TryRemoveCellAt(worldPosition, out _);
        }

        /// <summary>
        /// Returns whether a visible modular cell owns the supplied board-space
        /// point. Booster targeting must use this presentation geometry rather
        /// than the disabled normal-play physics colliders.
        /// </summary>
        public bool ContainsVisibleCellAt(Vector2 worldPosition)
        {
            return !IsShredderInteractionLocked && TryGetCellIndexAt(worldPosition, out _);
        }

        /// <summary>
        /// Returns a visual attachment point suitable for a vertically moving
        /// carrier such as the rocket booster. A contiguous vertical stem is
        /// preferred and the carrier connects at its midpoint. This lets an L
        /// shape be lifted from its two-or-more stacked cells rather than from
        /// its unsupported end cell.
        /// </summary>
        public Vector2 GetPreferredRocketAttachmentPoint()
        {
            if (collisionCellVisuals == null || collisionCellVisuals.Count == 0)
                return transform.position;

            Bounds combinedBounds = default;
            bool hasVisual = false;
            for (int index = 0; index < collisionCellVisuals.Count; index++)
            {
                SpriteRenderer visual = collisionCellVisuals[index];
                if (visual == null || !visual.gameObject.activeInHierarchy)
                    continue;

                if (hasVisual)
                    combinedBounds.Encapsulate(visual.bounds);
                else
                {
                    combinedBounds = visual.bounds;
                    hasVisual = true;
                }
            }

            if (!hasVisual)
                return transform.position;

            Vector2 visualCentre = combinedBounds.center;
            if (TryGetVerticalStemAttachmentPoint(visualCentre, out Vector2 stemAttachmentPoint))
                return stemAttachmentPoint;

            Vector2 attachmentPoint = visualCentre;
            float closestDistance = float.PositiveInfinity;
            for (int index = 0; index < collisionCellVisuals.Count; index++)
            {
                SpriteRenderer visual = collisionCellVisuals[index];
                if (visual == null || !visual.gameObject.activeInHierarchy)
                    continue;

                Vector2 cellCentre = visual.bounds.center;
                float distance = (cellCentre - visualCentre).sqrMagnitude;
                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                attachmentPoint = cellCentre;
            }

            return attachmentPoint;
        }

        private bool TryGetVerticalStemAttachmentPoint(
            Vector2 visualCentre,
            out Vector2 attachmentPoint)
        {
            attachmentPoint = default;
            int longestStemCount = 1;
            float closestDistance = float.PositiveInfinity;

            for (int index = 0; index < collisionCellVisuals.Count; index++)
            {
                SpriteRenderer stemStart = collisionCellVisuals[index];
                if (stemStart == null || !stemStart.gameObject.activeInHierarchy ||
                    HasDirectlyAdjacentCellBelow(stemStart))
                {
                    continue;
                }

                Bounds stemBounds = stemStart.bounds;
                SpriteRenderer currentStemTop = stemStart;
                int stemCount = 1;
                while (TryGetDirectlyAdjacentCellAbove(currentStemTop, out SpriteRenderer nextStemCell))
                {
                    stemBounds.Encapsulate(nextStemCell.bounds);
                    currentStemTop = nextStemCell;
                    stemCount++;
                }

                if (stemCount < 2)
                    continue;

                Vector2 candidate = stemBounds.center;
                float distance = (candidate - visualCentre).sqrMagnitude;
                if (stemCount < longestStemCount ||
                    (stemCount == longestStemCount && distance >= closestDistance))
                {
                    continue;
                }

                longestStemCount = stemCount;
                closestDistance = distance;
                attachmentPoint = candidate;
            }

            return longestStemCount >= 2;
        }

        private bool HasDirectlyAdjacentCellBelow(SpriteRenderer cell)
        {
            for (int index = 0; index < collisionCellVisuals.Count; index++)
            {
                SpriteRenderer candidate = collisionCellVisuals[index];
                if (candidate != null && candidate != cell &&
                    candidate.gameObject.activeInHierarchy &&
                    AreVerticallyAdjacent(candidate.bounds, cell.bounds))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetDirectlyAdjacentCellAbove(
            SpriteRenderer cell,
            out SpriteRenderer aboveCell)
        {
            aboveCell = null;
            float closestGap = float.PositiveInfinity;
            Bounds cellBounds = cell.bounds;
            for (int index = 0; index < collisionCellVisuals.Count; index++)
            {
                SpriteRenderer candidate = collisionCellVisuals[index];
                if (candidate == null || candidate == cell ||
                    !candidate.gameObject.activeInHierarchy ||
                    !AreVerticallyAdjacent(cellBounds, candidate.bounds))
                {
                    continue;
                }

                float gap = Mathf.Abs(candidate.bounds.min.y - cellBounds.max.y);
                if (gap >= closestGap)
                    continue;

                closestGap = gap;
                aboveCell = candidate;
            }

            return aboveCell != null;
        }

        private static bool AreVerticallyAdjacent(Bounds lowerBounds, Bounds upperBounds)
        {
            float xAlignmentTolerance = Mathf.Min(lowerBounds.size.x, upperBounds.size.x) * .1f;
            float edgeTolerance = Mathf.Min(lowerBounds.size.y, upperBounds.size.y) * .1f;
            return Mathf.Abs(lowerBounds.center.x - upperBounds.center.x) <= xAlignmentTolerance &&
                   Mathf.Abs(lowerBounds.max.y - upperBounds.min.y) <= edgeTolerance;
        }

        /// <summary>
        /// Removes a cell and returns its exact visual/progress payload for a
        /// booster impact or another effect that must preserve the voxel reward.
        /// </summary>
        public bool TryRemoveCellAt(Vector2 worldPosition, out RemovedCell removedCell)
        {
            removedCell = default;
            if (IsShredderInteractionLocked || collisionCells == null || collisionCellVisuals == null)
                return false;

            if (!TryGetCellIndexAt(worldPosition, out int targetIndex))
                return false;

            return TryRemoveCellAtIndex(targetIndex, out removedCell);
        }

        /// <summary>
        /// Applies a hammer hit to a visible cell. An armed bomb is defused
        /// in place; its icon and countdown are removed without changing the
        /// piece's cells or authoritative grid footprint.
        /// </summary>
        public bool TryRemoveCellWithHammer(Vector2 worldPosition, out RemovedCell removedCell)
        {
            removedCell = default;
            if (IsShredderInteractionLocked || collisionCells == null || collisionCellVisuals == null)
                return false;

            if (!TryGetCellIndexAt(worldPosition, out int targetIndex))
                return false;

            if (IsBomb && !IsBombDefused)
            {
                DefuseBomb();
                return true;
            }

            bool removesFinalCell = collisionCells.Count == 1;
            int sourcePieceId = SourcePieceId;
            bool removed = TryRemoveCellAtIndex(targetIndex, out removedCell);
            if (removed && removesFinalCell)
                PrototypeBoard.Active?.NotifyPieceHammerDestroyed(sourcePieceId);

            return removed;
        }

        /// <summary>
        /// Removes the modular visual cell that owns an already-authoritative
        /// grid coordinate. The grid resolves ownership first; this method
        /// only maps the confirmed fine cell back to its prefab part slot.
        /// </summary>
        public bool TryRemoveCellAt(GridCoordinate coordinate, out RemovedCell removedCell)
        {
            removedCell = default;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null)
                return false;

            return TryRemoveCellAt(
                GravityLevelGridCoordinates.FineCellToWorld(level, coordinate),
                out removedCell);
        }

        private bool TryRemoveCellAtIndex(
            int targetIndex,
            out RemovedCell removedCell)
        {
            removedCell = default;
            if (targetIndex < 0 || collisionCells == null || collisionCellVisuals == null ||
                targetIndex >= collisionCells.Count || targetIndex >= collisionCellVisuals.Count)
                return false;

            // Keep the complete pre-hit shape until the grid accepts the new
            // topology. A damaged visual with the old grid footprint is never
            // a valid gameplay state: it causes suspended fragments, overlap
            // with obstacles and an incomplete selection outline.
            List<int> preHitIndices = new List<int>(collisionCells.Count);
            for (int index = 0; index < collisionCells.Count; index++)
                preHitIndices.Add(index);
            List<RuntimePieceFragmentCell> preHitCells = BuildFragmentCells(preHitIndices);
            float remainingBeforeHit = RemainingProgressUnits;

            SpriteRenderer targetVisual = collisionCellVisuals[targetIndex];
            BoxCollider2D targetCollider = collisionCells[targetIndex];
            int cellCountBeforeRemoval = Mathf.Max(1, collisionCellVisuals.Count);
            removedCell = new RemovedCell(
                GetCellWorldCenter(targetCollider, targetVisual),
                GetCellWorldSize(targetCollider, targetVisual),
                VisualColor,
                RemainingProgressUnits / cellCountBeforeRemoval,
                Mathf.Max(1, VoxelBlockBuilder.Subdivisions * VoxelBlockBuilder.Subdivisions));
            RemainingProgressUnits = Mathf.Max(0f, RemainingProgressUnits - removedCell.progressUnits);

            if (isSelected)
                SetSelected(false);

            ClearIceVisuals();
            ClearBombVisuals();
            ClearWholePiecePresentation();

            SpriteRenderer removedVisual = collisionCellVisuals[targetIndex];
            BoxCollider2D removedCollider = collisionCells[targetIndex];
            if (removedCollider != null)
                removedCollider.enabled = false;

            collisionCellVisuals.RemoveAt(targetIndex);
            collisionCells.RemoveAt(targetIndex);
            fullCollisionCellSizes.RemoveAt(targetIndex);

            // The cells belong to authored prefab slots. Reset the slot and
            // return its pooled voxels instead of destroying the hierarchy;
            // it can then be rebuilt safely if this hit splits the piece.
            RuntimePieceFactory.ResetPiecePartSlot(this, FindPartSlot(removedCollider, removedVisual));

            if (compositeCollider != null)
                compositeCollider.GenerateGeometry();
            CacheSolidColliders();
            InvalidateVoxelCache();

            // Avoid leaving the original silhouette around a modified piece.
            SetOutlineRenderersEnabled(false);

            if (collisionCells.Count == 0)
            {
                ReportDestroyed();
                ReleaseInstance();
            }
            else
            {
                List<List<int>> components = FindConnectedComponents();
                bool topologyCommitted;
                if (components.Count > 1)
                {
                    topologyCommitted = SplitDisconnectedCells(components);
                }
                else
                {
                    topologyCommitted = PrototypeBoard.Active != null &&
                        PrototypeBoard.Active.TryRefreshHammerPieceGeometry(this);
                }

                if (!topologyCommitted)
                {
                    RestoreRejectedHammerHit(preHitCells, remainingBeforeHit);
                    removedCell = default;
                    return false;
                }

                if (components.Count <= 1)
                {
                    // A hammer can leave the piece connected while changing
                    // its silhouette. Rebuild its pooled slots from the same
                    // factory path as split fragments so no prior atlas
                    // transform, ice layer or voxel state survives the hit.
                    GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
                    if (level != null)
                    {
                        RuntimePieceFactory.RebuildFragment(
                            this,
                            level,
                            BuildFragmentCells(components[0]),
                            VisualColor,
                            RemainingProgressUnits);
                        Physics2D.SyncTransforms();
                    }

                    ApplyCollisionProfile();
                    PrototypeBoard board = PrototypeBoard.Active;
                    RefreshFreezeState(board != null ? board.DestroyedPieceCount : 0);
                }

                // Clearing the pre-hit overlay avoids sizing it from stale
                // cells. Recreate it only after the surviving root has its
                // accepted retained-cell geometry. Split-off roots are normal
                // fragments; the original root remains the sole bomb owner.
                RefreshBombState();
            }

            return true;
        }

        private void DefuseBomb()
        {
            bool isDefusePresentationPlaying = PlayBombDefusePresentation();
            IsBombDefused = true;
            ClearBombVisuals(isDefusePresentationPlaying);
        }

        // The rendered voxel grid is intentionally made of child VoxelShard
        // objects. Its parent SpriteRenderer is a disabled placeholder, so it
        // cannot be used as hit-test authority. Use the authored cell
        // bounds transformed into the piece's unscaled coordinate space
        // and its cached cell size instead. This remains valid
        // regardless of atlas sprite scales or presentation states.
        private bool TryGetCellIndexAt(Vector2 worldPosition, out int targetIndex)
        {
            targetIndex = -1;
            int cellCount = collisionCells != null
                ? collisionCells.Count
                : (collisionCellVisuals != null ? collisionCellVisuals.Count : 0);
            if (cellCount == 0 || fullCollisionCellSizes == null)
                return false;

            Vector2 pieceLocalPoint = transform.InverseTransformPoint(worldPosition);
            float closestDistance = float.PositiveInfinity;
            for (int index = 0; index < cellCount; index++)
            {
                if (index >= fullCollisionCellSizes.Count)
                    continue;

                BoxCollider2D cell = collisionCells != null && index < collisionCells.Count
                    ? collisionCells[index]
                    : null;
                SpriteRenderer visual = collisionCellVisuals != null && index < collisionCellVisuals.Count
                    ? collisionCellVisuals[index]
                    : null;

                if (cell != null && !cell.enabled)
                    continue;
                if (cell == null && (visual == null || !visual.gameObject.activeInHierarchy))
                    continue;

                Vector2 cellWorldCenter = cell != null
                    ? (Vector2)cell.bounds.center
                    : (visual != null ? (Vector2)visual.bounds.center : Vector2.zero);
                Vector2 cellLocalCenter = transform.InverseTransformPoint(cellWorldCenter);

                Vector2 localSize = fullCollisionCellSizes[index];
                Vector2 halfSize = localSize * .5f;
                Vector2 diff = pieceLocalPoint - cellLocalCenter;
                if (Mathf.Abs(diff.x) > halfSize.x + .005f || Mathf.Abs(diff.y) > halfSize.y + .005f)
                    continue;

                float distance = diff.sqrMagnitude;
                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                targetIndex = index;
            }

            return targetIndex >= 0;
        }

        private static Vector2 GetCellWorldCenter(BoxCollider2D cell, SpriteRenderer visual)
        {
            if (cell != null)
                return cell.transform.TransformPoint(cell.offset);

            return visual != null ? (Vector2)visual.bounds.center : Vector2.zero;
        }

        private static Vector2 GetCellWorldSize(BoxCollider2D cell, SpriteRenderer visual)
        {
            if (cell != null)
            {
                Vector3 scale = cell.transform.lossyScale;
                return Vector2.Scale(cell.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            }

            return visual != null ? (Vector2)visual.bounds.size : Vector2.one;
        }

        private void RestoreRejectedHammerHit(
            IReadOnlyList<RuntimePieceFragmentCell> preHitCells,
            float remainingBeforeHit)
        {
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null || preHitCells == null || preHitCells.Count == 0)
                return;

            RuntimePieceFactory.RebuildFragment(
                this,
                level,
                preHitCells,
                VisualColor,
                remainingBeforeHit);
            ConfigureRemainingProgress(remainingBeforeHit);
            SetSelected(false);
            ApplyCollisionProfile();

            PrototypeBoard board = PrototypeBoard.Active;
            RefreshFreezeState(board != null ? board.DestroyedPieceCount : 0);
        }

        private bool SplitDisconnectedCells(List<List<int>> components)
        {
            if (components == null || components.Count <= 1)
                return false;

            int largestIndex = 0;
            for (int i = 1; i < components.Count; i++)
            {
                if (components[i].Count > components[largestIndex].Count)
                    largestIndex = i;
            }

            List<int> largest = components[largestIndex];
            components[largestIndex] = components[0];
            components[0] = largest;

            int totalCellCount = collisionCells.Count;
            float remainingBeforeSplit = RemainingProgressUnits;
            List<List<RuntimePieceFragmentCell>> fragmentCells =
                new List<List<RuntimePieceFragmentCell>>(components.Count);
            for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
                fragmentCells.Add(BuildFragmentCells(components[componentIndex]));

            List<PuzzlePiece> fragments = new List<PuzzlePiece>(components.Count);
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null)
                return false;

            float primaryProgress = remainingBeforeSplit * components[0].Count / totalCellCount;
            RuntimePieceFactory.RebuildFragment(
                this,
                level,
                fragmentCells[0],
                VisualColor,
                primaryProgress);
            fragments.Add(this);

            for (int componentIndex = 1; componentIndex < components.Count; componentIndex++)
            {
                float componentProgress = remainingBeforeSplit * components[componentIndex].Count / totalCellCount;
                PuzzlePiece fragment = RuntimePieceFactory.CreateFragment(
                    $"{name} - Split {componentIndex}",
                    level,
                    transform.position,
                    transform.rotation,
                    transform.localScale,
                    fragmentCells[componentIndex],
                    VisualColor,
                    PresentationMaterial,
                    componentProgress);
                fragments.Add(fragment);
            }

            // Newly rented fragment roots and their slot colliders must be
            // visible to the immediate grid-model transaction below. This is
            // a one-shot topology edit, not a gameplay collision query.
            Physics2D.SyncTransforms();

            PrototypeBoard board = PrototypeBoard.Active;
            // Register fragments as settled candidates. WakeUpGravity then
            // commits their legal fall targets and starts the matching grid
            // presentation tween. Registering them as Falling here skips the
            // planner entirely, leaving their visual roots behind the grid.
            if (board == null || !board.TryRegisterHammerFragments(fragments, PieceState.Placed))
            {
                // The caller restores the complete pre-hit source root. Do not
                // rebuild this root as disconnected presentation geometry.
                for (int index = 1; index < fragments.Count; index++)
                {
                    if (fragments[index] != null)
                        fragments[index].ReleaseInstance();
                }
                return false;
            }

            PuzzleDragController.WakeUpGravity();
            return true;
        }

        private List<List<int>> FindConnectedComponents()
        {
            const float adjacencyTolerance = .001f;
            List<List<int>> components = new List<List<int>>();
            bool[] visited = new bool[collisionCells.Count];
            Queue<int> pending = new Queue<int>();

            for (int start = 0; start < collisionCells.Count; start++)
            {
                if (visited[start])
                    continue;

                List<int> component = new List<int>();
                visited[start] = true;
                pending.Enqueue(start);
                while (pending.Count > 0)
                {
                    int current = pending.Dequeue();
                    component.Add(current);
                    for (int candidate = 0; candidate < collisionCells.Count; candidate++)
                    {
                        if (visited[candidate] ||
                            !CellsAreConnected(current, candidate, adjacencyTolerance))
                            continue;

                        visited[candidate] = true;
                        pending.Enqueue(candidate);
                    }
                }

                components.Add(component);
            }

            return components;
        }

        private bool CellsAreConnected(int first, int second, float tolerance)
        {
            // A cell collider can be a child of its authored part slot. Its
            // transform.localPosition is then relative to that slot, rather
            // than this piece. Compare all cells in the common piece space so
            // hammer topology splitting follows the rendered shape.
            Vector2 firstCentre = GetCellLocalCenter(collisionCells[first]);
            Vector2 secondCentre = GetCellLocalCenter(collisionCells[second]);
            Vector2 firstHalf = fullCollisionCellSizes[first] * .5f;
            Vector2 secondHalf = fullCollisionCellSizes[second] * .5f;
            float xDistance = Mathf.Abs(firstCentre.x - secondCentre.x);
            float yDistance = Mathf.Abs(firstCentre.y - secondCentre.y);
            float xReach = firstHalf.x + secondHalf.x;
            float yReach = firstHalf.y + secondHalf.y;

            bool sharesVerticalEdge =
                Mathf.Abs(xDistance - xReach) <= tolerance &&
                yDistance < yReach - tolerance;
            bool sharesHorizontalEdge =
                Mathf.Abs(yDistance - yReach) <= tolerance &&
                xDistance < xReach - tolerance;
            bool overlaps =
                xDistance < xReach - tolerance &&
                yDistance < yReach - tolerance;
            return sharesVerticalEdge || sharesHorizontalEdge || overlaps;
        }

        private Vector2 GetCellLocalCenter(BoxCollider2D cell)
        {
            if (cell == null)
                return Vector2.zero;

            return transform.InverseTransformPoint(cell.transform.TransformPoint(cell.offset));
        }

        private void ApplyCollisionProfile()
        {
            if (collisionCells == null || fullCollisionCellSizes == null)
                return;

            float inset = useFullCollisionGeometry
                ? 0f
                : GravityGridMetrics.RestingPieceCollisionSkinInCells;

            // Inset every modular cell around its own centre. Scaling the common
            // root instead would move the cells of concave pieces relative to
            // their artwork and recreate the intertwining bug.
            for (int i = 0; i < collisionCells.Count; i++)
            {
                Vector2 fullSize = fullCollisionCellSizes[i];
                collisionCells[i].size = new Vector2(
                    Mathf.Max(.01f, fullSize.x - inset * 2f),
                    Mathf.Max(.01f, fullSize.y - inset * 2f));
            }

            if (compositeCollider != null)
                compositeCollider.GenerateGeometry();

            if (Body != null)
                Body.WakeUp();
        }

        /// <summary>
        /// Atomically hands this piece from normal board ownership to the
        /// shredder. Its current grid footprint is reserved before the visual
        /// feed begins, so no gravity move or placement can enter it.
        /// </summary>
        public bool TryBeginShredderHandoff(bool reportDestructionImmediately = true)
        {
            if (IsShredderInteractionLocked || IsFrozen)
                return false;

            PrototypeBoard board = PrototypeBoard.Active;
            if (board != null)
            {
                if (!board.TryReservePieceInGrid(this, PieceState.HandoffToPhysics))
                    return false;
            }

            return TryBeginShredderFeedAfterReservation(
                reportDestructionImmediately,
                keepReservationUntilDespawn: false);
        }

        /// <summary>
        /// Begins the map-exit phase of the shredder flow. Grid ownership is
        /// retained as a reservation while a later handoff controller clears
        /// the piece to its physics line; this method never enables physics or
        /// starts shred presentation.
        /// </summary>
        public bool TryBeginShredderExitHandoff()
        {
            if (IsShredderInteractionLocked || IsFrozen)
                return false;

            PrototypeBoard board = PrototypeBoard.Active;
            if (board != null &&
                !board.TryReservePieceInGrid(this, PieceState.HandoffToPhysics))
            {
                return false;
            }

            GridFallView?.Cancel();
            shredderHandoffMode = ShredderHandoffMode.PhysicsIsland;
            keepShredderGridReservationUntilDespawn = true;
            SetSelected(false);
            Debug.Log($"[ShredderHandoff] Exit handoff locked targeting for piece {SourcePieceId}.", this);
            return true;
        }

        /// <summary>
        /// Locks a member in the short-lived physical support island after
        /// the board atomically reserves the full support chain. This is not a
        /// shred: the member remains visible and has no destruction/progress
        /// side effects until it physically reaches the mouth gate.
        /// </summary>
        public bool TryEnterShredderPhysicsIslandHandoff()
        {
            if (IsShredderInteractionLocked || IsFrozen)
                return false;

            PrototypeBoard board = PrototypeBoard.Active;
            if (board != null &&
                (!board.TryGetPieceModel(this, out PieceModel model) ||
                 model.State != PieceState.HandoffToPhysics))
            {
                return false;
            }

            // An island must start from a grid-synchronised pose. The planner
            // rejects falling pieces, but cancel a stale visual handle here as
            // a defensive last line before Unity physics owns this body.
            GridFallView?.Cancel();
            shredderHandoffMode = ShredderHandoffMode.PhysicsIsland;
            keepShredderGridReservationUntilDespawn = true;
            SetSelected(false);
            Debug.Log($"[ShredderHandoff] Targeting locked at handoff for piece {SourcePieceId}.", this);
            return true;
        }

        /// <summary>
        /// Cancels a transaction that reserved an island before any Dynamic
        /// physics body or shred presentation was started. The board performs
        /// the matching Reserved-to-Occupied rollback first; this method only
        /// releases the local interaction lock.
        /// </summary>
        internal void CancelShredderPhysicsIslandHandoff()
        {
            if (!IsInShredderPhysicsHandoff)
                return;

            shredderHandoffMode = ShredderHandoffMode.None;
            keepShredderGridReservationUntilDespawn = false;
        }

        /// <summary>
        /// Promotes an already handoff-locked physical member to the actual
        /// shredder feed. This is the sole point where destruction, progress,
        /// clipping and cell-frontier release become eligible.
        /// </summary>
        public bool TryBeginShredderFeedFromPhysicsIsland()
        {
            if (!IsInShredderPhysicsHandoff)
                return false;

            return TryBeginShredderFeedAfterReservation(
                reportDestructionImmediately: true,
                keepReservationUntilDespawn: true);
        }

        private bool TryBeginShredderFeedAfterReservation(
            bool reportDestructionImmediately,
            bool keepReservationUntilDespawn)
        {
            if (IsBeingShredded)
                return false;

            ShredderHandoffMode previousMode = shredderHandoffMode;
            if (previousMode != ShredderHandoffMode.None &&
                previousMode != ShredderHandoffMode.PhysicsIsland)
            {
                return false;
            }

            // The catch zone accepts only a settled anchor, but cancel any
            // stale presentation handle defensively before the feed becomes
            // the sole movement owner of the Rigidbody.
            GridFallView?.Cancel();
            shredderHandoffMode = ShredderHandoffMode.Feeding;
            keepShredderGridReservationUntilDespawn = keepReservationUntilDespawn;
            SetSelected(false);

            PrototypeBoard board = PrototypeBoard.Active;
            if (board != null)
            {
                if (!board.TryGetPieceModel(this, out PieceModel model))
                {
                    shredderHandoffMode = previousMode;
                    keepShredderGridReservationUntilDespawn =
                        previousMode == ShredderHandoffMode.PhysicsIsland;
                    return false;
                }

                CaptureShredderReservationCells(model);
                if (reportDestructionImmediately)
                    board.TryLockFinalShredderOutcome(this);
            }

            // The normal shredder owns destruction at handoff time. Rocket
            // presentation keeps the same grid reservation, but defers the
            // destruction event until its carried piece has actually left the
            // screen, so frozen-piece counters stay visually in sync.
            if (reportDestructionImmediately)
                ReportDestroyed();

            // A normal feed releases its grid frontier progressively. An
            // island keeps the whole chain reserved instead, but both modes
            // should let unrelated settled grid pieces reconsider their state
            // after the handoff is complete.
            PuzzleDragController.WakeUpGravity();
            return true;
        }

        private void BuildIceVisuals()
        {
            if (collisionCellVisuals == null)
                return;

            // A dedicated 1x1 overlay keeps modular frozen shapes on the same
            // new atlas as whole silhouettes. When it is assigned it already
            // contains the complete ice treatment, so a second frost layer
            // would wash out the sprite and duplicate its rim.
            Sprite frostOverlay = iceOverlaySprite != null
                ? iceOverlaySprite
                : PrototypeBootstrap.GetSquareSprite();

            foreach (SpriteRenderer source in collisionCellVisuals)
            {
                if (source == null)
                    continue;

                CreateIceLayer(
                    source,
                    "Ice Overlay",
                    Vector3.one,
                    iceOverlayTint,
                    source.sortingOrder + 5,
                    frostOverlay);
                if (iceOverlaySprite != null)
                    continue;

                CreateIceLayer(
                    source,
                    "Ice Frost",
                    new Vector3(.72f, .72f, 1f),
                    iceFrostTint,
                    source.sortingOrder + 6,
                    frostOverlay);
            }
        }

        private void BuildBombVisuals()
        {
            if (bombOverlaySprite == null || collisionCellVisuals == null)
                return;

            Bounds combinedBounds = default;
            SpriteRenderer sortingSource = null;
            Vector2 weightedCenter = Vector2.zero;
            float totalCellArea = 0f;
            for (int index = 0; index < collisionCellVisuals.Count; index++)
            {
                SpriteRenderer source = collisionCellVisuals[index];
                if (source == null)
                    continue;

                if (sortingSource == null)
                {
                    sortingSource = source;
                    combinedBounds = source.bounds;
                }
                else
                {
                    combinedBounds.Encapsulate(source.bounds);
                }

                Vector2 sourceSize = source.bounds.size;
                float sourceArea = sourceSize.x * sourceSize.y;
                if (sourceArea > Mathf.Epsilon)
                {
                    weightedCenter += (Vector2)source.bounds.center * sourceArea;
                    totalCellArea += sourceArea;
                }
            }

            if (sortingSource == null)
                return;

            Vector2 targetSize = GetBombOverlayTargetSize();
            Vector2 desiredCenter = totalCellArea > Mathf.Epsilon
                ? weightedCenter / totalCellArea
                : combinedBounds.center;
            Vector2 overlayCenter = DoesBombFootprintFitPiece(desiredCenter, targetSize)
                ? desiredCenter
                : GetClosestBombCellCenter(desiredCenter, combinedBounds.center);

            // A bomb is a property of the complete authored piece, not of
            // each cell. It renders once at the shape's center when that
            // one-block footprint fits. Concave shapes instead use the most
            // central occupied cell so the icon never floats in empty space.
            CreateBombOverlay(
                sortingSource,
                overlayCenter,
                targetSize);
        }

        private void CreateBombOverlay(
            SpriteRenderer sortingSource,
            Vector2 overlayCenter,
            Vector2 targetSize)
        {
            if (!TryGetAvailableIceSlot(out SpriteRenderer renderer))
            {
                Debug.LogWarning("[PuzzlePiece] Bomb presentation slot capacity exceeded.", this);
                return;
            }

            renderer.transform.SetParent(transform, false);
            renderer.transform.position = new Vector3(
                overlayCenter.x,
                overlayCenter.y,
                transform.position.z - .01f);
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = GetOverlayScaleToFit(
                targetSize,
                bombOverlaySprite,
                bombOverlayFill);
            renderer.gameObject.name = "Bomb Overlay";
            renderer.sprite = bombOverlaySprite;
            renderer.color = Color.white;
            renderer.sortingLayerID = sortingSource.sortingLayerID;
            renderer.sortingOrder = sortingSource.sortingOrder + 5;
            renderer.enabled = true;
            bombOverlayRenderer = renderer;
        }

        private Vector2 GetBombOverlayTargetSize()
        {
            if (fullCollisionCellSizes == null || fullCollisionCellSizes.Count == 0)
                return Vector2.one;

            Vector2 largestCellSize = Vector2.one;
            float largestCellArea = 0f;
            for (int index = 0; index < fullCollisionCellSizes.Count; index++)
            {
                Vector2 cellSize = fullCollisionCellSizes[index];
                float cellArea = cellSize.x * cellSize.y;
                if (cellSize.x <= 0f || cellSize.y <= 0f || cellArea <= largestCellArea)
                    continue;

                largestCellSize = cellSize;
                largestCellArea = cellArea;
            }

            return largestCellSize;
        }

        private bool DoesBombFootprintFitPiece(Vector2 center, Vector2 size)
        {
            const float edgeInset = .0001f;
            Vector2 halfSize = size * .5f;
            float horizontalInset = Mathf.Max(0f, halfSize.x - edgeInset);
            float verticalInset = Mathf.Max(0f, halfSize.y - edgeInset);

            return IsPointInsideBombPiece(center) &&
                   IsPointInsideBombPiece(center + new Vector2(-horizontalInset, -verticalInset)) &&
                   IsPointInsideBombPiece(center + new Vector2(-horizontalInset, verticalInset)) &&
                   IsPointInsideBombPiece(center + new Vector2(horizontalInset, -verticalInset)) &&
                   IsPointInsideBombPiece(center + new Vector2(horizontalInset, verticalInset));
        }

        private bool IsPointInsideBombPiece(Vector2 point)
        {
            for (int index = 0; index < collisionCellVisuals.Count; index++)
            {
                SpriteRenderer cell = collisionCellVisuals[index];
                if (cell == null)
                    continue;

                Bounds bounds = cell.bounds;
                if (point.x >= bounds.min.x && point.x <= bounds.max.x &&
                    point.y >= bounds.min.y && point.y <= bounds.max.y)
                    return true;
            }

            return false;
        }

        private Vector2 GetClosestBombCellCenter(Vector2 target, Vector2 fallback)
        {
            Vector2 closestCenter = fallback;
            float closestDistance = float.PositiveInfinity;
            for (int index = 0; index < collisionCellVisuals.Count; index++)
            {
                SpriteRenderer cell = collisionCellVisuals[index];
                if (cell == null)
                    continue;

                Vector2 center = cell.bounds.center;
                float distance = (center - target).sqrMagnitude;
                if (distance >= closestDistance)
                    continue;

                closestCenter = center;
                closestDistance = distance;
            }

            return closestCenter;
        }

        private static Vector3 GetOverlayScaleToFit(
            Vector2 targetSize,
            Sprite overlaySprite,
            float fill)
        {
            if (overlaySprite == null)
                return Vector3.one;

            Vector2 overlaySize = overlaySprite.bounds.size;
            if (targetSize.x <= 0f || targetSize.y <= 0f || overlaySize.x <= 0f || overlaySize.y <= 0f)
                return Vector3.one;

            float scale = Mathf.Min(targetSize.x / overlaySize.x, targetSize.y / overlaySize.y);
            scale *= Mathf.Clamp(fill, .1f, 1f);
            return new Vector3(scale, scale, 1f);
        }

        private void UpdateIceCounter(int remainingCount)
        {
            if (collisionCellVisuals == null || collisionCellVisuals.Count == 0)
                return;

            if (iceCounterText == null)
            {
                Debug.LogWarning("[PuzzlePiece] Ice counter presentation is missing from BlockPiece.prefab.", this);
                return;
            }

            GetModularPresentationBounds(out Vector2 localCenter, out _);

            iceCounterText.gameObject.SetActive(true);
            iceCounterText.isOrthographic = true;
            iceCounterText.enabled = true;
            iceCounterText.enableAutoSizing = false;

            if (iceCounterFont != null)
                iceCounterText.font = iceCounterFont;
            else if (iceCounterText.font == null && defaultCounterFont != null)
                iceCounterText.font = defaultCounterFont;

            iceCounterText.color = iceCounterTextColor;
            iceCounterText.fontSize = iceCounterFontSize;
            iceCounterText.outlineColor = iceCounterOutlineColor;
            iceCounterText.outlineWidth = iceCounterOutlineWidth;

            int targetSortingLayer = collisionCellVisuals[0] != null
                ? collisionCellVisuals[0].sortingLayerID
                : 0;
            if (wholePieceRenderer != null && wholePieceRenderer.enabled)
                targetSortingLayer = wholePieceRenderer.sortingLayerID;

            iceCounterText.renderer.sortingLayerID = targetSortingLayer;
            iceCounterText.renderer.sortingOrder = Mathf.Max(iceCounterSortingOrder, 50);

            iceCounterText.text = Mathf.Max(0, remainingCount).ToString();

            iceCounterText.transform.SetParent(transform, false);
            iceCounterText.transform.localPosition = new Vector3(
                localCenter.x + iceCounterOffset.x,
                localCenter.y + iceCounterOffset.y,
                -.15f);
            iceCounterText.transform.localRotation = Quaternion.identity;
            iceCounterText.transform.localScale = Vector3.one;

            iceCounterText.rectTransform.sizeDelta = new Vector2(20f, 5f);
            iceCounterText.ForceMeshUpdate();
        }

        private void UpdateBombCounter(int remainingCount)
        {
            TextMeshPro counterText = bombCounterText != null ? bombCounterText : iceCounterText;
            if (collisionCellVisuals == null || collisionCellVisuals.Count == 0 || counterText == null)
                return;

            GetModularPresentationBounds(out Vector2 localCenter, out _);

            Vector3 targetLocalPos;
            if (bombOverlayRenderer != null && bombOverlayRenderer.enabled)
            {
                Vector3 localOverlayCenter = transform.InverseTransformPoint(bombOverlayRenderer.bounds.center);
                targetLocalPos = new Vector3(
                    localOverlayCenter.x + bombCounterOffset.x,
                    localOverlayCenter.y + bombCounterOffset.y,
                    -.1f);
            }
            else
            {
                targetLocalPos = new Vector3(
                    localCenter.x + bombCounterOffset.x,
                    localCenter.y + bombCounterOffset.y,
                    -.1f);
            }

            counterText.gameObject.SetActive(true);
            counterText.isOrthographic = true;
            counterText.enabled = true;
            counterText.enableAutoSizing = false;

            if (bombCounterFont != null)
                counterText.font = bombCounterFont;
            else if (counterText.font == null && defaultCounterFont != null)
                counterText.font = defaultCounterFont;

            counterText.color = bombCounterTextColor;
            counterText.fontSize = bombCounterFontSize;
            counterText.outlineColor = bombCounterOutlineColor;
            counterText.outlineWidth = bombCounterOutlineWidth;

            int targetSortingLayer = collisionCellVisuals[0] != null
                ? collisionCellVisuals[0].sortingLayerID
                : 0;
            if (bombOverlayRenderer != null && bombOverlayRenderer.enabled)
                targetSortingLayer = bombOverlayRenderer.sortingLayerID;

            counterText.renderer.sortingLayerID = targetSortingLayer;
            counterText.renderer.sortingOrder = Mathf.Max(bombCounterSortingOrder, 50);

            counterText.text = Mathf.Max(0, remainingCount).ToString();

            counterText.transform.SetParent(transform, false);
            counterText.transform.localPosition = targetLocalPos;
            counterText.transform.localRotation = Quaternion.identity;
            counterText.transform.localScale = Vector3.one;

            counterText.rectTransform.sizeDelta = new Vector2(20f, 5f);
            counterText.ForceMeshUpdate();
        }

        private void CreateIceLayer(
            SpriteRenderer source,
            string layerName,
            Vector3 scale,
            Color color,
            int sortingOrder,
            Sprite overlaySprite)
        {
            if (!TryGetAvailableIceSlot(out SpriteRenderer renderer))
            {
                Debug.LogWarning("[PuzzlePiece] Ice presentation slot capacity exceeded.", this);
                return;
            }

            renderer.transform.SetParent(source.transform, false);
            renderer.transform.localPosition = Vector3.zero;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.gameObject.name = layerName;
            renderer.sprite = overlaySprite != null ? overlaySprite : source.sprite;
            // The modular brick atlas and the ice overlay have different
            // pixels-per-unit values.  A child renderer inherits its source
            // cell's transform scale, but not its sprite bounds, so applying
            // only the authored effect multiplier made fallback ice appear
            // tiny or oversized (most visibly after a hammer split).  Fit the
            // overlay in the source renderer's local sprite space first, then
            // apply the intentional crack/frost multiplier.
            renderer.transform.localScale = GetIceLayerScale(source.sprite, renderer.sprite, scale);
            renderer.color = color;
            renderer.sharedMaterial = icePresentationMaterial != null
                ? icePresentationMaterial
                : source.sharedMaterial;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = true;
            iceRenderers.Add(renderer);
        }

        private static Vector3 GetIceLayerScale(
            Sprite sourceSprite,
            Sprite overlaySprite,
            Vector3 presentationMultiplier)
        {
            if (sourceSprite == null || overlaySprite == null)
                return presentationMultiplier;

            Vector2 sourceSize = sourceSprite.bounds.size;
            Vector2 overlaySize = overlaySprite.bounds.size;
            if (sourceSize.x <= 0f || sourceSize.y <= 0f ||
                overlaySize.x <= 0f || overlaySize.y <= 0f)
                return presentationMultiplier;

            return new Vector3(
                presentationMultiplier.x * sourceSize.x / overlaySize.x,
                presentationMultiplier.y * sourceSize.y / overlaySize.y,
                presentationMultiplier.z);
        }

        /// <summary>
        /// The first ice slot is reserved as the intact whole-piece renderer.
        /// Generic ice overlays must never borrow it: their release tween
        /// would otherwise fade the brick sprite itself to transparent.
        /// </summary>
        private bool TryGetAvailableIceSlot(out SpriteRenderer renderer)
        {
            for (int index = 0; index < iceSlots.Count; index++)
            {
                SpriteRenderer candidate = iceSlots[index];
                if (candidate == null || candidate == wholePieceRenderer || candidate == bombOverlayRenderer ||
                    candidate == bombDefuseLeftRenderer || candidate == bombDefuseRightRenderer ||
                    bombShredderFragmentRenderers.Contains(candidate) || iceRenderers.Contains(candidate) ||
                    joinedPieceRenderers.Contains(candidate))
                    continue;

                renderer = candidate;
                return true;
            }

            renderer = null;
            return false;
        }

        private void PlayIceCrackFeedback()
        {
            TweenConfig tweenConfig = GridFallView != null ? GridFallView.Config : null;
            GetIceEffectPresentation(out Vector3 position, out int sortingLayerId, out int sortingOrder);
            iceParticleVfxHandle?.PlayCrack(position, sortingLayerId, sortingOrder);

            for (int index = 0; index < iceRenderers.Count; index++)
            {
                SpriteRenderer renderer = iceRenderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;

                DOTween.Kill(renderer);
                Vector3 restingScale = renderer.transform.localScale;

                if (tweenConfig == null)
                    continue;

                Sequence crack = DOTween.Sequence().Append(
                    renderer.transform.DOPunchScale(
                        restingScale * tweenConfig.IceCrackScaleMultiplier,
                        tweenConfig.IceCrackDuration,
                        tweenConfig.IceCrackVibrato,
                        tweenConfig.IceCrackElasticity));
                crack.SetLink(renderer.gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
            }
        }

        private void PlayIceReleaseAnimation(
            int? effectSortingLayerId = null,
            int? effectSortingOrder = null)
        {
            iceReleaseAnimating = true;
            GetIceEffectPresentation(out Vector3 position, out int sortingLayerId, out int sortingOrder);
            if (effectSortingLayerId.HasValue)
                sortingLayerId = effectSortingLayerId.Value;
            if (effectSortingOrder.HasValue)
                sortingOrder = effectSortingOrder.Value;
            iceParticleVfxHandle?.PlayBreak(position, sortingLayerId, sortingOrder);
            TweenConfig tweenConfig = GridFallView != null ? GridFallView.Config : null;
            if (tweenConfig == null)
            {
                ClearIceVisuals();
                return;
            }

            Sequence release = DOTween.Sequence();
            bool hasIce = false;
            for (int index = 0; index < iceRenderers.Count; index++)
            {
                SpriteRenderer renderer = iceRenderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;

                hasIce = true;
                DOTween.Kill(renderer);
                release.Join(renderer.transform.DOPunchScale(
                    renderer.transform.localScale * tweenConfig.IceReleaseScaleMultiplier,
                    tweenConfig.IceReleaseDuration,
                    tweenConfig.IceReleaseVibrato,
                    tweenConfig.IceReleaseElasticity));
                release.Join(renderer.DOFade(0f, tweenConfig.IceReleaseDuration));
            }

            if (!hasIce)
            {
                ClearIceVisuals();
                return;
            }

            release.OnComplete(ClearIceVisuals)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
        }

        /// <summary>
        /// Dedicated ice atlas art uses the reserved whole-piece renderer,
        /// while generic ice uses temporary overlay slots. Before switching a
        /// frozen piece back to its normal brick sprite, copy that dedicated
        /// art into an available pooled overlay slot so it can fade and emit
        /// the same break particle feedback as every other ice block.
        /// </summary>
        private void CaptureWholePieceIceReleaseLayer()
        {
            if (wholePieceRenderer == null || wholePieceIceSprite == null ||
                !wholePieceRenderer.enabled || wholePieceRenderer.sprite != wholePieceIceSprite ||
                !TryGetAvailableIceSlot(out SpriteRenderer releaseRenderer))
                return;

            releaseRenderer.transform.SetParent(transform, false);
            releaseRenderer.transform.localPosition = wholePieceRenderer.transform.localPosition;
            releaseRenderer.transform.localRotation = wholePieceRenderer.transform.localRotation;
            releaseRenderer.transform.localScale = wholePieceRenderer.transform.localScale;
            releaseRenderer.sprite = wholePieceRenderer.sprite;
            releaseRenderer.color = wholePieceRenderer.color;
            releaseRenderer.sharedMaterial = wholePieceRenderer.sharedMaterial;
            releaseRenderer.flipX = wholePieceRenderer.flipX;
            releaseRenderer.sortingLayerID = wholePieceRenderer.sortingLayerID;
            releaseRenderer.sortingOrder = wholePieceRenderer.sortingOrder + 1;
            releaseRenderer.gameObject.name = "Ice Atlas Release";
            releaseRenderer.enabled = true;
            iceRenderers.Add(releaseRenderer);
        }

        private Vector3 GetIceEffectPosition()
        {
            Bounds combinedBounds = default;
            bool hasIceVisual = wholePieceRenderer != null &&
                                wholePieceIceSprite != null &&
                                wholePieceRenderer.enabled &&
                                wholePieceRenderer.sprite == wholePieceIceSprite;
            if (hasIceVisual)
                combinedBounds = wholePieceRenderer.bounds;

            for (int index = 0; index < iceRenderers.Count; index++)
            {
                SpriteRenderer visual = iceRenderers[index];
                if (visual == null || !visual.enabled)
                    continue;

                if (hasIceVisual)
                    combinedBounds.Encapsulate(visual.bounds);
                else
                {
                    combinedBounds = visual.bounds;
                    hasIceVisual = true;
                }
            }

            // Modular ice intentionally uses the four full-size brick cells
            // themselves so their sixteen stud details remain visible. Those
            // cells are also the correct bounds owner for crack/break VFX.
            if (!hasIceVisual && preferModularIcePresentation && collisionCellVisuals != null)
            {
                for (int index = 0; index < collisionCellVisuals.Count; index++)
                {
                    SpriteRenderer visual = collisionCellVisuals[index];
                    if (visual == null || !visual.enabled)
                        continue;

                    if (hasIceVisual)
                        combinedBounds.Encapsulate(visual.bounds);
                    else
                    {
                        combinedBounds = visual.bounds;
                        hasIceVisual = true;
                    }
                }
            }

            return hasIceVisual ? combinedBounds.center : transform.position;
        }

        private void GetIceEffectPresentation(
            out Vector3 position,
            out int sortingLayerId,
            out int sortingOrder)
        {
            position = GetIceEffectPosition();
            sortingLayerId = 0;
            sortingOrder = 1;

            if (wholePieceRenderer != null && wholePieceIceSprite != null &&
                wholePieceRenderer.enabled && wholePieceRenderer.sprite == wholePieceIceSprite)
            {
                sortingLayerId = wholePieceRenderer.sortingLayerID;
                sortingOrder = wholePieceRenderer.sortingOrder + 1;
            }

            for (int index = 0; index < iceRenderers.Count; index++)
            {
                SpriteRenderer renderer = iceRenderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;

                sortingLayerId = renderer.sortingLayerID;
                sortingOrder = Mathf.Max(sortingOrder, renderer.sortingOrder + 1);
            }

            if (preferModularIcePresentation && collisionCellVisuals != null)
            {
                for (int index = 0; index < collisionCellVisuals.Count; index++)
                {
                    SpriteRenderer renderer = collisionCellVisuals[index];
                    if (renderer == null || !renderer.enabled)
                        continue;

                    sortingLayerId = renderer.sortingLayerID;
                    sortingOrder = Mathf.Max(sortingOrder, renderer.sortingOrder + 1);
                }
            }
        }

        private void ClearIceVisuals()
        {
            iceReleaseAnimating = false;
            previousFrozenRemaining = -1;
            foreach (SpriteRenderer renderer in iceRenderers)
            {
                if (renderer == null)
                    continue;

                DOTween.Kill(renderer);
                renderer.enabled = false;
                renderer.transform.SetParent(iceSlotsRoot, false);
            }
            iceRenderers.Clear();

            if (iceCounterText != null &&
                (iceCounterText != bombCounterText || !IsBomb || IsBombDefused))
            {
                iceCounterText.enabled = false;
                iceCounterText.gameObject.SetActive(false);
            }
        }

        private bool PlayBombDefusePresentation()
        {
            if (bombOverlayRenderer == null || !bombOverlayRenderer.enabled ||
                bombOverlayRenderer.sprite == null || bombDefuseSplitMaterial == null ||
                bombDefusePropertyBlock == null)
            {
                return false;
            }

            TweenConfig tweenConfig = GridFallView != null ? GridFallView.Config : null;
            if (tweenConfig == null || !TryGetAvailableIceSlot(out SpriteRenderer leftRenderer))
                return false;

            // Reserve the first pooled slot before acquiring the second so the
            // generic allocator cannot hand back the same renderer twice.
            bombDefuseLeftRenderer = leftRenderer;
            if (!TryGetAvailableIceSlot(out SpriteRenderer rightRenderer))
            {
                bombDefuseLeftRenderer = null;
                return false;
            }
            bombDefuseLeftRenderer = null;

            SpriteRenderer source = bombOverlayRenderer;
            Bounds spriteBounds = source.sprite.bounds;
            Vector2 spriteMinimum = spriteBounds.min;
            Vector2 spriteMaximum = spriteBounds.max;
            float splitX = spriteBounds.center.x;
            ConfigureBombFragmentRenderer(
                leftRenderer,
                source,
                spriteMinimum,
                new Vector2(splitX, spriteMaximum.y),
                "Bomb Defuse Left");
            ConfigureBombFragmentRenderer(
                rightRenderer,
                source,
                new Vector2(splitX, spriteMinimum.y),
                spriteMaximum,
                "Bomb Defuse Right");
            bombDefuseLeftRenderer = leftRenderer;
            bombDefuseRightRenderer = rightRenderer;

            Vector3 leftStart = leftRenderer.transform.localPosition;
            Vector3 rightStart = rightRenderer.transform.localPosition;
            float duration = tweenConfig.BombDefuseSplitDuration;
            Sequence splitSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            splitSequence.Append(leftRenderer.transform.DOLocalMoveX(
                leftStart.x - tweenConfig.BombDefuseHalfSeparation,
                duration).SetEase(tweenConfig.BombDefuseSplitEase));
            splitSequence.Join(rightRenderer.transform.DOLocalMoveX(
                rightStart.x + tweenConfig.BombDefuseHalfSeparation,
                duration).SetEase(tweenConfig.BombDefuseSplitEase));
            splitSequence.Append(DOVirtual.Float(
                0f,
                1f,
                tweenConfig.BombDefuseFadeDuration,
                SetBombDefuseFade)
                .SetEase(tweenConfig.BombDefuseFadeEase));
            splitSequence.OnComplete(ClearBombDefusePresentation);
            return true;
        }

        private void ConfigureBombFragmentRenderer(
            SpriteRenderer renderer,
            SpriteRenderer source,
            Vector2 clipMinimum,
            Vector2 clipMaximum,
            string rendererName)
        {
            renderer.transform.SetParent(transform, false);
            renderer.transform.localPosition = source.transform.localPosition;
            renderer.transform.localRotation = source.transform.localRotation;
            renderer.transform.localScale = source.transform.localScale;
            renderer.gameObject.name = rendererName;
            renderer.sprite = source.sprite;
            renderer.color = source.color;
            renderer.sharedMaterial = bombDefuseSplitMaterial;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder;
            renderer.enabled = true;

            ApplyBombFragmentProperties(renderer, 0f, clipMinimum, clipMaximum);
        }

        private void SetBombDefuseFade(float fade)
        {
            if (bombDefuseLeftRenderer != null)
                SetBombFragmentFade(bombDefuseLeftRenderer, fade);
            if (bombDefuseRightRenderer != null)
                SetBombFragmentFade(bombDefuseRightRenderer, fade);
        }

        private void SetBombFragmentFade(SpriteRenderer renderer, float fade)
        {
            renderer.GetPropertyBlock(bombDefusePropertyBlock);
            bombDefusePropertyBlock.SetFloat(BombDefuseFadeId, Mathf.Clamp01(fade));
            renderer.SetPropertyBlock(bombDefusePropertyBlock);
        }

        private void ApplyBombFragmentProperties(
            SpriteRenderer renderer,
            float fade,
            Vector2 clipMinimum,
            Vector2 clipMaximum)
        {
            renderer.GetPropertyBlock(bombDefusePropertyBlock);
            bombDefusePropertyBlock.SetFloat(BombDefuseFadeId, Mathf.Clamp01(fade));
            bombDefusePropertyBlock.SetVector(BombFragmentClipMinLocalId, clipMinimum);
            bombDefusePropertyBlock.SetVector(BombFragmentClipMaxLocalId, clipMaximum);
            renderer.SetPropertyBlock(bombDefusePropertyBlock);
        }

        private void ClearBombDefusePresentation()
        {
            ClearBombDefuseRenderer(ref bombDefuseLeftRenderer);
            ClearBombDefuseRenderer(ref bombDefuseRightRenderer);
        }

        private void ClearBombDefuseRenderer(ref SpriteRenderer renderer)
        {
            if (renderer == null)
                return;

            DOTween.Kill(renderer);
            DOTween.Kill(renderer.transform);
            renderer.enabled = false;
            renderer.sprite = null;
            renderer.color = Color.white;
            renderer.sharedMaterial = null;
            renderer.SetPropertyBlock(null);
            renderer.transform.SetParent(iceSlotsRoot, false);
            renderer = null;
        }

        private void ClearBombShredderFragments()
        {
            for (int index = 0; index < bombShredderFragmentRenderers.Count; index++)
            {
                SpriteRenderer renderer = bombShredderFragmentRenderers[index];
                if (renderer == null)
                    continue;

                DOTween.Kill(renderer);
                DOTween.Kill(renderer.transform);
                renderer.enabled = false;
                renderer.sprite = null;
                renderer.color = Color.white;
                renderer.sharedMaterial = null;
                renderer.SetPropertyBlock(null);
                renderer.transform.SetParent(iceSlotsRoot, false);
            }

            bombShredderFragmentRenderers.Clear();
        }

        private void ClearBombVisuals(bool preserveDefusePresentation = false)
        {
            if (!preserveDefusePresentation)
                ClearBombDefusePresentation();
            ClearBombShredderFragments();

            previousBombRemaining = -1;
            if (bombOverlayRenderer != null)
            {
                DOTween.Kill(bombOverlayRenderer);
                bombOverlayRenderer.enabled = false;
                bombOverlayRenderer.transform.SetParent(iceSlotsRoot, false);
                bombOverlayRenderer = null;
            }
            if (bombCounterText != null && (bombCounterText != iceCounterText || !IsFrozen))
            {
                bombCounterText.enabled = false;
                bombCounterText.gameObject.SetActive(false);
            }
        }

        private void RefreshWholePiecePresentation()
        {
            if (wholePieceRenderer == null || collisionCellVisuals == null ||
                collisionCellVisuals.Count == 0)
                return;

            bool useModularNormal = !IsFrozen && preferModularNormalPresentation;
            bool useModularIce = IsFrozen && preferModularIcePresentation;
            if (!IsFrozen && !useModularNormal && joinedPieceSegments.Count > 0)
            {
                RefreshJoinedPiecePresentation();
                RuntimePieceFactory.RefreshOutline(this);
                return;
            }

            for (int index = 0; index < joinedPieceRenderers.Count; index++)
                ResetWholePieceRenderer(joinedPieceRenderers[index]);
            joinedPieceRenderers.Clear();
            Sprite sprite = IsFrozen && wholePieceIceSprite != null && !useModularIce
                ? wholePieceIceSprite
                : wholePieceNormalSprite;
            if (useModularNormal || useModularIce)
                sprite = null;
            if (sprite == null)
            {
                wholePieceRenderer.enabled = false;
                SetModularCellPresentationVisible(true);
                RuntimePieceFactory.RefreshOutline(this);
                return;
            }

            GetModularPresentationBounds(out Vector2 center, out Vector2 size);
            if (sprite.bounds.size.x <= 0f || sprite.bounds.size.y <= 0f)
                return;

            wholePieceRenderer.transform.SetParent(transform, false);
            wholePieceRenderer.transform.localPosition = center;
            bool swapsDimensions = wholePieceTransform.QuarterTurns % 2 != 0;
            wholePieceRenderer.transform.localRotation = Quaternion.Euler(
                0f,
                0f,
                wholePieceTransform.QuarterTurns * 90f);
            wholePieceRenderer.sprite = sprite;
            wholePieceRenderer.flipX = wholePieceTransform.FlipX;
            wholePieceRenderer.flipY = false;
            bool tileNormalSprite = sprite == wholePieceNormalSprite &&
                                    tileWholePieceNormalPresentation &&
                                    wholePieceTransform.QuarterTurns == 0 &&
                                    !wholePieceTransform.FlipX;
            if (tileNormalSprite)
            {
                float moduleScale = size.x >= size.y
                    ? size.y / sprite.bounds.size.y
                    : size.x / sprite.bounds.size.x;
                wholePieceRenderer.drawMode = SpriteDrawMode.Tiled;
                wholePieceRenderer.tileMode = SpriteTileMode.Continuous;
                wholePieceRenderer.transform.localScale = new Vector3(moduleScale, moduleScale, 1f);
                wholePieceRenderer.size = size / moduleScale;
            }
            else
            {
                wholePieceRenderer.drawMode = SpriteDrawMode.Simple;
                wholePieceRenderer.transform.localScale = new Vector3(
                    (swapsDimensions ? size.y : size.x) / sprite.bounds.size.x,
                    (swapsDimensions ? size.x : size.y) / sprite.bounds.size.y,
                    1f);
            }
            wholePieceRenderer.sharedMaterial = IsFrozen && wholePieceIceSprite != null
                ? icePresentationMaterial
                : GetNormalPresentationMaterial();
            // Palette materials own normal-brick colour. SpriteRenderer tint
            // remains white there so material colour is never multiplied a
            // second time. VisualColor is still retained for voxel flights,
            // shards, and legacy unmaterialed pieces.
            wholePieceRenderer.color = IsFrozen || PresentationMaterial != null
                ? Color.white
                : VisualColor;
            wholePieceRenderer.sortingOrder = 5;
            wholePieceRenderer.enabled = true;
            SetModularCellPresentationVisible(false);
            configuredShredderRenderers = new[] { wholePieceRenderer };
            RuntimePieceFactory.RefreshOutline(this);
        }

        private void RefreshJoinedPiecePresentation()
        {
            if (joinedPieceSegments.Count > iceSlots.Count)
            {
                Debug.LogWarning(
                    $"[PuzzlePiece] Joined presentation needs {joinedPieceSegments.Count} slots, " +
                    $"but the prefab provides {iceSlots.Count}. Falling back to modular cells.",
                    this);
                SetModularCellPresentationVisible(true);
                configuredShredderRenderers = null;
                return;
            }

            GetModularPresentationBounds(out Vector2 center, out Vector2 totalSize);
            GetJoinedPresentationExtents(out int width, out int height);
            Vector2 unitSize = new Vector2(totalSize.x / width, totalSize.y / height);
            joinedPieceRenderers.Clear();
            for (int index = 0; index < joinedPieceSegments.Count; index++)
            {
                JoinedPieceSegment segment = joinedPieceSegments[index];
                SpriteRenderer renderer = iceSlots[index];
                if (renderer == null || segment.Sprite == null ||
                    segment.Sprite.bounds.size.x <= 0f || segment.Sprite.bounds.size.y <= 0f)
                    continue;

                Vector2 targetSize = Vector2.Scale(segment.Size, unitSize);
                renderer.transform.SetParent(transform, false);
                renderer.transform.localPosition = center + Vector2.Scale(segment.Offset, unitSize);
                renderer.transform.localRotation = Quaternion.identity;
                renderer.sprite = segment.Sprite;
                renderer.flipX = false;
                renderer.flipY = false;
                if (segment.Tiled)
                {
                    float scale = targetSize.x >= targetSize.y
                        ? targetSize.y / segment.Sprite.bounds.size.y
                        : targetSize.x / segment.Sprite.bounds.size.x;
                    renderer.drawMode = SpriteDrawMode.Tiled;
                    renderer.tileMode = SpriteTileMode.Continuous;
                    renderer.transform.localScale = new Vector3(scale, scale, 1f);
                    renderer.size = targetSize / scale;
                }
                else
                {
                    renderer.drawMode = SpriteDrawMode.Simple;
                    renderer.transform.localScale = new Vector3(
                        targetSize.x / segment.Sprite.bounds.size.x,
                        targetSize.y / segment.Sprite.bounds.size.y,
                        1f);
                }

                renderer.sharedMaterial = GetNormalPresentationMaterial();
                renderer.color = PresentationMaterial != null ? Color.white : VisualColor;
                renderer.sortingOrder = 5 + index;
                renderer.enabled = true;
                joinedPieceRenderers.Add(renderer);
            }

            if (joinedPieceRenderers.Count == 0)
            {
                SetModularCellPresentationVisible(true);
                configuredShredderRenderers = null;
                return;
            }

            wholePieceRenderer = joinedPieceRenderers[0];
            SetModularCellPresentationVisible(false);
            configuredShredderRenderers = joinedPieceRenderers.ToArray();
        }

        private Material GetNormalPresentationMaterial()
        {
            return PresentationMaterial != null
                ? PresentationMaterial
                : fallbackPresentationMaterial;
        }

        private void GetJoinedPresentationExtents(out int width, out int height)
        {
            float minimumX = float.PositiveInfinity;
            float minimumY = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float maximumY = float.NegativeInfinity;
            for (int index = 0; index < joinedPieceSegments.Count; index++)
            {
                JoinedPieceSegment segment = joinedPieceSegments[index];
                minimumX = Mathf.Min(minimumX, segment.Offset.x - segment.Size.x * .5f);
                minimumY = Mathf.Min(minimumY, segment.Offset.y - segment.Size.y * .5f);
                maximumX = Mathf.Max(maximumX, segment.Offset.x + segment.Size.x * .5f);
                maximumY = Mathf.Max(maximumY, segment.Offset.y + segment.Size.y * .5f);
            }

            width = Mathf.Max(1, Mathf.RoundToInt(maximumX - minimumX));
            height = Mathf.Max(1, Mathf.RoundToInt(maximumY - minimumY));
        }

        private void SetModularCellPresentationVisible(bool visible)
        {
            if (collisionCellVisuals == null)
                return;

            for (int index = 0; index < collisionCellVisuals.Count; index++)
            {
                SpriteRenderer visual = collisionCellVisuals[index];
                if (visual != null)
                    visual.enabled = visible;
            }

            // Voxel shards are a separate pooled presentation path. Leaving
            // them enabled underneath a whole-piece sprite double-renders the
            // piece and produces the dense, pixelated patterns seen on partial
            // transparent artwork.
            for (int index = 0; index < configuredVoxelShards.Count; index++)
            {
                VoxelShard voxel = configuredVoxelShards[index];
                if (voxel != null)
                    voxel.Renderer.enabled = visible;
            }
        }

        private void GetModularPresentationBounds(out Vector2 center, out Vector2 size)
        {
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int index = 0; index < collisionCellVisuals.Count; index++)
            {
                SpriteRenderer visual = collisionCellVisuals[index];
                if (visual == null || index >= fullCollisionCellSizes.Count)
                    continue;

                Vector2 halfSize = fullCollisionCellSizes[index] * .5f;
                Vector2 position = visual.transform.localPosition;
                minimum = Vector2.Min(minimum, position - halfSize);
                maximum = Vector2.Max(maximum, position + halfSize);
            }

            if (float.IsPositiveInfinity(minimum.x))
            {
                center = Vector2.zero;
                size = Vector2.one;
                return;
            }

            center = (minimum + maximum) * .5f;
            size = maximum - minimum;
        }

        public float LowestColliderPoint()
        {
            if (compositeCollider != null && compositeCollider.enabled)
                return compositeCollider.bounds.min.y;

            if (solidColliders == null)
                CacheSolidColliders();

            float lowest = transform.position.y;
            bool found = false;
            foreach (Collider2D collider in solidColliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                lowest = found ? Mathf.Min(lowest, collider.bounds.min.y) : collider.bounds.min.y;
                found = true;
            }

            return lowest;
        }

        private void CacheSolidColliders(bool captureDefaultMaterials = false)
        {
            solidColliders = GetComponentsInChildren<Collider2D>();
            if (!captureDefaultMaterials)
                return;

            defaultSolidColliderMaterials = new PhysicsMaterial2D[solidColliders.Length];
            defaultSolidColliderLayers = new int[solidColliders.Length];
            for (int index = 0; index < solidColliders.Length; index++)
            {
                defaultSolidColliderMaterials[index] = solidColliders[index] != null
                    ? solidColliders[index].sharedMaterial
                    : null;
                defaultSolidColliderLayers[index] = solidColliders[index] != null
                    ? solidColliders[index].gameObject.layer
                    : defaultPhysicsLayer;
            }
        }

        private void ApplyShredderPhysicsLayer()
        {
            if (!ShredderPhysicsLayers.IsAvailable)
                return;

            gameObject.layer = ShredderPhysicsLayers.PieceLayer;
            if (solidColliders == null)
                return;

            for (int index = 0; index < solidColliders.Length; index++)
            {
                if (solidColliders[index] != null)
                    solidColliders[index].gameObject.layer = ShredderPhysicsLayers.PieceLayer;
            }
        }

        private void RestoreDefaultPhysicsLayers()
        {
            gameObject.layer = defaultPhysicsLayer;
            if (solidColliders == null)
                return;

            for (int index = 0; index < solidColliders.Length; index++)
            {
                Collider2D collider = solidColliders[index];
                if (collider == null)
                    continue;

                collider.gameObject.layer = defaultSolidColliderLayers != null &&
                                            index < defaultSolidColliderLayers.Length
                    ? defaultSolidColliderLayers[index]
                    : defaultPhysicsLayer;
            }
        }
    }
}
