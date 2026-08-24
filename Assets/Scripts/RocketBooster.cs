using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GravityPuzzle.Config;
using GravityPuzzle.Core.StateMachine;
using GravityPuzzle.Gameplay.Input;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;

namespace GravityPuzzle
{
    /// <summary>
    /// Rocket Booster component.
    /// Works seamlessly with BoosterButton component or standalone.
    /// Manages piece selection and launching a rocket to destroy the piece.
    /// </summary>
    public sealed class RocketBooster : MonoBehaviour
    {
        public static bool IsTargeting =>
            activeBooster != null || Time.frameCount <= suppressGameplayThroughFrame;

        [Header("UI References (Assign in Inspector)")]
        [Tooltip("Optional BoosterButton component reference. Auto-found if null.")]
        [SerializeField] private BoosterButton boosterButtonRef;

        [Tooltip("The UI Button that activates this Rocket Booster.")]
        [SerializeField] private Button boosterButton;

        [Tooltip("Text component displaying remaining booster count (TextMeshPro).")]
        [SerializeField] private TextMeshProUGUI countTmpText;

        [Tooltip("Alternative Text component displaying remaining booster count (Legacy UI Text).")]
        [SerializeField] private Text countUiText;

        [Header("Booster Count Settings")]
        [SerializeField, Tooltip("Starting count for rocket booster.")]
        private int initialCount = 3;

        [Header("Rocket Visual & Launch Settings")]
        [Tooltip("Optional authored rocket visual prefab. Add BoosterVisualView to its root to enable pooled presentation.")]
        [SerializeField] private GameObject rocketVisualPrefab;
        [SerializeField] private TweenConfig tweenConfig;
        [SerializeField, Tooltip("Gameplay camera used for rocket presentation. Defaults to the camera supplied by Runtime Piece Factory Bootstrap.")]
        private Camera gameplayCamera;

        [SerializeField, Tooltip("Rocket scale multiplier.")]
        private Vector3 rocketScale = Vector3.one;

        [SerializeField, Tooltip("Sorting order for rocket renderers.")]
        private int rocketSortingOrder = 30000;

        private static RocketBooster activeBooster;
        private static int suppressGameplayThroughFrame = -1;
        private PrototypeBoard boundBoard;
        private CanvasGroup buttonCanvasGroup;
        private bool launchInProgress;
        private int remainingCount = 3;
        private BoosterVisualView rocketVisualPrefabView;
        private GameObjectPool<BoosterVisualView> rocketVisualPool;

        public int RemainingCount => boosterButtonRef != null ? boosterButtonRef.RemainingCount : remainingCount;

        private void Awake()
        {
            remainingCount = initialCount;
            EnsureReferences();
            buttonCanvasGroup = boosterButton != null ? boosterButton.GetComponent<CanvasGroup>() : null;
            InitializeRocketVisualPool();
        }

        private void Start()
        {
            if (gameplayCamera == null)
                gameplayCamera = PrototypeBootstrap.SceneCamera;

            if (gameplayCamera == null)
                Debug.LogError("[RocketBooster] No gameplay camera is configured on Runtime Piece Factory Bootstrap.", this);
        }

        private void OnEnable()
        {
            EnsureReferences();
            if (boosterButtonRef != null)
            {
                boosterButtonRef.onBoosterClicked.RemoveListener(ActivateRocketBooster);
                boosterButtonRef.onBoosterClicked.AddListener(ActivateRocketBooster);
            }
            else if (boosterButton != null)
            {
                boosterButton.onClick.RemoveListener(ActivateRocketBooster);
                boosterButton.onClick.AddListener(ActivateRocketBooster);
            }

            SynchronizeLevel();
            UpdateCountUI();
            RefreshButtonState();
        }

        private void Update()
        {
            if (PrototypeBoard.Active != null && PrototypeBoard.Active != boundBoard)
            {
                SynchronizeLevel();
            }

            if (activeBooster == this)
            {
                ProcessTargetInput();
            }

            RefreshButtonState();
        }

        private void OnDisable()
        {
            if (boosterButtonRef != null)
            {
                boosterButtonRef.onBoosterClicked.RemoveListener(ActivateRocketBooster);
            }
            if (boosterButton != null)
            {
                boosterButton.onClick.RemoveListener(ActivateRocketBooster);
            }

            if (boundBoard != null)
                boundBoard.GameStateChanged -= HandleGameStateChanged;

            CancelRocketSelection();
        }

