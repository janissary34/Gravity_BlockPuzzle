using UnityEngine;

namespace GravityPuzzle.Gameplay.Input
{
    /// <summary>
    /// One frame of pointer input for the board, in screen pixels, with mouse-button semantics:
    /// Pressed on the first frame of contact, Held while in contact (including the press frame),
    /// Released on the frame contact ends.
    /// </summary>
    public struct BoardPointerSample
    {
        public Vector2 ScreenPosition;
        public bool Pressed;
        public bool Held;
        public bool Released;
    }

    /// <summary>
    /// Alternative pointer source for PuzzleDragController (used by Creative Mode scripted takes).
    /// GetSample is a pure function of the frame number, so a source needs no Update loop and a
    /// replay is deterministic per frame.
    /// </summary>
    public interface IBoardPointerSource
    {
        bool IsActive { get; }
        BoardPointerSample GetSample(int frame);
    }
}
