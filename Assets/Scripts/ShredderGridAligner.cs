using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle
{
    /// <summary>
    /// Reusable component that automatically aligns and scales Shredder GameObjects 
    /// centered directly underneath grid columns at the bottom of a level.
    /// Runs seamlessly in Edit Mode [ExecuteAlways] or via ContextMenu button.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class ShredderGridAligner : MonoBehaviour
    {
        [Header("Grid Configuration")]
        [SerializeField, Tooltip("Transform representing the center of the grid map. Defaults to this transform if unassigned.")]
        private Transform gridTransform;

        [SerializeField, Tooltip("Total number of columns across the grid (e.g. 8 columns).")]
        private int columnCount = 8;

        [SerializeField, Tooltip("Width of each grid column/cell in world units.")]
        private float columnWidth = 1.0f;

        [Header("Shredder Elements")]
        [SerializeField, Tooltip("List of pre-placed Shredder Transforms to align. If empty, the script automatically uses child transforms.")]
        private List<Transform> shredderTransforms = new List<Transform>();

        [SerializeField, Tooltip("Optional Shredder Prefab to instantiate if the list count is less than columnCount.")]
        private GameObject shredderPrefab;

        [Header("Position & Scaling Adjustments")]
        [SerializeField, Tooltip("Vertical offset (Y) relative to the grid center/bottom.")]
        private float yOffset = -4.0f;

        [SerializeField, Tooltip("If true, automatically resizes each shredder to fit the column width.")]
        private bool autoScaleShredders = true;

        [SerializeField, Tooltip("Scale multiplier for shredder size relative to column width (1.0 = exact column diameter).")]
        private float scaleMultiplier = 1.0f;

        [Header("Editor Live Preview")]
        [SerializeField, Tooltip("If true, automatically updates shredder alignment in Scene view when Inspector values change.")]
        private bool autoAlignInEditor = true;

        private void OnValidate()
        {
            if (autoAlignInEditor && !Application.isPlaying)
            {
                AlignShredders();
            }
        }

        private void Awake()
        {
            AlignShredders();
        }

        /// <summary>
        /// Align and fit all shredder elements directly underneath the grid columns.
        /// Right-click the component header in the Inspector and select 'Align Shredders' to run manually.
        /// </summary>
        [ContextMenu("Align Shredders")]
        public void AlignShredders()
        {
            if (columnCount <= 0 || columnWidth <= 0f)
                return;

            Transform grid = gridTransform != null ? gridTransform : transform;
            Vector3 gridCenter = grid.position;

            // Auto-collect child transforms if the list is unassigned or empty
            if (shredderTransforms == null || shredderTransforms.Count == 0)
            {
                CollectChildShredders();
            }

            // Spawn missing shredders if prefab is assigned
            if (shredderPrefab != null && shredderTransforms.Count < columnCount)
            {
                int needed = columnCount - shredderTransforms.Count;
                for (int i = 0; i < needed; i++)
                {
                    GameObject spawned = Instantiate(shredderPrefab, transform);
                    spawned.name = $"Shredder_{shredderTransforms.Count + 1}";
                    shredderTransforms.Add(spawned.transform);
                }
            }

            if (shredderTransforms.Count == 0)
                return;

            // Calculate starting X so column centers are symmetrically aligned relative to grid X
            float totalWidth = columnCount * columnWidth;
            float startX = gridCenter.x - (totalWidth * 0.5f) + (columnWidth * 0.5f);
            float targetY = gridCenter.y + yOffset;

            int limit = Mathf.Min(columnCount, shredderTransforms.Count);

            for (int i = 0; i < limit; i++)
            {
                Transform shredder = shredderTransforms[i];
                if (shredder == null) continue;

                // Position X directly centered under column i
                float posX = startX + (i * columnWidth);
                shredder.position = new Vector3(posX, targetY, shredder.position.z);

                // Dynamically fit shredder scale to column width
                if (autoScaleShredders)
                {
                    float targetScale = columnWidth * scaleMultiplier;
                    shredder.localScale = new Vector3(targetScale, targetScale, shredder.localScale.z);
                }
            }
        }

        /// <summary>
        /// Automatically populates the shredder list from direct child transforms.
        /// </summary>
        private void CollectChildShredders()
        {
            shredderTransforms.Clear();
            foreach (Transform child in transform)
            {
                if (child != null && child != transform)
                {
                    shredderTransforms.Add(child);
                }
            }
        }
    }
}
