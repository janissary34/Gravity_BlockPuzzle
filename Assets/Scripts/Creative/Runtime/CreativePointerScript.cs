using System;
using GravityPuzzle.Creative.Config;
using GravityPuzzle.Gameplay.Input;
using UnityEngine;

namespace GravityPuzzle.Creative.Runtime
{
    /// <summary>
    /// Scripted pointer for a creative take, consumed by PuzzleDragController through
    /// <see cref="IBoardPointerSource"/>. Drags are converted from board cells to screen pixels once,
    /// when the script starts (the capture resolution is fixed before the level builds), so
    /// per-frame sampling is a pure, allocation-free function of the frame number.
    /// Screen coordinates are used deliberately: the controller then runs exactly the code path
    /// device input uses, and the creative hand can follow the same position.
    /// The script is active only from its start until its last release; afterwards device input
    /// works again.
    /// </summary>
    public sealed class CreativePointerScript : IBoardPointerSource
    {
        private readonly int[] startFrames;
        private readonly int[] releaseFrames;
        private readonly Vector2[] fromWorld;
        private readonly Vector2[] toWorld;
        private readonly Vector2[] fromScreen;
        private readonly Vector2[] toScreen;
        private CreativeEventLog log;
        private Vector2 viewportPixels;
        private int lastLoggedFrame = -1;
        private readonly int lastFrame;
        private bool started;
        private int originFrame = -1;

        /// <summary>Builds the timeline. Throws ArgumentException when the script breaks the authoring rules.</summary>
        public CreativePointerScript(CreativeScript script, GravityLevelDefinition level)
        {
            if (script == null)
                throw new ArgumentException("No script supplied.");
            if (level == null)
                throw new ArgumentException("No level supplied; drags are authored in board cells of the take's level.");
            int count = script.DragCount;
            startFrames = new int[count];
            releaseFrames = new int[count];
            fromWorld = new Vector2[count];
            toWorld = new Vector2[count];
            fromScreen = new Vector2[count];
            toScreen = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                CreativeScript.Drag drag = script.GetDrag(i);
                if (drag.startFrame < 0)
                    throw new ArgumentException($"Drag {i}: startFrame must not be negative.");
                if (drag.moveFrames < 1)
                    throw new ArgumentException($"Drag {i}: moveFrames must be at least 1.");
                if (i > 0 && drag.startFrame <= releaseFrames[i - 1])
                    throw new ArgumentException(
                        $"Drag {i} starts at frame {drag.startFrame}, but drag {i - 1} releases at frame {releaseFrames[i - 1]}; " +
                        "leave at least one frame between them.");
                startFrames[i] = drag.startFrame;
                releaseFrames[i] = drag.startFrame + drag.moveFrames;
                fromWorld[i] = CellToWorld(level, drag.fromCell);
                toWorld[i] = CellToWorld(level, drag.toCell);
                lastFrame = releaseFrames[i];
            }
        }

        public int DragCount => startFrames.Length;

        /// <summary>Last frame (relative to the start) at which the script produces input.</summary>
        public int LastFrame => lastFrame;

        /// <summary>
        /// Active from Start until the last release. Frame 0 of the script is the first frame the
        /// drag controller actually samples it (set lazily in GetSample): Start is called from a
        /// coroutine, which Unity resumes AFTER Update, so anchoring at Start would skip frame 0.
        /// </summary>
        public bool IsActive => started && (originFrame < 0 || Time.frameCount - originFrame <= lastFrame);

        /// <summary>
        /// Converts the drags to screen pixels and arms the script; frame 0 is the next sampled
        /// frame. Every sample handed to the drag controller is also written to <paramref name="log"/>.
        /// </summary>
        public void Start(Camera camera, CreativeEventLog log)
        {
            this.log = log;
            viewportPixels = new Vector2(camera.pixelWidth, camera.pixelHeight);
            lastLoggedFrame = -1;
            for (int i = 0; i < startFrames.Length; i++)
            {
                fromScreen[i] = camera.WorldToScreenPoint(fromWorld[i]);
                toScreen[i] = camera.WorldToScreenPoint(toWorld[i]);
            }
            originFrame = -1;
            started = true;
        }

        public void Stop()
        {
            started = false;
            originFrame = -1;
        }

        /// <summary>Unity frame that became script frame 0, or -1 before the first sample.</summary>
        public int OriginFrame => originFrame;

        public BoardPointerSample GetSample(int frame)
        {
            if (originFrame < 0)
                originFrame = frame;

            int t = frame - originFrame;
            for (int i = 0; i < startFrames.Length; i++)
            {
                if (t < startFrames[i] || t > releaseFrames[i])
                    continue;

                float k = (float)(t - startFrames[i]) / (releaseFrames[i] - startFrames[i]);
                var sample = new BoardPointerSample
                {
                    ScreenPosition = Vector2.Lerp(fromScreen[i], toScreen[i], k),
                    Pressed = t == startFrames[i],
                    Held = t < releaseFrames[i],
                    Released = t == releaseFrames[i],
                };
                LogSample(frame, sample);
                return sample;
            }

            return default;
        }

        private void LogSample(int frame, BoardPointerSample sample)
        {
            if (log == null || frame == lastLoggedFrame)
                return;

            lastLoggedFrame = frame;
            PointerPhase phase = sample.Pressed ? PointerPhase.Press
                : sample.Released ? PointerPhase.Release
                : PointerPhase.Move;
            log.AddPointerSample(sample.ScreenPosition.x / viewportPixels.x, sample.ScreenPosition.y / viewportPixels.y, phase);
        }

        /// <summary>World position of a board-cell coordinate ((0, 0) = centre of the lower-left cell).</summary>
        public static Vector2 CellToWorld(GravityLevelDefinition level, Vector2 cell)
        {
            return new Vector2(-level.boardColumns * .5f + cell.x + .5f, -level.boardRows * .5f + cell.y + .5f);
        }
    }
}
