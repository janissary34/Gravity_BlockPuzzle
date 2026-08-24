using UnityEngine;

namespace GravityPuzzle
{
    /// <summary>
    /// Automatically adjusts RectTransform anchors to fit within mobile Screen.safeArea cutouts (notches/dynamic island).
    /// Attach this component to a top-level UI Container panel inside your Canvas.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class UISafeArea : MonoBehaviour
    {
        private RectTransform rectTransform;
        private Rect lastSafeArea = new Rect(0, 0, 0, 0);
        private Vector2Int lastScreenSize = new Vector2Int(0, 0);
        private ScreenOrientation lastOrientation = ScreenOrientation.Unknown;

        private void Awake()
        {
            CacheReferences();
            ApplySafeArea();
        }

        private void OnEnable()
        {
            CacheReferences();
            ApplySafeArea();
        }

        private void Update()
        {
            if (HasScreenOrSafeAreaChanged())
            {
                ApplySafeArea();
            }
        }

        private bool HasScreenOrSafeAreaChanged()
        {
            return lastSafeArea != Screen.safeArea ||
                   lastScreenSize.x != Screen.width ||
                   lastScreenSize.y != Screen.height ||
                   lastOrientation != Screen.orientation;
        }

        public void ApplySafeArea()
        {
            if (rectTransform == null)
                return;

            Rect safeArea = Screen.safeArea;

            lastSafeArea = safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            lastOrientation = Screen.orientation;

            if (Screen.width <= 0 || Screen.height <= 0)
                return;

            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;

            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private void CacheReferences()
        {
            if (rectTransform == null)
                rectTransform = GetComponent<RectTransform>();
        }
    }
}
