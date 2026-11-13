using System;
using System.Collections.Generic;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;
using UnityEngine.Rendering;

namespace GravityPuzzle.Presentation.VFX
{
    /// <summary>
    /// Draws a straight, flat streak in the piece's own colour behind
    /// moving pieces: grid falls, open freefall, the shredder feed and,
    /// optionally, drag. Presentation only: it reads piece transforms,
    /// lifecycle state and cell bounds, and never writes gameplay or piece
    /// state.
    ///
    /// Every streak is built into one dynamic world-space mesh per frame, so
    /// the feature needs no pooled objects and costs a single draw call. Each
    /// piece gets one contiguous streak spanning its full width that points
    /// straight back along the dominant motion axis. It starts behind the
    /// nearest trailing column (hidden behind the piece art), so stepped
    /// shapes such as L read as a single streak, and fades out continuously
    /// along its whole length (piece back edge plus the distance travelled in
    /// the trail window).
    /// When the piece stops, its samples age out and the streak retracts.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class FallTrailPresentation : MonoBehaviour
    {
        private const float GeometryEpsilon = .0001f;
        // Rows along each streak used to sample the alpha fade curve.
        private const int FadeRows = 12;

        [SerializeField] private FallTrailConfig config;

        private sealed class MotionHistory
        {
            public Vector2[] Positions;
            public float[] Times;
            public int Count;
            public int Stamp;

            public void Prepare(int capacity)
            {
                if (Positions == null || Positions.Length != capacity)
                {
                    Positions = new Vector2[capacity];
                    Times = new float[capacity];
                }

                Count = 0;
            }
        }

        private readonly Dictionary<PuzzlePiece, MotionHistory> histories =
            new Dictionary<PuzzlePiece, MotionHistory>(32);
        private readonly Stack<MotionHistory> historyPool = new Stack<MotionHistory>(32);
        private readonly List<PuzzlePiece> staleHistories = new List<PuzzlePiece>(16);

        private readonly List<Bounds> cellBuffer = new List<Bounds>(64);
        // Back-most edge per column (lane) perpendicular to the motion axis.
        private readonly Dictionary<int, float> laneBackEdges = new Dictionary<int, float>(32);

        private readonly List<Vector3> vertices = new List<Vector3>(2048);
        private readonly List<Color32> colors = new List<Color32>(2048);
        private readonly List<int> triangles = new List<int>(4096);

        private Mesh mesh;
        private int frameStamp;

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogWarning("[FallTrail] FallTrailConfig is not assigned; piece trails are disabled.", this);
                enabled = false;
                return;
            }

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            mesh = new Mesh { name = "Fall Trail Mesh", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;

            meshRenderer.sortingOrder = config.SortingOrder;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // Vertices are written in world space.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        private void OnDisable()
        {
            if (mesh != null)
                mesh.Clear();
            ReturnAllHistories();
        }

        private void LateUpdate()
        {
            vertices.Clear();
            colors.Clear();
            triangles.Clear();

            if (!config.Enabled)
            {
                ReturnAllHistories();
                mesh.Clear();
                return;
            }

            frameStamp++;
            float now = Time.time;
            PrototypeBoard board = PrototypeBoard.Active;
            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                if (piece == null || !piece.isActiveAndEnabled)
                    continue;

                MotionHistory history = GetHistory(piece);
                history.Stamp = frameStamp;
                if (!IsTrailEligible(board, piece))
                {
                    history.Count = 0;
                    continue;
                }

                RecordSample(history, piece.transform.position, now);
                if (TryMeasureTrail(history, now, out float trailLength, out Vector2 motionDirection))
                    AppendPieceTrail(piece, trailLength, motionDirection);
            }

            ReturnStaleHistories();
            UploadMesh();
        }

        private bool IsTrailEligible(PrototypeBoard board, PuzzlePiece piece)
        {
            if (board == null || !board.TryGetPieceModel(piece, out PieceModel model))
                return false;

            switch (model.State)
            {
                case PieceState.Dragging:
                    return config.ShowWhileDragging;
                case PieceState.Spawned:
                case PieceState.Despawned:
                    return false;
                default:
                    // Placed is kept so a landed piece's tail retracts smoothly
                    // instead of disappearing on the landing frame.
                    return true;
            }
        }

