using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using GravityPuzzle.Config;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;
using GravityPuzzle.Presentation.VFX;

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

        [SerializeField, Tooltip("The active HUD canvas that owns the progress slider. Used only when a legacy scene reference points at a disabled HUD.")]
        private Canvas activeHudCanvas;

        [SerializeField, Tooltip("Owns the timing and easing of progress presentation tweens.")]
        private TweenConfig tweenConfig;

        [Header("Progress Particle System")]
        [SerializeField, Tooltip("Progress Particle System component for flying voxels.")]
        private ParticleSystem progressParticleSystem;
        private ProgressVoxelParticleSystem progressVoxelVfx;

        [Header("Progress Voxel Pool (Fallback)")]
        [SerializeField, Tooltip("Authored UI prefab used for fallback progress-bar flight.")]
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
        private int pendingVfxProgressArrivalCount;
        private bool boardClearCompletionRequested;
        private bool completionPresentationFinished;

        // World-space ParticleSystem effects cannot be composited reliably over
        // an overlay canvas. The prewarmed UI view is the deterministic flight
        // presenter for Scene_Tuna's Screen Space - Overlay HUD; the particle
        // system remains available for authored world-space HUDs.
        private bool CanUseWorldParticleVfx => progressVoxelVfx != null &&
                                                progressVoxelVfx.CanRenderFlights &&
                                                progressCanvas != null &&
                                                progressCanvas.renderMode != RenderMode.ScreenSpaceOverlay;

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
        private int ProgressVoxelUiBurstCount => tweenConfig.ProgressVoxelUiBurstCount;

        public bool HasActiveFlyingVoxels => activeFlyingVoxelCount > 0;
        public bool HasPendingProgressPresentation => HasActiveFlyingVoxels ||
                                                      pendingVfxProgressArrivalCount > 0 ||
                                                      (sliderFillTween != null && sliderFillTween.IsActive());
        public bool IsCompletionPresentationFinished => completionPresentationFinished;
        private bool HasInFlightProgressPresentation => HasActiveFlyingVoxels || pendingVfxProgressArrivalCount > 0;

        /// <summary>
        /// Commits the final authoritative progress when the board has no
        /// remaining runtime pieces. Flight views are presentation only: a
        /// killed tween, exhausted pool, or hidden HUD must never leave a
        /// cleared board unable to enter its result state.
        /// </summary>
        public void CompleteForBoardClear()
        {
            if (totalBlockUnitsInLevel <= 0 || IsLevelComplete)
                return;

            boardClearCompletionRequested = true;
            TryCommitBoardClearProgress();
        }

        private void TryCommitBoardClearProgress()
        {
            if (!boardClearCompletionRequested || HasInFlightProgressPresentation || IsLevelComplete)
                return;

            float missingUnits = totalBlockUnitsInLevel - currentShreddedUnits;
            if (missingUnits <= .0001f)
                return;

            boardClearCompletionRequested = false;
            Debug.LogWarning(
                $"[LevelProgress] Board cleared with {missingUnits:0.###} unscheduled progress units; committing the authoritative remainder.",
                this);
            AddProgress(missingUnits);
        }

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
            RefreshHudPresentationReferences();
            InitializeParticleVfxReference();
            // The migrated HUD's prefab can retain an authored preview value.
            // Clear that presentation value before the first rendered frame;
            // the level runtime supplies the real denominator immediately
            // afterwards in InitializeLevelProgress.
            ResetProgress();
            ConfigureFlyingProgressVoxelPool();
        }

        private void InitializeParticleVfxReference()
        {
            if (progressParticleSystem != null)
            {
                progressVoxelVfx = progressParticleSystem.GetComponent<ProgressVoxelParticleSystem>();
                if (progressVoxelVfx == null)
                    Debug.LogWarning("[LevelProgress] The assigned particle system has no ProgressVoxelParticleSystem. UI flight fallback will be used.", this);
            }
        }

        private void ResolveActiveHudSlider()
        {
            if (IsActiveHudProgressSlider(progressSlider))
                return;

            if (activeHudCanvas == null)
                return;

            // Scene1 formerly pointed to the disabled Timer_Canvas slider. The
            // active HUD is an explicit composition-root reference; this small
            // Awake-only lookup merely resolves its authored progress control.
            Slider[] sliders = activeHudCanvas.GetComponentsInChildren<Slider>(true);
            for (int index = 0; index < sliders.Length; index++)
            {
                Slider candidate = sliders[index];
                if (candidate != null && candidate.gameObject.name == "UI_Progress_Slider")
                {
                    progressSlider = candidate;
                    return;
                }
            }

            // Do not retain a valid-but-wrong legacy slider reference. Its
            // handle belongs to a different canvas, so progress flights would
            // visibly land away from the active HUD even when the bar itself
            // was updating correctly.
            progressSlider = null;
            Debug.LogError("[LevelProgress] Active HUD Canvas has no authored UI_Progress_Slider.", activeHudCanvas);
        }

        private bool IsActiveHudProgressSlider(Slider slider)
        {
            return slider != null &&
                   activeHudCanvas != null &&
                   slider.gameObject.name == "UI_Progress_Slider" &&
                   slider.transform.IsChildOf(activeHudCanvas.transform);
        }

        private void RefreshHudPresentationReferences()
        {
            ResolveActiveHudSlider();
            EnsureSliderReference();
            CachePresentationReferences();
        }

        private void Start()
        {
            // Scene reloads can run this manager's Awake before the HUD prefab
            // has enabled its children. The serialized HUD root is authoritative,
            // so refresh once more at Start without depending on active state.
            RefreshHudPresentationReferences();
            mainCamera = PrototypeBootstrap.SceneCamera;
            if (mainCamera == null)
                Debug.LogError("[LevelProgress] No gameplay camera is configured on Runtime Piece Factory Bootstrap.", this);

            if (progressVoxelVfx != null)
            {
                // The particles must render in front of the world-space
                // progress canvas; otherwise their final position is hidden
                // behind the slider handle/pin.
                if (progressCanvas != null)
                {
                    progressVoxelVfx.SetRendererSortingOrder(progressCanvas.sortingOrder + 1);
                    progressVoxelVfx.SetTargetPosition(GetTargetWorldPosition());
                }
            }
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

            // Prefer the composition-root HUD reference. The slider reference
            // may still be the legacy disabled Timer_Canvas during migration.
            progressCanvas = activeHudCanvas != null && activeHudCanvas.isActiveAndEnabled
                ? activeHudCanvas
                : progressSlider.GetComponentInParent<Canvas>(true);
            progressCanvasRect = progressCanvas != null ? progressCanvas.transform as RectTransform : null;
            progressTargetRect = progressSlider.handleRect != null
                ? progressSlider.handleRect
                : progressSlider.fillRect != null
                    ? progressSlider.fillRect
                    : progressSlider.GetComponent<RectTransform>();

            if (progressCanvas == null || progressCanvasRect == null || progressTargetRect == null)
                Debug.LogError("[LevelProgress] Slider presentation references are incomplete. The Slider must be inside an authored Canvas.", this);

            if (progressVoxelVfx != null)
                progressVoxelVfx.SetTargetPosition(GetTargetWorldPosition());
        }

        /// <summary>
        /// Auto-scans the scene for all breakable block units and configures the UI Slider bounds.
        /// </summary>
        public void InitializeLevelProgress()
        {
            RefreshHudPresentationReferences();

            hasAuthoredLevelTotal = false;
            authoredBlockUnits = 0;
            totalBlockUnitsInLevel = CountActiveBlockUnitsInScene();
            ResetProgress();
        }

        public void InitializeLevelProgress(GravityLevelDefinition level)
        {
            RefreshHudPresentationReferences();
            hasAuthoredLevelTotal = true;
            authoredBlockUnits = CountAuthoredPuzzlePieces(level);
            if (authoredBlockUnits <= 0)
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
            completionPresentationFinished = false;
            boardClearCompletionRequested = false;
            activeFlyingVoxelCount = 0;
            pendingVfxProgressArrivalCount = 0;

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
        public void SpawnFlyingVoxel(Vector3 startWorldPos, Color voxelColor, float progressAmount, Action onArrival = null, int particleCount = 1)
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

            if (flyingProgressVoxelPool == null && !CanUseWorldParticleVfx)
            {
                // There is no authored presenter available. Preserve the
                // authoritative level state instead of allowing a cosmetic
                // failure to make the level impossible to complete.
                AddProgress(progressAmount);
                onArrival?.Invoke();
                return;
            }

            if (CanUseWorldParticleVfx)
            {
                progressVoxelVfx.SetTargetPosition(GetTargetWorldPosition());
                int flightGroupId = progressVoxelVfx.EmitVoxel(
                    startWorldPos,
                    Opaque(voxelColor),
                    VoxelFlightDuration,
                    Mathf.Max(1, particleCount));
                if (progressVoxelVfx.IsFlightGroupActive(flightGroupId))
                {
                    StartCoroutine(ApplyProgressWhenVfxArrives(progressAmount, onArrival, flightGroupId));
                    return;
                }
            }

            // The handle is the visible leading edge of the fill. Landing there
            // makes each voxel read as material entering the progress bar rather
            // than merely flying toward its static background.
            // Scene bootstrap owns the gameplay-camera reference. Retrieve that
            // cached dependency again at the handoff boundary in case a manager
            // was enabled before the bootstrap's Start pass.
            if (mainCamera == null)
                mainCamera = PrototypeBootstrap.SceneCamera;

            if (progressCanvas == null || progressCanvasRect == null || progressTargetRect == null || mainCamera == null)
            {
                Debug.LogWarning("[LevelProgress] UI flight references are unavailable; applying progress without a flight.", this);
                AddProgress(progressAmount);
                onArrival?.Invoke();
                return;
            }

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
            // The flight is a HUD presentation, not board art. Keep pooled
            // views above authored HUD siblings so they stay visible during the
            // entire route to the slider handle.
            flyingVoxel.transform.SetAsLastSibling();
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
            bool flightResolved = false;
            void ResolveFlight()
            {
                if (flightResolved)
                    return;

                flightResolved = true;
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
                TryCommitBoardClearProgress();
            }

            // A linked tween can be killed when an authored UI object is
            // disabled. That is a presentation interruption, never a reason
            // to leave board completion waiting for a cosmetic arrival.
            flightSequence.OnComplete(ResolveFlight);
            flightSequence.OnKill(ResolveFlight);
        }

        /// <summary>
        /// Presents one logical reward as several pooled UI voxels while keeping
        /// the total gameplay progress exactly equal to totalProgressAmount.
        /// </summary>
        public void SpawnFlyingVoxelBurst(Vector3 startWorldPos, Color voxelColor, float totalProgressAmount, int flightCount)
        {
            if (levelCompletedTriggered) return;

            if (CanUseWorldParticleVfx)
            {
                progressVoxelVfx.SetTargetPosition(GetTargetWorldPosition());
                int flightGroupId = progressVoxelVfx.EmitVoxelBurst(
                    startWorldPos,
                    Opaque(voxelColor),
                    flightCount,
                    VoxelFlightDuration);
                if (progressVoxelVfx.IsFlightGroupActive(flightGroupId))
                {
                    StartCoroutine(ApplyProgressWhenVfxArrives(totalProgressAmount, null, flightGroupId));
                    return;
                }
            }

            // The authored particle count belongs to the single-draw-call world
            // VFX. Mapping all of those particles to individual pooled UI Images
            // exhausted the fallback pool as soon as several pieces reached the
            // shredder together. Keep a small, readable HUD burst and distribute
            // the same authoritative progress across it.
            int count = Mathf.Clamp(flightCount, 1, ProgressVoxelUiBurstCount);
            float progressPerFlight = totalProgressAmount / count;
            for (int i = 0; i < count; i++)
                SpawnFlyingVoxel(startWorldPos, voxelColor, progressPerFlight, null);
        }

        private IEnumerator ApplyProgressWhenVfxArrives(float progressAmount, Action onArrival, int flightGroupId)
        {
            pendingVfxProgressArrivalCount++;
            float elapsed = 0f;
            float maximumWait = progressVoxelVfx != null
                ? progressVoxelVfx.MaximumFlightDuration(VoxelFlightDuration) + .25f
                : 0f;

            while (progressVoxelVfx != null && progressVoxelVfx.IsFlightGroupActive(flightGroupId))
            {
                elapsed += Time.deltaTime;
                if (elapsed >= maximumWait)
                {
                    Debug.LogWarning("[LevelProgress] A progress VFX flight exceeded its visual timeout; advancing level progress.", this);
                    break;
                }

                yield return null;
            }

            // The group has rendered its last target frame. Advance the pin
            // only after the frame is complete, never from a duration estimate.
            yield return new WaitForEndOfFrame();
            pendingVfxProgressArrivalCount = Mathf.Max(0, pendingVfxProgressArrivalCount - 1);
            AddProgress(progressAmount);
            onArrival?.Invoke();
            TryCommitBoardClearProgress();
        }

        public Vector3 GetTargetWorldPosition()
        {
            if (progressTargetRect == null)
                return Vector3.up * 4f;

            if (mainCamera == null)
                mainCamera = PrototypeBootstrap.SceneCamera;

            if (progressCanvas == null && progressSlider != null)
                progressCanvas = progressSlider.GetComponentInParent<Canvas>();

            Camera uiCamera = (progressCanvas != null && progressCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? progressCanvas.worldCamera
                : null;

            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, progressTargetRect.position);
            if (mainCamera != null)
            {
                Ray targetRay = mainCamera.ScreenPointToRay(screenPoint);
                float rayZ = targetRay.direction.z;
                if (Mathf.Abs(rayZ) > .0001f)
                {
                    float distanceToGameplayPlane = -targetRay.origin.z / rayZ;
                    if (distanceToGameplayPlane >= 0f)
                        return targetRay.GetPoint(distanceToGameplayPlane);
                }

                Debug.LogWarning("[LevelProgress] Could not project the progress pin onto the gameplay plane.", this);
            }

            Vector3 fallback = progressTargetRect.position;
            fallback.z = 0f;
            return fallback;
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
                {
                    sliderFillTween.ChangeEndValue(currentShreddedUnits, true);
                    sliderFillTween.OnUpdate(UpdateProgressVoxelTarget);
                }
                else
                {
                    sliderFillTween = progressSlider.DOValue(currentShreddedUnits, SliderFillDuration)
                        .SetEase(SliderFillEase)
                        .SetLink(progressSlider.gameObject, LinkBehaviour.KillOnDisable)
                        .SetAutoKill(true)
                        .OnUpdate(UpdateProgressVoxelTarget);
                }

            }
            else if (progressSlider != null)
            {
                progressSlider.value = currentShreddedUnits;
                UpdateProgressVoxelTarget();
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

        private void UpdateProgressVoxelTarget()
        {
            if (progressVoxelVfx != null)
                progressVoxelVfx.SetTargetPosition(GetTargetWorldPosition());
        }

        private void TriggerLevelCompleted()
        {
            if (completionPresentationFinished)
                return;

            completionPresentationFinished = true;
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
            int subdivisions = Mathf.Max(1, level.subdivisions);
            foreach (PieceDefinition piece in level.EnumerateAllPieceDefinitions())
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
