using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public sealed class GeneratedRuntimePieceRootProvider : IRuntimePieceRootProvider
    {
        public RuntimePieceRoot Create(string pieceName)
        {
            GameObject rootObject = new GameObject(pieceName);
            Rigidbody2D body = rootObject.AddComponent<Rigidbody2D>();
            CompositeCollider2D composite = rootObject.AddComponent<CompositeCollider2D>();
            LineRenderer outline = rootObject.AddComponent<LineRenderer>();
            PuzzlePiece piece = rootObject.AddComponent<PuzzlePiece>();

            return new RuntimePieceRoot(
                rootObject,
                body,
                composite,
                outline,
                piece);
        }
    }
}
