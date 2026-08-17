using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

namespace GravityPuzzle
{
    /// <summary>
    /// One-use-per-level hammer. Press the UI button to enter targeting mode,
    /// then tap one visible cell belonging to a movable puzzle piece.
    /// </summary>
    public sealed class HammerBooster : MonoBehaviour
    {
        // Hammer topology editing is intentionally paused while the board uses
        // the legacy/runtime hybrid.  A hit must not mutate colliders or grid
        // occupancy until disconnected-component splitting has one atomic
        // lifecycle transaction.  The targeting animation remains available
        // as harmless feedback and does not consume an inventory use.
        private static readonly bool TopologyEditingEnabled = false;

        public static bool IsTargeting =>
            activeBooster != null || Time.frameCount <= suppressGameplayThroughFrame;

        [Header("Hammer Booster")]
        [Tooltip("Optional. Assign a Button to wire its click automatically.")]
        public Button boosterButton;

        [Tooltip("Optional world-space hammer visual. A simple runtime hammer is used when omitted.")]
        [SerializeField] private GameObject hammerVisualPrefab;

        [SerializeField, Range(.15f, .2f)] private float impactDuration = .18f;
        [SerializeField] private float hammerHeight = 1.25f;
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
        private CanvasGroup buttonCanvasGroup;
        private bool impactInProgress;

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

            CancelHammerSelection();
        }

        /// <summary>
        /// Public Button OnClick entry point. The next valid puzzle-cell tap is
        /// removed; tapping empty space does not consume the one-time booster.
        /// </summary>
        public void ActivateHammerBooster()
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

