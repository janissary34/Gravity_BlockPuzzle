using System;
using UnityEngine;

namespace GravityPuzzle.Creative.Config
{
    /// <summary>
    /// A scripted gameplay sequence for a creative take: drags authored in BOARD CELLS
    /// (x from the left column, y from the bottom row; (0, 0) is the centre of the lower-left
    /// cell), so scripts are independent of screen size and target cell centres exactly.
    /// Frames are relative to the moment the script starts.
    ///
    /// Authoring rules (validated when the take starts):
    /// - each drag lasts at least 1 frame;
    /// - a drag must start at least 1 frame after the previous drag's release;
    /// - leave enough frames after a release for falls and shredding to settle before the next
    ///   press, otherwise the press can find the board busy and select nothing (logged).
    /// </summary>
    [CreateAssetMenu(menuName = "Gravity Puzzle/Creative/Scripted Gameplay", fileName = "CreativeScript")]
    public sealed class CreativeScript : ScriptableObject
    {
        [Serializable]
        public struct Drag
        {
            [Tooltip("Frame (relative to script start) of the press.")]
            [Min(0)] public int startFrame;
            [Tooltip("Frames from press to release (the release frame is startFrame + moveFrames).")]
            [Min(1)] public int moveFrames;
            [Tooltip("Board cell pressed (x = column from the left, y = row from the bottom).")]
            public Vector2 fromCell;
            [Tooltip("Board cell where the pointer is released.")]
            public Vector2 toCell;
        }

        [Tooltip("Frames to wait after the level is ready before the script starts.")]
        [Min(0)] [SerializeField] private int startDelayFrames = 30;

        [SerializeField] private Drag[] drags = Array.Empty<Drag>();

        public int StartDelayFrames => startDelayFrames;
        public int DragCount => drags != null ? drags.Length : 0;
        public Drag GetDrag(int index) => drags[index];
    }
}
