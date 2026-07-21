using UnityEngine;
using System.Collections.Generic;

namespace GravityPuzzle
{
    /// <summary>
    /// Identifies the root of one complete movable puzzle piece.
    /// Its child colliders form the body and the smaller hook geometry.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PuzzlePiece : MonoBehaviour
    {
        public Rigidbody2D Body { get; private set; }
        public bool IsSelected => isSelected;
        public bool IsBeingShredded => beingShredded;
        public float CurrentCollisionInset
        {
            get
            {
                if (collisionGeometryRoot == null)
                    return 0f;

                return isSelected
                    ? GravityGridMetrics.DraggingPieceCollisionSkinInCells
                    : GravityGridMetrics.RestingPieceCollisionSkinInCells;
            }
        }

        private readonly List<SpriteRenderer> normalRenderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> selectedRenderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> outlineRenderers = new List<SpriteRenderer>();
        private Transform selectionVisualsRoot;
        private Transform collisionGeometryRoot;
        private CompositeCollider2D compositeCollider;
        private Vector3 restingCollisionScale = Vector3.one;
        private Vector3 draggingCollisionScale = Vector3.one;
        private bool selectionVisualsBuilt;
        private bool beingShredded;
        private bool isSelected;

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
        }

        private void Start()
        {
            BuildSelectionVisuals();
        }

        public void SetSelected(bool isSelected)
        {
            this.isSelected = isSelected;

            ApplyCollisionProfile(isSelected);

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
            Transform collisionRoot,
            CompositeCollider2D composite,
            Vector2 restingScale,
            Vector2 draggingScale)
        {
            collisionGeometryRoot = collisionRoot;
            compositeCollider = composite;
            restingCollisionScale = new Vector3(restingScale.x, restingScale.y, 1f);
            draggingCollisionScale = new Vector3(draggingScale.x, draggingScale.y, 1f);
            ApplyCollisionProfile(isSelected);
        }

        private void ApplyCollisionProfile(bool dragging)
        {
            if (collisionGeometryRoot == null)
                return;

            collisionGeometryRoot.localScale = dragging
                ? draggingCollisionScale
                : restingCollisionScale;
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
            return true;
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
                if (original.transform.IsChildOf(selectionVisualsRoot))
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
