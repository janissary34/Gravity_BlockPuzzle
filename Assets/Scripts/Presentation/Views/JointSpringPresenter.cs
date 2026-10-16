using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    public sealed class JointSpringPresenter : MonoBehaviour
    {
        [SerializeField] private JointSpringView springPrefab;
        [SerializeField] private int prewarmCapacity = 16;
        [SerializeField] private Sprite springSprite;
        [SerializeField] private Material springMaterial;
        [SerializeField] private int sortingOrder = 5;
        [SerializeField] private float springScale = 0.14514376f;

        [SerializeField] private float studOffset = 0.18f;

        private PrototypeBoard boardState;
        private PieceJointGraph jointGraph;
        private GravityLevelDefinition activeLevel;
        private readonly Dictionary<int, JointSpringView> activeSpringViews = new Dictionary<int, JointSpringView>();
        private readonly List<PieceJointGraph.Joint> jointBuffer = new List<PieceJointGraph.Joint>();
        private readonly HashSet<int> currentSpringKeys = new HashSet<int>();

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

            boardState = board;
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

            ClearAll();
        }

        private void LateUpdate()
        {
            UpdateSpringPositions();
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
                if (!CalculateJointGeometry(joint, out Vector3 worldMidpoint, out bool isVertical, out PuzzlePiece pieceA))
                    continue;

                Vector3 studOffsetVec = isVertical
                    ? new Vector3(studOffset, 0f, 0f)
                    : new Vector3(0f, studOffset, 0f);

                int key1 = joint.Id * 2;
                int key2 = joint.Id * 2 + 1;
                currentSpringKeys.Add(key1);
                currentSpringKeys.Add(key2);

                UpdateOrSpawnSpring(key1, joint, worldMidpoint - studOffsetVec, isVertical, pieceA);
                UpdateOrSpawnSpring(key2, joint, worldMidpoint + studOffsetVec, isVertical, pieceA);
            }

            // Return removed joints to pool
            List<int> toRemove = null;
            foreach (var kvp in activeSpringViews)
            {
                if (!currentSpringKeys.Contains(kvp.Key))
                {
                    if (toRemove == null)
                        toRemove = new List<int>();
                    toRemove.Add(kvp.Key);
                }
            }

            if (toRemove != null)
            {
                for (int i = 0; i < toRemove.Count; i++)
                {
                    int id = toRemove[i];
                    if (activeSpringViews.TryGetValue(id, out JointSpringView view))
                    {
                        view.Despawn();
                        activeSpringViews.Remove(id);
                    }
                }
            }
        }

        private void UpdateOrSpawnSpring(
            int key,
            PieceJointGraph.Joint joint,
            Vector3 position,
            bool isVertical,
            PuzzlePiece pieceA)
        {
            if (!activeSpringViews.TryGetValue(key, out JointSpringView view))
            {
                if (JointSpringPool.TryRent(out view))
                {
                    view.Initialize(
                        key,
                        joint.MemberA,
                        joint.MemberB,
                        position,
                        isVertical,
                        springSprite,
                        springMaterial,
                        sortingOrder,
                        springScale);

                    if (pieceA != null)
                        view.transform.SetParent(pieceA.transform, true);

                    activeSpringViews[key] = view;
                }
            }
            else
            {
                view.UpdateTransform(position, isVertical);
                if (pieceA != null && view.transform.parent != pieceA.transform)
                    view.transform.SetParent(pieceA.transform, true);
            }
        }

        private void UpdateSpringPositions()
        {
            if (boardState == null || activeLevel == null)
                return;

            for (int i = 0; i < jointBuffer.Count; i++)
            {
                PieceJointGraph.Joint joint = jointBuffer[i];
                if (!CalculateJointGeometry(joint, out Vector3 worldMidpoint, out bool isVertical, out _))
                    continue;

                Vector3 studOffsetVec = isVertical
                    ? new Vector3(studOffset, 0f, 0f)
                    : new Vector3(0f, studOffset, 0f);

                int key1 = joint.Id * 2;
                int key2 = joint.Id * 2 + 1;

                if (activeSpringViews.TryGetValue(key1, out JointSpringView view1))
                    view1.UpdateTransform(worldMidpoint - studOffsetVec, isVertical);

                if (activeSpringViews.TryGetValue(key2, out JointSpringView view2))
                    view2.UpdateTransform(worldMidpoint + studOffsetVec, isVertical);
            }
        }

        private bool CalculateJointGeometry(
            PieceJointGraph.Joint joint,
            out Vector3 worldMidpoint,
            out bool isVertical,
            out PuzzlePiece pieceA)
        {
            worldMidpoint = Vector3.zero;
            isVertical = false;
            pieceA = null;

            if (boardState.BoardSnapshot == null)
                return false;

            if (!boardState.BoardSnapshot.TryGetPiece(joint.MemberA, out PieceModel modelA) ||
                !boardState.BoardSnapshot.TryGetPiece(joint.MemberB, out PieceModel modelB))
            {
                return false;
            }

            pieceA = boardState.FindActivePiece(joint.MemberA);
            PuzzlePiece pieceB = boardState.FindActivePiece(joint.MemberB);

            int subdivisions = Mathf.Max(1, activeLevel.subdivisions);
            float fineCellSize = 1f / subdivisions;
            int blockSize = subdivisions;

            Vector3 localBlockCenterA = new Vector3(
                (modelA.PivotOffset.X + joint.BlockA.X + (blockSize - 1) * 0.5f) * fineCellSize,
                (modelA.PivotOffset.Y + joint.BlockA.Y + (blockSize - 1) * 0.5f) * fineCellSize,
                0f);

            Vector3 localBlockCenterB = new Vector3(
                (modelB.PivotOffset.X + joint.BlockB.X + (blockSize - 1) * 0.5f) * fineCellSize,
                (modelB.PivotOffset.Y + joint.BlockB.Y + (blockSize - 1) * 0.5f) * fineCellSize,
                0f);

            Vector3 worldStudA = pieceA != null
                ? pieceA.transform.TransformPoint(localBlockCenterA)
                : (Vector3)GravityLevelGridCoordinates.FineCellToWorld(activeLevel, modelA.Anchor.Offset(joint.BlockA)) +
                  new Vector3((blockSize - 1) * 0.5f * fineCellSize, (blockSize - 1) * 0.5f * fineCellSize, 0f);

            Vector3 worldStudB = pieceB != null
                ? pieceB.transform.TransformPoint(localBlockCenterB)
                : (Vector3)GravityLevelGridCoordinates.FineCellToWorld(activeLevel, modelB.Anchor.Offset(joint.BlockB)) +
                  new Vector3((blockSize - 1) * 0.5f * fineCellSize, (blockSize - 1) * 0.5f * fineCellSize, 0f);

            worldMidpoint = (worldStudA + worldStudB) * 0.5f;
            worldMidpoint.z = -0.1f;

            Vector3 delta = worldStudB - worldStudA;
            isVertical = Mathf.Abs(delta.y) > Mathf.Abs(delta.x);
            return true;
        }

        private Vector3 ModelFineToWorld(Vector2 fineCoord, float fineCellSize)
        {
            return new Vector3(
                -activeLevel.boardColumns * 0.5f + fineCoord.x * fineCellSize,
                -activeLevel.boardRows * 0.5f + fineCoord.y * fineCellSize,
                0f);
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
        }
    }
}
