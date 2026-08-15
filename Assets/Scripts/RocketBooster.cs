using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
        [Tooltip("Optional rocket visual prefab. A runtime procedurally generated rocket is used when omitted.")]
        [SerializeField] private GameObject rocketVisualPrefab;

        [SerializeField, Tooltip("Duration in seconds for rocket to travel from bottom off-screen to piece center.")]
        private float entranceDuration = 0.75f;

        [SerializeField, Tooltip("Pause duration in seconds at piece center before blasting off.")]
        private float piecePauseDuration = 1.0f;

        [SerializeField, Tooltip("Rocket launch animation duration in seconds.")]
        private float launchDuration = 0.55f;

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

        public int RemainingCount => boosterButtonRef != null ? boosterButtonRef.RemainingCount : remainingCount;

        private void Awake()
        {
            remainingCount = initialCount;
            EnsureReferences();
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

            CancelRocketSelection();
        }

        public void ActivateRocketBooster()
        {
            SynchronizeLevel();
            int currentUses = boosterButtonRef != null ? boosterButtonRef.RemainingCount : remainingCount;
            if (currentUses <= 0 || LevelTimerUI.IsGameOver)
            {
                Debug.LogWarning($"[RocketBooster] Cannot activate: currentUses={currentUses}, IsGameOver={LevelTimerUI.IsGameOver}");
                RefreshButtonState();
                return;
            }

            activeBooster = this;
            Debug.Log($"[RocketBooster] Activated! Tap any piece to launch rocket (Remaining Uses: {currentUses})");
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

        private void ProcessTargetInput()
        {
            int currentUses = boosterButtonRef != null ? boosterButtonRef.RemainingCount : remainingCount;
            if (LevelTimerUI.IsGameOver || currentUses <= 0)
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

            Camera gameCamera = Camera.main;
            if (gameCamera == null)
                return;

            Vector3 pointer = screenPosition;
            pointer.z = -gameCamera.transform.position.z;
            Vector2 worldPosition = gameCamera.ScreenToWorldPoint(pointer);

            var pieces = PuzzlePiece.ActivePieces;
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                PuzzlePiece piece = pieces[i];
                if (piece == null || piece.IsBeingShredded || !TryStartRocketImpact(piece, worldPosition))
                    continue;

                Debug.Log($"[RocketBooster] Target tap hit piece: {piece.name}");
                boundBoard?.StartTimer();
                activeBooster = null;
                suppressGameplayThroughFrame = Time.frameCount;
                RefreshButtonState();
                return;
            }
        }

        private bool TryStartRocketImpact(PuzzlePiece piece, Vector2 worldPosition)
        {
            Collider2D[] hits = Physics2D.OverlapPointAll(worldPosition);
            bool belongsToPiece = false;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] != null && hits[i].GetComponentInParent<PuzzlePiece>() == piece)
                {
                    belongsToPiece = true;
                    break;
                }
            }

            int currentUses = boosterButtonRef != null ? boosterButtonRef.RemainingCount : remainingCount;
            if (!belongsToPiece || launchInProgress || currentUses <= 0)
                return false;

            // Decrement remaining count if standalone
            if (boosterButtonRef == null)
            {
                remainingCount--;
                if (remainingCount < 0) remainingCount = 0;
                UpdateCountUI();
            }

            launchInProgress = true;
            StartCoroutine(PlayRocketLaunchSequence(piece));
            return true;
        }

        private IEnumerator PlayRocketLaunchSequence(PuzzlePiece piece)
        {
            if (piece == null)
            {
                launchInProgress = false;
                yield break;
            }

            piece.TryBeginShredding();
            piece.SetSelected(false);
            piece.PrepareForShredderPhysics();

            Camera camera = Camera.main;
            Vector3 piecePos = GetPieceCenter(piece);

            float camY = camera != null ? camera.transform.position.y : 0f;
            float camOrtho = camera != null ? camera.orthographicSize : 10f;
            float offscreenBottomY = camY - camOrtho - 6f;

            Vector3 startPos = new Vector3(piecePos.x, offscreenBottomY, -3f);
            Vector3 centerPos = new Vector3(piecePos.x, piecePos.y, -3f);

            // 1. Spawn Rocket Visual at off-screen bottom position
            GameObject rocket = CreateRocketVisual();
            rocket.transform.position = startPos;
            rocket.transform.rotation = Quaternion.identity;
            SetSortingOrder(rocket, rocketSortingOrder);

            // 2. Animate rocket from screen bottom up to piece center (entranceDuration)
            Tween entranceTween = rocket.transform.DOMove(centerPos, entranceDuration)
                .SetEase(Ease.OutCubic)
                .SetLink(rocket, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            yield return entranceTween.WaitForCompletion();

            if (rocket == null || piece == null)
            {
                if (rocket != null) Destroy(rocket);
                launchInProgress = false;
                yield break;
            }

            // 3. Attach piece transform to rocket at piece center
            piece.transform.SetParent(rocket.transform, true);

            // 4. Pause at piece center for piecePauseDuration (with engine ignition micro-rumble)
            Vector3 initialPos = rocket.transform.position;
            float elapsed = 0f;
            while (piece != null && rocket != null && elapsed < piecePauseDuration)
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
            Color pieceColor = piece.VisualColor;
            float targetY = camY + camOrtho + 7f;

            ShredderParticleEffects.SpawnBurst(rocket.transform.position, pieceColor, 25, 14, 10);

            Tween launchTween = rocket.transform.DOMoveY(targetY, launchDuration)
                .SetEase(Ease.InQuad)
                .SetLink(rocket, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            yield return launchTween.WaitForCompletion();

            // 6. Rocket launch animation finished! Decrement count now
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
                int grainCount = 10;
                float progressPerGrain = piece.RemainingProgressUnits / Mathf.Max(1, grainCount);
                ShredderVoxelHandoff.SpawnStream(
                    new Vector2(piecePos.x, targetY),
                    pieceColor,
                    grainCount,
                    progressPerGrain);
            }

            if (piece != null)
            {
                piece.ReportDestroyed();
                piece.gameObject.SetActive(false);
            }

            if (rocket != null)
            {
                Destroy(rocket);
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

        private GameObject CreateRocketVisual()
        {
            Vector3 effectiveScale = rocketScale;
            if (effectiveScale.sqrMagnitude < 0.0001f)
            {
                effectiveScale = Vector3.one;
            }

            GameObject root;
            if (rocketVisualPrefab != null)
            {
                root = Instantiate(rocketVisualPrefab);
                root.SetActive(true);
                Vector3 baseScale = rocketVisualPrefab.transform.localScale;
                if (baseScale.sqrMagnitude < 0.0001f) baseScale = Vector3.one;
                root.transform.localScale = Vector3.Scale(baseScale, effectiveScale);
                SetSortingOrder(root, rocketSortingOrder);
                return root;
            }

            root = new GameObject("RocketVisual");

            // Body (Rocket cone/body)
            GameObject bodyObj = PrototypeBootstrap.CreateVisualBlock("RocketBody", Vector2.zero, new Vector2(0.7f, 1.4f), new Color(0.95f, 0.2f, 0.2f));
            bodyObj.transform.SetParent(root.transform, false);

            // Nose cone
            GameObject noseObj = PrototypeBootstrap.CreateVisualBlock("RocketNose", new Vector2(0f, 0.85f), new Vector2(0.5f, 0.5f), new Color(0.98f, 0.98f, 0.98f));
            noseObj.transform.SetParent(root.transform, false);

            // Fins (Left & Right)
            GameObject leftFin = PrototypeBootstrap.CreateVisualBlock("LeftFin", new Vector2(-0.45f, -0.4f), new Vector2(0.3f, 0.5f), new Color(0.2f, 0.25f, 0.4f));
            leftFin.transform.SetParent(root.transform, false);

            GameObject rightFin = PrototypeBootstrap.CreateVisualBlock("RightFin", new Vector2(0.45f, -0.4f), new Vector2(0.3f, 0.5f), new Color(0.2f, 0.25f, 0.4f));
            rightFin.transform.SetParent(root.transform, false);

            // Thruster flame
            GameObject flameObj = PrototypeBootstrap.CreateVisualBlock("ThrusterFlame", new Vector2(0f, -0.9f), new Vector2(0.45f, 0.65f), new Color(1f, 0.55f, 0.1f));
            flameObj.transform.SetParent(root.transform, false);

            root.transform.localScale = Vector3.Scale(Vector3.one, effectiveScale);
            root.SetActive(true);
            SetSortingOrder(root, rocketSortingOrder);
            return root;
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

        private void EnsureReferences()
        {
            if (boosterButtonRef == null)
            {
                boosterButtonRef = GetRocketBoosterButton();
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
            if (boosterButtonRef != null) return boosterButtonRef;

            boosterButtonRef = GetComponent<BoosterButton>();
            if (boosterButtonRef != null) return boosterButtonRef;

            BoosterButton[] allButtons = Object.FindObjectsOfType<BoosterButton>();
            foreach (var b in allButtons)
            {
                if (b != null && b.gameObject.name.ToLower().Contains("rocket"))
                {
                    boosterButtonRef = b;
                    return b;
                }
            }

            if (allButtons.Length > 0) boosterButtonRef = allButtons[0];
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

            CancelRocketSelection();
            boundBoard = activeBoard;
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

        private void RefreshButtonState()
        {
            if (boosterButtonRef != null)
            {
                boosterButtonRef.RefreshButtonState();
                return;
            }

            if (boosterButton == null)
                return;

            if (buttonCanvasGroup == null)
            {
                buttonCanvasGroup = boosterButton.GetComponent<CanvasGroup>();
                if (buttonCanvasGroup == null)
                    buttonCanvasGroup = boosterButton.gameObject.AddComponent<CanvasGroup>();
            }

            bool visible = true;
            buttonCanvasGroup.alpha = visible ? 1f : 0.4f;
            buttonCanvasGroup.interactable = visible;
            buttonCanvasGroup.blocksRaycasts = visible;
            boosterButton.interactable =
                visible &&
                remainingCount > 0 &&
                activeBooster != this &&
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
