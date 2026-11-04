using System;
using System.Collections;
using GravityPuzzle.Config;
using GravityPuzzle.Core.StateMachine;
using GravityPuzzle.Creative.Config;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;

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
        private readonly MonoBehaviour coroutineHost;
        private readonly CreativeEventLog log = new CreativeEventLog(1024);

        private bool begun;
        private bool finishing;
        private bool ended;
        private Coroutine safetyRoutine;
        private Coroutine captureDelayRoutine;
        private CreativePointerScript pointerScript;

        public CreativeSession(
            CreativeTakeConfig config,
            CreativeTakeRequest request,
            PrototypeBoard board,
            PuzzleDragController dragController,
            Camera camera,
            CreativeSceneBindings bindings,
            CreativeBoosterInventory inventory,
            MonoBehaviour coroutineHost)
        {
            this.config = config;
            this.request = request;
            this.board = board;
            this.dragController = dragController;
            this.camera = camera;
            this.bindings = bindings;
            this.inventory = inventory;
            this.coroutineHost = coroutineHost;
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
            for (int frame = 0; frame < config.CaptureStartDelayFrames; frame++)
                yield return null;

            if (ended)
                yield break;

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
            pointerScript.Start(camera);
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