        private void RecordSample(MotionHistory history, Vector2 position, float now)
        {
            float teleport = config.TeleportDistance;
            if (history.Count > 0 && (position - history.Positions[0]).sqrMagnitude > teleport * teleport)
                history.Count = 0;

            int capacity = history.Positions.Length;
            int kept = Mathf.Min(history.Count, capacity - 1);
            if (kept > 0)
            {
                Array.Copy(history.Positions, 0, history.Positions, 1, kept);
                Array.Copy(history.Times, 0, history.Times, 1, kept);
            }

            history.Positions[0] = position;
            history.Times[0] = now;
            history.Count = kept + 1;

            // Keep everything inside the trail window plus the first sample
            // beyond it, which the length measurement interpolates against.
            float cutoff = now - config.TrailDuration;
            for (int index = 1; index < history.Count; index++)
            {
                if (history.Times[index] < cutoff)
                {
                    history.Count = index + 1;
                    break;
                }
            }
        }

        /// <summary>
        /// Measures how far the piece travelled inside the trail window (capped
        /// at the maximum length) and its most recent motion direction.
        /// </summary>
        private bool TryMeasureTrail(
            MotionHistory history,
            float now,
            out float trailLength,
            out Vector2 motionDirection)
        {
            trailLength = 0f;
            motionDirection = Vector2.zero;
            if (history.Count < 2)
                return false;

            float cutoff = now - config.TrailDuration;
            for (int index = 1; index < history.Count; index++)
            {
                Vector2 newer = history.Positions[index - 1];
                Vector2 older = history.Positions[index];
                bool finished = false;
                if (history.Times[index] < cutoff)
                {
                    float newerTime = history.Times[index - 1];
                    float span = newerTime - history.Times[index];
                    float fraction = span > GeometryEpsilon ? (newerTime - cutoff) / span : 0f;
                    older = Vector2.Lerp(newer, older, Mathf.Clamp01(fraction));
                    finished = true;
                }

                Vector2 step = newer - older;
                if (motionDirection == Vector2.zero && step.sqrMagnitude > GeometryEpsilon * GeometryEpsilon)
                    motionDirection = step.normalized;

                trailLength += step.magnitude;
                if (finished)
                    break;
            }

            trailLength = Mathf.Min(trailLength, config.MaximumLength);
            return trailLength >= config.MinimumVisibleLength && motionDirection != Vector2.zero;
        }

        private void AppendPieceTrail(PuzzlePiece piece, float trailLength, Vector2 motionDirection)
        {
            if (piece.GetActiveCellBounds(cellBuffer) == 0)
                return;

            Vector2 cellSize = cellBuffer[0].size;
            if (cellSize.x <= GeometryEpsilon || cellSize.y <= GeometryEpsilon)
                return;

            // The streak leaves from the side opposite the dominant motion axis.
            // "Back" distances are measured along that side, "perp" across it.
            bool horizontal = Mathf.Abs(motionDirection.x) > Mathf.Abs(motionDirection.y);
            float backSign = horizontal
                ? (motionDirection.x > 0f ? -1f : 1f)
                : (motionDirection.y > 0f ? -1f : 1f);
            float halfAlong = (horizontal ? cellSize.x : cellSize.y) * .5f;
            float halfPerp = (horizontal ? cellSize.y : cellSize.x) * .5f;

            Vector2 firstCenter = cellBuffer[0].center;
            float laneOrigin = horizontal ? firstCenter.y : firstCenter.x;
            laneBackEdges.Clear();
            float perpMin = float.PositiveInfinity;
            float perpMax = float.NegativeInfinity;
            for (int index = 0; index < cellBuffer.Count; index++)
            {
                Vector2 center = cellBuffer[index].center;
                float along = horizontal ? center.x : center.y;
                float perp = horizontal ? center.y : center.x;
                float backEdge = along * backSign + halfAlong;
                int lane = Mathf.RoundToInt((perp - laneOrigin) / (halfPerp * 2f));
                if (!laneBackEdges.TryGetValue(lane, out float laneEdge) || backEdge > laneEdge)
                    laneBackEdges[lane] = backEdge;

                perpMin = Mathf.Min(perpMin, perp - halfPerp);
                perpMax = Mathf.Max(perpMax, perp + halfPerp);
            }

            float nearBack = float.PositiveInfinity;
            float farBack = float.NegativeInfinity;
            foreach (KeyValuePair<int, float> lane in laneBackEdges)
            {
                nearBack = Mathf.Min(nearBack, lane.Value);
                farBack = Mathf.Max(farBack, lane.Value);
            }

            perpMin += config.SideInset;
            perpMax -= config.SideInset;
            if (perpMax - perpMin <= GeometryEpsilon)
                return;

            AppendStreak(
                horizontal,
                backSign,
                perpMin,
                perpMax,
                nearBack - config.HeadOverlap,
                farBack,
                trailLength,
                Color.Lerp(piece.VisualColor, Color.white, config.WhiteBlend));
        }

