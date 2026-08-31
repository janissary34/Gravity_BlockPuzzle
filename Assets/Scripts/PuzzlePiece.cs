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

namespace GravityPuzzle
{
    /// <summary>
    /// Identifies the root of one complete movable puzzle piece.
    /// Its child colliders form the body and the smaller hook geometry.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(CompositeCollider2D), typeof(LineRenderer))]
    public sealed class PuzzlePiece : MonoBehaviour, IPoolable, IPoolReturnReceiver<PuzzlePiece>
    {
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
        private const string IceCounterPresentationName = "Ice Counter Presentation";
        private static readonly List<PuzzlePiece> activePieces = new List<PuzzlePiece>();

        public static IReadOnlyList<PuzzlePiece> ActivePieces => activePieces;
        public Rigidbody2D Body { get; private set; }
        public PieceGridFallView GridFallView { get; private set; }
        public CompositeCollider2D CompositeCollider => compositeCollider;
        public LineRenderer Outline => rootOutline;
        public bool IsSelected => isSelected;
        public bool IsBeingShredded => beingShredded;
        public bool IsFrozen { get; private set; }
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
            : isSelected
                ? GravityGridMetrics.DraggingPieceCollisionSkinInCells
                : GravityGridMetrics.RestingPieceCollisionSkinInCells;

