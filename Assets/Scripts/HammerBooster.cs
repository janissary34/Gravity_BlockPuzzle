using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;
using GravityPuzzle.Config;
using GravityPuzzle.Core.StateMachine;
using GravityPuzzle.Gameplay.Input;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;

namespace GravityPuzzle
{
    /// <summary>
    /// One-use-per-level hammer. Press the UI button to enter targeting mode,
    /// then tap one visible cell belonging to a movable puzzle piece.
    /// </summary>
    public sealed class HammerBooster : MonoBehaviour
    {
        // A valid hammer hit removes its targeted runtime cell, then commits
        // the remaining topology to the authoritative board grid.
        private const bool TopologyEditingEnabled = true;

        public static bool IsTargeting =>
            activeBooster != null || Time.frameCount <= suppressGameplayThroughFrame;

        [Header("Hammer Booster")]
        [Tooltip("Optional. Assign a Button to wire its click automatically.")]
        public Button boosterButton;

        [Tooltip("Optional authored hammer visual prefab. Add BoosterVisualView to its root to enable pooled presentation.")]
        [SerializeField] private GameObject hammerVisualPrefab;
        [SerializeField] private TweenConfig tweenConfig;
        [SerializeField, Tooltip("Gameplay camera used for hammer presentation. Defaults to the camera supplied by Runtime Piece Factory Bootstrap.")]
        private Camera gameplayCamera;

        [SerializeField] private float popScaleMultiplier = 1.7f;
        [Tooltip("Local offset from the prefab pivot to the striking face of the hammer head.")]
        [SerializeField] private Vector2 hammerHeadLocalOffset = new Vector2(0f, .45f);
        [SerializeField] private int hammerSortingOrder = 100;
        [Tooltip("World-space height of the hammer pivot above the selected block during the swing.")]
        [SerializeField] private float hammerHoverHeightOffset = .85f;

        // Testing override: the hammer can be used repeatedly without waiting
        // for a new level.
        public bool HasBeenUsedThisLevel => false;

        private static HammerBooster activeBooster;
        private static int suppressGameplayThroughFrame = -1;
        private PrototypeBoard boundBoard;
        private BoosterButton boosterButtonRef;
        private CanvasGroup buttonCanvasGroup;
        private bool impactInProgress;
        private BoosterVisualView hammerVisualPrefabView;
        private GameObjectPool<BoosterVisualView> hammerVisualPool;

        private void Awake()
        {
            if (boosterButton == null)
                boosterButton = GetComponent<Button>();

            boosterButtonRef = GetComponent<BoosterButton>();
            buttonCanvasGroup = boosterButton != null ? boosterButton.GetComponent<CanvasGroup>() : null;
            InitializeHammerVisualPool();
        }

