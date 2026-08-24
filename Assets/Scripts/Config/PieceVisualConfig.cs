using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "PieceVisualConfig", menuName = "Gravity Puzzle/Config/Piece Visuals")]
    public sealed class PieceVisualConfig : ScriptableObject
    {
        [SerializeField] private List<PieceVisualDefinition> definitions = new List<PieceVisualDefinition>();

        [Header("Outline Presentation")]
        [SerializeField, Min(0.001f)] private float restingOutlineWidth = 0.05f;
        [SerializeField, Min(0.001f)] private float selectedOutlineWidth = 0.08f;
        [SerializeField] private Color restingOutlineColor = Color.black;
        [SerializeField] private Color selectedOutlineColor = Color.white;
        [SerializeField] private int restingOutlineSortingOrder = 10;
        [SerializeField] private int selectedOutlineSortingOrder = 20;
        [SerializeField, Range(0, 8)] private int outlineCornerVertices = 4;
        [SerializeField, Range(0, 8)] private int outlineCapVertices = 4;

        public IReadOnlyList<PieceVisualDefinition> Definitions => definitions;
        public float RestingOutlineWidth => restingOutlineWidth;
        public float SelectedOutlineWidth => selectedOutlineWidth;
        public Color RestingOutlineColor => restingOutlineColor;
        public Color SelectedOutlineColor => selectedOutlineColor;
        public int RestingOutlineSortingOrder => restingOutlineSortingOrder;
        public int SelectedOutlineSortingOrder => selectedOutlineSortingOrder;
        public int OutlineCornerVertices => outlineCornerVertices;
        public int OutlineCapVertices => outlineCapVertices;

        public bool TryGet(string visualId, out PieceVisualDefinition definition)
        {
            for (int index = 0; index < definitions.Count; index++)
            {
                PieceVisualDefinition candidate = definitions[index];
                if (candidate.VisualId == visualId)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = default;
            return false;
        }
    }

    [Serializable]
    public struct PieceVisualDefinition
    {
        [SerializeField] private string visualId;
        [SerializeField] private Sprite sprite;
        [SerializeField] private Color tint;

        public string VisualId => visualId;
        public Sprite Sprite => sprite;
        public Color Tint => tint;
    }
}
