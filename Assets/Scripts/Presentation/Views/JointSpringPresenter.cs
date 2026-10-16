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

        private PrototypeBoard boardState;
        private PieceJointGraph jointGraph;
        private GravityLevelDefinition activeLevel;
        private readonly Dictionary<int, JointSpringView> activeSpringViews = new Dictionary<int, JointSpringView>();
        private readonly List<PieceJointGraph.Joint> jointBuffer = new List<PieceJointGraph.Joint>();

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
            HashSet<int> currentJointIds = new HashSet<int>();

            for (int i = 0; i < jointBuffer.Count; i++)
            {
                PieceJointGraph.Joint joint = jointBuffer[i];
                currentJointIds.Add(joint.Id);

                if (!CalculateJointGeometry(joint, out Vector3 worldMidpoint, out bool isVertical, out PuzzlePiece pieceA))
                    continue;

                if (!activeSpringViews.TryGetValue(joint.Id, out JointSpringView view))
                {
                    if (JointSpringPool.TryRent(out view))
                    {
                        view.Initialize(
                            joint.Id,
                            joint.MemberA,
                            joint.MemberB,
                            worldMidpoint,
                            isVertical,
                            springSprite,
                            springMaterial,
                            sortingOrder,
                            springScale);

                        if (pieceA != null)
                            view.transform.SetParent(pieceA.transform, true);

                        activeSpringViews[joint.Id] = view;
                    }
                }
                else
                {
                    view.UpdateTransform(worldMidpoint, isVertical);
                    if (pieceA != null && view.transform.parent != pieceA.transform)
                        view.transform.SetParent(pieceA.transform, true);
                }
            }

            // Return removed joints to pool
            List<int> toRemove = null;
            foreach (var kvp in activeSpringViews)
            {
                if (!currentJointIds.Contains(kvp.Key))
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

        private void UpdateSpringPositions()
        {
            if (boardState == null || activeLevel == null)
                return;

            foreach (var kvp in activeSpringViews)
            {
                JointSpringView view = kvp.Value;
                if (view == null)
                    continue;

                // Find the joint data
                for (int i = 0; i < jointBuffer.Count; i++)
                {
                    if (jointBuffer[i].Id == view.JointId)
                    {
                        if (CalculateJointGeometry(jointBuffer[i], out Vector3 worldMidpoint, out bool isVertical, out _))
                            view.UpdateTransform(worldMidpoint, isVertical);
                        break;
                    }
                }
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

            Vector2 studCenterA = new Vector2(
                modelA.Anchor.X + joint.BlockA.X + blockSize * 0.5f,
                modelA.Anchor.Y + joint.BlockA.Y + blockSize * 0.5f);

            Vector2 studCenterB = new Vector2(
                modelB.Anchor.X + joint.BlockB.X + blockSize * 0.5f,
                modelB.Anchor.Y + joint.BlockB.Y + blockSize * 0.5f);

            Vector3 worldStudA = pieceA != null
                ? pieceA.transform.position + new Vector3(
                    (joint.BlockA.X + blockSize * 0.5f) * fineCellSize,
                    (joint.BlockA.Y + blockSize * 0.5f) * fineCellSize,
                    0f)
                : ModelFineToWorld(studCenterA, fineCellSize);

            Vector3 worldStudB = pieceB != null
                ? pieceB.transform.position + new Vector3(
                    (joint.BlockB.X + blockSize * 0.5f) * fineCellSize,
                    (joint.BlockB.Y + blockSize * 0.5f) * fineCellSize,
                    0f)
                : ModelFineToWorld(studCenterB, fineCellSize);

            worldMidpoint = (worldStudA + worldStudB) * 0.5f;
            worldMidpoint.z = -0.05f;

            Vector2 delta = studCenterB - studCenterA;
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
