using DG.Tweening;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Presentation-only owner of a piece's Key or Lock icon and of a Key's
    /// flight into a Lock. Renderers are borrowed from the owning piece's
    /// pooled presentation slots and fully reset before they are returned.
    /// The owning piece decides the Key/Lock gameplay state; this class only
    /// reports when the arrival presentation has finished.
    /// </summary>
    public sealed class KeyLockBadgePresentation
    {
        private readonly Transform slotRoot;
        private SpriteRenderer badgeRenderer;
        private SpriteRenderer flightRenderer;
        private Sequence flightSequence;
        private Sequence breakSequence;
        private Vector3 lastBadgeWorldPosition;
        private bool hasBadgePosition;

        public KeyLockBadgePresentation(Transform slotRoot)
        {
            this.slotRoot = slotRoot;
        }

        public bool HasBadge => badgeRenderer != null;
        public bool IsKeyFlightPlaying => flightSequence != null && flightSequence.IsActive();

        public void SetSortingOrder(int sortingOrder)
        {
            if (badgeRenderer != null)
                badgeRenderer.sortingOrder = sortingOrder;
            if (flightRenderer != null)
                flightRenderer.sortingOrder = sortingOrder;
        }

        /// <summary>
        /// Returns the live badge centre, or its last centre after it was
        /// cleared, so a Key destroyed by its final Hammer hit can still fly.
        /// </summary>
        public bool TryGetBadgeWorldPosition(out Vector3 position)
        {
            if (badgeRenderer != null)
                lastBadgeWorldPosition = badgeRenderer.transform.position;

            position = lastBadgeWorldPosition;
            return hasBadgePosition;
        }

        public bool Owns(SpriteRenderer renderer)
        {
            return renderer != null && (renderer == badgeRenderer || renderer == flightRenderer);
        }

        public void ShowBadge(
            SpriteRenderer slot,
            Transform owner,
            Sprite sprite,
            Material material,
            Color color,
            Vector3 worldPosition,
            Vector3 localScale,
            int sortingLayerId,
            int sortingOrder,
            string objectName)
        {
            ClearBadge();
            if (slot == null || owner == null || sprite == null)
                return;

            ConfigureSlot(slot, owner, sprite, material, color, sortingLayerId, sortingOrder, objectName);
            slot.transform.position = worldPosition;
            slot.transform.localScale = localScale;
            badgeRenderer = slot;
            lastBadgeWorldPosition = worldPosition;
            hasBadgePosition = true;
        }

        public void ClearBadge()
        {
            KillBreak();
            if (badgeRenderer != null)
                lastBadgeWorldPosition = badgeRenderer.transform.position;
            Release(ref badgeRenderer);
        }

        /// <summary>
        /// Plays a Key jumping from <paramref name="worldStart"/> to this
        /// piece's Lock badge, then turning inside it. <paramref name="onArrived"/>
        /// runs only if the sequence completes; a kill (pool return or Clear)
        /// never reports an arrival. Returns false when nothing can be played.
        /// </summary>
        public bool PlayKeyArrival(
            SpriteRenderer slot,
            Transform owner,
            Sprite keySprite,
            Material material,
            Color color,
            Vector3 worldStart,
            Vector3 localScale,
            TweenConfig config,
            TweenCallback onArrived)
        {
            if (slot == null || owner == null || keySprite == null || config == null || badgeRenderer == null)
                return false;

            KillFlight();
            Release(ref flightRenderer);
            ConfigureSlot(
                slot,
                owner,
                keySprite,
                material,
                color,
                badgeRenderer.sortingLayerID,
                badgeRenderer.sortingOrder + 1,
                "Key Flight");

            // The flight runs in the Lock's local space so it still lands on a
            // Lock that moves meanwhile (for example one carried by the Rocket).
            Vector3 localTarget = badgeRenderer.transform.localPosition;
            localTarget.z -= .01f;
            Vector3 localStart = owner.InverseTransformPoint(worldStart);
            localStart.z = localTarget.z;
            slot.transform.localPosition = localStart;
            slot.transform.localScale = localScale;
            flightRenderer = slot;

            float ownerScale = Mathf.Max(Mathf.Abs(owner.lossyScale.y), .0001f);
            flightSequence = DOTween.Sequence()
                .Append(slot.transform
                    .DOLocalJump(localTarget, config.KeyFlightArcHeight / ownerScale, 1, config.KeyFlightDuration)
                    .SetEase(config.KeyFlightEase))
                .Append(slot.transform.DOLocalRotate(
                    new Vector3(0f, 0f, config.KeyTurnAngle),
                    config.KeyTurnDuration,
                    RotateMode.LocalAxisAdd))
                .OnComplete(onArrived)
                .SetLink(owner.gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            return true;
        }

        /// <summary>
        /// Grows and fades the Lock badge and any arrived Key, then returns
        /// both renderers. Without a config they are returned immediately.
        /// </summary>
        public void PlayLockBreak(TweenConfig config, Transform owner)
        {
            KillFlight();
            KillBreak();
            if (config == null || owner == null || (badgeRenderer == null && flightRenderer == null))
            {
                Clear();
                return;
            }

            breakSequence = DOTween.Sequence();
            AppendBreak(breakSequence, badgeRenderer, config);
            AppendBreak(breakSequence, flightRenderer, config);
            breakSequence
                .OnComplete(Clear)
                .SetLink(owner.gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
        }

        public void Clear()
        {
            KillFlight();
            KillBreak();
            Release(ref badgeRenderer);
            Release(ref flightRenderer);
            hasBadgePosition = false;
        }

        private static void AppendBreak(Sequence sequence, SpriteRenderer renderer, TweenConfig config)
        {
            if (renderer == null)
                return;

            Transform target = renderer.transform;
            sequence.Join(target
                .DOScale(target.localScale * config.LockBreakScale, config.LockBreakDuration)
                .SetEase(config.LockBreakEase));
            sequence.Join(renderer
                .DOFade(0f, config.LockBreakDuration)
                .SetEase(config.LockBreakEase));
        }

        private static void ConfigureSlot(
            SpriteRenderer slot,
            Transform owner,
            Sprite sprite,
            Material material,
            Color color,
            int sortingLayerId,
            int sortingOrder,
            string objectName)
        {
            slot.transform.SetParent(owner, false);
            slot.transform.localRotation = Quaternion.identity;
            slot.gameObject.name = objectName;
            slot.sprite = sprite;
            if (material != null)
                slot.sharedMaterial = material;
            slot.SetPropertyBlock(null);
            slot.color = color;
            slot.flipX = false;
            slot.flipY = false;
            slot.drawMode = SpriteDrawMode.Simple;
            slot.maskInteraction = SpriteMaskInteraction.None;
            slot.sortingLayerID = sortingLayerId;
            slot.sortingOrder = sortingOrder;
            slot.enabled = true;
        }

        private void Release(ref SpriteRenderer renderer)
        {
            if (renderer == null)
                return;

            DOTween.Kill(renderer);
            DOTween.Kill(renderer.transform);
            renderer.enabled = false;
            renderer.sprite = null;
            renderer.color = Color.white;
            renderer.sharedMaterial = null;
            renderer.SetPropertyBlock(null);
            renderer.transform.SetParent(slotRoot, false);
            renderer.transform.localPosition = Vector3.zero;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = Vector3.one;
            renderer = null;
        }

        private void KillFlight()
        {
            if (flightSequence != null)
            {
                flightSequence.Kill();
                flightSequence = null;
            }
        }

        private void KillBreak()
        {
            if (breakSequence != null)
            {
                breakSequence.Kill();
                breakSequence = null;
            }
        }
    }
}
