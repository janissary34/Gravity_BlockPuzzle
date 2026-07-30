using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace GravityPuzzle
{
    /// <summary>
    /// Standalone Level Progress Manager utilizing DOTween.
        /// Counts the rendered voxel population at level start,
    /// animates flying voxels to the UI Slider with arched DOJump,
    /// applies punch scale & DOValue lerping, and fires OnLevelCompleted.
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelProgressManager : MonoBehaviour
    {
        // One visible 3x3 board voxel becomes this many micro sand grains.
        // The same value is used for the slider denominator and arrivals.
        public const int SandGrainsPerRenderedVoxel = 12;

        public static LevelProgressManager Instance { get; private set; }

        public static LevelProgressManager EnsureInstance()
        {
            if (Instance != null)
                return Instance;

            LevelProgressManager existing = UnityEngine.Object.FindObjectOfType<LevelProgressManager>();
            if (existing != null)
                return existing;

            GameObject managerObject = new GameObject("Level Progress Manager");
            return managerObject.AddComponent<LevelProgressManager>();
        }

        [Header("UI Slider Setup")]
        [SerializeField, Tooltip("Drag and drop your UI Slider component here.")]
        private Slider progressSlider;

        [SerializeField, Tooltip("Duration of the slider DOValue fill animation.")]
        private float sliderFillDuration = 0.12f;

        [SerializeField, Tooltip("Punch scale applied to the slider upon voxel arrival.")]
        private Vector3 sliderPunchScale = new Vector3(0.045f, 0.045f, 0f);

        [SerializeField, Tooltip("Duration of the slider punch scale effect.")]
        private float sliderPunchDuration = 0.12f;

        [Header("Flying Voxel FX Setup")]
        [SerializeField, Tooltip("Flight duration of voxel flying from shredder to UI Slider.")]
        private float voxelFlyDuration = 0.55f;

        [SerializeField, Tooltip("Arched jump height for the flying voxel trajectory.")]
        private float voxelJumpPower = 0.8f;

        [SerializeField, Tooltip("Size of the spawned flying voxel cube in world units.")]
        private float voxelSize = 0.32f;

        [Header("Progress State (Read-Only)")]
        [SerializeField] private int totalBlockUnitsInLevel;
        [SerializeField] private float currentShreddedUnits;
        private int authoredBlockUnits;

        public int TotalBlockUnits => totalBlockUnitsInLevel;
        public int TotalAuthoredBlockUnits => authoredBlockUnits;
        public float CurrentShreddedUnits => currentShreddedUnits;
        public bool IsLevelComplete => totalBlockUnitsInLevel > 0 && currentShreddedUnits >= totalBlockUnitsInLevel - .0001f;

        /// <summary>
        /// Event fired when the level is completed (100% capacity reached).
        /// </summary>
        public event Action OnLevelCompleted;

        /// <summary>
        /// Event fired whenever progress updates: (currentShredded, totalUnits).
        /// </summary>
        public event Action<float, int> OnProgressChanged;

        private Camera mainCamera;
        private Tweener sliderFillTween;
        private Tween sliderPunchTween;
        private bool levelCompletedTriggered;
        private int activeFlyingVoxelCount;
        private bool hasAuthoredLevelTotal;
        private float nextSliderPulseTime;

        public bool HasActiveFlyingVoxels => activeFlyingVoxelCount > 0;
        public bool HasPendingProgressPresentation => HasActiveFlyingVoxels ||
                                                      (sliderFillTween != null && sliderFillTween.IsActive());

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            EnsureSliderReference();
        }

        private void Start()
        {
            EnsureSliderReference();
            Debug.Log($"[LevelProgress] Start: authored={authoredBlockUnits}, runtime={totalBlockUnitsInLevel}, " +
                      $"sliderMax={(progressSlider != null ? progressSlider.maxValue : -1f)}");
        }

        private void EnsureSliderReference()
        {
            if (progressSlider == null)
            {
                progressSlider = FindObjectOfType<Slider>(true);
                if (progressSlider == null)
                {
                    GameObject sliderObj = GameObject.Find("Voxel_Slider") ?? GameObject.Find("Slider") ?? GameObject.Find("Progress_Slider");
                    if (sliderObj != null)
                    {
                        progressSlider = sliderObj.GetComponent<Slider>();
                    }
                }

                if (progressSlider == null)
                    progressSlider = CreateRuntimeProgressSlider();
            }
        }

        private static Slider CreateRuntimeProgressSlider()
        {
            GameObject canvasObject = new GameObject("Voxel Progress Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);

            GameObject sliderObject = new GameObject("Voxel_Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
            sliderObject.transform.SetParent(canvasObject.transform, false);
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(.5f, 1f);
            sliderRect.anchorMax = new Vector2(.5f, 1f);
            sliderRect.pivot = new Vector2(.5f, 1f);
            sliderRect.anchoredPosition = new Vector2(0f, -110f);
            sliderRect.sizeDelta = new Vector2(460f, 42f);

            Image background = sliderObject.GetComponent<Image>();
            background.color = new Color(.08f, .1f, .18f, .94f);

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(sliderObject.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, .12f);
            fillRect.anchorMax = new Vector2(1f, .88f);
            fillRect.offsetMin = new Vector2(6f, 0f);
            fillRect.offsetMax = new Vector2(-6f, 0f);
            Image fill = fillObject.GetComponent<Image>();
            fill.color = new Color(.2f, .9f, .95f, 1f);

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.targetGraphic = background;
            slider.fillRect = fillRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;
            return slider;
        }

        /// <summary>
        /// Auto-scans the scene for all breakable block units and configures the UI Slider bounds.
        /// </summary>
        public void InitializeLevelProgress()
        {
            EnsureSliderReference();

            hasAuthoredLevelTotal = false;
            authoredBlockUnits = 0;
            totalBlockUnitsInLevel = CountActiveBlockUnitsInScene();
            ResetProgress();
        }

        public void InitializeLevelProgress(GravityLevelDefinition level)
        {
            EnsureSliderReference();
            hasAuthoredLevelTotal = true;
            // Count authored, breakable board blocks only. VoxelShards, background
            // cells, UI, and pooled objects are visual implementation details.
            authoredBlockUnits = CountActiveBlockUnitsInScene();
            totalBlockUnitsInLevel = authoredBlockUnits;
            ResetProgress();
            Debug.Log($"[LevelProgress] Initialized maxValue={(progressSlider != null ? progressSlider.maxValue : -1f)}, " +
                      $"authoredUnits={authoredBlockUnits}, level='{level.levelName}'.");
        }

        private void ResetProgress()
        {
            currentShreddedUnits = 0f;
            levelCompletedTriggered = false;
            activeFlyingVoxelCount = 0;

            if (progressSlider != null)
            {
                progressSlider.DOKill();
                progressSlider.minValue = 0f;
                progressSlider.maxValue = Mathf.Max(1, totalBlockUnitsInLevel);
                progressSlider.value = 0f;
                progressSlider.wholeNumbers = false;
            }

            OnProgressChanged?.Invoke(currentShreddedUnits, totalBlockUnitsInLevel);
        }

        /// <summary>
        /// Instantiates a solid-colored flying voxel at startWorldPos that flies in an arched trajectory
        /// up to the UI Slider bar, punches the slider scale, and increments level progress upon arrival.
        /// </summary>
        /// <param name="startWorldPos">World position where the voxel was shredded.</param>
        /// <param name="voxelColor">Color of the block being shredded.</param>
        public void SpawnFlyingVoxel(Vector3 startWorldPos, Color voxelColor, float progressAmount, Action onArrival = null)
        {
            if (levelCompletedTriggered)
            {
                onArrival?.Invoke();
                return;
            }

            EnsureSliderReference();
            Canvas canvas = progressSlider != null
                ? progressSlider.GetComponentInParent<Canvas>()
                : null;
            if (canvas == null)
            {
                onArrival?.Invoke();
                return;
            }

            RectTransform canvasRect = canvas.transform as RectTransform;
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Camera worldCamera = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
            if (canvasRect == null || worldCamera == null)
            {
                onArrival?.Invoke();
                return;
            }

            RectTransform targetRect = progressSlider.fillRect != null
                ? progressSlider.fillRect
                : progressSlider.GetComponent<RectTransform>();
            if (targetRect == null)
            {
                onArrival?.Invoke();
                return;
            }

            Vector2 start = ScreenToCanvasPoint(canvasRect, worldCamera.WorldToScreenPoint(startWorldPos), uiCamera);
            Vector2 target = ScreenToCanvasPoint(
                canvasRect,
                RectTransformUtility.WorldToScreenPoint(uiCamera, targetRect.position),
                uiCamera);
            target += new Vector2(UnityEngine.Random.Range(-20f, 20f), UnityEngine.Random.Range(-4f, 4f));

            GameObject flyingVoxel = new GameObject("Flying Voxel UI", typeof(RectTransform), typeof(Image));
            flyingVoxel.transform.SetParent(canvas.transform, false);
            RectTransform voxelRect = flyingVoxel.GetComponent<RectTransform>();
            voxelRect.anchorMin = new Vector2(.5f, .5f);
            voxelRect.anchorMax = new Vector2(.5f, .5f);
            voxelRect.sizeDelta = Vector2.one * Mathf.Max(18f, voxelSize * 58f);
            voxelRect.anchoredPosition = start;
            Image voxelImage = flyingVoxel.GetComponent<Image>();
            voxelImage.sprite = PrototypeBootstrap.GetSquareSprite();
            voxelImage.color = Opaque(voxelColor);
            voxelImage.raycastTarget = false;

            activeFlyingVoxelCount++;

            // Physics has already ended below the grinder. This is now a pure,
            // smooth UI tween from the exact world hand-off position to the bar.
            // The control point gives the stream a generous lower bend before it
            // funnels upward. EaseInCubic supplies the slow-to-fast launch.
            Vector2 control = Vector2.Lerp(start, target, .42f) + Vector2.up * Mathf.Max(115f, voxelJumpPower * 220f);
            float flightDuration = voxelFlyDuration + UnityEngine.Random.Range(-.08f, .12f);
            Sequence flightSequence = DOTween.Sequence();
            // A tiny gathering pause at the lower bend makes the direction change read.
            flightSequence.AppendInterval(UnityEngine.Random.Range(.07f, .16f));
            flightSequence.Append(DOVirtual.Float(0f, 1f, flightDuration, progress =>
                voxelRect.anchoredPosition = QuadraticBezier(start, control, target, progress)).SetEase(Ease.InCubic));
            flightSequence.Join(voxelRect.DORotate(new Vector3(0f, 0f, UnityEngine.Random.Range(-160f, 160f)), flightDuration, RotateMode.FastBeyond360));
            flightSequence.OnComplete(() =>
            {
                // Trigger UI Slider Punch Scale feedback on each voxel arrival.
                if (progressSlider != null && Time.unscaledTime >= nextSliderPulseTime)
                {
                    if (sliderPunchTween != null && sliderPunchTween.IsActive())
                        sliderPunchTween.Kill(true);

                    sliderPunchTween = progressSlider.transform.DOPunchScale(sliderPunchScale, sliderPunchDuration, 6, 0.5f);
                    nextSliderPulseTime = Time.unscaledTime + .09f;
                }

                // The flight voxel is destroyed only after reaching the bar.
                if (flyingVoxel != null)
                    Destroy(flyingVoxel);

                activeFlyingVoxelCount = Mathf.Max(0, activeFlyingVoxelCount - 1);
                AddProgress(progressAmount);
                onArrival?.Invoke();
            });
        }

        private static Vector2 ScreenToCanvasPoint(RectTransform canvasRect, Vector2 screenPoint, Camera uiCamera)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPoint,
                uiCamera,
                out Vector2 localPoint);
            return localPoint;
        }

        private static Vector2 QuadraticBezier(Vector2 start, Vector2 control, Vector2 end, float progress)
        {
            float inverse = 1f - progress;
            return inverse * inverse * start + 2f * inverse * progress * control + progress * progress * end;
        }

        /// <summary>
        /// Increments the shredded count and smoothly animates the UI Slider fill using DOTween DOValue.
        /// Fires OnLevelCompleted when 100% is reached.
        /// </summary>
        /// <param name="amount">Number of block units shredded (default 1).</param>
        public void AddProgress(float amount)
        {
            if (levelCompletedTriggered) return;

            EnsureSliderReference();

            // Safety check: if totalBlockUnits evaluated to 0 on Start (e.g., runtime level load), recalculate now
            if (totalBlockUnitsInLevel <= 0 && !hasAuthoredLevelTotal)
            {
                totalBlockUnitsInLevel = CountActiveBlockUnitsInScene();
            }

            currentShreddedUnits += amount;
            if (totalBlockUnitsInLevel > 0)
            {
                currentShreddedUnits = Mathf.Min(currentShreddedUnits, totalBlockUnitsInLevel);
                if (totalBlockUnitsInLevel - currentShreddedUnits <= .0001f)
                    currentShreddedUnits = totalBlockUnitsInLevel;
            }

            if (progressSlider != null)
            {
                progressSlider.minValue = 0f;
                // Keep the authored denominator locked. No scene object can change this at runtime.
                progressSlider.maxValue = Mathf.Max(1, hasAuthoredLevelTotal
                    ? authoredBlockUnits
                    : totalBlockUnitsInLevel);

                if (sliderFillTween != null && sliderFillTween.IsActive())
                    sliderFillTween.Kill();

                sliderFillTween = progressSlider.DOValue(currentShreddedUnits, sliderFillDuration)
                    .SetEase(Ease.OutQuad);

                Debug.Log($"[LevelProgress] AddProgress(+{amount:0.###}): value={currentShreddedUnits:0.###}/{progressSlider.maxValue:0.###}, " +
                          $"authored={authoredBlockUnits}.");
            }

            OnProgressChanged?.Invoke(currentShreddedUnits, totalBlockUnitsInLevel);

            if (totalBlockUnitsInLevel > 0 && currentShreddedUnits >= totalBlockUnitsInLevel - .0001f && !levelCompletedTriggered)
            {
                levelCompletedTriggered = true;
                
                // Complete remaining DOValue animation then trigger OnLevelCompleted
                if (sliderFillTween != null && sliderFillTween.IsActive())
                {
                    sliderFillTween.OnComplete(TriggerLevelCompleted);
                }
                else
                {
                    TriggerLevelCompleted();
                }
            }
        }

        private void TriggerLevelCompleted()
        {
            Debug.Log($"<color=green>[LevelProgressManager] 🌟 LEVEL COMPLETED! All {totalBlockUnitsInLevel} units shredded.</color>");
            OnLevelCompleted?.Invoke();
        }

        private Vector3 GetSliderWorldPosition()
        {
            EnsureSliderReference();
            Camera cam = Camera.main;
            if (cam == null) cam = FindObjectOfType<Camera>();

            if (progressSlider != null)
            {
                RectTransform targetRect = progressSlider.fillRect != null 
                    ? (RectTransform)progressSlider.fillRect.transform 
                    : progressSlider.GetComponent<RectTransform>();

                if (targetRect != null && cam != null)
                {
                    Vector3 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, targetRect.position);
                    float depth = Mathf.Abs(cam.transform.position.z);
                    Vector3 worldPoint = cam.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, depth > 0 ? depth : 10f));
                    worldPoint.z = 0f;

                    // Ensure the target world position is above the camera center (top UI position)
                    if (worldPoint.y > cam.transform.position.y)
                    {
                        return worldPoint;
                    }
                }
            }

            // Guaranteed Fallback: Top-center of Camera Viewport (90% up screen height)
            if (cam != null)
            {
                Vector3 topWorldPoint = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.90f, Mathf.Abs(cam.transform.position.z)));
                topWorldPoint.z = 0f;
                return topWorldPoint;
            }

            return new Vector3(0f, 4.2f, 0f);
        }

        /// <summary>
        /// Counts actual authored board-block units only; backgrounds, voxel meshes,
        /// UI objects, and pools can never affect gameplay progress.
        /// </summary>
        private static int CountActiveBlockUnitsInScene()
        {
            int total = 0;
            foreach (PuzzlePiece piece in FindObjectsOfType<PuzzlePiece>())
            {
                if (piece != null && !piece.IsBeingShredded)
                    total += Mathf.Max(1, piece.ProgressUnits);
            }
            return total;
        }

        private static int CountAuthoredPuzzlePieces(GravityLevelDefinition level)
        {
            if (level == null)
                return 0;

            int totalUnits = 0;
            if (level.pieces == null)
                return totalUnits;

            int subdivisions = Mathf.Max(1, level.subdivisions);
            foreach (PieceDefinition piece in level.pieces)
            {
                if (piece == null || piece.cells == null)
                    continue;

                HashSet<Vector2Int> occupiedBoardBlocks = new HashSet<Vector2Int>();
                foreach (PieceCellDefinition cell in piece.cells)
                {
                    if (cell.type != PieceCellType.Block)
                        continue;

                    Vector2Int absolute = piece.origin + QuarterTurnUtility.Rotate(cell.localCell, piece.quarterTurns);
                    occupiedBoardBlocks.Add(new Vector2Int(
                        Mathf.FloorToInt((float)absolute.x / subdivisions),
                        Mathf.FloorToInt((float)absolute.y / subdivisions)));
                }

                totalUnits += occupiedBoardBlocks.Count;
            }

            return totalUnits;
        }

        private static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