        private void Start()
        {
            if (gameplayCamera == null)
                gameplayCamera = PrototypeBootstrap.SceneCamera;

            if (gameplayCamera == null)
                Debug.LogError("[HammerBooster] No gameplay camera is configured on Runtime Piece Factory Bootstrap.", this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveBooster()
        {
            activeBooster = null;
            suppressGameplayThroughFrame = -1;
        }

        private void OnEnable()
        {
            if (boosterButton != null)
                boosterButton.onClick.AddListener(ActivateHammerBooster);

            SynchronizeLevel();
            RefreshButtonState();
        }

        private void Update()
        {
            if (PrototypeBoard.Active != null && PrototypeBoard.Active != boundBoard)
                SynchronizeLevel();

            if (activeBooster == this)
                ProcessTargetInput();

            RefreshButtonState();
        }

        private void OnDisable()
        {
            if (boosterButton != null)
                boosterButton.onClick.RemoveListener(ActivateHammerBooster);

            if (boundBoard != null)
                boundBoard.GameStateChanged -= HandleGameStateChanged;

            CancelHammerSelection();
        }

        /// <summary>
        /// Public Button OnClick entry point. The next valid puzzle-cell tap is
        /// removed; tapping empty space does not consume the one-time booster.
        /// </summary>
        public void ActivateHammerBooster()
        {
            SynchronizeLevel();
            // A player may change their mind after arming the rocket. Selection
            // belongs to one tool at a time, so cancel the other tool first.
            RocketBooster.CancelActiveSelection();

            if (activeBooster == this)
            {
                // Both the Button and BoosterButton compatibility component
                // can forward one UI click. The second delivery must be a
                // no-op; toggling here would immediately cancel the first.
                return;
            }

            if (boundBoard == null || !boundBoard.IsLevelRunning || LevelTimerUI.IsGameOver)
            {
                Debug.LogWarning(
                    $"[HammerBooster] Activation rejected. board={(boundBoard != null)}, running={(boundBoard != null && boundBoard.IsLevelRunning)}, gameOver={LevelTimerUI.IsGameOver}, targeting={IsTargeting}.",
                    this);
                RefreshButtonState();
                return;
            }

            activeBooster = this;
            Debug.Log("[HammerBooster] Targeting enabled.", this);
            RefreshButtonState();
        }

        /// <summary>Cancels targeting without consuming the booster.</summary>
        public void CancelHammerSelection()
        {
            if (activeBooster == this)
                activeBooster = null;

            RefreshButtonState();
        }

        /// <summary>Cancels whichever hammer instance currently owns targeting.</summary>
        public static void CancelActiveSelection()
        {
            if (activeBooster != null)
                activeBooster.CancelHammerSelection();
        }

        private void ProcessTargetInput()
        {
            if (boundBoard == null || !boundBoard.IsLevelRunning || LevelTimerUI.IsGameOver)
            {
                CancelHammerSelection();
                return;
            }

            Vector2 screenPosition;
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase != TouchPhase.Began || IsPointerOverUI(touch.fingerId))
                    return;
                screenPosition = touch.position;
            }
            else
            {
                if (!Input.GetMouseButtonDown(0) || IsPointerOverUI())
                    return;
                screenPosition = Input.mousePosition;
            }

            if (!PuzzleDragController.TryScreenToBoardWorld(screenPosition, out Vector2 worldPosition))
                return;

            if (BoardTargetResolver.TryResolve(boundBoard, worldPosition, out BoardTargetResolver.Target target) &&
                TryStartHammerImpact(target.Piece, target.WorldPosition))
            {
                boundBoard.StartTimer();
                activeBooster = null;
                // Update order between UI/booster/drag components is not fixed.
                // Suppress the rest of this frame so the target tap cannot also
                // begin dragging the newly modified piece.
                suppressGameplayThroughFrame = Time.frameCount;
                RefreshButtonState();
                return;
            }

            Debug.LogWarning(
                "[HammerBooster] No occupied board cell found at target.",
                this);
        }

        private bool TryStartHammerImpact(
            PuzzlePiece piece,
            Vector2 impactPosition)
        {
            // Validate before consuming the booster. The actual removal happens
            // at the animation impact frame, not at target selection.
            if (piece == null || impactInProgress)
                return false;

            // A falling piece has already committed its destination in the
            // board model while its visible cells are still travelling. The
            // hammer is intentionally aimed at the visible cell, so stop the
            // presentation owner before scheduling the strike.
            PuzzleDragController.CancelGridFallForTargetedAction(piece);
            impactInProgress = true;
            PlayHammerSwing(piece, impactPosition);
            return true;
        }

