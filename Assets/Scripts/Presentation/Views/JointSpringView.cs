using System;
using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;
using DG.Tweening;

namespace GravityPuzzle.Presentation.Views
{
    public sealed class JointSpringView : MonoBehaviour, IPoolable, IPoolReturnReceiver<JointSpringView>
    {
        [SerializeField] private SpriteRenderer spriteRenderer;

        private Action<JointSpringView> returnHandler;
        private Action<JointSpringView> releaseCompleted;
        private Sequence releaseTween;
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
            {
                spriteRenderer.color = Color.white;
                spriteRenderer.enabled = true;
            }
        }

        public void OnDespawn()
        {
            if (releaseTween != null)
            {
                releaseTween.Kill();
                releaseTween = null;
            }
            releaseCompleted = null;
            JointId = -1;
            MemberA = -1;
            MemberB = -1;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = Color.white;
                spriteRenderer.enabled = false;
            }
        }

        public void PlayShredderRelease(Transform parent, float duration, float travel, float lift,
            float rotation, Action<JointSpringView> completed)
        {
            transform.SetParent(parent, true);
            releaseCompleted = completed;
            duration = Mathf.Max(.01f, duration);
            // Joint keys distinguish the two springs in a seam. Alternate the
            // kick and flick deterministically without a random allocation or
            // depending on the sprite's horizontal/vertical authored rotation.
            float direction = (JointId & 1) == 0 ? -1f : 1f;
            float variation = (JointId & 2) == 0 ? .85f : 1f;
            Vector3 position = transform.position;
            Vector3 angles = transform.eulerAngles;
            releaseTween = DOTween.Sequence().SetLink(gameObject, LinkBehaviour.KillOnDisable).SetAutoKill(true);
            releaseTween.Join(transform.DOMove(
                position + new Vector3(direction * travel * variation, lift * variation, 0f), duration)
                .SetEase(Ease.OutQuad));
            releaseTween.Join(transform.DORotate(
                angles + new Vector3(0f, 0f, -direction * rotation * variation), duration, RotateMode.FastBeyond360)
                .SetEase(Ease.OutQuad));
            if (spriteRenderer != null)
                releaseTween.Insert(duration * .25f,
                    spriteRenderer.DOFade(0f, duration * .75f).SetEase(Ease.InQuad));
            releaseTween.OnComplete(CompleteRelease);
        }

        private void CompleteRelease()
        {
            releaseTween = null;
            Action<JointSpringView> completed = releaseCompleted;
            releaseCompleted = null;
            completed?.Invoke(this);
            Despawn();
        }

        public void Despawn()
        {
            returnHandler?.Invoke(this);
        }
    }
}
