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

        private void OnDrawGizmos()
        {
            if (!showGizmos || cellSize.x <= 0f || cellSize.y <= 0f) return;
            
            Gizmos.color = gridColor;
            
            for (int x = -gridRadiusX; x <= gridRadiusX; x++)
            {
                for (int y = -gridRadiusY; y <= gridRadiusY; y++)
                {
                    Vector3 center = new Vector3(
                        gridOriginOffset.x + x * cellSize.x,
                        gridOriginOffset.y + y * cellSize.y,
                        0f
                    );
                    
                    Gizmos.DrawSphere(center, cellSize.x * 0.15f);
                    Gizmos.DrawWireCube(center, new Vector3(cellSize.x, cellSize.y, 0f));
                }
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            OnDrawGizmos();
        }

        private Texture2D dotTexture;

        private void OnGUI()
        {
            if (!showGizmos || cellSize.x <= 0f || cellSize.y <= 0f) return;
            if (Camera.main == null) return;

            if (dotTexture == null)
            {
                dotTexture = new Texture2D(1, 1);
                dotTexture.SetPixel(0, 0, gridColor);
                dotTexture.Apply();
            }

            GUI.color = Color.white; // Texture is already colored

            for (int x = -gridRadiusX; x <= gridRadiusX; x++)
            {
                for (int y = -gridRadiusY; y <= gridRadiusY; y++)
                {
                    Vector3 worldPos = new Vector3(
                        gridOriginOffset.x + x * cellSize.x,
                        gridOriginOffset.y + y * cellSize.y,
                        0f
                    );

                    Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
                    
                    if (screenPos.z < 0) continue;

                    float guiY = Screen.height - screenPos.y;

                    // Draw a massive 12x12 red box
                    Rect rect = new Rect(screenPos.x - 6, guiY - 6, 12, 12);
                    GUI.DrawTexture(rect, dotTexture);
                }
            }
        }
    }
}