        private void PlayHammerSwing(
            PuzzlePiece piece,
            Vector2 impactPosition)
        {
            if (tweenConfig == null)
            {
                Debug.LogError("[HammerBooster] TweenConfig is required for hammer presentation.", this);
                impactInProgress = false;
                return;
            }

            if (!TryRentHammerVisual(out BoosterVisualView hammerView))
            {
                ApplyHammerImpact(piece, impactPosition);
                impactInProgress = false;
                if (TopologyEditingEnabled)
                    GetHammerBoosterButton()?.TryConsumeUse();
                return;
            }

            GameObject hammer = hammerView.gameObject;
            Transform hammerTransform = hammer.transform;
            Vector3 defaultScale = hammerTransform.localScale;
            Vector3 impactScale = defaultScale * popScaleMultiplier;
            Camera camera = gameplayCamera;
            Vector3 impact = impactPosition;
            bool targetIsLeft = impact.x < 0f;
            float facingYAngle = targetIsLeft ? 0f : -180f;
            float windUpAngle = targetIsLeft ? 18f : -18f;
            const float impactAngle = 0f;
            Vector3 buttonAnchor = GetHammerButtonWorldPosition(camera, impact.z);
            Vector3 screenCentre = GetViewportWorldPoint(camera, .5f, .48f, impact.z);
            // The hammer root stops above the block. From this stabilized point
            // only its head arcs via rotation; no tween pushes it into the mesh.
            Vector3 hoverPoint = impact + Vector3.up * hammerHoverHeightOffset;
            Vector3 exitPoint = buttonAnchor;
            Vector3 exitControl = hoverPoint + Vector3.up * .92f + (exitPoint - hoverPoint).normalized * .42f;

            hammerTransform.position = buttonAnchor;
            hammerTransform.localScale = Vector3.zero;
            hammerTransform.rotation = Quaternion.Euler(0f, facingYAngle, 0f);
            SetHammerSortingOrder(hammer);

            Sequence swing = DOTween.Sequence()
                .SetLink(hammer, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            // Phase 1: royal-style pop from the bottom of the play area.
            swing.Append(hammerTransform.DOMove(screenCentre, tweenConfig.HammerEntranceDuration)
                .SetEase(tweenConfig.HammerEntranceMoveEase));
            swing.Join(hammerTransform.DOScale(impactScale, tweenConfig.HammerEntranceDuration)
                .SetEase(tweenConfig.HammerEntranceScaleEase));
            // Phase 2: arrive above the block, settle, then swing only around
            // the local pivot so the head traces a clean rotational arc.
            swing.Append(hammerTransform.DOMove(hoverPoint, tweenConfig.HammerApproachDuration)
                .SetEase(tweenConfig.HammerApproachEase));
            swing.Join(hammerTransform.DORotate(new Vector3(0f, facingYAngle, windUpAngle), tweenConfig.HammerApproachDuration)
                .SetEase(tweenConfig.HammerApproachEase));
            swing.AppendInterval(tweenConfig.HammerWindUpDelay);
            swing.Append(hammerTransform.DORotate(new Vector3(0f, facingYAngle, impactAngle), tweenConfig.HammerStrikeDuration)
                .SetEase(tweenConfig.HammerStrikeEase));
            swing.AppendCallback(() => ApplyHammerImpact(piece, impactPosition));
            // Phase 4: shrink along an exit arc once the hit has registered.
            swing.Append(DOVirtual.Float(0f, 1f, tweenConfig.HammerExitDuration, progress =>
                hammerTransform.position = QuadraticBezier(hoverPoint, exitControl, exitPoint, progress))
                .SetEase(tweenConfig.HammerExitEase));
            swing.Join(hammerTransform.DOScale(Vector3.zero, tweenConfig.HammerExitDuration)
                .SetEase(tweenConfig.HammerExitScaleEase));
            swing.Join(hammerTransform.DORotate(new Vector3(0f, facingYAngle, 0f), tweenConfig.HammerExitDuration)
                .SetEase(tweenConfig.HammerExitEase));
            swing.OnComplete(() =>
            {
                hammerVisualPool.Return(hammerView);
                impactInProgress = false;
                if (TopologyEditingEnabled)
                    GetHammerBoosterButton()?.TryConsumeUse();
            });
        }

        private BoosterButton GetHammerBoosterButton()
        {
            return boosterButtonRef;
        }

        private void ApplyHammerImpact(PuzzlePiece piece, Vector2 impactPosition)
        {
            if (TopologyEditingEnabled &&
                piece != null && piece.TryRemoveCellAt(impactPosition, out PuzzlePiece.RemovedCell cell))
            {
                Color color = new Color(cell.color.r, cell.color.g, cell.color.b, 1f);
                LevelProgressManager manager = LevelProgressManager.Instance;
                if (manager != null)
                    manager.SpawnFlyingVoxelBurst(
                        cell.worldPosition,
                        color,
                        cell.progressUnits,
                        cell.renderedVoxelCount);
            }

            Camera camera = gameplayCamera;
            if (camera != null)
                camera.transform.DOShakePosition(
                        tweenConfig.HammerCameraShakeDuration,
                        tweenConfig.HammerCameraShakeStrength,
                        tweenConfig.HammerCameraShakeVibrato,
                        tweenConfig.HammerCameraShakeRandomness,
                        false,
                        true)
                    .SetLink(camera.gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
        }

        private static Vector3 GetViewportWorldPoint(Camera camera, float x, float y, float z)
        {
            if (camera == null)
                return new Vector3(0f, 0f, z);

            float depth = Mathf.Abs(camera.transform.position.z - z);
            Vector3 point = camera.ViewportToWorldPoint(new Vector3(x, y, depth));
            point.z = z;
            return point;
        }

        private Vector3 GetHammerButtonWorldPosition(Camera gameCamera, float z)
        {
            if (boosterButton == null || gameCamera == null)
                return GetViewportWorldPoint(gameCamera, .5f, .05f, z);

            RectTransform buttonRect = boosterButton.GetComponent<RectTransform>();
            Canvas canvas = boosterButton.GetComponentInParent<Canvas>();
            Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(canvasCamera, buttonRect.position);
            float depth = Mathf.Abs(gameCamera.transform.position.z - z);
            Vector3 worldPosition = gameCamera.ScreenToWorldPoint(
                new Vector3(screenPosition.x, screenPosition.y, depth));
            worldPosition.z = z;
            return worldPosition;
        }

        private static Vector3 QuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float progress)
        {
            float inverse = 1f - progress;
            return inverse * inverse * start + 2f * inverse * progress * control + progress * progress * end;
        }

        private void InitializeHammerVisualPool()
        {
            if (hammerVisualPrefab == null)
                return;

            hammerVisualPrefabView = hammerVisualPrefab.GetComponent<BoosterVisualView>();
            if (hammerVisualPrefabView == null)
            {
                Debug.LogWarning("[HammerBooster] Hammer visual prefab has no BoosterVisualView; hammer will run without a presentation visual.", this);
                return;
            }

            hammerVisualPool = new GameObjectPool<BoosterVisualView>(hammerVisualPrefabView, transform, 1);
            hammerVisualPool.Prewarm();
        }

        private void SetHammerSortingOrder(GameObject hammer)
        {
            SpriteRenderer[] renderers = hammer.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sortingOrder = hammerSortingOrder + i;
        }

        private bool TryRentHammerVisual(out BoosterVisualView hammerView)
        {
            hammerView = null;
            return hammerVisualPool != null && hammerVisualPool.TryRent(out hammerView);
        }

        private static bool IsPointerOverUI(int fingerId = -1)
        {
            if (EventSystem.current == null)
                return false;

            return fingerId >= 0
                ? EventSystem.current.IsPointerOverGameObject(fingerId)
                : EventSystem.current.IsPointerOverGameObject();
        }

        private void SynchronizeLevel()
        {
            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard == null || activeBoard == boundBoard)
                return;

            if (boundBoard != null)
                boundBoard.GameStateChanged -= HandleGameStateChanged;

            CancelHammerSelection();
            boundBoard = activeBoard;
            boundBoard.GameStateChanged += HandleGameStateChanged;
            RefreshButtonState();
        }

        private void HandleGameStateChanged(GameState previousState, GameState nextState)
        {
            if (nextState == GameState.LevelComplete || nextState == GameState.Result)
                CancelHammerSelection();

            RefreshButtonState();
        }

        private void RefreshButtonState()
        {
            if (boosterButton == null)
                return;

            bool visible = true;
            if (buttonCanvasGroup != null)
            {
                buttonCanvasGroup.alpha = visible ? 1f : 0f;
                buttonCanvasGroup.interactable = visible;
                buttonCanvasGroup.blocksRaycasts = visible;
            }
            boosterButton.interactable =
                visible &&
                activeBooster != this &&
                boundBoard != null &&
                boundBoard.IsLevelRunning &&
                !LevelTimerUI.IsGameOver;
        }
    }
}
