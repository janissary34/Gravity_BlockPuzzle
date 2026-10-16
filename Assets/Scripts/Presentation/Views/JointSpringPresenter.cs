using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;

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

            ClearAll();
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
                    out PuzzlePiece anchorPiece))
                {
                    continue;
                }

                int key1 = joint.Id * 2;
                int key2 = joint.Id * 2 + 1;
                currentSpringKeys.Add(key1);
                currentSpringKeys.Add(key2);

                UpdateOrSpawnSpring(key1, joint, studMidpoint1, rotation, isVertical, anchorPiece);
                UpdateOrSpawnSpring(key2, joint, studMidpoint2, rotation, isVertical, anchorPiece);
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
            PuzzlePiece anchorPiece)
        {
            int targetSortingLayer = anchorPiece != null ? anchorPiece.PrimarySortingLayerID : 0;
            int targetSortingOrder = Mathf.Max(sortingOrder, 20);
            float scale = springScale > 0f ? springScale : 0.1495036f;

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
            out PuzzlePiece anchorPiece)
        {
            studMidpoint1 = Vector3.zero;
            studMidpoint2 = Vector3.zero;
            rotation = Quaternion.identity;
            isVertical = false;
            anchorPiece = null;

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

