using GravityPuzzle.Config;
using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle
{
    /// <summary>
    /// Pooled static guides plus a hidden throat shelf for the short-lived
    /// shredder physics island. The shelf sits strictly below the coordinate
    /// capture plane: it catches only a body that has already become eligible
    /// for its own feed, never a global admission queue.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class ShredderPhysicsMouth : MonoBehaviour, IPoolable
    {
        [SerializeField] private Rigidbody2D staticBody;
        [SerializeField] private BoxCollider2D leftGuide;
        [SerializeField] private BoxCollider2D rightGuide;
        [SerializeField] private BoxCollider2D throatShelf;

        public float GuideDepth { get; private set; }

        private void Awake()
        {
            if (staticBody == null)
                staticBody = GetComponent<Rigidbody2D>();
        }

        /// <summary>
        /// Configures a complete, collision-safe mouth. A physics island must
        /// never run with only part of its containment geometry available.
        /// </summary>
        public bool Configure(
            float leftEdge,
            float rightEdge,
            float cutterY,
            ShredderConfig config,
            float requiredGuideDepth)
        {
            if (config == null || staticBody == null || leftGuide == null ||
                rightGuide == null || throatShelf == null)
                return false;

            float thickness = config.PhysicsIslandGuideThickness;
            // The configured depth is a designer minimum. The actual support
            // chain can be taller, and its lateral containment must extend to
            // the highest body that starts in this short-lived physics island.
            float depth = Mathf.Max(config.PhysicsIslandGuideDepth, requiredGuideDepth);
            float clearance = config.PhysicsIslandGuideClearance;
            float guideCenterY = cutterY + depth * .5f;

            transform.position = Vector3.zero;
            if (staticBody != null)
            {
                staticBody.bodyType = RigidbodyType2D.Static;
                staticBody.simulated = true;
            }

            ConfigureGuide(
                leftGuide,
                leftEdge - clearance - thickness * .5f,
                guideCenterY,
                thickness,
                depth,
                config.FeedPhysicsMaterial);
            ConfigureGuide(
                rightGuide,
                rightEdge + clearance + thickness * .5f,
                guideCenterY,
                thickness,
                depth,
                config.FeedPhysicsMaterial);
            ConfigureGuide(
                throatShelf,
                (leftEdge + rightEdge) * .5f,
                cutterY - thickness * .5f,
                Mathf.Max(.01f, rightEdge - leftEdge + clearance * 2f),
                thickness,
                config.FeedPhysicsMaterial);
            // The shelf is a presentation containment fallback only. Its top
            // is constructed at cutterY, while dynamic members become eligible
            // at cutterY + PhysicsIslandCaptureDistance, so it can never be a
            // gameplay admission barrier.
            if (config.PhysicsIslandCaptureDistance <= 0f)
            {
                Debug.LogError(
                    "[ShredderPhysicsMouth] The coordinate capture plane must remain above the throat shelf.",
                    this);
                return false;
            }
            GuideDepth = depth;
            Physics2D.SyncTransforms();
            return true;
        }

        private static void ConfigureGuide(
            BoxCollider2D guide,
            float x,
            float y,
            float thickness,
            float depth,
            PhysicsMaterial2D material)
        {
            if (guide == null)
                return;

            guide.transform.localPosition = new Vector3(x, y, 0f);
            guide.transform.localRotation = Quaternion.identity;
            guide.size = new Vector2(thickness, depth);
            guide.isTrigger = false;
            guide.sharedMaterial = material;
            guide.enabled = true;
            if (ShredderPhysicsLayers.IsAvailable)
                guide.gameObject.layer = ShredderPhysicsLayers.GuideLayer;
        }

        public void OnSpawn()
        {
            GuideDepth = 0f;
            if (leftGuide != null)
                leftGuide.enabled = false;
            if (rightGuide != null)
                rightGuide.enabled = false;
            if (throatShelf != null)
                throatShelf.enabled = false;
        }

        public void OnDespawn()
        {
            GuideDepth = 0f;
            if (leftGuide != null)
                leftGuide.enabled = false;
            if (rightGuide != null)
                rightGuide.enabled = false;
            if (throatShelf != null)
                throatShelf.enabled = false;
            if (staticBody != null)
                staticBody.simulated = false;
        }
    }
}
