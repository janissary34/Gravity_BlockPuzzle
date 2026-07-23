using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GravityPuzzle
{
    /// <summary>
    /// One-use-per-level hammer. Press the UI button to enter targeting mode,
    /// then tap one visible cell belonging to a movable puzzle piece.
    /// </summary>
    public sealed class HammerBooster : MonoBehaviour
    {
        public static bool IsTargeting =>
            activeBooster != null || Time.frameCount <= suppressGameplayThroughFrame;

        [Header("Hammer Booster")]
        [Tooltip("Optional. Assign a Button to wire its click automatically.")]
        public Button boosterButton;

        public bool HasBeenUsedThisLevel => usedThisLevel;

        private static HammerBooster activeBooster;
        private static int suppressGameplayThroughFrame = -1;
        private PrototypeBoard boundBoard;
        private CanvasGroup buttonCanvasGroup;
        private bool usedThisLevel;

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
                LevelTimerUI.IsGameOver || usedThisLevel || IsTargeting)
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
                if (piece == null || !piece.TryRemoveCellAt(worldPosition))
                    continue;

                boundBoard?.StartTimer();
                usedThisLevel = true;
                activeBooster = null;
                // Update order between UI/booster/drag components is not fixed.
                // Suppress the rest of this frame so the target tap cannot also
                // begin dragging the newly modified piece.
                suppressGameplayThroughFrame = Time.frameCount;
                RefreshButtonState();
                return;
            }
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
            usedThisLevel = false;
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

            bool visible = !usedThisLevel;
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
