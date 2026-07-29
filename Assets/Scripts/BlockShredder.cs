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
        [Header("Object Pool Sizes (Mobile Optimization)")]
        [SerializeField, Tooltip("Initial size of pre-allocated Stone Voxel pool.")]
        private int stonePoolSize = 60;

        [SerializeField, Tooltip("Initial size of pre-allocated Gem Voxel pool.")]
        private int gemPoolSize = 30;

        [Header("Voxel Prefabs (Optional - Auto-generates if unassigned)")]
        [SerializeField, Tooltip("Optional Stone Voxel prefab. Leave blank to auto-generate styled sprite voxels.")]
        private GameObject stoneVoxelPrefab;

        [SerializeField, Tooltip("Optional Gem Voxel prefab. Leave blank to auto-generate styled sprite voxels.")]
        private GameObject gemVoxelPrefab;

        [Header("Shredder Explosion Forces")]
        [SerializeField, Tooltip("Base explosion impulse force applied to shattered voxels.")]
        private float shredExplosionForce = 3.8f;

        [SerializeField, Tooltip("Primary direction vector for shredder blade ejection force (e.g., Upward/Outward).")]
        private Vector2 shredForceDirection = new Vector2(0f, 1.2f);

        [SerializeField, Tooltip("Random spread angle for voxel ejection (degrees).")]
        private float ejectionSpreadAngle = 60f;

        [Header("Voxel Lifetime & Visuals")]
        [SerializeField, Tooltip("Lifetime of Stone debris voxels before recycling (seconds).")]
        private float stoneLifetime = 1.2f;

        [SerializeField, Tooltip("Total voxel count generated per shredded block tile.")]
        private int voxelsPerBlock = 16;

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

        // Object pools
        private readonly Queue<GameObject> stonePool = new Queue<GameObject>();
        private readonly Queue<GemFlyToUI> gemPool = new Queue<GemFlyToUI>();
        private readonly List<ActiveStoneVoxel> activeStones = new List<ActiveStoneVoxel>();

        private Transform poolContainer;
        private static Sprite defaultVoxelSprite;

        private struct ActiveStoneVoxel
        {
            public GameObject gameObject;
            public SpriteRenderer renderer;
            public Rigidbody2D body;
            public float lifetime;
            public float elapsed;
            public Color baseColor;
        }

        private void Awake()
        {
            InitializePools();
        }

        private void Start()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetUIRectTransform == null && targetUISlider != null)
                targetUIRectTransform = targetUISlider.GetComponent<RectTransform>();
        }

        private void InitializePools()
        {
            GameObject containerGO = new GameObject($"VoxelPool_{gameObject.name}");
            poolContainer = containerGO.transform;
            DontDestroyOnLoad(containerGO);

            // Pre-allocate Stone Voxel pool
            for (int i = 0; i < stonePoolSize; i++)
            {
                GameObject stone = CreateStoneVoxelInstance();
                stone.SetActive(false);
                stonePool.Enqueue(stone);
            }

            // Pre-allocate Gem Voxel pool
            for (int i = 0; i < gemPoolSize; i++)
            {
                GemFlyToUI gem = CreateGemVoxelInstance();
                gem.gameObject.SetActive(false);
                gemPool.Enqueue(gem);
            }
        }

        private GameObject CreateStoneVoxelInstance()
        {
            GameObject stone;
            if (stoneVoxelPrefab != null)
            {
                stone = Instantiate(stoneVoxelPrefab, poolContainer);
            }
            else
            {
                stone = new GameObject("StoneVoxel");
                stone.transform.SetParent(poolContainer, false);

                SpriteRenderer sr = stone.AddComponent<SpriteRenderer>();
                sr.sprite = GetDefaultVoxelSprite();
                sr.color = new Color(0.55f, 0.58f, 0.65f);
                sr.sortingOrder = 25;

                Rigidbody2D rb = stone.AddComponent<Rigidbody2D>();
                rb.gravityScale = 1.0f;

                stone.AddComponent<BoxCollider2D>();
            }
            return stone;
        }

        private GemFlyToUI CreateGemVoxelInstance()
        {
            GameObject gemGO;
            if (gemVoxelPrefab != null)
            {
                gemGO = Instantiate(gemVoxelPrefab, poolContainer);
            }
            else
            {
                gemGO = new GameObject("GemVoxel");
                gemGO.transform.SetParent(poolContainer, false);

                SpriteRenderer sr = gemGO.AddComponent<SpriteRenderer>();
                sr.sprite = GetDefaultVoxelSprite();
                sr.color = new Color(0.18f, 0.85f, 1.0f); // Radiant Cyan Gem color
                sr.sortingOrder = 30;

                Rigidbody2D rb = gemGO.AddComponent<Rigidbody2D>();
                rb.gravityScale = 1.0f;

                gemGO.AddComponent<BoxCollider2D>();
            }

            GemFlyToUI gemComponent = gemGO.GetComponent<GemFlyToUI>();
            if (gemComponent == null)
            {
                gemComponent = gemGO.AddComponent<GemFlyToUI>();
            }
            return gemComponent;
        }

        private static Sprite GetDefaultVoxelSprite()
        {
            if (defaultVoxelSprite == null)
            {
                Texture2D tex = new Texture2D(16, 16);
                Color[] pixels = new Color[16 * 16];
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        bool border = (x == 0 || x == 15 || y == 0 || y == 15);
                        pixels[y * 16 + x] = border ? new Color(1f, 1f, 1f, 0.85f) : Color.white;
                    }
                }
                tex.SetPixels(pixels);
                tex.Apply();
                defaultVoxelSprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16);
            }
            return defaultVoxelSprite;
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
            Color pieceColor = ExtractPieceColor(piece);

            // Shred block into voxel grid at impact zone
            ShatterIntoVoxels(contactPoint, pieceColor);

            // Destroy original block GameObject
            Destroy(piece.gameObject);
        }

        private Color ExtractPieceColor(PuzzlePiece piece)
        {
            SpriteRenderer[] renderers = piece.GetComponentsInChildren<SpriteRenderer>();
            foreach (SpriteRenderer r in renderers)
            {
                if (r.gameObject.name.StartsWith("Selected Fill") ||
                    r.gameObject.name.StartsWith("White Selection Outline") ||
                    r.gameObject.name.StartsWith("Ice ") ||
                    r.gameObject.name.StartsWith("Block Border"))
                    continue;

                return r.color;
            }
            return new Color(0.6f, 0.6f, 0.7f);
        }

        private void ShatterIntoVoxels(Vector2 impactPoint, Color blockColor)
        {
            int totalVoxels = voxelsPerBlock;
            int gemCount = Mathf.RoundToInt(totalVoxels * gemRatio);
            int stoneCount = totalVoxels - gemCount;

            // Spawn Stone Debris Voxels
            for (int i = 0; i < stoneCount; i++)
            {
                SpawnStoneVoxel(impactPoint, blockColor);
            }

            // Spawn Gem Flying Voxels
            for (int i = 0; i < gemCount; i++)
            {
                SpawnGemVoxel(impactPoint);
            }
        }

        private void SpawnStoneVoxel(Vector2 impactPoint, Color blockColor)
        {
            GameObject stone = stonePool.Count > 0 ? stonePool.Dequeue() : CreateStoneVoxelInstance();
            stone.transform.position = impactPoint + Random.insideUnitCircle * 0.15f;
            stone.transform.localScale = Vector3.one * Random.Range(0.08f, 0.15f);
            stone.SetActive(true);

            SpriteRenderer sr = stone.GetComponent<SpriteRenderer>();
            Color finalColor = Color.Lerp(blockColor, Color.white, Random.Range(0f, 0.25f));
            if (sr != null)
            {
                sr.color = finalColor;
                sr.sortingOrder = 25;
            }

            Rigidbody2D rb = stone.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.simulated = true;
                Vector2 forceDir = CalculateEjectionVector();
                rb.velocity = forceDir * shredExplosionForce * Random.Range(0.7f, 1.3f);
                rb.angularVelocity = Random.Range(-540f, 540f);
            }

            Collider2D col = stone.GetComponent<Collider2D>();
            if (col != null) col.enabled = true;

            activeStones.Add(new ActiveStoneVoxel
            {
                gameObject = stone,
                renderer = sr,
                body = rb,
                lifetime = stoneLifetime * Random.Range(0.85f, 1.25f),
                elapsed = 0f,
                baseColor = finalColor
            });
        }

        private void SpawnGemVoxel(Vector2 impactPoint)
        {
            GemFlyToUI gem = gemPool.Count > 0 ? gemPool.Dequeue() : CreateGemVoxelInstance();
            gem.gameObject.transform.position = impactPoint + Random.insideUnitCircle * 0.12f;
            gem.gameObject.transform.localScale = Vector3.one * Random.Range(0.12f, 0.18f);
            gem.gameObject.SetActive(true);

            ActiveGemFlightCount++;

            Vector2 popDir = CalculateEjectionVector() * (shredExplosionForce * 0.6f);

            gem.Launch(
                impactPoint,
                popDir,
                targetUIRectTransform,
                targetUISlider,
                targetCamera,
                gemFlyDuration,
                gemFlyEase,
                RecycleGemVoxel
            );
        }

        private Vector2 CalculateEjectionVector()
        {
            float randomAngle = Random.Range(-ejectionSpreadAngle * 0.5f, ejectionSpreadAngle * 0.5f);
            Quaternion rotation = Quaternion.Euler(0, 0, randomAngle);
            Vector2 baseDir = shredForceDirection.sqrMagnitude > 0.01f ? shredForceDirection.normalized : Vector2.up;
            return rotation * baseDir;
        }

        private void Update()
        {
            UpdateActiveStones();
        }

        private void UpdateActiveStones()
        {
            for (int i = activeStones.Count - 1; i >= 0; i--)
            {
                ActiveStoneVoxel stone = activeStones[i];
                stone.elapsed += Time.deltaTime;

                if (stone.elapsed >= stone.lifetime)
                {
                    // Recycle stone
                    stone.gameObject.SetActive(false);
                    stonePool.Enqueue(stone.gameObject);
                    activeStones.RemoveAt(i);
                }
                else
                {
                    // Fade alpha out near end of lifetime
                    float progress = stone.elapsed / stone.lifetime;
                    if (progress > 0.5f && stone.renderer != null)
                    {
                        float alpha = Mathf.Lerp(1f, 0f, (progress - 0.5f) * 2f);
                        Color c = stone.baseColor;
                        c.a = alpha;
                        stone.renderer.color = c;
                    }
                    activeStones[i] = stone;
                }
            }
        }

        private void RecycleGemVoxel(GemFlyToUI gem)
        {
            if (gem != null)
            {
                gem.gameObject.SetActive(false);
                gemPool.Enqueue(gem);
                ActiveGemFlightCount = Mathf.Max(0, ActiveGemFlightCount - 1);
            }
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