        /// <summary>Cancels targeting without consuming the booster.</summary>
        public void CancelHammerSelection()
        {
            if (activeBooster == this)
                activeBooster = null;

            RefreshButtonState();
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

            Camera gameCamera = Camera.main;
            if (gameCamera == null)
                return;

            Vector3 pointer = screenPosition;
            pointer.z = -gameCamera.transform.position.z;
            Vector2 worldPosition = gameCamera.ScreenToWorldPoint(pointer);

            // Iterate backwards so the most recently registered visible piece
            // wins if malformed level data overlaps two cells.
            var pieces = PuzzlePiece.ActivePieces;
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                PuzzlePiece piece = pieces[i];
                if (piece == null || !TryStartHammerImpact(piece, worldPosition))
                    continue;

                boundBoard?.StartTimer();
                activeBooster = null;
                // Update order between UI/booster/drag components is not fixed.
                // Suppress the rest of this frame so the target tap cannot also
                // begin dragging the newly modified piece.
                suppressGameplayThroughFrame = Time.frameCount;
                RefreshButtonState();
                return;
            }
        }

        private bool TryStartHammerImpact(PuzzlePiece piece, Vector2 worldPosition)
        {
            // Validate before consuming the booster. The actual removal happens
            // at the animation impact frame, not at target selection.
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
            if (!belongsToPiece || impactInProgress)
                return false;

            impactInProgress = true;
            PlayHammerSwing(piece, worldPosition);
            return true;
        }

        private void PlayHammerSwing(PuzzlePiece piece, Vector2 impactPosition)
        {
            GameObject hammer = CreateHammerVisual();
            Transform hammerTransform = hammer.transform;
            Vector3 defaultScale = hammerTransform.localScale;
            Vector3 impactScale = defaultScale * popScaleMultiplier;
            Camera camera = Camera.main;
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
            swing.Append(hammerTransform.DOMove(screenCentre, .35f).SetEase(Ease.OutQuart));
            swing.Join(hammerTransform.DOScale(impactScale, .35f).SetEase(Ease.OutSine));
            // Phase 2: arrive above the block, settle, then swing only around
            // the local pivot so the head traces a clean rotational arc.
            swing.Append(hammerTransform.DOMove(hoverPoint, .45f).SetEase(Ease.OutSine));
            swing.Join(hammerTransform.DORotate(new Vector3(0f, facingYAngle, windUpAngle), .45f).SetEase(Ease.OutSine));
            swing.AppendInterval(.1f);
            swing.Append(hammerTransform.DORotate(new Vector3(0f, facingYAngle, impactAngle), .12f).SetEase(Ease.InQuad));
            swing.AppendCallback(() => ApplyHammerImpact(piece, impactPosition));
            // Phase 4: shrink along an exit arc once the hit has registered.
            swing.Append(DOVirtual.Float(0f, 1f, .2f, progress =>
                hammerTransform.position = QuadraticBezier(hoverPoint, exitControl, exitPoint, progress))
                .SetEase(Ease.OutSine));
            swing.Join(hammerTransform.DOScale(Vector3.zero, .2f).SetEase(Ease.InBack));
            swing.Join(hammerTransform.DORotate(new Vector3(0f, facingYAngle, 0f), .2f).SetEase(Ease.OutSine));
            swing.OnComplete(() =>
            {
                Destroy(hammer);
                impactInProgress = false;
                if (TopologyEditingEnabled)
                    GetHammerBoosterButton()?.TryConsumeUse();
            });
        }

        private BoosterButton GetHammerBoosterButton()
        {
            if (boosterButton != null)
            {
                var b = boosterButton.GetComponent<BoosterButton>();
                if (b != null) return b;
            }

            BoosterButton[] allButtons = Object.FindObjectsOfType<BoosterButton>();
            foreach (var b in allButtons)
            {
                if (b != null && b.gameObject.name.ToLower().Contains("hammer"))
                {
                    return b;
                }
            }
            return null;
        }

        private void ApplyHammerImpact(PuzzlePiece piece, Vector2 impactPosition)
        {
            if (TopologyEditingEnabled &&
                piece != null && piece.TryRemoveCellAt(impactPosition, out PuzzlePiece.RemovedCell cell))
            {
                Color color = new Color(cell.color.r, cell.color.g, cell.color.b, 1f);
                // Match the shredder exactly: every original rendered voxel
                // emits the same number of slider-bound grains on a hammer hit.
                int grainCount = cell.renderedVoxelCount * LevelProgressManager.SandGrainsPerRenderedVoxel;
                ShredderParticleEffects.SpawnBurst(
                    cell.worldPosition,
                    color,
                    cell.renderedVoxelCount,
                    cell.renderedVoxelCount,
                    cell.renderedVoxelCount);
                ShredderVoxelHandoff.SpawnStream(
                    cell.worldPosition,
                    color,
                    grainCount,
                    cell.progressUnits / Mathf.Max(1, grainCount));
            }

            Camera camera = Camera.main;
            if (camera != null)
                camera.transform.DOShakePosition(.15f, .12f, 18, 90f, false, true)
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

        private GameObject CreateHammerVisual()
        {
            if (hammerVisualPrefab != null)
                return Instantiate(hammerVisualPrefab);

            GameObject hammer = new GameObject("Hammer Booster Impact");
            CreateHammerPart(hammer.transform, "Handle", new Vector2(0f, -.22f), new Vector2(.13f, .68f), new Color(.38f, .20f, .08f));
            CreateHammerPart(hammer.transform, "Head", new Vector2(0f, .16f), new Vector2(.62f, .24f), new Color(.62f, .66f, .72f));
            return hammer;
        }

        private void SetHammerSortingOrder(GameObject hammer)
        {
            SpriteRenderer[] renderers = hammer.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sortingOrder = hammerSortingOrder + i;
        }

        private static void CreateHammerPart(Transform parent, string partName, Vector2 localPosition, Vector2 size, Color color)
        {
            GameObject part = new GameObject(partName);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = new Vector3(size.x, size.y, 1f);
            SpriteRenderer renderer = part.AddComponent<SpriteRenderer>();
            renderer.sprite = PrototypeBootstrap.GetSquareSprite();
            renderer.color = color;
            renderer.sortingOrder = 50;
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

            CancelHammerSelection();
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
    }
}
