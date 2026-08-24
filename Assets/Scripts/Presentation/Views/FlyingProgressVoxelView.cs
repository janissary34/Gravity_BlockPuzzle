using DG.Tweening;
using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>Authored UI grain used exclusively for progress-bar arrival presentation.</summary>
    [RequireComponent(typeof(RectTransform), typeof(Image))]
    public sealed class FlyingProgressVoxelView : MonoBehaviour, IPoolable
    {
        private RectTransform cachedRectTransform;
        private Image cachedImage;

        public RectTransform RectTransform => cachedRectTransform;

        private void Awake()
        {
            cachedRectTransform = GetComponent<RectTransform>();
            cachedImage = GetComponent<Image>();
        }

        public void Configure(Vector2 position, float size, Sprite sprite, Color color)
        {
            cachedRectTransform.anchorMin = new Vector2(.5f, .5f);
            cachedRectTransform.anchorMax = new Vector2(.5f, .5f);
            cachedRectTransform.sizeDelta = Vector2.one * size;
            cachedRectTransform.anchoredPosition = position;
            cachedRectTransform.localRotation = Quaternion.identity;
            cachedImage.sprite = sprite;
            cachedImage.color = color;
            cachedImage.raycastTarget = false;
        }

        public void OnSpawn()
        {
            cachedRectTransform.localScale = Vector3.one;
            cachedRectTransform.localRotation = Quaternion.identity;
        }

        public void OnDespawn()
        {
            cachedRectTransform.DOKill();
        }
    }
}
