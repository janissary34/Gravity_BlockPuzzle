using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GravityPuzzle.Config;
using GravityPuzzle.Core.StateMachine;
using GravityPuzzle.Gameplay.Input;
using GravityPuzzle.Gameplay.Tutorial;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Infrastructure.Services;
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

        [Tooltip("Canvas group used to control the Rocket Booster button visibility and input.")]
        [SerializeField] private CanvasGroup buttonCanvasGroup;

        [Tooltip("Scene-authored selection glow shown while Rocket is introduced or targeting.")]
        [SerializeField] private GameObject selectionGlow;

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
        private bool launchInProgress;
        private int remainingCount = 3;
        private int lastActivationFrame = -1;
        private BoosterVisualView rocketVisualPrefabView;
        private GameObjectPool<BoosterVisualView> rocketVisualPool;

        public int RemainingCount => boosterButtonRef != null
            ? boosterButtonRef.RemainingCount
            : BoosterInventoryRuntime.Current.GetCount(BoosterRewardType.Rocket);

        public void SetSelectionGlowVisible(bool visible)
        {
            if (selectionGlow != null && selectionGlow.activeSelf != visible)
                selectionGlow.SetActive(visible);

            if (boosterButtonRef != null)
                boosterButtonRef.SetSelectionGlowVisible(visible);
        }

        private void Awake()
        {
            remainingCount = initialCount;
            EnsureReferences();
            SetSelectionGlowVisible(false);
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

            BoosterInventoryRuntime.Current.CountChanged += HandleInventoryCountChanged;

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

            IBoosterFirstUseTutorialGate tutorialGate = BoosterFirstUseTutorialRuntime.Gate;
            bool isFirstUse = tutorialGate != null &&
                (tutorialGate.IsBoosterHighlighted(BoosterRewardType.Rocket) ||
                 tutorialGate.IsFirstUseActive(BoosterRewardType.Rocket));

            if (isFirstUse)
            {
                SetSelectionGlowVisible(true);
            }
            else if (activeBooster != this)
            {
                SetSelectionGlowVisible(false);
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

            BoosterInventoryRuntime.Current.CountChanged -= HandleInventoryCountChanged;

            if (boundBoard != null)
                boundBoard.GameStateChanged -= HandleGameStateChanged;

            SetSelectionGlowVisible(false);
            CancelRocketSelection();
        }

        private void HandleInventoryCountChanged(BoosterRewardType changedType, int count)
        {
            if (changedType != BoosterRewardType.Rocket)
                return;

            UpdateCountUI();
            RefreshButtonState();
        }

        public void ActivateRocketBooster()
        {
            // A BoosterButton and a legacy direct Button listener can both
            // dispatch one UI press. Collapse that compatibility duplicate so
            // only a later physical press toggles targeting off.
            if (lastActivationFrame == Time.frameCount)
                return;

            lastActivationFrame = Time.frameCount;
            SynchronizeLevel();
            IBoosterFirstUseTutorialGate tutorialGate = BoosterFirstUseTutorialRuntime.Gate;
            if (tutorialGate != null && !tutorialGate.AllowsBoosterActivation(BoosterRewardType.Rocket))
                return;
            // Only one board-targeting tool may own the next board tap.
            HammerBooster.CancelActiveSelection();

            if (activeBooster == this)
            {
                CancelRocketSelection();
                return;
            }

            int currentUses = RemainingCount;
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
            SetSelectionGlowVisible(true);
            BoosterTargetingPresentation.Show(BoosterTargetingPresentation.Mode.Rocket);
            tutorialGate?.NotifyBoosterActivated(BoosterRewardType.Rocket);
            Debug.Log($"[RocketBooster] Targeting enabled. remainingUses={currentUses}.", this);
            RefreshButtonState();
        }

        public void CancelRocketSelection()
        {
            bool ownedSelection = activeBooster == this;
            if (ownedSelection)
            {
                activeBooster = null;
            }

            if (ownedSelection)
                BoosterTargetingPresentation.Hide();

            IBoosterFirstUseTutorialGate gate = BoosterFirstUseTutorialRuntime.Gate;
            bool isFirstUseActive = gate != null && gate.IsFirstUseActive(BoosterRewardType.Rocket);
            if (!isFirstUseActive)
                SetSelectionGlowVisible(false);

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
            int currentUses = RemainingCount;
            if (boundBoard == null ||
                !boundBoard.IsLevelRunning ||
                LevelTimerUI.IsGameOver ||
                currentUses <= 0)
            {
                CancelRocketSelection();
                return;
            }

            Vector2 screenPosition = default;
            bool receivedPointerDown = false;
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    screenPosition = touch.position;
                    receivedPointerDown = true;
                }
            }

            // The Device Simulator can retain a non-Began legacy touch while
            // still reporting a new mouse press. Do not let that stale touch
            // swallow cancellation or targeting input.
            if (!receivedPointerDown && Input.GetMouseButtonDown(0))
            {
                screenPosition = Input.mousePosition;
                receivedPointerDown = true;
            }

            if (!receivedPointerDown)
                return;

            ProcessTargetPosition(screenPosition);
        }

        /// <summary>
        /// Routes a pointer position from the UI targeting surface to the
        /// armed rocket. Returns false when no rocket is armed.
        /// </summary>
        public static bool TryHandleTargetingPointer(Vector2 screenPosition)
        {
            if (activeBooster == null)
                return false;

            activeBooster.ProcessTargetPosition(screenPosition);
            return true;
        }

        private void ProcessTargetPosition(Vector2 screenPosition)
        {
            if (activeBooster != this)
                return;

            if (!PuzzleDragController.TryScreenToBoardWorld(screenPosition, out Vector2 worldPosition))
            {
                // An invalid target click is an explicit cancellation; it does
                // not consume a rocket use.
                CancelRocketSelection();
                return;
            }

            Debug.Log($"[RocketBooster] Target click received at screen={screenPosition}, world={worldPosition}.", this);

            IBoosterFirstUseTutorialGate tutorialGate = BoosterFirstUseTutorialRuntime.Gate;
            if (BoardTargetResolver.TryResolve(boundBoard, worldPosition, out BoardTargetResolver.Target target))
            {
                Debug.Log(
                    $"[RocketBooster] Target resolved: piece='{target.Piece.name}', sourceId={target.Piece.SourcePieceId}, cell={target.Cell}, world={target.WorldPosition}.",
                    target.Piece);

                if ((tutorialGate == null || tutorialGate.AllowsTarget(BoosterRewardType.Rocket, target)) &&
                    TryStartRocketImpact(target.Piece))
                {
                    Debug.Log($"[RocketBooster] Target tap accepted: {target.Piece.name}");
                    boundBoard.StartTimer();
                    activeBooster = null;
                    BoosterTargetingPresentation.Hide();
                    suppressGameplayThroughFrame = Time.frameCount;
                    RefreshButtonState();
                    return;
                }

                if (tutorialGate != null && tutorialGate.BlocksBoardDrag)
                    return;

                Debug.LogWarning(
                    $"[RocketBooster] Target was resolved but rejected before launch: '{target.Piece.name}'.",
                    target.Piece);
            }

            // Empty-space taps and rejected targets cancel targeting without
            // consuming the booster, so the player can resume normal play.
            CancelRocketSelection();
        }

        private bool TryStartRocketImpact(PuzzlePiece piece)
        {
            int currentUses = RemainingCount;
            if (piece == null || piece.IsShredderInteractionLocked || launchInProgress || currentUses <= 0)
                return false;

            // Prevent an in-flight grid presentation tween from continuing to
            // move the piece after the rocket takes ownership of its transform.
            PuzzleDragController.CancelGridFallForTargetedAction(piece);

            // Validate the authoritative grid handoff before consuming the
            // target tap. This keeps one-cell pieces selectable even when a
            // previous presentation left their grid state unavailable.
            if (!piece.TryBeginShredderHandoff(reportDestructionImmediately: false))
            {
                Debug.LogWarning(
                    $"[RocketBooster] Target '{piece.name}' could not reserve its grid cells for rocket handoff.",
                    piece);
                return false;
            }

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

            piece.PrepareForPresentationRemoval();

            Camera camera = gameplayCamera;
            Vector3 piecePos = piece.GetPreferredRocketAttachmentPoint();

            float camY = camera != null ? camera.transform.position.y : 0f;
            float camOrtho = camera != null ? camera.orthographicSize : 10f;
            float offscreenBottomY = camY - camOrtho - 6f;

            Vector3 startPos = new Vector3(piecePos.x, offscreenBottomY, -3f);
            Vector3 centerPos = new Vector3(piecePos.x, piecePos.y, -3f);

            // Visuals are optional presentation only. Gameplay must not depend
            // on a prefab being configured.
            if (!TryRentRocketVisual(out BoosterVisualView rocketView))
            {
                // Preserve the same ice-impact timing when the optional
                // visual is unavailable: the ice breaks only once the
                // entrance travel has reached the targeted piece.
                yield return new WaitForSeconds(tweenConfig.RocketEntranceDuration);
                if (piece != null && piece.IsFrozen)
                    piece.BreakIceForRocket();

                yield return new WaitForSeconds(tweenConfig.RocketTargetPauseDuration);
                BreakTargetJointsForLaunch(piece);
                yield return new WaitForSeconds(tweenConfig.RocketLaunchDuration);
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

            // 3. The rocket reached the piece. Ice is broken at this impact
            // moment, rather than when the player first selected the target.
            if (piece.IsFrozen)
                piece.BreakIceForRocket(
                    GetRocketSortingLayerId(rocket),
                    rocketSortingOrder + 1);

            // 4. The rocket reached a real occupied cell, not the bounding-box
            // centre (which can be empty for an L-shaped piece). Parenting at
            // this point makes the carrier feel physically attached to it.
            piece.transform.SetParent(rocket.transform, true);
            // The carrier must remain visibly in front of the carried piece
            // for the entire flight.  The piece keeps a high order so it stays
            // above the board, while the rocket is one step higher.
            SetPieceSortingOrder(piece, rocketSortingOrder - 1);

            // 5. Pause at piece center for target-pause duration (with engine ignition micro-rumble)
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

            // The piece remains connected while the rocket approaches and
            // locks on. Its joints break only when the rocket takes it away.
            BreakTargetJointsForLaunch(piece);

            // 6. BLAST OFF! Launch rocket + piece into the sky
            float targetY = camY + camOrtho + 7f;


            Tween launchTween = rocket.transform.DOMoveY(targetY, tweenConfig.RocketLaunchDuration)
                .SetEase(tweenConfig.RocketLaunchEase)
                .SetLink(rocket, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            yield return launchTween.WaitForCompletion();

            // 7. Rocket launch animation finished.
            CompleteRocketImpact(piece, piecePos, targetY);
            rocketVisualPool.Return(rocketView);
        }

        private static void BreakTargetJointsForLaunch(PuzzlePiece piece)
        {
            if (piece == null)
                return;

            if (PrototypeBoard.Active?.BreakJointsForPiece(piece) > 0)
                PuzzleDragController.WakeUpGravity();
        }

        private void CompleteRocketImpact(PuzzlePiece piece, Vector3 piecePos, float targetY)
        {
            Color pieceColor = piece != null ? piece.VisualColor : Color.white;
            BoosterButton targetButton = GetRocketBoosterButton();
            bool consumed = targetButton != null
                ? targetButton.TryConsumeUse()
                : BoosterInventoryRuntime.Current.TryConsume(BoosterRewardType.Rocket);

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
                // The carried piece is now off-screen. Report its destruction
                // only at this completion point so ice counters decrement when
                // the rocket removal is visually complete, not on target tap.
                piece.ReportDestroyed();
                piece.ReleaseInstance();
                HapticFeedbackRuntime.RocketImpact();
            }

            if (consumed)
            {
                SetSelectionGlowVisible(false);
                BoosterFirstUseTutorialRuntime.Gate?.NotifyEffectApplied(BoosterRewardType.Rocket);
            }

            activeBooster = null;
            launchInProgress = false;
            RefreshButtonState();
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

            // This is a world-space sprite, so it must not inherit the UI
            // button/Canvas transform used by this booster component.
            rocketVisualPool = new GameObjectPool<BoosterVisualView>(rocketVisualPrefabView, null, 1);
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

        private static int GetRocketSortingLayerId(GameObject rocket)
        {
            SpriteRenderer renderer = rocket != null
                ? rocket.GetComponentInChildren<SpriteRenderer>(true)
                : null;
            return renderer != null ? renderer.sortingLayerID : 0;
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

            if (buttonCanvasGroup == null)
            {
                if (boosterButton != null)
                    buttonCanvasGroup = boosterButton.GetComponent<CanvasGroup>();
                if (buttonCanvasGroup == null)
                    buttonCanvasGroup = GetComponent<CanvasGroup>();
            }

            if (selectionGlow == null)
            {
                if (boosterButtonRef != null && boosterButtonRef.SelectionGlow != null)
                    selectionGlow = boosterButtonRef.SelectionGlow;
                else
                {
                    Transform glowChild = transform.Find("RocketSelectionGlow")
                        ?? transform.Find("selectionGlow")
                        ?? transform.Find("SelectionGlow");
                    if (glowChild != null)
                        selectionGlow = glowChild.gameObject;
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

            string countStr = RemainingCount.ToString();
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
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            int levelUseCount = level != null ? level.rocketBoosterCount : initialCount;
            if (boosterButtonRef != null)
            {
                boosterButtonRef.ConfigureLevelUseCount(levelUseCount);
            }
            else
            {
                remainingCount = levelUseCount;
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
            if (BoosterTargetingPresentation.IsBoosterButtonSuppressed(buttonCanvasGroup))
                return;

            if (boosterButton == null)
                return;

            int currentUses = RemainingCount;
            bool hasUses = currentUses > 0;
            bool isIntroduced = boosterButtonRef != null
                ? boosterButtonRef.IsIntroduced
                : (GravityLevelRuntime.CurrentLevelNumber >= BoosterButton.GetIntroductionLevel(BoosterRewardType.Rocket) &&
                   (BoosterInventoryRuntime.Current.IsUnlocked(BoosterRewardType.Rocket) || hasUses));

            if (!isIntroduced)
            {
                if (buttonCanvasGroup != null)
                {
                    buttonCanvasGroup.alpha = 1f;
                    buttonCanvasGroup.interactable = false;
                    buttonCanvasGroup.blocksRaycasts = false;
                }

                boosterButton.interactable = false;
                return;
            }

            if (buttonCanvasGroup != null)
            {
                buttonCanvasGroup.alpha = hasUses ? 1f : 0.55f;
                buttonCanvasGroup.interactable = hasUses;
                buttonCanvasGroup.blocksRaycasts = hasUses;
            }

            // A repeat click while targeting is already armed is a no-op, so
            // retaining interactivity prevents Unity from tinting its icon as disabled.
            boosterButton.interactable =
                hasUses &&
                boundBoard != null &&
                boundBoard.IsLevelRunning &&
                !LevelTimerUI.IsGameOver;
        }

    }
}
