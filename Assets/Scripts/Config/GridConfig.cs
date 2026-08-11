using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "GridConfig", menuName = "Gravity Puzzle/Config/Grid")]
    public sealed class GridConfig : ScriptableObject
    {
        [Min(1)] [SerializeField] private int columns = 6;
        [Min(1)] [SerializeField] private int rows = 8;
        [Min(1)] [SerializeField] private int subdivisions = 4;
        [Min(.001f)] [SerializeField] private float cellSize = 1f;
        [SerializeField] private Vector2 origin;

        public int Columns => columns;
        public int Rows => rows;
        public int Subdivisions => subdivisions;
        public float CellSize => cellSize;
        public Vector2 Origin => origin;
    }
}
