using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle
{
    [CreateAssetMenu(fileName = "LevelSequence", menuName = "Gravity Puzzle/Level Sequence")]
    public sealed class GravityLevelSequence : ScriptableObject
    {
        [Tooltip("Levels play from top to bottom. Drag elements to change their order.")]
        public List<GravityLevelDefinition> levels = new List<GravityLevelDefinition>();
    }
}
