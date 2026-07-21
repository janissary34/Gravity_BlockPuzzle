using UnityEngine;

namespace GravityPuzzle
{
    /// <summary>
    /// Lets a player pick up a piece and freely move it. Gravity resumes on release.
    /// The next iteration replaces the simple physics movement with our fine-grid hook solver.
    /// </summary>
    public sealed class FreeDragPiece : MonoBehaviour
    {
        private Rigidbody2D body;
        private Vector2 grabOffset;
        private bool isDragging;

        // Hook pieces use several child colliders. They must all control the ONE
        // Rigidbody2D on the parent, otherwise the L shape breaks into separate bars.
        private void Awake()
        {
            body = GetComponentInParent<Rigidbody2D>();

            if (body == null)
                Debug.LogError($"{name} needs a Rigidbody2D on its parent hook piece.");
        }

        private void OnMouseDown()
        {
            if (body == null)
                return;

            Vector2 pointer = PointerWorldPosition();
            grabOffset = body.position - pointer;
            isDragging = true;
            body.velocity = Vector2.zero;
            body.bodyType = RigidbodyType2D.Kinematic;
        }

        private void OnMouseDrag()
        {
            if (isDragging)
                body.MovePosition(PointerWorldPosition() + grabOffset);
        }

        private void OnMouseUp()
        {
            isDragging = false;
            body.bodyType = RigidbodyType2D.Dynamic;
        }

        private static Vector2 PointerWorldPosition()
        {
            Vector3 screenPoint = Input.mousePosition;
            screenPoint.z = -Camera.main.transform.position.z;
            return Camera.main.ScreenToWorldPoint(screenPoint);
        }
    }
}
