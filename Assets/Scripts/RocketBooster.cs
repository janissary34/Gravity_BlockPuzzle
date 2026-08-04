using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GravityPuzzle
{
    /// <summary>
    /// Rocket Booster: Press the UI button to enter targeting mode,
    /// then tap any visible piece. A rocket attaches and launches the ENTIRE piece
    /// into the sky, completely destroying it and awarding progress.
    /// </summary>
    public sealed class RocketBooster : MonoBehaviour
    {
        public static bool IsTargeting =>
            activeBooster != null || Time.frameCount <= suppressGameplayThroughFrame;

        [Header("Rocket Booster")]
        [Tooltip("Optional. Assign a Button to wire its click automatically. If unassigned, searches for 'rocket_booster_btn'.")]
        public Button boosterButton;

        [Tooltip("Optional rocket visual prefab. A runtime procedurally generated rocket is used when omitted.")]
        [SerializeField] private GameObject rocketVisualPrefab;

        [SerializeField, Tooltip("Rocket launch speed duration in seconds.")]
        private float launchDuration = 0.55f;

        [SerializeField, Tooltip("Roket Boyutu (Scale multiplier).")]
        private Vector3 rocketScale = Vector3.one;

        [SerializeField] private int rocketSortingOrder = 30000;

        private static RocketBooster activeBooster;
        private static int suppressGameplayThroughFrame = -1;
        private PrototypeBoard boundBoard;
        private CanvasGroup buttonCanvasGroup;
        private bool launchInProgress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveBooster()
        {
            activeBooster = null;
            suppressGameplayThroughFrame = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoAttachToScene()
        {
            if (Object.FindObjectOfType<RocketBooster>() != null)
                return;

            GameObject mgr = GameObject.Find("ProgressManagerObject");
            if (mgr == null)
            {
                mgr = new GameObject("RocketBoosterManager");
            }

            mgr.AddComponent<RocketBooster>();
        }

        private void OnEnable()
        {
            FindAndWireButton();
            if (boosterButton != null)
            {
                boosterButton.onClick.RemoveListener(ActivateRocketBooster);
                boosterButton.onClick.AddListener(ActivateRocketBooster);
            }

            SynchronizeLevel();
            RefreshButtonState();
        }

        private void Update()
        {
            if (boosterButton == null)
            {
                FindAndWireButton();
            }

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
            if (boosterButton != null)
            {
                boosterButton.onClick.RemoveListener(ActivateRocketBooster);
            }

            CancelRocketSelection();
        }

        public void ActivateRocketBooster()
        {
            SynchronizeLevel();
            if (boundBoard == null || !boundBoard.IsLevelRunning ||
                LevelTimerUI.IsGameOver || IsTargeting)
            {
                RefreshButtonState();
                return;
            }

            activeBooster = this;
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
            if (boundBoard == null || !boundBoard.IsLevelRunning || LevelTimerUI.IsGameOver)
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

            if (!belongsToPiece || launchInProgress)
                return false;

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

            // Flag piece as being shredded/destroyed so gravity and drag ignore it
            piece.TryBeginShredding();
            piece.SetSelected(false);
            piece.PrepareForShredderPhysics();

            Camera camera = Camera.main;
            Vector3 piecePos = piece.transform.position;

            // 1. Spawn Rocket Visual directly under piece with high visibility
            GameObject rocket = CreateRocketVisual();
            rocket.transform.position = new Vector3(piecePos.x, piecePos.y - 0.7f, -3f);
            rocket.transform.rotation = Quaternion.identity;
            SetSortingOrder(rocket, rocketSortingOrder);

            // 2. Attach piece transform to rocket so it moves WITH rocket
            piece.transform.SetParent(rocket.transform, true);

            // 3. Engine ignition micro-shake & rumbling
            Vector3 initialPos = rocket.transform.position;
            float rumbleTime = 0.22f;
            float elapsed = 0f;
            while (piece != null && rocket != null && elapsed < rumbleTime)
            {
                elapsed += Time.deltaTime;
                rocket.transform.position = initialPos + new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(-0.04f, 0.04f), 0f);
                yield return null;
            }

            if (rocket == null)
            {
                launchInProgress = false;
                yield break;
            }

            // 4. BLAST OFF! Launch rocket + piece into the sky
            Color pieceColor = piece.VisualColor;
            float targetY = camera != null ? camera.transform.position.y + camera.orthographicSize + 7f : piecePos.y + 17f;

            ShredderParticleEffects.SpawnBurst(rocket.transform.position, pieceColor, 25, 14, 10);

            Tween launchTween = rocket.transform.DOMoveY(targetY, launchDuration).SetEase(Ease.InQuad);
            yield return launchTween.WaitForCompletion();

            // 5. Award progress & despawn
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

            launchInProgress = false;
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

        private void FindAndWireButton()
        {
            if (boosterButton != null) return;

            Button[] buttons = Resources.FindObjectsOfTypeAll<Button>();
            foreach (Button b in buttons)
            {
                if (b == null) continue;
                string name = b.gameObject.name.ToLower();
                if (name.Contains("rocketbooster") || name.Contains("rocket_btn") || name.Contains("rocket"))
                {
                    boosterButton = b;
                    boosterButton.onClick.RemoveListener(ActivateRocketBooster);
                    boosterButton.onClick.AddListener(ActivateRocketBooster);
                    break;
                }
            }
        }

        private void SynchronizeLevel()
        {
            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard == null || activeBoard == boundBoard)
                return;

            CancelRocketSelection();
            boundBoard = activeBoard;
            RefreshButtonState();
        }

        private void RefreshButtonState()
        {
            if (boosterButton == null)
                return;

            if (buttonCanvasGroup == null)
            {
                buttonCanvasGroup = boosterButton.GetComponent<CanvasGroup>();
                if (buttonCanvasGroup == null)
                    buttonCanvasGroup = boosterButton.gameObject.AddComponent<CanvasGroup>();
            }

            bool visible = true;
            buttonCanvasGroup.alpha = visible ? 1f : 0f;
            buttonCanvasGroup.interactable = visible;
            buttonCanvasGroup.blocksRaycasts = visible;
            boosterButton.interactable =
                visible &&
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
