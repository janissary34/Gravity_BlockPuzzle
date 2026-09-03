using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// One pooled, world-space rectangle used to show a Hammer-selectable
    /// runtime cell. The prefab must contain this component and a LineRenderer.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class HammerTargetCellOutlineView : MonoBehaviour
    {
        [SerializeField, Min(.001f)] private float lineWidth = .045f;
        [SerializeField] private Color lineColor = Color.white;
        [SerializeField] private int sortingOrderOffset = 2;

        private LineRenderer line;

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 5;
            line.enabled = false;
        }

        public void Show(
            PuzzlePiece.TargetableCell cell,
            Material sharedMaterial,
            int baseSortingOrder)
        {
            if (line == null || sharedMaterial == null)
            {
                gameObject.SetActive(false);
                return;
            }

            float halfWidth = cell.Size.x * .5f;
            float halfHeight = cell.Size.y * .5f;
            Vector2 center = cell.Center;
            line.sharedMaterial = sharedMaterial;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.startColor = lineColor;
            line.endColor = lineColor;
            line.sortingOrder = baseSortingOrder + sortingOrderOffset;
            line.SetPosition(0, new Vector3(center.x - halfWidth, center.y - halfHeight, 0f));
            line.SetPosition(1, new Vector3(center.x - halfWidth, center.y + halfHeight, 0f));
            line.SetPosition(2, new Vector3(center.x + halfWidth, center.y + halfHeight, 0f));
            line.SetPosition(3, new Vector3(center.x + halfWidth, center.y - halfHeight, 0f));
            line.SetPosition(4, new Vector3(center.x - halfWidth, center.y - halfHeight, 0f));
            line.enabled = true;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (line != null)
                line.enabled = false;
            gameObject.SetActive(false);
        }
    }
}
