using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// One pooled, world-space rectangle used to show a Hammer-selectable
    /// runtime cell. The prefab must contain this component and a LineRenderer.
    /// Corners on the piece silhouette are rounded to follow the brick art;
    /// corners shared with neighbouring cells stay square.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class HammerTargetCellOutlineView : MonoBehaviour
    {
        [System.Flags]
        public enum RoundedCorners
        {
            None = 0,
            BottomLeft = 1 << 0,
            TopLeft = 1 << 1,
            TopRight = 1 << 2,
            BottomRight = 1 << 3,
        }

        private const int CornerCount = 4;

        [SerializeField, Min(.001f)] private float lineWidth = .045f;
        [SerializeField] private Color lineColor = Color.white;
        [SerializeField] private int sortingOrderOffset = 2;
        [Tooltip("World-space radius of the brick art's outer corners. Applied only to corners on the piece silhouette.")]
        [SerializeField, Min(0f)] private float cornerRadius = .18f;
        [SerializeField, Range(1, 16)] private int cornerSegments = 6;

        private LineRenderer line;

        private static Material defaultOutlineMaterial;
        public static Material GetDefaultOutlineMaterial()
        {
            if (defaultOutlineMaterial != null)
                return defaultOutlineMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
                defaultOutlineMaterial = new Material(shader) { name = "Hammer Outline Material" };

            return defaultOutlineMaterial;
        }

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            if (line != null && line.sharedMaterial != null && defaultOutlineMaterial == null)
            {
                defaultOutlineMaterial = line.sharedMaterial;
            }
            if (line != null)
            {
                line.useWorldSpace = true;
                line.loop = true;
                line.positionCount = CornerCount;
                line.enabled = false;
            }
        }

        public void Show(
            PuzzlePiece.TargetableCell cell,
            RoundedCorners roundedCorners,
            Material sharedMaterial,
            int baseSortingOrder)
        {
            if (line == null)
                line = GetComponent<LineRenderer>();

            if (sharedMaterial == null)
                sharedMaterial = GetDefaultOutlineMaterial();

            if (line == null || sharedMaterial == null)
            {
                gameObject.SetActive(false);
                return;
            }

            line.sharedMaterial = sharedMaterial;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.startColor = lineColor;
            line.endColor = lineColor;
            line.sortingOrder = baseSortingOrder + sortingOrderOffset;
            line.loop = true;
            WriteOutlinePositions(cell, roundedCorners);
            line.enabled = true;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (line != null)
                line.enabled = false;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Writes a clockwise loop starting at the bottom-left corner. Each
        /// rounded corner contributes an arc; square corners a single point.
        /// </summary>
        private void WriteOutlinePositions(PuzzlePiece.TargetableCell cell, RoundedCorners roundedCorners)
        {
            const float z = -0.02f;
            float halfWidth = cell.Size.x * .5f;
            float halfHeight = cell.Size.y * .5f;
            float radius = Mathf.Min(cornerRadius, Mathf.Min(halfWidth, halfHeight));
            int segments = Mathf.Max(1, cornerSegments);

            int count = 0;
            for (int corner = 0; corner < CornerCount; corner++)
                count += IsRounded(roundedCorners, corner, radius) ? segments + 1 : 1;
            line.positionCount = count;

            Vector2 center = cell.Center;
            int index = 0;
            for (int corner = 0; corner < CornerCount; corner++)
            {
                // Corner order: BL, TL, TR, BR. Sign of the outward direction
                // on each axis for that corner.
                float signX = corner < 2 ? -1f : 1f;
                float signY = corner == 0 || corner == 3 ? -1f : 1f;
                Vector2 point = new Vector2(
                    center.x + signX * halfWidth,
                    center.y + signY * halfHeight);

                if (!IsRounded(roundedCorners, corner, radius))
                {
                    line.SetPosition(index++, new Vector3(point.x, point.y, z));
                    continue;
                }

                Vector2 arcCenter = new Vector2(point.x - signX * radius, point.y - signY * radius);
                // Clockwise traversal: BL starts pointing down (270°), each
                // following corner starts 90° earlier.
                float startAngle = (270f - 90f * corner) * Mathf.Deg2Rad;
                for (int segment = 0; segment <= segments; segment++)
                {
                    float angle = startAngle - (Mathf.PI * .5f) * segment / segments;
                    line.SetPosition(index++, new Vector3(
                        arcCenter.x + Mathf.Cos(angle) * radius,
                        arcCenter.y + Mathf.Sin(angle) * radius,
                        z));
                }
            }
        }

        private static bool IsRounded(RoundedCorners roundedCorners, int corner, float radius)
        {
            return radius > 0f && (roundedCorners & (RoundedCorners)(1 << corner)) != 0;
        }
    }
}