        public void ActivateRocketBooster()
        {
            SynchronizeLevel();
            // Only one board-targeting tool may own the next board tap.
            HammerBooster.CancelActiveSelection();

            if (activeBooster == this)
            {
                // The BoosterButton event can be delivered after this
                // component's direct Button listener for the same UI press.
                // Keep the selection armed instead of toggling it off.
                return;
            }

            int currentUses = boosterButtonRef != null ? boosterButtonRef.RemainingCount : remainingCount;
            if (currentUses <= 0 ||
                boundBoard == null ||
                !boundBoard.IsLevelRunning ||
                LevelTimerUI.IsGameOver)
            {
                Debug.LogWarning($"[RocketBooster] Cannot activate: currentUses={currentUses}, IsGameOver={LevelTimerUI.IsGameOver}");
                RefreshButtonState();
                return;
            }

            activeBooster = this;
            Debug.Log($"[RocketBooster] Targeting enabled. remainingUses={currentUses}.", this);
            RefreshButtonState();
        }

        public void CancelRocketSelection()
        {
            if (activeBooster == this)
            {
                activeBooster = null;
            }

            RefreshButtonState();
        }

        /// <summary>Cancels whichever rocket instance currently owns targeting.</summary>
        public static void CancelActiveSelection()
        {
            if (activeBooster != null)
                activeBooster.CancelRocketSelection();
        }

        private void ProcessTargetInput()
        {
            int currentUses = boosterButtonRef != null ? boosterButtonRef.RemainingCount : remainingCount;
            if (boundBoard == null ||
                !boundBoard.IsLevelRunning ||
                LevelTimerUI.IsGameOver ||
                currentUses <= 0)
            {
                CancelRocketSelection();
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
                TryStartRocketImpact(target.Piece))
            {
                Debug.Log($"[RocketBooster] Target tap hit piece: {target.Piece.name}");
                boundBoard.StartTimer();
                activeBooster = null;
                suppressGameplayThroughFrame = Time.frameCount;
                RefreshButtonState();
                return;
            }

            Debug.LogWarning(
                "[RocketBooster] No occupied board cell found at target.",
                this);
        }

        private bool TryStartRocketImpact(PuzzlePiece piece)
        {
            int currentUses = boosterButtonRef != null ? boosterButtonRef.RemainingCount : remainingCount;
            if (piece == null || piece.IsBeingShredded || launchInProgress || currentUses <= 0)
                return false;

            // Prevent an in-flight grid presentation tween from continuing to
            // move the piece after the rocket takes ownership of its transform.
            PuzzleDragController.CancelGridFallForTargetedAction(piece);
            launchInProgress = true;
            StartCoroutine(PlayRocketLaunchSequence(piece));
            return true;
        }

