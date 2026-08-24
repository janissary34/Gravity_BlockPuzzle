using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using GravityPuzzle.Config;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;

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
        // One visible 3x3 board voxel becomes 1 micro sand grain.
        // The same value is used for the slider denominator and arrivals.
        public const int SandGrainsPerRenderedVoxel = 1;

        public static LevelProgressManager Instance { get; private set; }

        public static LevelProgressManager EnsureInstance()
        {
            if (Instance == null)
                Debug.LogError("[LevelProgress] No authored LevelProgressManager is active. Add it to the scene and assign its Slider.");

            return Instance;
        }

        [Header("UI Slider Setup")]
        [SerializeField, Tooltip("Drag and drop your UI Slider component here.")]
        private Slider progressSlider;

        [SerializeField, Tooltip("Owns the timing and easing of progress presentation tweens.")]
        private TweenConfig tweenConfig;

        [Header("Progress Voxel Pool")]
        [SerializeField, Tooltip("Authored UI prefab used for each progress-bar flight.")]
        private FlyingProgressVoxelView flyingProgressVoxelPrefab;
        [SerializeField, Tooltip("Owns the prewarmed progress-flight capacity.")]
        private PoolConfig poolConfig;
        [SerializeField, Tooltip("Optional inactive-parent for returned progress voxels.")]
        private Transform flyingProgressVoxelPoolParent;

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
        private Canvas progressCanvas;
        private RectTransform progressCanvasRect;
        private RectTransform progressTargetRect;
        private Tweener sliderFillTween;
        private Tween sliderPunchTween;
        private bool levelCompletedTriggered;
        private int activeFlyingVoxelCount;
        private bool hasAuthoredLevelTotal;
        private float nextSliderPulseTime;
        private bool hasTweenConfig;
        private GameObjectPool<FlyingProgressVoxelView> flyingProgressVoxelPool;

        private float SliderFillDuration => tweenConfig.ProgressSliderFillDuration;
        private Ease SliderFillEase => tweenConfig.ProgressSliderFillEase;
        private float VoxelFlightDuration => tweenConfig.ProgressVoxelFlightDuration;
        private Ease VoxelFlightEase => tweenConfig.ProgressVoxelFlightEase;
        private float SliderPunchDuration => tweenConfig.ProgressSliderPunchDuration;
        private float VoxelRotationRange => tweenConfig.ProgressVoxelRotationRange;
        private int SliderPunchVibrato => tweenConfig.ProgressSliderPunchVibrato;
        private float SliderPunchElasticity => tweenConfig.ProgressSliderPunchElasticity;
        private float SliderPulseCooldown => tweenConfig.ProgressSliderPulseCooldown;
        private Vector3 SliderPunchScale => tweenConfig.ProgressSliderPunchScale;
        private float ProgressVoxelCurveDropMultiplier => tweenConfig.ProgressVoxelCurveDropMultiplier;
        private float ProgressVoxelUiSize => tweenConfig.ProgressVoxelUiSize;

        public bool HasActiveFlyingVoxels => activeFlyingVoxelCount > 0;
        public bool HasPendingProgressPresentation => HasActiveFlyingVoxels ||
                                                      (sliderFillTween != null && sliderFillTween.IsActive());

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[LevelProgress] Duplicate manager disabled. Keep one authored LevelProgressManager in the scene.", this);
                enabled = false;
                return;
            }

            Instance = this;
            hasTweenConfig = tweenConfig != null;
            if (!hasTweenConfig)
                Debug.LogWarning("[LevelProgress] TweenConfig is missing; progress will update without tween presentation.", this);
            EnsureSliderReference();
            CachePresentationReferences();
            ConfigureFlyingProgressVoxelPool();
        }

        private void Start()
        {
            mainCamera = PrototypeBootstrap.SceneCamera;
            if (mainCamera == null)
                Debug.LogError("[LevelProgress] No gameplay camera is configured on Runtime Piece Factory Bootstrap.", this);
        }

        private void ConfigureFlyingProgressVoxelPool()
        {
            if (flyingProgressVoxelPrefab == null || poolConfig == null || poolConfig.ProgressVoxelCapacity <= 0)
            {
                Debug.LogWarning("[LevelProgress] Progress voxel prefab or PoolConfig is missing; progress flights will be presented instantly.", this);
                return;
            }

            Transform poolParent = flyingProgressVoxelPoolParent != null
                ? flyingProgressVoxelPoolParent
                : transform;
            flyingProgressVoxelPool = new GameObjectPool<FlyingProgressVoxelView>(
                flyingProgressVoxelPrefab,
                poolParent,
                poolConfig.ProgressVoxelCapacity);
            flyingProgressVoxelPool.Prewarm();
        }

        private void EnsureSliderReference()
        {
            if (progressSlider == null)
            {
                Debug.LogError("[LevelProgress] Progress Slider is not assigned. Assign the authored UI Slider in the Inspector.", this);
                return;
            }

            // Progress is game state, not player input. Keep the authored visual
            // state intact and disable navigation instead of creating runtime UI.
            progressSlider.interactable = true;
            Navigation navigation = progressSlider.navigation;
            navigation.mode = Navigation.Mode.None;
            progressSlider.navigation = navigation;
        }

        private void CachePresentationReferences()
        {
            if (progressSlider == null)
                return;

            progressCanvas = progressSlider.GetComponentInParent<Canvas>();
            progressCanvasRect = progressCanvas != null ? progressCanvas.transform as RectTransform : null;
            progressTargetRect = progressSlider.handleRect != null
                ? progressSlider.handleRect
                : progressSlider.fillRect != null
                    ? progressSlider.fillRect
                    : progressSlider.GetComponent<RectTransform>();

            if (progressCanvas == null || progressCanvasRect == null || progressTargetRect == null)
                Debug.LogError("[LevelProgress] Slider presentation references are incomplete. The Slider must be inside an authored Canvas.", this);
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
        /// to the Slider Handle, then increments level progress on arrival.
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

            if (!hasTweenConfig)
            {
                AddProgress(progressAmount);
                onArrival?.Invoke();
                return;
            }

            EnsureSliderReference();
            if (progressCanvas == null || progressCanvasRect == null || progressTargetRect == null || mainCamera == null)
            {
                // Presentation can be unavailable in an incompletely authored
                // scene, but shredding must never lose its logical reward.
                // Apply it immediately rather than silently recycling the
                // physical voxel without advancing the slider.
                AddProgress(progressAmount);
                onArrival?.Invoke();
                return;
            }

            // The handle is the visible leading edge of the fill. Landing there
            // makes each voxel read as material entering the progress bar rather
            // than merely flying toward its static background.
            Camera uiCamera = progressCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : progressCanvas.worldCamera;
            Vector2 start = ScreenToCanvasPoint(progressCanvasRect, mainCamera.WorldToScreenPoint(startWorldPos), uiCamera);
            Vector2 target = ScreenToCanvasPoint(
                progressCanvasRect,
                RectTransformUtility.WorldToScreenPoint(uiCamera, progressTargetRect.position),
                uiCamera);
            // Keep a very small spread so separate grains remain visible while
            // still clearly converging on the Handle game object's position.
            target += new Vector2(UnityEngine.Random.Range(-6f, 6f), UnityEngine.Random.Range(-3f, 3f));

            if (flyingProgressVoxelPool == null || !flyingProgressVoxelPool.TryRent(out FlyingProgressVoxelView flyingVoxel))
            {
                Debug.LogWarning("[LevelProgress] Progress voxel pool is unavailable or exhausted; applying progress without a flight.", this);
                AddProgress(progressAmount);
                onArrival?.Invoke();
                return;
            }

            flyingVoxel.transform.SetParent(progressCanvas.transform, false);
            RectTransform voxelRect = flyingVoxel.RectTransform;
            flyingVoxel.Configure(
                start,
                Mathf.Max(18f, ProgressVoxelUiSize * 58f),
                PrototypeBootstrap.GetSquareSprite(),
                Opaque(voxelColor));

            activeFlyingVoxelCount++;

            // Continue the real free-fall motion for a short distance in UI
            // space, then curve upward toward the bar. The Bezier's first
            // tangent points down, so there is no abrupt stop-and-go corner.
            float curveDrop = Mathf.Max(42f, ProgressVoxelCurveDropMultiplier * 85f);
            Vector2 control = start + new Vector2(
                UnityEngine.Random.Range(-28f, 28f),
                -curveDrop);
            float flightDuration = VoxelFlightDuration + UnityEngine.Random.Range(-.08f, .12f);
            Sequence flightSequence = DOTween.Sequence()
                .SetLink(flyingVoxel.gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true)
                .SetDelay(UnityEngine.Random.Range(0f, .12f));
            flightSequence.Append(DOVirtual.Float(0f, 1f, flightDuration, progress =>
                voxelRect.anchoredPosition = QuadraticBezier(start, control, target, progress)).SetEase(VoxelFlightEase));
            flightSequence.Join(voxelRect.DORotate(new Vector3(0f, 0f, UnityEngine.Random.Range(-VoxelRotationRange, VoxelRotationRange)), flightDuration, RotateMode.FastBeyond360));
            flightSequence.OnComplete(() =>
            {
                // Trigger UI Slider Punch Scale feedback on each voxel arrival.
                if (progressSlider != null && Time.unscaledTime >= nextSliderPulseTime)
                {
                    if (sliderPunchTween != null && sliderPunchTween.IsActive())
                        sliderPunchTween.Kill(true);

                    sliderPunchTween = progressSlider.transform.DOPunchScale(SliderPunchScale, SliderPunchDuration, SliderPunchVibrato, SliderPunchElasticity)
                        .SetLink(progressSlider.gameObject, LinkBehaviour.KillOnDisable)
                        .SetAutoKill(true);
                    nextSliderPulseTime = Time.unscaledTime + SliderPulseCooldown;
                }

                // The flight voxel is recycled only after reaching the bar.
                if (flyingVoxel != null)
                {
                    voxelRect.DOKill();
                    flyingProgressVoxelPool.Return(flyingVoxel);
                }

                activeFlyingVoxelCount = Mathf.Max(0, activeFlyingVoxelCount - 1);
                AddProgress(progressAmount);
                onArrival?.Invoke();
            });
        }

        /// <summary>
        /// Presents one logical reward as several pooled UI voxels while keeping
        /// the total gameplay progress exactly equal to totalProgressAmount.
        /// </summary>
        public void SpawnFlyingVoxelBurst(Vector3 startWorldPos, Color voxelColor, float totalProgressAmount, int flightCount)
        {
            int count = Mathf.Max(1, flightCount);
            float progressPerFlight = totalProgressAmount / count;
            for (int i = 0; i < count; i++)
                SpawnFlyingVoxel(startWorldPos, voxelColor, progressPerFlight, null);
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

            if (progressSlider != null && hasTweenConfig)
            {
                progressSlider.minValue = 0f;
                // Keep the authored denominator locked. No scene object can change this at runtime.
                progressSlider.maxValue = Mathf.Max(1, hasAuthoredLevelTotal
                    ? authoredBlockUnits
                    : totalBlockUnitsInLevel);

                if (sliderFillTween != null && sliderFillTween.IsActive())
                    sliderFillTween.Kill();

                sliderFillTween = progressSlider.DOValue(currentShreddedUnits, SliderFillDuration)
                    .SetEase(SliderFillEase)
                    .SetLink(progressSlider.gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);

            }
            else if (progressSlider != null)
            {
                progressSlider.value = currentShreddedUnits;
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

        /// <summary>
        /// Counts actual authored board-block units only; backgrounds, voxel meshes,
        /// UI objects, and pools can never affect gameplay progress.
        /// </summary>
        private static int CountActiveBlockUnitsInScene()
        {
            int total = 0;
            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                // Empty authored entries are invalid level data and cannot emit any
                // shredding progress. They must not make the level target unreachable.
                if (piece != null && !piece.IsBeingShredded && piece.HasRuntimeBlockCells)
                    total += piece.ProgressUnits;
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
