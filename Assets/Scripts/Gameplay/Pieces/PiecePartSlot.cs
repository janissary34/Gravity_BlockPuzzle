using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public sealed class PiecePartSlot : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private BoxCollider2D collision;

        public SpriteRenderer Visual => visual;
        public BoxCollider2D Collision => collision;

        public void ResetSlot()
        {
            if (visual != null)
                visual.enabled = false;
            if (collision != null)
                collision.enabled = false;
            transform.localPosition = Vector3.zero;
            transform.localScale = Vector3.one;
            if (collision != null)
            {
                collision.transform.localPosition = Vector3.zero;
                collision.transform.localScale = Vector3.one;
            }
        }

#if UNITY_EDITOR
        public void ConfigureForAuthoring(SpriteRenderer slotVisual, BoxCollider2D slotCollision)
        {
            visual = slotVisual;
            collision = slotCollision;
        }
#endif
    }
}
