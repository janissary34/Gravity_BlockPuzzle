using DG.Tweening;
using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "TweenConfig", menuName = "Gravity Puzzle/Config/Tween")]
    public sealed class TweenConfig : ScriptableObject
    {
        [Min(1)] [SerializeField] private int tweenCapacity = 200;
        [Min(1)] [SerializeField] private int sequenceCapacity = 125;
        [SerializeField] private float pieceMoveDuration = .12f;
        [SerializeField] private Ease pieceMoveEase = Ease.OutQuad;
        [Min(.001f)] [SerializeField] private float gridFallDuration = .12f;
        [SerializeField] private Ease gridFallEase = Ease.OutQuad;
        [SerializeField] private float shredDuration = .3f;
        [SerializeField] private Ease shredEase = Ease.InQuad;

        public int TweenCapacity => tweenCapacity;
        public int SequenceCapacity => sequenceCapacity;
        public float PieceMoveDuration => pieceMoveDuration;
        public Ease PieceMoveEase => pieceMoveEase;
        public float GridFallDuration => gridFallDuration;
        public Ease GridFallEase => gridFallEase;
        public float ShredDuration => shredDuration;
        public Ease ShredEase => shredEase;
    }
}