        private IEnumerator PlayRocketLaunchSequence(PuzzlePiece piece)
        {
            if (piece == null || tweenConfig == null)
            {
                if (tweenConfig == null)
                    Debug.LogError("[RocketBooster] TweenConfig is required for rocket presentation.", this);
                launchInProgress = false;
                yield break;
            }

            if (!piece.TryBeginShredderHandoff())
            {
                launchInProgress = false;
                yield break;
            }

            piece.PrepareForPresentationRemoval();

            Camera camera = gameplayCamera;
            Vector3 piecePos = GetPieceCenter(piece);

            float camY = camera != null ? camera.transform.position.y : 0f;
            float camOrtho = camera != null ? camera.orthographicSize : 10f;
            float offscreenBottomY = camY - camOrtho - 6f;

            Vector3 startPos = new Vector3(piecePos.x, offscreenBottomY, -3f);
            Vector3 centerPos = new Vector3(piecePos.x, piecePos.y, -3f);

            // Visuals are optional presentation only. Gameplay must not depend
            // on a prefab being configured.
            if (!TryRentRocketVisual(out BoosterVisualView rocketView))
            {
                CompleteRocketImpact(piece, piecePos, camY + camOrtho + 7f);
                yield break;
            }

            GameObject rocket = rocketView.gameObject;
            // 1. Rent Rocket Visual at off-screen bottom position
            rocket.transform.position = startPos;
            rocket.transform.rotation = Quaternion.identity;
            SetSortingOrder(rocket, rocketSortingOrder);

            // 2. Animate rocket from screen bottom up to piece center.
            Tween entranceTween = rocket.transform.DOMove(centerPos, tweenConfig.RocketEntranceDuration)
                .SetEase(tweenConfig.RocketEntranceEase)
                .SetLink(rocket, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            yield return entranceTween.WaitForCompletion();

            if (rocket == null || piece == null)
            {
                if (rocket != null) rocketVisualPool.Return(rocketView);
                launchInProgress = false;
                yield break;
            }

            // 3. Attach piece transform to rocket at piece center
            piece.transform.SetParent(rocket.transform, true);
            // The carrier must remain visibly in front of the carried piece
            // for the entire flight.  The piece keeps a high order so it stays
            // above the board, while the rocket is one step higher.
            SetPieceSortingOrder(piece, rocketSortingOrder - 1);

            // 4. Pause at piece center for target-pause duration (with engine ignition micro-rumble)
            Vector3 initialPos = rocket.transform.position;
            float elapsed = 0f;
            while (piece != null && rocket != null && elapsed < tweenConfig.RocketTargetPauseDuration)
            {
                elapsed += Time.deltaTime;
                rocket.transform.position = initialPos + new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(-0.03f, 0.03f), 0f);
                yield return null;
            }

            if (rocket != null) rocket.transform.position = initialPos;

            if (rocket == null)
            {
                launchInProgress = false;
                yield break;
            }

            // 5. BLAST OFF! Launch rocket + piece into the sky
            float targetY = camY + camOrtho + 7f;


            Tween launchTween = rocket.transform.DOMoveY(targetY, tweenConfig.RocketLaunchDuration)
                .SetEase(tweenConfig.RocketLaunchEase)
                .SetLink(rocket, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            yield return launchTween.WaitForCompletion();

            // 6. Rocket launch animation finished.
            CompleteRocketImpact(piece, piecePos, targetY);
            rocketVisualPool.Return(rocketView);
        }

        private void CompleteRocketImpact(PuzzlePiece piece, Vector3 piecePos, float targetY)
        {
            Color pieceColor = piece != null ? piece.VisualColor : Color.white;
            BoosterButton targetButton = GetRocketBoosterButton();
            if (targetButton != null)
            {
                targetButton.TryConsumeUse();
            }
            else
            {
                remainingCount--;
                if (remainingCount < 0) remainingCount = 0;
                UpdateCountUI();
            }

            // 7. Award progress & despawn
            if (LevelProgressManager.Instance != null && piece != null)
            {
                LevelProgressManager.Instance.SpawnFlyingVoxelBurst(
                    new Vector2(piecePos.x, targetY),
                    pieceColor,
                    piece.RemainingProgressUnits,
                    piece.ActiveVoxelPresentationCount);
            }

            if (piece != null)
            {
                piece.ReleaseInstance();
            }

            activeBooster = null;
            launchInProgress = false;
            RefreshButtonState();
        }

        private Vector3 GetPieceCenter(PuzzlePiece piece)
        {
            if (piece == null) return Vector3.zero;

            SpriteRenderer[] renderers = piece.GetComponentsInChildren<SpriteRenderer>();
            if (renderers != null && renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    if (renderers[i] != null && renderers[i].enabled)
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }
                }
                return bounds.center;
            }

            Collider2D[] colliders = piece.GetComponentsInChildren<Collider2D>();
            if (colliders != null && colliders.Length > 0)
            {
                Bounds bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                {
                    if (colliders[i] != null && colliders[i].enabled)
                    {
                        bounds.Encapsulate(colliders[i].bounds);
                    }
                }
                return bounds.center;
            }

            return piece.transform.position;
        }

        private void InitializeRocketVisualPool()
        {
            if (rocketVisualPrefab == null)
                return;

            rocketVisualPrefabView = rocketVisualPrefab.GetComponent<BoosterVisualView>();
            if (rocketVisualPrefabView == null)
            {
                Debug.LogWarning("[RocketBooster] Rocket visual prefab has no BoosterVisualView; rocket will run without a presentation visual.", this);
                return;
            }

            rocketVisualPool = new GameObjectPool<BoosterVisualView>(rocketVisualPrefabView, transform, 1);
            rocketVisualPool.Prewarm();
        }

