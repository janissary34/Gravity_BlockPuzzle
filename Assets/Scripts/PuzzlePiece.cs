using UnityEngine;
using System.Collections.Generic;
using TMPro;

namespace GravityPuzzle
{
    /// <summary>
    /// Identifies the root of one complete movable puzzle piece.
    /// Its child colliders form the body and the smaller hook geometry.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PuzzlePiece : MonoBehaviour
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
        private static readonly List<PuzzlePiece> activePieces = new List<PuzzlePiece>();

        public static IReadOnlyList<PuzzlePiece> ActivePieces => activePieces;
        public Rigidbody2D Body { get; private set; }
        public bool IsSelected => isSelected;
        public bool IsBeingShredded => beingShredded;
        public bool IsFrozen { get; private set; }
        /// <summary>Authored board-block units represented by this draggable piece.</summary>
        public int ProgressUnits { get; private set; } = 1;
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

        private readonly List<SpriteRenderer> normalRenderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> selectedRenderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> outlineRenderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> iceRenderers = new List<SpriteRenderer>();
        private Transform selectionVisualsRoot;
        private CompositeCollider2D compositeCollider;
        private List<BoxCollider2D> collisionCells;
        private List<Vector2> fullCollisionCellSizes;
        private List<SpriteRenderer> collisionCellVisuals;
        private Collider2D[] solidColliders;
        private bool selectionVisualsBuilt;
        private bool beingShredded;
        private bool useFullCollisionGeometry;
        private bool isSelected;
        private bool destructionReported;
        private int frozenUntilDestroyedCount;
        private TextMeshPro iceCounterText;
        private float iceCounterFontSize = 36f;
        private Color iceCounterTextColor = Color.black;
        private Color iceCounterOutlineColor = Color.white;
        private float iceCounterOutlineWidth = .18f;
        private Vector2 iceCounterOffset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActivePieces()
        {
            activePieces.Clear();
        }

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
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

        private void Start()
        {
            CacheSolidColliders();
            BuildSelectionVisuals();
        }

