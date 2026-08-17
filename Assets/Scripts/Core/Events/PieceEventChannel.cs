using System;
using UnityEngine;

namespace GravityPuzzle.Core.Events
{
    [CreateAssetMenu(fileName = "PieceEventChannel", menuName = "Gravity Puzzle/Events/Piece")]
    public sealed class PieceEventChannel : ScriptableObject
    {
        public event Action<int> Raised;

        public void Raise(int pieceId)
        {
            Raised?.Invoke(pieceId);
        }
    }
}