        private bool TryRentRocketVisual(out BoosterVisualView rocketView)
        {
            rocketView = null;
            if (rocketVisualPool == null || !rocketVisualPool.TryRent(out rocketView))
                return false;

            Vector3 effectiveScale = rocketScale.sqrMagnitude > 0.0001f ? rocketScale : Vector3.one;
            rocketView.transform.localScale = Vector3.Scale(rocketView.AuthoredScale, effectiveScale);
            SetSortingOrder(rocketView.gameObject, rocketSortingOrder);
            return true;
        }

        private void SetSortingOrder(GameObject go, int order)
        {
            SpriteRenderer[] renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var r in renderers)
            {
                if (r != null)
                {
                    r.sortingOrder = order;
                    r.maskInteraction = SpriteMaskInteraction.None;
                }
            }

            Canvas[] canvases = go.GetComponentsInChildren<Canvas>(true);
            foreach (var c in canvases)
            {
                if (c != null)
                {
                    c.overrideSorting = true;
                    c.sortingOrder = order;
                }
            }
        }

        private static void SetPieceSortingOrder(PuzzlePiece piece, int order)
        {
            if (piece == null)
                return;

            SpriteRenderer[] renderers = piece.GetComponentsInChildren<SpriteRenderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                    renderers[index].sortingOrder = order;
            }

            if (piece.Outline != null)
                piece.Outline.sortingOrder = order + 1;
        }

        private void EnsureReferences()
        {
            if (boosterButtonRef == null)
            {
                boosterButtonRef = GetComponent<BoosterButton>();
            }

            if (boosterButton == null)
            {
                boosterButton = GetComponent<Button>();
            }

            if (countTmpText == null && countUiText == null)
            {
                countTmpText = GetComponentInChildren<TextMeshProUGUI>(true);
                if (countTmpText == null)
                {
                    countUiText = GetComponentInChildren<Text>(true);
                }
            }
        }

        private BoosterButton GetRocketBoosterButton()
        {
            return boosterButtonRef;
        }

        public void UpdateCountUI()
        {
            if (boosterButtonRef != null)
            {
                boosterButtonRef.UpdateCountUI();
                return;
            }

            string countStr = remainingCount.ToString();
            if (countTmpText != null)
            {
                countTmpText.text = countStr;
            }
            if (countUiText != null)
            {
                countUiText.text = countStr;
            }
        }

        private void SynchronizeLevel()
        {
            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard == null || activeBoard == boundBoard)
                return;

            if (boundBoard != null)
                boundBoard.GameStateChanged -= HandleGameStateChanged;

            CancelRocketSelection();
            boundBoard = activeBoard;
            boundBoard.GameStateChanged += HandleGameStateChanged;
            if (boosterButtonRef != null)
            {
                boosterButtonRef.ResetCount(initialCount);
            }
            else
            {
                remainingCount = initialCount;
                UpdateCountUI();
            }
            RefreshButtonState();
        }

        private void HandleGameStateChanged(GameState previousState, GameState nextState)
        {
            if (nextState == GameState.LevelComplete || nextState == GameState.Result)
                CancelRocketSelection();

            RefreshButtonState();
        }

        private void RefreshButtonState()
        {
            if (boosterButtonRef != null)
            {
                boosterButtonRef.RefreshButtonState();
                return;
            }

            if (boosterButton == null)
                return;

            bool visible = true;
            if (buttonCanvasGroup != null)
            {
                buttonCanvasGroup.alpha = visible ? 1f : 0.4f;
                buttonCanvasGroup.interactable = visible;
                buttonCanvasGroup.blocksRaycasts = visible;
            }
            int currentUses = boosterButtonRef != null ? boosterButtonRef.RemainingCount : remainingCount;
            boosterButton.interactable =
                visible &&
                currentUses > 0 &&
                activeBooster != this &&
                boundBoard != null &&
                boundBoard.IsLevelRunning &&
                !LevelTimerUI.IsGameOver;
        }

        private static bool IsPointerOverUI(int fingerId = -1)
        {
            if (EventSystem.current == null)
                return false;

            if (fingerId >= 0)
                return EventSystem.current.IsPointerOverGameObject(fingerId);

            return EventSystem.current.IsPointerOverGameObject();
        }
    }
}
