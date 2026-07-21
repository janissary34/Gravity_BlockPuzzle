using UnityEngine;

namespace GravityPuzzle
{
    [ExecuteAlways]
    public class GridVisualizer : MonoBehaviour
    {
        [Header("Grid Snapping Calibration")]
        [Tooltip("Width and Height of a single grid cell in World Units")]
        public Vector2 cellSize = new Vector2(0.25f, 0.25f);
        [Tooltip("X and Y offset to align the grid's (0,0) center with the visual background")]
        public Vector2 gridOriginOffset = new Vector2(0.125f, 0.125f);

        [Header("Visuals")]
        public Color gridColor = new Color(1f, 0f, 0f, 0.9f); // Bright Red!
        public int gridRadiusX = 15;
        public int gridRadiusY = 20;
        public bool showGizmos = true;

        private void OnEnable()
        {
            RefreshVisuals();
        }

        private void OnValidate()
        {
            RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            if (cellSize.x <= 0f || cellSize.y <= 0f) return;

            // Clear old dots
            while (transform.childCount > 0)
            {
                DestroyImmediate(transform.GetChild(0).gameObject);
            }

            if (!showGizmos) return;

            // Create a simple sprite if we don't have one
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            Sprite dotSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);

            for (int x = -gridRadiusX; x <= gridRadiusX; x++)
            {
                for (int y = -gridRadiusY; y <= gridRadiusY; y++)
                {
                    Vector3 worldPos = new Vector3(
                        gridOriginOffset.x + x * cellSize.x,
                        gridOriginOffset.y + y * cellSize.y,
                        0f
                    );

                    GameObject dot = new GameObject($"Dot_{x}_{y}");
                    dot.transform.SetParent(transform);
                    dot.transform.position = worldPos;
                    // Make the dot small (e.g. 0.05 world units)
                    dot.transform.localScale = new Vector3(0.08f, 0.08f, 1f);

                    SpriteRenderer sr = dot.AddComponent<SpriteRenderer>();
                    sr.sprite = dotSprite;
                    sr.color = gridColor;
                    sr.sortingOrder = 999; // Ensure it renders on top
                }
            }
        }

    }
}