        /// <summary>
        /// One full-width strip from <paramref name="startBack"/> (just inside
        /// the piece) to the end of the trail, fading continuously along its
        /// whole length so the trace dissolves gradually.
        /// </summary>
        private void AppendStreak(
            bool horizontal,
            float backSign,
            float perpMin,
            float perpMax,
            float startBack,
            float fadeStartBack,
            float trailLength,
            Color color)
        {
            int baseVertex = vertices.Count;
            float endBack = fadeStartBack + trailLength;
            for (int row = 0; row <= FadeRows; row++)
            {
                float progress = row / (float)FadeRows;
                AppendStreakRow(
                    horizontal,
                    backSign,
                    perpMin,
                    perpMax,
                    Mathf.Lerp(startBack, endBack, progress),
                    WithAlpha(color, config.HeadAlpha * config.EvaluateAlpha(progress)));
            }

            for (int row = 0; row < FadeRows; row++)
            {
                int a = baseVertex + row * 2;
                triangles.Add(a);
                triangles.Add(a + 2);
                triangles.Add(a + 1);
                triangles.Add(a + 1);
                triangles.Add(a + 2);
                triangles.Add(a + 3);
            }
        }

        private void AppendStreakRow(
            bool horizontal,
            float backSign,
            float perpMin,
            float perpMax,
            float back,
            Color32 color)
        {
            float along = back * backSign;
            vertices.Add(horizontal ? new Vector3(along, perpMin) : new Vector3(perpMin, along));
            vertices.Add(horizontal ? new Vector3(along, perpMax) : new Vector3(perpMax, along));
            colors.Add(color);
            colors.Add(color);
        }

        private void UploadMesh()
        {
            mesh.Clear();
            if (vertices.Count == 0)
                return;

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateBounds();
        }

        private MotionHistory GetHistory(PuzzlePiece piece)
        {
            if (histories.TryGetValue(piece, out MotionHistory history))
                return history;

            history = historyPool.Count > 0 ? historyPool.Pop() : new MotionHistory();
            history.Prepare(config.MaximumSamples);
            histories.Add(piece, history);
            return history;
        }

        private void ReturnStaleHistories()
        {
            staleHistories.Clear();
            foreach (KeyValuePair<PuzzlePiece, MotionHistory> pair in histories)
            {
                if (pair.Value.Stamp != frameStamp)
                    staleHistories.Add(pair.Key);
            }

            for (int index = 0; index < staleHistories.Count; index++)
            {
                PuzzlePiece piece = staleHistories[index];
                historyPool.Push(histories[piece]);
                histories.Remove(piece);
            }
        }

        private void ReturnAllHistories()
        {
            foreach (KeyValuePair<PuzzlePiece, MotionHistory> pair in histories)
                historyPool.Push(pair.Value);
            histories.Clear();
        }

        private static Color32 WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
