using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;
using GravityPuzzle.Config;

namespace GravityPuzzle.Presentation.Views
{
    public sealed class JointSpringPresenter : MonoBehaviour
    {
        [Header("Spring Visual Assets")]
        [SerializeField] private JointSpringView springPrefab;
        [SerializeField] private int prewarmCapacity = 16;
        [SerializeField] private Sprite springSprite;
        [SerializeField] private Material springMaterial;
        [SerializeField] private int sortingOrder = 20;
        [Tooltip("Optional authored sprite-local stud and spring-end anchors. Unauthored sprites keep the legacy placement.")]
        [SerializeField] private JointAnchorConfig anchorConfig;
        [Tooltip("Log which placement path each joint uses when views are reconciled.")]
        [SerializeField] private bool logAnchorDiagnostics;

        [Header("Alignment & Position Tuning")]
        [Tooltip("Scale multiplier for the joint spiral rings (default ~0.1495).")]
        [SerializeField, Min(0.01f)] private float springScale = 0.1495036f;

        [Tooltip("Distance from the block center along the seam to each stud (default 0.25).")]
        [SerializeField, Min(0f)] private float studOffset = 0.25f;

        [Tooltip("Vertical depth offset of the holes from sprite center (default 0.099). Adjusts how high or low the rings sit on the studs.")]
        [SerializeField] private float holeDepthOffset = 0.099f;

        [Tooltip("Global manual nudge (X, Y) applied to all spirals.")]
        [SerializeField] private Vector2 positionOffset = Vector2.zero;

        [Tooltip("Extra manual nudge applied only when pieces are connected side-by-side (horizontal).")]
        [SerializeField] private Vector2 horizontalJointOffset = Vector2.zero;

        [Tooltip("Extra manual nudge applied only when pieces are connected vertically.")]
        [SerializeField] private Vector2 verticalJointOffset = Vector2.zero;

        private PrototypeBoard boardState;
        private PieceJointGraph jointGraph;
        private GravityLevelDefinition activeLevel;
        private readonly Dictionary<int, JointSpringView> activeSpringViews = new Dictionary<int, JointSpringView>();
        private readonly List<PieceJointGraph.Joint> jointBuffer = new List<PieceJointGraph.Joint>();
        private readonly HashSet<int> currentSpringKeys = new HashSet<int>();
        private readonly List<int> springKeysToRemove = new List<int>();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && boardState != null && activeSpringViews.Count > 0)
            {
                ReconcileSpringViews();
            }
        }