        public void SetSelected(bool isSelected)
        {
            // A piece is committed to the shredder as soon as it enters the
            // catch zone. It must not regain drag selection during the feed.
            if (isSelected && (IsFrozen || IsBeingShredded))
                return;

            this.isSelected = isSelected;

            ApplyCollisionProfile();

            // Script recompiles during Play Mode can clear these runtime lists while
            // leaving the generated outline objects alive. Rebuild/reconnect them
            // at the moment of selection so the highlight can never disappear.
            BuildSelectionVisuals();

            // Resting pieces grip one another. During a drag, contact friction is
            // removed temporarily so adjacent pieces cannot jam against each other.
            PrototypeBootstrap.SetDraggingFriction(this, isSelected);

            foreach (SpriteRenderer renderer in normalRenderers)
                renderer.enabled = !isSelected;

            foreach (SpriteRenderer renderer in selectedRenderers)
                renderer.enabled = isSelected;

            foreach (SpriteRenderer renderer in outlineRenderers)
                renderer.enabled = isSelected;
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

        public void ConfigureProgressUnits(int units)
        {
            ProgressUnits = Mathf.Max(1, units);
            RemainingProgressUnits = ProgressUnits;
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

        public void ReleaseCollisionCellsAtOrBelow(float worldY)
        {
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
            Physics2D.SyncTransforms();
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

            EnsureBlockBorders();
            ApplyCollisionProfile();
            CacheSolidColliders();
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
                UpdateIceCounter(frozenUntilDestroyedCount - destroyedPieceCount);
            }
            else
            {
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
        /// Removes a cell and returns its exact visual/progress payload for a
        /// booster impact or another effect that must preserve the voxel reward.
        /// </summary>
        public bool TryRemoveCellAt(Vector2 worldPosition, out RemovedCell removedCell)
        {
            removedCell = default;
            if (beingShredded || collisionCells == null || collisionCellVisuals == null)
                return false;

            int targetIndex = -1;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < collisionCellVisuals.Count; i++)
            {
                SpriteRenderer visual = collisionCellVisuals[i];
                if (visual == null || !visual.bounds.Contains(worldPosition))
                    continue;

                float distance = ((Vector2)visual.bounds.center - worldPosition).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    targetIndex = i;
                }
            }

            if (targetIndex < 0)
                return false;

            SpriteRenderer targetVisual = collisionCellVisuals[targetIndex];
            int cellCountBeforeRemoval = Mathf.Max(1, collisionCellVisuals.Count);
            removedCell = new RemovedCell(
                targetVisual != null ? (Vector2)targetVisual.bounds.center : worldPosition,
                targetVisual != null ? (Vector2)targetVisual.bounds.size : Vector2.one,
                VisualColor,
                RemainingProgressUnits / cellCountBeforeRemoval,
                targetVisual != null
                    ? Mathf.Max(1, targetVisual.GetComponentsInChildren<VoxelShard>(true).Length)
                    : 1);
            RemainingProgressUnits = Mathf.Max(0f, RemainingProgressUnits - removedCell.progressUnits);

            if (isSelected)
                SetSelected(false);

            ClearIceVisuals();

            SpriteRenderer removedVisual = collisionCellVisuals[targetIndex];
            BoxCollider2D removedCollider = collisionCells[targetIndex];
            if (removedCollider != null)
                removedCollider.enabled = false;

            RemoveSelectionVisualFor(removedVisual);
            collisionCellVisuals.RemoveAt(targetIndex);
            collisionCells.RemoveAt(targetIndex);
            fullCollisionCellSizes.RemoveAt(targetIndex);

            if (removedVisual != null)
                Destroy(removedVisual.gameObject);
            if (removedCollider != null)
                Destroy(removedCollider.gameObject);

            if (compositeCollider != null)
                compositeCollider.GenerateGeometry();
            CacheSolidColliders();

            // Avoid leaving the original silhouette around a modified piece.
            LineRenderer perimeter = GetComponent<LineRenderer>();
            if (perimeter != null)
                perimeter.enabled = false;

            if (collisionCells.Count == 0)
            {
                ReportDestroyed();
                Destroy(gameObject);
            }
            else
            {
                SplitDisconnectedCells();
                ApplyCollisionProfile();
                PrototypeBoard board = PrototypeBoard.Active;
                RefreshFreezeState(board != null ? board.DestroyedPieceCount : 0);
            }

            return true;
        }

        private void SplitDisconnectedCells()
        {
            List<List<int>> components = FindConnectedComponents();
            if (components.Count <= 1)
                return;

            int largestIndex = 0;
            for (int i = 1; i < components.Count; i++)
            {
                if (components[i].Count > components[largestIndex].Count)
                    largestIndex = i;
            }

            List<int> largest = components[largestIndex];
            components[largestIndex] = components[0];
            components[0] = largest;

            Transform sourceCollisionRoot = collisionCells[0].transform.parent;
            int totalCellCount = collisionCells.Count;
            float remainingBeforeSplit = RemainingProgressUnits;
            List<int> movedIndices = new List<int>();
            for (int componentIndex = 1; componentIndex < components.Count; componentIndex++)
            {
                float componentProgress = remainingBeforeSplit * components[componentIndex].Count / totalCellCount;
                CreateIndependentPiece(
                    components[componentIndex],
                    sourceCollisionRoot,
                    componentIndex,
                    componentProgress);
                movedIndices.AddRange(components[componentIndex]);
            }

            movedIndices.Sort();
            for (int i = movedIndices.Count - 1; i >= 0; i--)
            {
                int sourceIndex = movedIndices[i];
                collisionCells.RemoveAt(sourceIndex);
                collisionCellVisuals.RemoveAt(sourceIndex);
                fullCollisionCellSizes.RemoveAt(sourceIndex);
            }
            RemainingProgressUnits = remainingBeforeSplit * collisionCells.Count / totalCellCount;

            if (compositeCollider != null)
                compositeCollider.GenerateGeometry();
            CacheSolidColliders();
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
            Vector2 firstCentre = collisionCells[first].transform.localPosition;
            Vector2 secondCentre = collisionCells[second].transform.localPosition;
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

        private void CreateIndependentPiece(
            List<int> component,
            Transform sourceCollisionRoot,
            int splitNumber,
            float remainingProgress)
        {
            GameObject splitObject = new GameObject($"{name} - Split {splitNumber}");
            splitObject.transform.SetParent(transform.parent, false);
            splitObject.transform.localPosition = transform.localPosition;
            splitObject.transform.localRotation = transform.localRotation;
            splitObject.transform.localScale = transform.localScale;

            Rigidbody2D splitBody = splitObject.AddComponent<Rigidbody2D>();
            CopyBodySettings(Body, splitBody);

            CompositeCollider2D splitComposite = splitObject.AddComponent<CompositeCollider2D>();
            splitComposite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            splitComposite.generationType = CompositeCollider2D.GenerationType.Synchronous;
            splitComposite.edgeRadius = 0f;

            GameObject collisionRootObject = new GameObject("Collision Geometry");
            Transform splitCollisionRoot = collisionRootObject.transform;
            splitCollisionRoot.SetParent(splitObject.transform, false);
            splitCollisionRoot.localPosition = sourceCollisionRoot.localPosition;
            splitCollisionRoot.localRotation = sourceCollisionRoot.localRotation;
            splitCollisionRoot.localScale = sourceCollisionRoot.localScale;

            List<BoxCollider2D> splitCells = new List<BoxCollider2D>(component.Count);
            List<SpriteRenderer> splitVisuals = new List<SpriteRenderer>(component.Count);
            List<Vector2> splitSizes = new List<Vector2>(component.Count);

            component.Sort();
            for (int i = 0; i < component.Count; i++)
            {
                int sourceIndex = component[i];
                BoxCollider2D cell = collisionCells[sourceIndex];
                SpriteRenderer visual = collisionCellVisuals[sourceIndex];
                Vector2 fullSize = fullCollisionCellSizes[sourceIndex];

                RemoveSelectionVisualFor(visual);
                cell.size = fullSize;
                cell.transform.SetParent(splitCollisionRoot, true);
                visual.transform.SetParent(splitObject.transform, true);
                splitCells.Add(cell);
                splitVisuals.Add(visual);
                splitSizes.Add(fullSize);
            }

            splitComposite.GenerateGeometry();
            PuzzlePiece splitPiece = splitObject.AddComponent<PuzzlePiece>();
            splitPiece.ConfigureCollisionGeometry(
                splitComposite,
                splitCells,
                splitVisuals,
                splitSizes);
            splitPiece.ConfigureRemainingProgress(remainingProgress);
            splitPiece.ConfigureFreeze(
                frozenUntilDestroyedCount,
                iceCounterFontSize,
                iceCounterTextColor,
                iceCounterOutlineColor,
                iceCounterOutlineWidth,
                iceCounterOffset);
        }

        private static void CopyBodySettings(Rigidbody2D source, Rigidbody2D target)
        {
            target.bodyType = RigidbodyType2D.Kinematic;
            target.simulated = source.simulated;
            target.useFullKinematicContacts = source.useFullKinematicContacts;
            target.collisionDetectionMode = source.collisionDetectionMode;
            target.interpolation = source.interpolation;
            target.constraints = source.constraints;
            target.sleepMode = source.sleepMode;
            target.gravityScale = source.gravityScale;
            target.mass = source.mass;
            target.drag = source.drag;
            target.angularDrag = source.angularDrag;
        }

        private void RemoveSelectionVisualFor(SpriteRenderer removedVisual)
        {
            int visualIndex = normalRenderers.IndexOf(removedVisual);
            if (visualIndex < 0)
                return;

            normalRenderers.RemoveAt(visualIndex);
            if (visualIndex < selectedRenderers.Count)
            {
                SpriteRenderer selected = selectedRenderers[visualIndex];
                selectedRenderers.RemoveAt(visualIndex);
                if (selected != null)
                    Destroy(selected.gameObject);
            }

            if (visualIndex < outlineRenderers.Count)
            {
                SpriteRenderer outline = outlineRenderers[visualIndex];
                outlineRenderers.RemoveAt(visualIndex);
                if (outline != null)
                    Destroy(outline.gameObject);
            }
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

        public bool TryBeginShredding()
        {
            if (beingShredded)
                return false;

            beingShredded = true;
            SetSelected(false);
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
                GameObject counterObject = new GameObject("Ice Remaining Count");
                counterObject.transform.SetParent(transform, true);
                iceCounterText = counterObject.AddComponent<TextMeshPro>();
                iceCounterText.alignment = TextAlignmentOptions.Center;
                iceCounterText.fontStyle = FontStyles.Bold;
            }

            // Apply style on every refresh, not only on object creation. This
            // keeps Play Mode previews in sync after editor recompilation and
            // after changing the serialized level settings.
            iceCounterText.color = iceCounterTextColor;
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

        private void EnsureBlockBorders()
        {
            if (collisionCellVisuals == null)
                return;

            for (int i = 0; i < collisionCellVisuals.Count; i++)
            {
                SpriteRenderer source = collisionCellVisuals[i];
                if (source == null || source.transform.Find("Block Border Top") != null)
                    continue;

                Vector2 fullSize = i < fullCollisionCellSizes.Count
                    ? fullCollisionCellSizes[i]
                    : (Vector2)source.bounds.size;
                float thickness = Mathf.Min(.025f, Mathf.Min(fullSize.x, fullSize.y) * .18f);
                float horizontalRatio = thickness / Mathf.Max(fullSize.y, .001f);
                float verticalRatio = thickness / Mathf.Max(fullSize.x, .001f);

                CreateBorderStrip(source, "Block Border Top",
                    new Vector2(0f, .5f - horizontalRatio * .5f),
                    new Vector2(1f, horizontalRatio));
                CreateBorderStrip(source, "Block Border Bottom",
                    new Vector2(0f, -.5f + horizontalRatio * .5f),
                    new Vector2(1f, horizontalRatio));
                CreateBorderStrip(source, "Block Border Left",
                    new Vector2(-.5f + verticalRatio * .5f, 0f),
                    new Vector2(verticalRatio, 1f));
                CreateBorderStrip(source, "Block Border Right",
                    new Vector2(.5f - verticalRatio * .5f, 0f),
                    new Vector2(verticalRatio, 1f));
            }
        }

        private static void CreateBorderStrip(
            SpriteRenderer source,
            string stripName,
            Vector2 localPosition,
            Vector2 localScale)
        {
            GameObject strip = new GameObject(stripName);
            strip.transform.SetParent(source.transform, false);
            strip.transform.localPosition = localPosition;
            strip.transform.localRotation = Quaternion.identity;
            strip.transform.localScale = new Vector3(localScale.x, localScale.y, 1f);

            SpriteRenderer renderer = strip.AddComponent<SpriteRenderer>();
            renderer.sprite = source.sprite;
            renderer.color = Color.black;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder + 8;
        }

        private void CreateIceLayer(
            SpriteRenderer source,
            string layerName,
            Vector3 scale,
            Color color,
            int sortingOrder)
        {
            GameObject layer = new GameObject(layerName);
            layer.transform.SetParent(source.transform, false);
            layer.transform.localPosition = Vector3.zero;
            layer.transform.localRotation = Quaternion.identity;
            layer.transform.localScale = scale;

            SpriteRenderer renderer = layer.AddComponent<SpriteRenderer>();
            renderer.sprite = source.sprite;
            renderer.color = color;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = sortingOrder;
            iceRenderers.Add(renderer);
        }

        private void ClearIceVisuals()
        {
            foreach (SpriteRenderer renderer in iceRenderers)
            {
                if (renderer == null)
                    continue;

                renderer.enabled = false;
                Destroy(renderer.gameObject);
            }
            iceRenderers.Clear();

            if (iceCounterText != null)
            {
                iceCounterText.enabled = false;
                Destroy(iceCounterText.gameObject);
                iceCounterText = null;
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

        private void CacheSolidColliders()
        {
            solidColliders = GetComponentsInChildren<Collider2D>();
        }

        private void BuildSelectionVisuals()
        {
            if (selectionVisualsBuilt && normalRenderers.Count > 0)
                return;

            normalRenderers.Clear();
            selectedRenderers.Clear();
            outlineRenderers.Clear();

            selectionVisualsRoot = transform.Find("Selection Visuals");
            if (selectionVisualsRoot == null)
            {
                GameObject visualRoot = new GameObject("Selection Visuals");
                visualRoot.transform.SetParent(transform, false);
                selectionVisualsRoot = visualRoot.transform;
            }

            SpriteRenderer[] pieceRenderers = GetComponentsInChildren<SpriteRenderer>(true);

            int visualIndex = 0;
            foreach (SpriteRenderer original in pieceRenderers)
            {
                if (original.transform.IsChildOf(selectionVisualsRoot) ||
                    original.gameObject.name.StartsWith("Ice ") ||
                    original.gameObject.name.StartsWith("Block Border"))
                    continue;

                normalRenderers.Add(original);

                // Draw the selection border inside the exact modular silhouette.
                // An outside outline makes an N-cell piece look larger than the
                // N-cell opening it is meant to fit through.
                SpriteRenderer selectedFill = FindOrCreateVisualCopy(
                    original, $"Selected Fill {visualIndex}", InsetScale(original, .025f),
                    original.color, original.sortingOrder + 2);

                // The white copy keeps the original, exact footprint. A slightly
                // inset coloured fill reveals it as an internal selection border.
                SpriteRenderer outline = FindOrCreateVisualCopy(
                    original, $"White Selection Outline {visualIndex}", Vector3.one,
                    Color.white, original.sortingOrder + 1);

                selectedRenderers.Add(selectedFill);
                outlineRenderers.Add(outline);
                visualIndex++;
            }

            selectionVisualsBuilt = true;
        }

        private SpriteRenderer FindOrCreateVisualCopy(
            SpriteRenderer source,
            string objectName,
            Vector3 scale,
            Color color,
            int sortingOrder)
        {
            Transform existing = selectionVisualsRoot.Find(objectName);
            SpriteRenderer renderer = existing != null
                ? existing.GetComponent<SpriteRenderer>()
                : null;

            if (renderer == null)
                return CreateVisualCopy(source, objectName, scale, color, sortingOrder);

            renderer.sprite = source.sprite;
            renderer.color = color;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = sortingOrder;
            MatchSourceTransform(renderer.transform, source.transform, scale);
            renderer.enabled = false;
            return renderer;
        }

        private SpriteRenderer CreateVisualCopy(
            SpriteRenderer source,
            string objectName,
            Vector3 scale,
            Color color,
            int sortingOrder)
        {
            GameObject copy = new GameObject(objectName);
            copy.transform.SetParent(selectionVisualsRoot, false);
            MatchSourceTransform(copy.transform, source.transform, scale);

            SpriteRenderer renderer = copy.AddComponent<SpriteRenderer>();
            renderer.sprite = source.sprite;
            renderer.color = color;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = false;
            return renderer;
        }

        private void MatchSourceTransform(Transform copy, Transform source, Vector3 expansionScale)
        {
            copy.localPosition = selectionVisualsRoot.InverseTransformPoint(source.position);
            copy.localRotation = Quaternion.Inverse(selectionVisualsRoot.rotation) * source.rotation;

            Vector3 sourceWorldScale = source.lossyScale;
            Vector3 rootWorldScale = selectionVisualsRoot.lossyScale;
            copy.localScale = new Vector3(
                sourceWorldScale.x * expansionScale.x / Mathf.Max(rootWorldScale.x, .001f),
                sourceWorldScale.y * expansionScale.y / Mathf.Max(rootWorldScale.y, .001f),
                sourceWorldScale.z / Mathf.Max(rootWorldScale.z, .001f));
        }

        private static Vector3 InsetScale(SpriteRenderer source, float worldInsetPerSide)
        {
            float width = Mathf.Max(source.transform.lossyScale.x, .001f);
            float height = Mathf.Max(source.transform.lossyScale.y, .001f);

            return new Vector3(
                Mathf.Max(.1f, 1f - (worldInsetPerSide * 2f / width)),
                Mathf.Max(.1f, 1f - (worldInsetPerSide * 2f / height)),
                1f);
        }
    }
}
