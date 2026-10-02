using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Scene-authored world marker for the one board target required by a
    /// first-use lesson. It is visual-only and is deliberately rendered above
    /// the board dimmer so the required target remains legible.
    /// </summary>
    public sealed class TutorialTargetIndicatorView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer brightnessOverlay;
        [SerializeField] private SpriteRenderer horizontalSight;
        [SerializeField] private SpriteRenderer verticalSight;
        [SerializeField, Range(0f, 1f)] private float sightLengthFraction = .78f;
        [SerializeField, Min(.01f)] private float sightWidth = .055f;
        [Tooltip("Keeps a fine-grid Hammer target readable on phone screens without changing its actual tap area.")]
        [SerializeField, Min(.05f)] private float minimumVisualTargetSize = .72f;

        public void Show(Vector2 center, Vector2 size, int sortingOrder)
        {
            float safeWidth = Mathf.Max(minimumVisualTargetSize, size.x);
            float safeHeight = Mathf.Max(minimumVisualTargetSize, size.y);
            float diameter = Mathf.Min(safeWidth, safeHeight) * sightLengthFraction;

            transform.position = new Vector3(center.x, center.y, 0f);
            transform.localScale = Vector3.one;

            SetRenderer(brightnessOverlay, new Vector2(safeWidth, safeHeight), sortingOrder);
            SetRenderer(horizontalSight, new Vector2(diameter, sightWidth), sortingOrder + 1);
            SetRenderer(verticalSight, new Vector2(sightWidth, diameter), sortingOrder + 1);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private static void SetRenderer(SpriteRenderer renderer, Vector2 scale, int sortingOrder)
        {
            if (renderer == null)
                return;

            renderer.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = true;
        }
    }
}