        private readonly List<SpriteRenderer> iceRenderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> iceSlots = new List<SpriteRenderer>();
        private Transform iceSlotsRoot;
        private readonly List<PiecePartSlot> partSlots = new List<PiecePartSlot>();
        private CompositeCollider2D compositeCollider;
        private LineRenderer rootOutline;
        private List<BoxCollider2D> collisionCells;
        private List<Vector2> fullCollisionCellSizes;
        private List<SpriteRenderer> collisionCellVisuals;
        private readonly List<ShredderReservationCell> shredderReservationCells =
            new List<ShredderReservationCell>();
        private readonly List<GridCoordinate> shredderCellsReadyToRelease =
            new List<GridCoordinate>();
        private float shredderReservationStartBodyY;
        private readonly List<VoxelShard> configuredVoxelShards = new List<VoxelShard>();
        private SpriteRenderer[] configuredShredderRenderers;
        private Collider2D[] solidColliders;
        private PhysicsMaterial2D[] defaultSolidColliderMaterials;
        private SpriteRenderer[] shredderPresentationRenderers;
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
        private bool beingShredded;
        private bool gridDespawned;
        private bool useFullCollisionGeometry;
        private bool isSelected;
        private bool destructionReported;
        private bool iceReleaseAnimating;
        private int frozenUntilDestroyedCount;
        private int previousFrozenRemaining = -1;
        private TextMeshPro iceCounterText;
        private Action<PuzzlePiece> returnToPool;
        private float iceCounterFontSize = 36f;
        private Color iceCounterTextColor = Color.black;
        private Color iceCounterOutlineColor = Color.white;
        private float iceCounterOutlineWidth = .18f;
        private Vector2 iceCounterOffset;

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
            Body = GetComponent<Rigidbody2D>();
            GridFallView = GetComponent<PieceGridFallView>();
            compositeCollider = GetComponent<CompositeCollider2D>();
            rootOutline = GetComponent<LineRenderer>();
            PiecePartSlot[] cachedPartSlots = GetComponentsInChildren<PiecePartSlot>(true);
            for (int index = 0; index < cachedPartSlots.Length; index++)
                partSlots.Add(cachedPartSlots[index]);
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
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
                    break;
                }
            }
        }

        public int PartSlotCount => partSlots.Count;

        public PiecePartSlot GetPartSlot(int index)
        {
            return index >= 0 && index < partSlots.Count ? partSlots[index] : null;
        }

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
            RestoreShredderPresentation();
            beingShredded = false;
            gridDespawned = false;
            destructionReported = false;
            iceReleaseAnimating = false;
            previousFrozenRemaining = -1;
            useFullCollisionGeometry = false;
            isSelected = false;
            IsFrozen = false;
            SourcePieceId = -1;
            if (rootOutline != null)
                rootOutline.enabled = true;
            if (compositeCollider != null)
                compositeCollider.enabled = true;
            RestoreDefaultCollisionMaterials();
            InvalidateVoxelCache();
        }

        public void OnDespawn()
        {
            isSelected = false;
            returnToPool = null;
            ClearIceVisuals();
            RestoreShredderPresentation();
            RestoreDefaultCollisionMaterials();
            RuntimePieceFactory.ResetPooledPiece(this);
            InvalidateVoxelCache();
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

        public IReadOnlyList<VoxelShard> ConfiguredVoxelShards => configuredVoxelShards;
        public SpriteRenderer[] ConfiguredShredderRenderers => configuredShredderRenderers;
        public IReadOnlyList<PiecePartSlot> PartSlots => partSlots;

        public void ClearVoxelPresentation()
        {
            configuredVoxelShards.Clear();
            configuredShredderRenderers = null;
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

            bool releasedShredderReservation = beingShredded;
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
            if (isSelected && (IsFrozen || IsBeingShredded))
                return;

            this.isSelected = isSelected;
            ApplyCollisionProfile();

            // Resting pieces grip one another. During a drag, contact friction is
            // removed temporarily so adjacent pieces cannot jam against each other.
            PrototypeBootstrap.SetDraggingFriction(this, isSelected);

            if (rootOutline != null)
            {
                float outlineWidth = isSelected ? selectedOutlineWidth : restingOutlineWidth;
                rootOutline.startWidth = outlineWidth;
                rootOutline.endWidth = outlineWidth;
                Color outlineColor = isSelected ? selectedOutlineColor : restingOutlineColor;
                rootOutline.startColor = outlineColor;
                rootOutline.endColor = outlineColor;
                rootOutline.sortingOrder = isSelected ? selectedOutlineSortingOrder : restingOutlineSortingOrder;
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
            SetSelected(isSelected);
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
            ConfigureFreeze(
                setup.FrozenMoveCount,
                setup.IceCounterFontSize,
                setup.IceCounterTextColor,
                setup.IceCounterOutlineColor,
                setup.IceCounterOutlineWidth,
                setup.IceCounterOffset);
        }

        private void PrepareForRuntimeSetup()
        {
            ClearIceVisuals();
            shredderReservationCells.Clear();
            shredderCellsReadyToRelease.Clear();
            shredderReservationStartBodyY = 0f;
            beingShredded = false;
            destructionReported = false;
            useFullCollisionGeometry = false;
            isSelected = false;
            IsFrozen = false;
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

                Bounds bounds = cell.bounds;
                int fineWidth = Mathf.Max(
                    1,
                    Mathf.RoundToInt(bounds.size.x * level.subdivisions));
                int fineHeight = Mathf.Max(
                    1,
                    Mathf.RoundToInt(bounds.size.y * level.subdivisions));
                float fineCellSize = 1f / level.subdivisions;
                GridCoordinate bottomLeft = GravityLevelGridCoordinates.WorldToFineCell(
                    level,
                    new Vector2(
                        bounds.min.x + fineCellSize * .5f,
                        bounds.min.y + fineCellSize * .5f));

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

            GridCoordinate pivot = GravityLevelGridCoordinates.WorldToFineCell(level, Body.position);
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
            if (Body == null)
                return;

            Body.simulated = true;
            Body.bodyType = RigidbodyType2D.Kinematic;
            Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Body.constraints = RigidbodyConstraints2D.None;
            Body.velocity = Vector2.down * config.FeedSpeed;
            Body.angularDrag = config.FeedAngularDrag;
            Body.angularVelocity = UnityEngine.Random.Range(
                -config.MaxFeedTiltAngle,
                config.MaxFeedTiltAngle);
            Body.AddTorque(
                UnityEngine.Random.Range(-1f, 1f) * config.TumbleTorque,
                ForceMode2D.Impulse);
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
        public void BeginShredderPresentation(SpriteRenderer[] renderers)
        {
            RestoreShredderPresentation();
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

            if (rootOutline != null)
                rootOutline.sortingOrder = shredderPresentationOutlineSortingOrder + sortingOrderOffset;
        }

        public void ApplyShredderPresentationClipping()
        {
            if (shredderPresentationRenderers == null)
                return;

            for (int index = 0; index < shredderPresentationRenderers.Length; index++)
            {
                SpriteRenderer renderer = shredderPresentationRenderers[index];
                if (renderer != null && renderer.transform.IsChildOf(transform))
                    renderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            }
        }

        public void HideShredderRenderer(SpriteRenderer renderer)
        {
            if (renderer != null && renderer.transform.IsChildOf(transform))
                renderer.enabled = false;
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
            }

            shredderPresentationRenderers = null;
            shredderPresentationEnabledStates = null;
            shredderPresentationMaskStates = null;
            shredderPresentationSortingOrders = null;
            if (rootOutline != null)
                rootOutline.sortingOrder = shredderPresentationOutlineSortingOrder;
        }

        public void ReleaseCollisionCellsAtOrBelow(float worldY)
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
                cell.enabled = false;
                geometryChanged = true;
            }

            if (!geometryChanged)
                return;

            if (compositeCollider != null)
                compositeCollider.GenerateGeometry();
            CacheSolidColliders();
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
                PrototypeBoard.Active?.TryReleaseShredderGridCells(
                    this,
                    shredderCellsReadyToRelease);
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
            float counterFontSize,
            Color counterTextColor,
            Color counterOutlineColor,
            float counterOutlineWidth,
            Vector2 counterOffset)
        {
            frozenUntilDestroyedCount = Mathf.Max(0, requiredDestroyedPieces);
            iceCounterFontSize = Mathf.Max(1f, counterFontSize);
            iceCounterTextColor = counterTextColor;
            iceCounterOutlineColor = counterOutlineColor;
            iceCounterOutlineWidth = Mathf.Clamp01(counterOutlineWidth);
            iceCounterOffset = counterOffset;
            PrototypeBoard board = PrototypeBoard.Active;
            RefreshFreezeState(board != null ? board.DestroyedPieceCount : 0);
        }

        public void RefreshFreezeState(int destroyedPieceCount)
        {
            bool shouldBeFrozen =
                frozenUntilDestroyedCount > 0 &&
                destroyedPieceCount < frozenUntilDestroyedCount &&
                !beingShredded;

            IsFrozen = shouldBeFrozen;
            if (shouldBeFrozen)
            {
                if (iceRenderers.Count == 0)
                    BuildIceVisuals();

                int remainingCount = frozenUntilDestroyedCount - destroyedPieceCount;
                if (previousFrozenRemaining >= 0 && remainingCount < previousFrozenRemaining)
                    PlayIceCrackFeedback(remainingCount, previousFrozenRemaining);

                iceReleaseAnimating = false;
                previousFrozenRemaining = remainingCount;
                UpdateIceCounter(remainingCount);
            }
            else
            {
                previousFrozenRemaining = -1;
                if (iceRenderers.Count > 0 && !iceReleaseAnimating)
                    PlayIceReleaseAnimation();
                else if (!iceReleaseAnimating)
                    ClearIceVisuals();

                if (Body != null)
                    Body.WakeUp();
            }
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
            return !beingShredded && TryGetCellIndexAt(worldPosition, out _);
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
            if (beingShredded || collisionCells == null || collisionCellVisuals == null)
                return false;

            if (!TryGetCellIndexAt(worldPosition, out int targetIndex))
                return false;

            return TryRemoveCellAtIndex(targetIndex, out removedCell);
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

        private bool TryRemoveCellAtIndex(int targetIndex, out RemovedCell removedCell)
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
            if (rootOutline != null)
                rootOutline.enabled = false;

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
                    ApplyCollisionProfile();
                    PrototypeBoard board = PrototypeBoard.Active;
                    RefreshFreezeState(board != null ? board.DestroyedPieceCount : 0);
                }
            }

            return true;
        }

        // The rendered voxel grid is intentionally made of child VoxelShard
        // objects. Its parent SpriteRenderer is a disabled placeholder, so it
        // cannot be used as hit-test authority. Use the authored part-slot
        // transform and its cached visual size instead. This remains valid
        // while normal-play colliders are disabled and never queries physics.
        private bool TryGetCellIndexAt(Vector2 worldPosition, out int targetIndex)
        {
            targetIndex = -1;
            if (collisionCellVisuals == null || fullCollisionCellSizes == null ||
                collisionCellVisuals.Count == 0)
                return false;

            float closestDistance = float.PositiveInfinity;
            for (int index = 0; index < collisionCellVisuals.Count; index++)
            {
                SpriteRenderer visual = collisionCellVisuals[index];
                if (visual == null || !visual.gameObject.activeInHierarchy ||
                    index >= fullCollisionCellSizes.Count)
                    continue;

                Vector2 localPoint = visual.transform.InverseTransformPoint(worldPosition);
                Vector2 halfSize = fullCollisionCellSizes[index] * .5f;
                if (Mathf.Abs(localPoint.x) > halfSize.x || Mathf.Abs(localPoint.y) > halfSize.y)
                    continue;

                float distance = localPoint.sqrMagnitude;
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
                    componentProgress);
                fragments.Add(fragment);
            }

            PrototypeBoard board = PrototypeBoard.Active;
            if (board == null || !board.TryRegisterHammerFragments(fragments, PieceState.Falling))
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
                : isSelected
                    ? GravityGridMetrics.DraggingPieceCollisionSkinInCells
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
        public bool TryBeginShredderHandoff()
        {
            if (beingShredded)
                return false;

            // A grid fall tween can still be in flight when the coordinate
            // catch zone captures this piece. Stop it before the feed becomes
            // the sole movement owner of the Rigidbody.
            GridFallView?.Cancel();
            beingShredded = true;
            SetSelected(false);
            PrototypeBoard board = PrototypeBoard.Active;
            if (board != null)
            {
                if (!board.TryReservePieceInGrid(this, PieceState.HandoffToPhysics))
                {
                    beingShredded = false;
                    return false;
                }

                if (board.TryGetPieceModel(this, out PieceModel model))
                    CaptureShredderReservationCells(model);

                board.TryLockFinalShredderOutcome(this);
            }

            ReportDestroyed();
            return true;
        }

        private void BuildIceVisuals()
        {
            if (collisionCellVisuals == null)
                return;

            foreach (SpriteRenderer source in collisionCellVisuals)
            {
                if (source == null)
                    continue;

                CreateIceLayer(
                    source,
                    "Ice Overlay",
                    Vector3.one,
                    new Color(.42f, .82f, 1f, .58f),
                    source.sortingOrder + 5);
                CreateIceLayer(
                    source,
                    "Ice Frost",
                    new Vector3(.72f, .72f, 1f),
                    new Color(.9f, 1f, 1f, .3f),
                    source.sortingOrder + 6);
            }
        }

        private void UpdateIceCounter(int remainingCount)
        {
            if (collisionCellVisuals == null || collisionCellVisuals.Count == 0)
                return;

            Bounds combinedBounds = collisionCellVisuals[0].bounds;
            for (int i = 1; i < collisionCellVisuals.Count; i++)
            {
                SpriteRenderer visual = collisionCellVisuals[i];
                if (visual != null)
                    combinedBounds.Encapsulate(visual.bounds);
            }

            if (iceCounterText == null)
            {
                Debug.LogWarning("[PuzzlePiece] Ice counter presentation is missing from BlockPiece.prefab.", this);
                return;
            }

            // Apply style on every refresh, not only on object creation. This
            // keeps Play Mode previews in sync after editor recompilation and
            // after changing the serialized level settings.
            iceCounterText.color = iceCounterTextColor;
            iceCounterText.enabled = true;
            iceCounterText.enableAutoSizing = false;
            iceCounterText.fontSize = iceCounterFontSize * RuntimeIceCounterFontScale;
            iceCounterText.outlineColor = iceCounterOutlineColor;
            iceCounterText.outlineWidth = iceCounterOutlineWidth * .1f;
            iceCounterText.renderer.sortingLayerID = collisionCellVisuals[0].sortingLayerID;
            iceCounterText.renderer.sortingOrder = 50;
            iceCounterText.text = Mathf.Max(0, remainingCount).ToString();
            iceCounterText.transform.position = new Vector3(
                combinedBounds.center.x + iceCounterOffset.x,
                combinedBounds.center.y + iceCounterOffset.y,
                transform.position.z - .1f);
            iceCounterText.rectTransform.sizeDelta = new Vector2(
                Mathf.Max(.75f, combinedBounds.size.x * .9f),
                Mathf.Max(.75f, combinedBounds.size.y * .9f));
            iceCounterText.ForceMeshUpdate();
        }

        private void CreateIceLayer(
            SpriteRenderer source,
            string layerName,
            Vector3 scale,
            Color color,
            int sortingOrder)
        {
            if (iceRenderers.Count >= iceSlots.Count)
            {
                Debug.LogWarning("[PuzzlePiece] Ice presentation slot capacity exceeded.", this);
                return;
            }

            SpriteRenderer renderer = iceSlots[iceRenderers.Count];
            renderer.transform.SetParent(source.transform, false);
            renderer.transform.localPosition = Vector3.zero;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = scale;
            renderer.gameObject.name = layerName;
            renderer.sprite = source.sprite;
            renderer.color = color;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = true;
            iceRenderers.Add(renderer);
        }

        private void PlayIceCrackFeedback(int remainingCount, int previousRemainingCount)
        {
            TweenConfig tweenConfig = GridFallView != null ? GridFallView.Config : null;
            float remainingFraction = previousRemainingCount > 0
                ? Mathf.Clamp01((float)remainingCount / previousRemainingCount)
                : 0f;

            for (int index = 0; index < iceRenderers.Count; index++)
            {
                SpriteRenderer renderer = iceRenderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;

                DOTween.Kill(renderer);
                Vector3 restingScale = renderer.transform.localScale;
                float targetAlpha = renderer.color.a * Mathf.Lerp(.55f, .9f, remainingFraction);

                if (tweenConfig == null)
                {
                    Color color = renderer.color;
                    color.a = targetAlpha;
                    renderer.color = color;
                    continue;
                }

                Sequence crack = DOTween.Sequence()
                    .Append(renderer.transform.DOPunchScale(
                        restingScale * tweenConfig.IceCrackScaleMultiplier,
                        tweenConfig.IceCrackDuration,
                        tweenConfig.IceCrackVibrato,
                        tweenConfig.IceCrackElasticity))
                    .Join(renderer.DOFade(targetAlpha, tweenConfig.IceCrackDuration));
                crack.SetLink(renderer.gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
            }
        }

        private void PlayIceReleaseAnimation()
        {
            iceReleaseAnimating = true;
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

            if (iceCounterText != null)
            {
                iceCounterText.enabled = false;
            }
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
            for (int index = 0; index < solidColliders.Length; index++)
            {
                defaultSolidColliderMaterials[index] = solidColliders[index] != null
                    ? solidColliders[index].sharedMaterial
                    : null;
            }
        }
    }
}
