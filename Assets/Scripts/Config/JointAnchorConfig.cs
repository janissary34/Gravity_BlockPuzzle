using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Config
{
    // Points use normalized sprite-rectangle coordinates, with (0,0) at bottom left.
    [CreateAssetMenu(fileName = "JointAnchorConfig", menuName = "Gravity Puzzle/Config/Joint Anchors")]
    public sealed class JointAnchorConfig : ScriptableObject
    {
        [Serializable]
        public sealed class StudLayout
        {
            public Sprite sprite;
            public List<Vector2> studs = new List<Vector2>();
        }

        [SerializeField] private List<StudLayout> layouts = new List<StudLayout>();
        [SerializeField] private Sprite springSprite;
        [SerializeField] private Vector2 springEndA = new Vector2(.15f, .75f);
        [SerializeField] private Vector2 springEndB = new Vector2(.85f, .75f);
        [SerializeField] private bool springEndsAuthored;
        [Tooltip("Optional fitting points derived from the manually adjusted spring reference. These are separate from the visible black ends.")]
        [SerializeField] private bool useReferenceFit;
        [SerializeField] private Vector2 referenceFitA;
        [SerializeField] private Vector2 referenceFitB;

        public bool TryGetStuds(Sprite sprite, out List<Vector2> studs)
        {
            for (int i = 0; i < layouts.Count; i++)
            {
                if (layouts[i] != null && layouts[i].sprite == sprite && layouts[i].studs.Count > 0)
                {
                    for (int j = 0; j < layouts[i].studs.Count; j++)
                        if (!IsValidPoint(layouts[i].studs[j]))
                        {
                            studs = null;
                            return false;
                        }
                    studs = layouts[i].studs;
                    return true;
                }
            }
            studs = null;
            return false;
        }

        public bool TryGetSpringEnds(Sprite sprite, out Vector2 a, out Vector2 b)
        {
            a = springEndA;
            b = springEndB;
            return springEndsAuthored && sprite != null && sprite == springSprite &&
                   IsValidPoint(a) && IsValidPoint(b) &&
                   (a - b).sqrMagnitude > .000001f;
        }

        public bool TryGetSpringFitPoints(Sprite sprite, out Vector2 a, out Vector2 b)
        {
            if (!TryGetSpringEnds(sprite, out a, out b))
                return false;
            if (!useReferenceFit)
                return true;
            if (!IsValidPoint(referenceFitA) || !IsValidPoint(referenceFitB) ||
                (referenceFitA - referenceFitB).sqrMagnitude <= .000001f)
                return false;
            a = referenceFitA;
            b = referenceFitB;
            return true;
        }

        public static bool IsValidPoint(Vector2 point)
        {
            return !float.IsNaN(point.x) && !float.IsNaN(point.y) &&
                   point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;
        }

        public static Vector2 SpriteLocalPoint(Sprite sprite, Vector2 normalized)
        {
            return (Vector2.Scale(normalized, sprite.rect.size) - sprite.pivot) / sprite.pixelsPerUnit;
        }
    }
}
