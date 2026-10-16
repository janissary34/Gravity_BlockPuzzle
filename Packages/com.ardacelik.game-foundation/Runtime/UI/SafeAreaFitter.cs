using UnityEngine;

namespace Arda.GameFoundation.UI
{
    /// <summary>
    /// Applies the current mobile safe area to a top-level UI RectTransform.
    /// It has no camera or bootstrap dependency and re-applies only when the
    /// screen geometry changes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform targetRect;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;
        private ScreenOrientation lastOrientation = ScreenOrientation.Unknown;

        private void Awake()
        {
            targetRect = GetComponent<RectTransform>();
            ApplySafeArea();
        }

        private void OnEnable()
        {
            ApplySafeArea();
        }

        private void Update()
        {
            if (Screen.safeArea != lastSafeArea ||
                Screen.width != lastScreenSize.x ||
                Screen.height != lastScreenSize.y ||
                Screen.orientation != lastOrientation)
            {
                ApplySafeArea();
            }
        }

        public void ApplySafeArea()
        {
            if (targetRect == null || Screen.width <= 0 || Screen.height <= 0)
                return;

            Rect safeArea = Screen.safeArea;
            lastSafeArea = safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            lastOrientation = Screen.orientation;

            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;
            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            targetRect.anchorMin = anchorMin;
            targetRect.anchorMax = anchorMax;
            targetRect.offsetMin = Vector2.zero;
            targetRect.offsetMax = Vector2.zero;
        }
    }
}
