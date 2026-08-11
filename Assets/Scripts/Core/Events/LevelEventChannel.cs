using System;
using UnityEngine;

namespace GravityPuzzle.Core.Events
{
    [CreateAssetMenu(fileName = "LevelEventChannel", menuName = "Gravity Puzzle/Events/Level")]
    public sealed class LevelEventChannel : ScriptableObject
    {
        public event Action Raised;

        public void Raise()
        {
            Raised?.Invoke();
        }
    }
}
