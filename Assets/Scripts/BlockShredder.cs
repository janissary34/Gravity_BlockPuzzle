using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace GravityPuzzle
{
    /// <summary>
    /// Handles block shredding triggers, pre-fractured composite block voxelization,
    /// object pooling for mobile optimization, and spawning Stone debris voxels & Gem fly voxels.
    /// </summary>
    [DisallowMultipleComponent]
    public class BlockShredder : MonoBehaviour
    {
        [Header("Shredder Explosion Forces")]
        [SerializeField, Tooltip("Base explosion impulse force applied to shattered voxels.")]
        private float shredExplosionForce = 3.8f;

        [SerializeField, Tooltip("Primary direction vector for shredder blade ejection force (e.g., Upward/Outward).")]
        private Vector2 shredForceDirection = new Vector2(0f, 1.2f);

        [SerializeField, Tooltip("Random spread angle for voxel ejection (degrees).")]
        private float ejectionSpreadAngle = 60f;

        [Header("Voxel Visuals")]
        [SerializeField, Tooltip("Fraction of voxels that are Gems (0.0 to 1.0).")]
        [Range(0.0f, 1.0f)]
        private float gemRatio = 0.25f;

        [Header("UI Slider Attraction Target")]
        [SerializeField, Tooltip("Top UI Slider reference to fill when Gems arrive.")]
        private Slider targetUISlider;

        [SerializeField, Tooltip("Specific RectTransform target for Gem attraction (defaults to slider handle/rect if null).")]
        private RectTransform targetUIRectTransform;

        [SerializeField, Tooltip("Camera used for Screen/World conversion (defaults to Camera.main).")]
        private Camera targetCamera;

        [Header("Gem Fly Settings")]
        [SerializeField, Tooltip("Duration of Gem flight from shredder to UI Target.")]
        private float gemFlyDuration = 0.75f;

        [SerializeField, Tooltip("DOTween Ease curve for Gem flight to UI.")]
        private Ease gemFlyEase = Ease.InBack;

        public static int ActiveGemFlightCount { get; private set; }
        public static bool HasActiveGemFlights => ActiveGemFlightCount > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGemFlightCount()
        {
            ActiveGemFlightCount = 0;
        }

        private void Start()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetUIRectTransform == null && targetUISlider != null)
                targetUIRectTransform = targetUISlider.GetComponent<RectTransform>();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryShredBlock(other, transform.position);
        }

        /// <summary>
        /// Attempts to shred an entering PuzzlePiece into Stone and Gem voxels.
        /// </summary>
        public void TryShredBlock(Collider2D targetCollider, Vector2 shredderCenter)
        {
            PuzzlePiece piece = targetCollider.GetComponentInParent<PuzzlePiece>();
            if (piece == null || !piece.TryBeginShredding())
                return;

            Vector2 contactPoint = targetCollider.ClosestPoint(shredderCenter);

            // Fetch all voxel shards from the modular block
            VoxelShard[] shards = piece.GetComponentsInChildren<VoxelShard>(true);
            if (shards.Length > 0)
            {
                int gemCount = Mathf.RoundToInt(shards.Length * gemRatio);
                List<VoxelShard> shardList = new List<VoxelShard>(shards);
                
                // Shuffle list to randomize which ones become gems
                for (int i = 0; i < shardList.Count; i++)
                {
                    VoxelShard temp = shardList[i];
                    int randomIndex = UnityEngine.Random.Range(i, shardList.Count);
                    shardList[i] = shardList[randomIndex];
                    shardList[randomIndex] = temp;
                }

                for (int i = 0; i < shardList.Count; i++)
                {
                    bool isGem = i < gemCount;
                    if (isGem) ActiveGemFlightCount++;

                    Vector2 forceDir = CalculateEjectionVector();
                    Vector2 ejectionForce = forceDir * shredExplosionForce * (isGem ? 0.6f : UnityEngine.Random.Range(0.7f, 1.3f));
                    
                    shardList[i].TriggerShred(
                        contactPoint, 
                        ejectionForce, 
                        isGem, 
                        RecycleGemVoxel,
                        targetUIRectTransform,
                        targetUISlider,
                        targetCamera,
                        gemFlyDuration,
                        gemFlyEase
                    );
                }
            }

            // Destroy original block GameObject (now empty of voxels)
            Destroy(piece.gameObject);
        }

        private Vector2 CalculateEjectionVector()
        {
            float randomAngle = Random.Range(-ejectionSpreadAngle * 0.5f, ejectionSpreadAngle * 0.5f);
            Quaternion rotation = Quaternion.Euler(0, 0, randomAngle);
            Vector2 baseDir = shredForceDirection.sqrMagnitude > 0.01f ? shredForceDirection.normalized : Vector2.up;
            return rotation * baseDir;
        }

        private void RecycleGemVoxel(GemFlyToUI gem)
        {
            ActiveGemFlightCount = Mathf.Max(0, ActiveGemFlightCount - 1);
        }

        /// <summary>
        /// Allows dynamically assigning the target UI Slider at runtime.
        /// </summary>
        public void SetTargetUISlider(Slider slider, RectTransform targetRect = null)
        {
            targetUISlider = slider;
            targetUIRectTransform = targetRect != null ? targetRect : (slider != null ? slider.GetComponent<RectTransform>() : null);
        }
    }
}