#endif

        public void Configure(
            PrototypeBoard board,
            PieceJointGraph graph,
            GravityLevelDefinition level,
            Transform poolParent,
            Sprite customSprite = null,
            Material customMaterial = null)
        {
            if (jointGraph != null)
                jointGraph.JointsChanged -= OnJointsChanged;

            if (boardState != null && boardState.JointSpringPresenter == this)
                boardState.JointSpringPresenter = null;

            ClearAll();

            boardState = board;
            if (boardState != null)
                boardState.JointSpringPresenter = this;

            jointGraph = graph;
            activeLevel = level;

            if (customSprite != null)
                springSprite = customSprite;
            if (customMaterial != null)
                springMaterial = customMaterial;

            Transform parent = poolParent != null ? poolParent : transform;
            JointSpringPool.Configure(springPrefab, parent, prewarmCapacity);

            if (jointGraph != null)
            {
                jointGraph.JointsChanged += OnJointsChanged;
                ReconcileSpringViews();
            }
        }

        private void OnDestroy()
        {
            if (jointGraph != null)
                jointGraph.JointsChanged -= OnJointsChanged;

            if (boardState != null && boardState.JointSpringPresenter == this)
                boardState.JointSpringPresenter = null;

            activeSpringViews.Clear();
            jointBuffer.Clear();
            springKeysToRemove.Clear();
            JointSpringPool.ResetPool();
        }

        private void LateUpdate()
        {
            // If joints exist but activeSpringViews is empty (e.g. pieces were not yet instantiated
            // during early bootstrap), reconcile as soon as the live pieces become available.
            if (activeSpringViews.Count == 0 && jointGraph != null && jointGraph.JointCount > 0 && boardState != null)
            {
                ReconcileSpringViews();
            }
        }

        public void OnJointsChanged()
        {
            ReconcileSpringViews();
        }

        public void ReconcileSpringViews()
        {
            if (jointGraph == null || activeLevel == null || boardState == null)
            {
                ClearAll();
                return;
            }

            jointBuffer.Clear();
            jointGraph.CollectJoints(jointBuffer);
            currentSpringKeys.Clear();

            for (int i = 0; i < jointBuffer.Count; i++)
            {
                PieceJointGraph.Joint joint = jointBuffer[i];
                if (!CalculateJointGeometry(
                    joint,
                    out Vector3 studMidpoint1,
                    out Vector3 studMidpoint2,
                    out Quaternion rotation,
                    out bool isVertical,
                    out PuzzlePiece anchorPiece,
                    out Quaternion rotation2,
                    out float scale1,
                    out float scale2,
                    out string placementStatus))
                {
                    continue;
                }

                if (logAnchorDiagnostics)
                    Debug.Log($"[JointSpring] joint={joint.Id} members={joint.MemberA},{joint.MemberB} placement={placementStatus}", this);

                int key1 = joint.Id * 2;
                int key2 = joint.Id * 2 + 1;
                currentSpringKeys.Add(key1);
                currentSpringKeys.Add(key2);

                UpdateOrSpawnSpring(key1, joint, studMidpoint1, rotation, isVertical, anchorPiece, scale1);
                UpdateOrSpawnSpring(key2, joint, studMidpoint2, rotation2, isVertical, anchorPiece, scale2);
            }

            // Return removed joints to pool
            springKeysToRemove.Clear();
            foreach (var kvp in activeSpringViews)
            {
                if (!currentSpringKeys.Contains(kvp.Key))
                    springKeysToRemove.Add(kvp.Key);
            }

            for (int i = 0; i < springKeysToRemove.Count; i++)
            {
                int id = springKeysToRemove[i];
                if (activeSpringViews.TryGetValue(id, out JointSpringView view))
                {
                    view.Despawn();
                    activeSpringViews.Remove(id);
                }
            }
        }

        private void UpdateOrSpawnSpring(
            int key,
            PieceJointGraph.Joint joint,
            Vector3 worldPosition,
            Quaternion rotation,
            bool isVertical,
            PuzzlePiece anchorPiece,
            float scale)
        {
            int targetSortingLayer = anchorPiece != null ? anchorPiece.PrimarySortingLayerID : 0;
            int targetSortingOrder = Mathf.Max(sortingOrder, 20);

            if (!activeSpringViews.TryGetValue(key, out JointSpringView view))
            {
                if (JointSpringPool.TryRent(out view))
                {
                    view.Initialize(
                        key,
                        joint.MemberA,
                        joint.MemberB,
                        worldPosition,
                        isVertical,
                        springSprite,
                        springMaterial,
                        targetSortingOrder,
                        scale);

                    view.SetSorting(targetSortingLayer, targetSortingOrder);

                    if (anchorPiece != null)
                    {
                        view.transform.SetParent(anchorPiece.transform, false);
                        view.transform.localPosition = anchorPiece.transform.InverseTransformPoint(worldPosition);
                        view.transform.localRotation = Quaternion.Inverse(anchorPiece.transform.rotation) * rotation;
                        view.transform.localScale = Vector3.one * scale;
                    }

                    activeSpringViews[key] = view;
                }
            }
            else
            {
                view.SetSorting(targetSortingLayer, targetSortingOrder);

                if (anchorPiece != null)
                {
                    if (view.transform.parent != anchorPiece.transform)
                        view.transform.SetParent(anchorPiece.transform, false);

                    view.transform.localPosition = anchorPiece.transform.InverseTransformPoint(worldPosition);
                    view.transform.localRotation = Quaternion.Inverse(anchorPiece.transform.rotation) * rotation;
                    view.transform.localScale = Vector3.one * scale;
                }
                else
                {
                    view.transform.position = worldPosition;
                    view.transform.rotation = rotation;
                    view.transform.localScale = Vector3.one * scale;
                }
            }
        }

        private bool CalculateJointGeometry(
            PieceJointGraph.Joint joint,
            out Vector3 studMidpoint1,
            out Vector3 studMidpoint2,
            out Quaternion rotation,
            out bool isVertical,
            out PuzzlePiece anchorPiece,
            out Quaternion rotation2,
            out float scale1,
            out float scale2,
            out string placementStatus)
        {
            studMidpoint1 = Vector3.zero;
            studMidpoint2 = Vector3.zero;
            rotation = Quaternion.identity;
            isVertical = false;
            anchorPiece = null;
            rotation2 = Quaternion.identity;
            scale1 = scale2 = springScale > 0f ? springScale : 0.1495036f;
            placementStatus = "legacy: missing config, active piece, or valid spring anchors";

            if (boardState == null || boardState.BoardSnapshot == null || activeLevel == null)
                return false;

            if (!boardState.BoardSnapshot.TryGetPiece(joint.MemberA, out PieceModel modelA) ||
                !boardState.BoardSnapshot.TryGetPiece(joint.MemberB, out PieceModel modelB))
            {
                return false;
            }

            PuzzlePiece pieceA = boardState.FindActivePiece(joint.MemberA);
            PuzzlePiece pieceB = boardState.FindActivePiece(joint.MemberB);
            anchorPiece = pieceA != null ? pieceA : pieceB;

            int subdivisions = Mathf.Max(1, activeLevel.subdivisions);
            float fineCellSize = 1f / subdivisions;
            float halfBlock = subdivisions * 0.5f * fineCellSize;

            Vector3 localBlockCenterA = new Vector3(
                (modelA.PivotOffset.X + joint.BlockA.X) * fineCellSize + halfBlock,
                (modelA.PivotOffset.Y + joint.BlockA.Y) * fineCellSize + halfBlock,
                0f);

            Vector3 localBlockCenterB = new Vector3(
                (modelB.PivotOffset.X + joint.BlockB.X) * fineCellSize + halfBlock,
                (modelB.PivotOffset.Y + joint.BlockB.Y) * fineCellSize + halfBlock,
                0f);

            Vector3 worldCenterA = pieceA != null
                ? pieceA.transform.TransformPoint(localBlockCenterA)
                : (Vector3)GravityLevelGridCoordinates.FineCellToWorld(activeLevel, modelA.Anchor.Offset(joint.BlockA)) +
                  new Vector3(halfBlock, halfBlock, 0f);

            Vector3 worldCenterB = pieceB != null
                ? pieceB.transform.TransformPoint(localBlockCenterB)
                : (Vector3)GravityLevelGridCoordinates.FineCellToWorld(activeLevel, modelB.Anchor.Offset(joint.BlockB)) +
                  new Vector3(halfBlock, halfBlock, 0f);

            Vector3 delta = worldCenterB - worldCenterA;
            isVertical = Mathf.Abs(delta.y) > Mathf.Abs(delta.x);

            Vector3 seamMidpoint = (worldCenterA + worldCenterB) * 0.5f;

            if (isVertical)
            {
                rotation = delta.y >= 0f
                    ? Quaternion.Euler(0f, 0f, 90f)
                    : Quaternion.Euler(0f, 0f, -90f);
            }
            else
            {
                rotation = Quaternion.identity;
            }

            // In SpiralSpring sprite:
            // Holes are offset from center pivot by +66.2 pixels (unscaled 0.662 units at PPU 100).
            // When scaled by springScale, holeOffsetY in world units is:
            float effectiveScale = springScale > 0f ? springScale : 0.1495036f;
            float holeOffsetY = holeDepthOffset != 0f ? holeDepthOffset : (0.662f * effectiveScale);
            Vector3 holeOffsetWorld = rotation * new Vector3(0f, holeOffsetY, 0f);

            // Stud spacing along the seam:
            // A 1x1 block has 2 rows and 2 columns of studs spaced 0.5 units apart (0.25 units from center).
            float spacing = studOffset > 0f ? studOffset : 0.25f;
            Vector3 studOffsetVec = isVertical
                ? new Vector3(spacing, 0f, 0f)
                : new Vector3(0f, spacing, 0f);

            Vector3 userNudge = (Vector3)positionOffset + (isVertical ? (Vector3)verticalJointOffset : (Vector3)horizontalJointOffset);

            studMidpoint1 = (seamMidpoint - studOffsetVec) - holeOffsetWorld + userNudge;
            studMidpoint1.z = -0.15f;

            studMidpoint2 = (seamMidpoint + studOffsetVec) - holeOffsetWorld + userNudge;
            studMidpoint2.z = -0.15f;

            rotation2 = rotation;
            if (anchorConfig != null && pieceA != null && pieceB != null &&
                anchorConfig.TryGetSpringFitPoints(springSprite, out Vector2 endA, out Vector2 endB))
            {
                placementStatus = "legacy: stud anchors unavailable near one or more joint endpoints";
                Vector3 along = isVertical ? Vector3.right : Vector3.up;
                Vector3 across = isVertical ? Vector3.up : Vector3.right;
                float sign = Vector3.Dot(delta, across) >= 0f ? 1f : -1f;
                Vector3 studA1 = worldCenterA + across * (sign * .25f) - along * .25f;
                Vector3 studB1 = worldCenterB - across * (sign * .25f) - along * .25f;
                Vector3 studA2 = worldCenterA + across * (sign * .25f) + along * .25f;
                Vector3 studB2 = worldCenterB - across * (sign * .25f) + along * .25f;
                if (TryFindStud(pieceA, studA1, out Vector3 a1) && TryFindStud(pieceB, studB1, out Vector3 b1) &&
                    TryFindStud(pieceA, studA2, out Vector3 a2) && TryFindStud(pieceB, studB2, out Vector3 b2))
                {
                    if (sign < 0f) { Vector3 t = a1; a1 = b1; b1 = t; t = a2; a2 = b2; b2 = t; }
                    if (TryFitSpring(a1, b1, endA, endB, out Vector3 p1, out Quaternion r1, out float s1) &&
                        TryFitSpring(a2, b2, endA, endB, out Vector3 p2, out Quaternion r2, out float s2))
                    {
                        studMidpoint1 = p1 + userNudge; studMidpoint1.z = -0.15f;
                        studMidpoint2 = p2 + userNudge; studMidpoint2.z = -0.15f;
                        rotation = r1; rotation2 = r2; scale1 = s1; scale2 = s2;
                        placementStatus = "authored anchors";
                    }
                }
            }

            return true;
        }

        private bool TryFindStud(PuzzlePiece piece, Vector3 expected, out Vector3 result)
        {
            result = Vector3.zero;
            float best = .3f * .3f;
            bool found = false;
            ConsiderRenderer(piece.WholePieceVisual, expected, ref best, ref found, ref result);
            var joined = piece.JoinedPieceVisuals;
            for (int i = 0; i < joined.Count; i++)
                ConsiderRenderer(joined[i], expected, ref best, ref found, ref result);
            var cells = piece.CollisionCellVisuals;
            if (cells != null)
                for (int i = 0; i < cells.Count; i++)
                    ConsiderRenderer(cells[i], expected, ref best, ref found, ref result);
            return found;
        }

        private void ConsiderRenderer(SpriteRenderer renderer, Vector3 expected, ref float best, ref bool found, ref Vector3 result)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sprite == null ||
                renderer.drawMode != SpriteDrawMode.Simple ||
                !anchorConfig.TryGetStuds(renderer.sprite, out List<Vector2> studs))
                return;
            for (int i = 0; i < studs.Count; i++)
            {
                Vector2 local = JointAnchorConfig.SpriteLocalPoint(renderer.sprite, studs[i]);
                if (renderer.flipX) local.x = -local.x;
                if (renderer.flipY) local.y = -local.y;
                Vector3 world = renderer.transform.TransformPoint(local);
                float distance = ((Vector2)(world - expected)).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; result = world; found = true;
            }
        }

        private bool TryFitSpring(Vector3 a, Vector3 b, Vector2 anchorA, Vector2 anchorB,
            out Vector3 position, out Quaternion rotation, out float scale)
        {
            Vector2 localA = JointAnchorConfig.SpriteLocalPoint(springSprite, anchorA);
            Vector2 localB = JointAnchorConfig.SpriteLocalPoint(springSprite, anchorB);
            Vector2 localDelta = localB - localA;
            Vector2 worldDelta = b - a;
            position = Vector3.zero; rotation = Quaternion.identity; scale = 0f;
            if (localDelta.sqrMagnitude < .000001f || worldDelta.sqrMagnitude < .000001f) return false;
            scale = worldDelta.magnitude / localDelta.magnitude;
            float angle = Mathf.Atan2(worldDelta.y, worldDelta.x) - Mathf.Atan2(localDelta.y, localDelta.x);
            rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
            position = a - rotation * (Vector3)(localA * scale);
            return true;
        }

        private void ClearAll()
        {
            foreach (var kvp in activeSpringViews)
            {
                if (kvp.Value != null)
                    kvp.Value.Despawn();
            }
            activeSpringViews.Clear();
            jointBuffer.Clear();
            springKeysToRemove.Clear();
        }
    }
}

