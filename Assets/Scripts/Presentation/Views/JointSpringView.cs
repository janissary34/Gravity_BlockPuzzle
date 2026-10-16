using System;
using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    public sealed class JointSpringView : MonoBehaviour, IPoolable, IPoolReturnReceiver<JointSpringView>
    {
        [SerializeField] private SpriteRenderer spriteRenderer;

        private Action<JointSpringView> returnHandler;
        public int JointId { get; private set; }
        public int MemberA { get; private set; }
        public int MemberB { get; private set; }

        public void SetPoolReturnHandler(Action<JointSpringView> handler)
        {
            returnHandler = handler;
        }

        public void Initialize(
            int jointId,
            int memberA,
            int memberB,
            Vector3 worldPosition,
            bool isVertical,
            Sprite springSprite = null,
            Material springMaterial = null,
            int sortingOrder = 20,
            float scale = 0.1495036f)
        {
            JointId = jointId;
            MemberA = memberA;
            MemberB = memberB;

            transform.position = worldPosition;
            transform.rotation = Quaternion.Euler(0f, 0f, isVertical ? 90f : 0f);
            transform.localScale = Vector3.one * scale;

            if (spriteRenderer != null)
            {
                if (springSprite != null)
                    spriteRenderer.sprite = springSprite;
                if (springMaterial != null)
                    spriteRenderer.material = springMaterial;
                spriteRenderer.sortingOrder = sortingOrder;
                spriteRenderer.enabled = true;
            }
        }

        public void SetSorting(int sortingLayerID, int sortingOrder)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingLayerID = sortingLayerID;
                spriteRenderer.sortingOrder = sortingOrder;
            }
        }

        public void UpdateTransform(Vector3 worldPosition, bool isVertical)
        {
            transform.position = worldPosition;
            transform.rotation = Quaternion.Euler(0f, 0f, isVertical ? 90f : 0f);
        }

        public void OnSpawn()
        {
            if (spriteRenderer != null)
                spriteRenderer.enabled = true;
        }

        public void OnDespawn()
        {
            JointId = -1;
            MemberA = -1;
            MemberB = -1;
            transform.SetParent(null, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            if (spriteRenderer != null)
                spriteRenderer.enabled = false;
        }

        public void Despawn()
        {
            returnHandler?.Invoke(this);
        }
    }
}
