using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "PieceVisualConfig", menuName = "Gravity Puzzle/Config/Piece Visuals")]
    public sealed class PieceVisualConfig : ScriptableObject
    {
        [SerializeField] private List<PieceVisualDefinition> definitions = new List<PieceVisualDefinition>();

        public IReadOnlyList<PieceVisualDefinition> Definitions => definitions;

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
