using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public readonly struct RuntimePieceRoot
    {
        public RuntimePieceRoot(
            GameObject gameObject,
            Rigidbody2D body,
            CompositeCollider2D compositeCollider,
            LineRenderer outline,
            PuzzlePiece piece)
        {
            GameObject = gameObject;
            Body = body;
            CompositeCollider = compositeCollider;
            Outline = outline;
            Piece = piece;
        }

        public GameObject GameObject { get; }
        public Transform Transform => GameObject.transform;
        public Rigidbody2D Body { get; }
        public CompositeCollider2D CompositeCollider { get; }
        public LineRenderer Outline { get; }
        public PuzzlePiece Piece { get; }
    }
}
