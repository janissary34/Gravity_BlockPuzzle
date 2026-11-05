using System;
using System.Collections;
using GravityPuzzle.Config;
using GravityPuzzle.Core.StateMachine;
using GravityPuzzle.Creative.Config;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Presentation.Views;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle.Creative.Runtime
{
    /// <summary>
    /// Mutable state of one creative take. Created by the scene composition root only when a take is
    /// explicitly requested; otherwise Creative Mode does not exist at runtime.
    ///
    /// Observes gameplay (board and inventory events) and never changes gameplay state, with one
    /// explicit exception: in Frozen timer mode it pauses the level timer through the board's
    /// owner-scoped TryPauseTimer API. Presentation changes are reversible runtime toggles
    /// (canvas enable, object active, renderer enable, camera clear colour).
    /// </summary>
    public sealed class CreativeSession
    {
#if UNITY_EDITOR
        /// <summary>
        /// Editor-only bridge to the editor recorder driver (which cannot be reached from runtime
        /// code): raised once per take, with the take name, when the level is visually ready to
        /// record. Subscribers must unsubscribe after the call and when Play Mode exits.
        /// </summary>
        public static event Action<string> CaptureReady;

        // Clears stale subscribers when domain reload on entering Play Mode is disabled. Runs before
        // the first scene loads, i.e. before the recorder driver subscribes at EnteredPlayMode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaptureReady()
        {
            CaptureReady = null;
        }
#endif

        private readonly CreativeTakeConfig config;
        private readonly CreativeTakeRequest request;
        private readonly PrototypeBoard board;
        private readonly PuzzleDragController dragController;
        private readonly Camera camera;
        private readonly CreativeSceneBindings bindings;
        private readonly CreativeBoosterInventory inventory;
        private readonly LevelRuntimeRoot levelRoot;
        private readonly MonoBehaviour coroutineHost;
        private readonly CreativeEventLog log;

        private bool begun;
        private bool finishing;
        private bool ended;
        private Coroutine safetyRoutine;
        private Coroutine captureDelayRoutine;
        private CreativePointerScript pointerScript;
        private bool progressBarMoved;
        private Vector3 originalProgressBarPosition;
        private readonly Vector3[] corners = new Vector3[4];

        public CreativeSession(
            CreativeTakeConfig config,
            CreativeTakeRequest request,
            PrototypeBoard board,
            PuzzleDragController dragController,
            Camera camera,
            CreativeSceneBindings bindings,
            CreativeBoosterInventory inventory,
            LevelRuntimeRoot levelRoot,
            MonoBehaviour coroutineHost)
        {
            this.config = config;
            this.request = request;
            this.board = board;
            this.dragController = dragController;
            this.camera = camera;
            this.bindings = bindings;
            this.inventory = inventory;
            this.levelRoot = levelRoot;
            this.coroutineHost = coroutineHost;
            // Sized for a pointer sample on every frame of the take, so drags never grow the list.
            log = new CreativeEventLog(Mathf.Max(1024, Mathf.CeilToInt(config.MaxSeconds * config.CaptureFrameRate) + 256));
            log.Add(CreativeEventKind.TakeStart);
        }

        /// <summary>Called by the composition root after the level has been built.</summary>
        public void Begin()
        {
            if (begun)
                return;

            begun = true;
            if (board != null)
            {
                board.GameStateChanged += OnGameStateChanged;
                board.TimerStarted += OnTimerStarted;
                board.PieceStateChanged += OnPieceStateChanged;
                board.BlockShredded += OnBlockShredded;
                board.LevelCleared += OnLevelCleared;
                board.LevelFailed += OnLevelFailed;
            }

            if (inventory != null)
                inventory.CountChanged += OnBoosterCountChanged;

            if (dragController != null)
                dragController.PieceReleased += OnPieceReleased;

            ApplyBackground();
            if (config.UIProfile != null && bindings == null)
                Debug.LogWarning($"[Creative] Take '{request.TakeName}' uses UI profile '{config.UIProfile.name}', but the scene has no CreativeSceneBindings; " +
                                 "run Gravity Puzzle/Creative/Author Scene1 UI Bindings. UI is left as the game shows it.", coroutineHost);
            ApplyUIProfile();
            log.Add(CreativeEventKind.LevelReady);
            captureDelayRoutine = coroutineHost.StartCoroutine(SignalCaptureReadyAfterDelay());
            safetyRoutine = coroutineHost.StartCoroutine(EndAfterSafetyLimit());
            if (config.Script != null && config.Script.DragCount > 0)
                coroutineHost.StartCoroutine(StartScriptAfterDelay());
            Debug.Log($"[Creative] Take '{request.TakeName}' started on level '{LevelName}'.", coroutineHost);
        }

        /// <summary>Unsubscribes and writes events.json. Safe to call more than once.</summary>
        public void End()
        {
            if (ended)
                return;

            ended = true;
            if (board != null)
            {
                board.GameStateChanged -= OnGameStateChanged;
                board.TimerStarted -= OnTimerStarted;
                board.PieceStateChanged -= OnPieceStateChanged;
                board.BlockShredded -= OnBlockShredded;
                board.LevelCleared -= OnLevelCleared;
                board.LevelFailed -= OnLevelFailed;
                board.ResumeTimer(this);
            }

            if (inventory != null)
                inventory.CountChanged -= OnBoosterCountChanged;

            if (dragController != null)
            {
                dragController.PieceReleased -= OnPieceReleased;
                dragController.ConfigureScriptedPointerSource(null);
            }

            pointerScript?.Stop();
            RestoreProgressBar();

            if (safetyRoutine != null && coroutineHost != null)
                coroutineHost.StopCoroutine(safetyRoutine);
            if (captureDelayRoutine != null && coroutineHost != null)
                coroutineHost.StopCoroutine(captureDelayRoutine);

            log.Add(CreativeEventKind.TakeEnd);
            string path = request.EventsPath;
            log.Write(path, request.TakeName, LevelName, config.CaptureWidth, config.CaptureHeight, config.CaptureFrameRate);
            Debug.Log($"[Creative] Take '{request.TakeName}' wrote {log.Count} events to {path}.", coroutineHost);
        }

        private string LevelName => config.Level != null ? config.Level.name : "(none)";

        // ---- presentation ---------------------------------------------------------------------

        // Runs after a rendered frame: a Screen Space - Camera canvas follows its camera only when it
        // renders, so in the frame the level moves the camera the canvas still has its old pose.
        // Returns false, with the reason, when the take must not be recorded.
        private bool PrepareProgressBar(out string failure)
        {
            failure = null;
            CreativeFramingProfile framing = config.Framing;
            bool framed = framing != null && framing.ProgressBarBoardGap > 0f;
            RectTransform visual = bindings != null ? bindings.ProgressBarVisual : null;
            if (IsHiddenByProfile(CreativeUIGroup.ProgressBar) || (!framed && visual == null))
                return true;

            RectTransform bar = bindings != null ? bindings.ProgressBar : null;
            Canvas canvas = bindings != null ? bindings.ProgressBarCanvas : null;
            if (bar == null || visual == null || canvas == null || camera == null)
            {
                failure = "the progress bar bindings or camera are missing; run Gravity Puzzle/Creative/Author Scene1 UI Bindings";
                return false;
            }

            if (levelRoot == null || !levelRoot.TryGetBoardFrameTop(out float frameTopWorld))
            {
                failure = "the board frame top could not be measured";
                return false;
            }

            Canvas.ForceUpdateCanvases();
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera != null ? canvas.worldCamera : camera;
            float padding = framing != null ? framing.BoardFrameArtPadding : 0f;
            float frameTop = camera.WorldToScreenPoint(new Vector3(0f, frameTopWorld - padding, 0f)).y;

            if (framed)
            {
                Rect before = AttachedScreenRect(visual, uiCamera);
                var from = new Vector2(before.center.x, before.yMin);
                var to = new Vector2(before.center.x, frameTop + framing.ProgressBarBoardGap * camera.pixelHeight);
                if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(bar, from, uiCamera, out Vector3 fromWorld) ||
                    !RectTransformUtility.ScreenPointToWorldPointInRectangle(bar, to, uiCamera, out Vector3 toWorld))
                {
                    failure = "the progress bar position could not be mapped to the canvas";
                    return false;
                }

                originalProgressBarPosition = bar.localPosition;
                progressBarMoved = true;
                bar.position += new Vector3(0f, toWorld.y - fromWorld.y, 0f);
            }

            Rect placed = AttachedScreenRect(visual, uiCamera);
            if (placed.yMin <= frameTop)
            {
                RestoreProgressBar();
                failure = $"the progress bar overlaps the board: its bottom is at {placed.yMin:0.#} px and the frame top at {frameTop:0.#} px (y up)";
                return false;
            }

            Rect safe = SafeScreenRect();
            if (placed.xMin < safe.xMin || placed.xMax > safe.xMax || placed.yMin < safe.yMin || placed.yMax > safe.yMax)
            {
                RestoreProgressBar();
                failure = $"the progress bar is outside the safe screen area: bar {placed}, safe area {safe}";
                return false;
            }

            return true;
        }

        // The bar plus the timer, which hangs below it, when the UI profile leaves the timer visible.
        private Rect AttachedScreenRect(RectTransform visual, Camera uiCamera)
        {
            Rect bounds = ScreenRect(visual, uiCamera);
            for (int b = 0; b < bindings.UIGroupCount; b++)
            {
                CreativeSceneBindings.UIGroupBinding binding = bindings.GetUIGroup(b);
                if (binding.group != CreativeUIGroup.Timer || binding.graphics == null)
                    continue;

                for (int g = 0; g < binding.graphics.Length; g++)
                {
                    Graphic graphic = binding.graphics[g];
                    // Inactive is fine: the timer object activates only when play starts, after this check.
                    if (graphic == null || !graphic.enabled)
                        continue;

                    Rect rect = ScreenRect(graphic.rectTransform, uiCamera);
                    bounds = Rect.MinMaxRect(
                        Mathf.Min(bounds.xMin, rect.xMin), Mathf.Min(bounds.yMin, rect.yMin),
                        Mathf.Max(bounds.xMax, rect.xMax), Mathf.Max(bounds.yMax, rect.yMax));
                }
            }

            return bounds;
        }

        private Rect ScreenRect(RectTransform rect, Camera uiCamera)
        {
            rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            Vector2 max = min;
            for (int c = 1; c < corners.Length; c++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[c]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // Screen.safeArea, as fractions applied to the camera's pixels. In the Editor's Game View
        // this is the whole view, so the check there means "inside the picture".
        private Rect SafeScreenRect()
        {
            Rect pixels = camera.pixelRect;
            Rect safe = Screen.safeArea;
            float width = Mathf.Max(1, Screen.width);
            float height = Mathf.Max(1, Screen.height);
            return new Rect(
                pixels.x + safe.x / width * pixels.width,
                pixels.y + safe.y / height * pixels.height,
                safe.width / width * pixels.width,
                safe.height / height * pixels.height);
        }

        private bool IsHiddenByProfile(CreativeUIGroup group)
        {
            CreativeUIProfile profile = config.UIProfile;
            if (profile == null)
                return false;

            for (int p = 0; p < profile.Count; p++)
            {
                CreativeUIProfile.GroupVisibility rule = profile.Get(p);
                if (rule.group == group && !rule.visible)
                    return true;
            }

            return false;
        }

        private void RestoreProgressBar()
        {
            if (!progressBarMoved)
                return;

            progressBarMoved = false;
            if (bindings != null && bindings.ProgressBar != null)
                bindings.ProgressBar.localPosition = originalProgressBarPosition;
        }

        private void ApplyBackground()
        {
            if (config.BackgroundMode != CreativeBackgroundMode.SolidColor)
                return;

            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = config.BackgroundColor;
            }

            if (bindings == null)
                return;

            for (int index = 0; index < bindings.BackdropRendererCount; index++)
            {
                Renderer backdrop = bindings.GetBackdropRenderer(index);
                if (backdrop != null)
                    backdrop.enabled = false;
            }
        }

        /// <summary>
        /// Hides every UI group the profile marks as not visible. Visible groups are left to the
        /// game. Re-applied after game-state changes because the game shows panels on its own.
        /// </summary>
        private void ApplyUIProfile()
        {
            CreativeUIProfile profile = config.UIProfile;
            if (profile == null || bindings == null)
                return;

            for (int p = 0; p < profile.Count; p++)
            {
                CreativeUIProfile.GroupVisibility rule = profile.Get(p);
                if (rule.visible)
                    continue;

                for (int b = 0; b < bindings.UIGroupCount; b++)
                {
                    CreativeSceneBindings.UIGroupBinding binding = bindings.GetUIGroup(b);
                    if (binding.group != rule.group)
                        continue;

                    if (binding.graphics != null)
                        for (int g = 0; g < binding.graphics.Length; g++)
                            if (binding.graphics[g] != null)
                                binding.graphics[g].enabled = false;

                    if (binding.canvases != null)
                        for (int c = 0; c < binding.canvases.Length; c++)
                            if (binding.canvases[c] != null)
                                binding.canvases[c].enabled = false;

                    if (binding.objects != null)
                        for (int o = 0; o < binding.objects.Length; o++)
                            if (binding.objects[o] != null)
                                binding.objects[o].SetActive(false);
                }
            }
        }

        // ---- observation -----------------------------------------------------------------------

        private void OnGameStateChanged(GameState previous, GameState next)
        {
            log.Add(CreativeEventKind.GameState, from: (int)previous, to: (int)next);
            // A revive Continue clears the board's pause owners and returns to Playing with the
            // timer already started, so the freeze is re-applied here as well.
            if (next == GameState.Playing)
                HoldFrozenTimer();
            ApplyUIProfile();
        }

        // The board raises GameStateChanged(Playing) before it marks the timer as started, so the
        // first pause request has to wait for TimerStarted. Pausing there is synchronous, inside
        // StartTimer, so the timer never ticks in Frozen mode.
        private void OnTimerStarted()
        {
            HoldFrozenTimer();
        }

        private void HoldFrozenTimer()
        {
            if (config.TimerMode == CreativeTimerMode.Frozen && board != null && !ended)
                board.TryPauseTimer(this);
        }

        private void OnPieceStateChanged(int pieceId, PieceState previous, PieceState next)
        {
            log.Add(CreativeEventKind.PieceState, pieceId, (int)previous, (int)next);
        }

        private void OnBlockShredded(int sourcePieceId)
        {
            log.Add(CreativeEventKind.PieceShredded, sourcePieceId);
        }

        private void OnPieceReleased(int pieceId, bool accepted)
        {
            log.Add(CreativeEventKind.PieceReleased, pieceId, value: accepted ? 1 : 0);
        }

        // ---- capture ---------------------------------------------------------------------------

        private IEnumerator SignalCaptureReadyAfterDelay()
        {
            int delay = Mathf.Max(1, config.CaptureStartDelayFrames);
            for (int frame = 0; frame < delay; frame++)
                yield return null;

            if (ended)
                yield break;

            if (!PrepareProgressBar(out string failure))
            {
                log.Add(CreativeEventKind.CaptureFailed);
                Debug.LogError($"[Creative] Take '{request.TakeName}' failed pre-recording validation: {failure}. Nothing is recorded.", coroutineHost);
                Complete();
                yield break;
            }

            log.Add(CreativeEventKind.CaptureReady);
#if UNITY_EDITOR
            CaptureReady?.Invoke(request.TakeName);
#endif
        }

        // ---- scripted gameplay -----------------------------------------------------------------

        private IEnumerator StartScriptAfterDelay()
        {
            CreativePointerScript script;
            try
            {
                script = new CreativePointerScript(config.Script, config.Level);
            }
            catch (System.ArgumentException exception)
            {
                Debug.LogError($"[Creative] Script '{config.Script.name}' is invalid: {exception.Message} The take continues without scripted input.", coroutineHost);
                yield break;
            }

            for (int frame = 0; frame < config.Script.StartDelayFrames; frame++)
                yield return null;

            if (ended || dragController == null || camera == null)
                yield break;

            pointerScript = script;
            pointerScript.Start(camera, log);
            dragController.ConfigureScriptedPointerSource(pointerScript);
            log.Add(CreativeEventKind.ScriptStart, value: pointerScript.DragCount);
        }

        private void OnBoosterCountChanged(BoosterRewardType booster, int remaining)
        {
            log.Add(CreativeEventKind.BoosterUsed, from: (int)booster, value: remaining);
        }

        private void OnLevelCleared()
        {
            log.Add(CreativeEventKind.LevelCleared);
            FinishAfterTail();
        }

        private void OnLevelFailed()
        {
            log.Add(CreativeEventKind.LevelFailed);
            FinishAfterTail();
        }

        // ---- take end --------------------------------------------------------------------------

        private void FinishAfterTail()
        {
            if (finishing)
                return;

            finishing = true;
            ApplyUIProfile();
            coroutineHost.StartCoroutine(EndAfterTail());
        }

        private IEnumerator EndAfterTail()
        {
            for (int frame = 0; frame < config.TailFrames; frame++)
                yield return null;

            Complete();
        }

        private IEnumerator EndAfterSafetyLimit()
        {
            yield return new WaitForSeconds(config.MaxSeconds);
            Debug.LogWarning($"[Creative] Take '{request.TakeName}' reached its {config.MaxSeconds}s limit without a result.", coroutineHost);
            Complete();
        }

        private void Complete()
        {
            End();
#if UNITY_EDITOR
            // Leaving Play Mode stops Unity Recorder and finalises the take's video.
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
