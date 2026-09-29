using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle
{
    public enum RevealAreaState
    {
        Locked,
        Unlocking,
        Unlocked
    }

    [Serializable]
    public struct RevealAreaBounds
    {
        [Tooltip("Bottom-left coordinate in fine-grid cells.")]
        public Vector2Int origin;
        [Tooltip("Width and height in fine-grid cells.")]
        public Vector2Int size;

        public int XMin => origin.x;
        public int YMin => origin.y;
        public int XMaxExclusive => origin.x + Mathf.Max(1, size.x);
        public int YMaxExclusive => origin.y + Mathf.Max(1, size.y);
    }

    [Serializable]
    public abstract class RevealAreaDefinition
    {
        [Tooltip("Stable identifier used to bind an authored RevealAreaPresentation in the scene.")]
        public string areaId = Guid.Empty.ToString();
        public string name = "Reveal Area";
        public RevealAreaBounds bounds = new RevealAreaBounds
        {
            origin = Vector2Int.zero,
            size = Vector2Int.one
        };
        [Tooltip("Editor-only authoring state. It never changes runtime reveal rules.")]
        public bool editorLocked;
        public List<PieceDefinition> hiddenPieces = new List<PieceDefinition>();
    }

    [Serializable]
    public sealed class BoxRevealDefinition : RevealAreaDefinition
    {
        [Min(0)]
        [Tooltip("Number of successfully shredder-committed pieces required to open this box.")]
        public int targetShredCount = 1;
    }

    [Serializable]
    public sealed class ElevatorRevealDefinition : RevealAreaDefinition
    {
    }
}
