using System;
using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Gravity;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GravityPuzzle
{
    public static class GravityLevelRuntime
    {
        private const string PreviewPathKey = "GravityPuzzle.PreviewLevelPath";
        private const string SequenceResourcePath = "LevelSequence";
        private static GravityLevelDefinition[] levels = Array.Empty<GravityLevelDefinition>();
        private static int currentLevelIndex = -1;
        private static bool levelSequenceInitialized;
        private static bool previewLaunchRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLevelSequence()
        {
            levels = Array.Empty<GravityLevelDefinition>();
            currentLevelIndex = -1;
            levelSequenceInitialized = false;
            previewLaunchRequested = false;
        }

        public static GravityLevelDefinition FindLevelToPlay()
        {
            if (levelSequenceInitialized)
                return CurrentLevel;

            levelSequenceInitialized = true;
            levels = FindAllLevels();
            currentLevelIndex = levels.Length > 0 ? 0 : -1;

#if UNITY_EDITOR
            string previewPath = EditorPrefs.GetString(PreviewPathKey, string.Empty);
            EditorPrefs.DeleteKey(PreviewPathKey);
            if (!string.IsNullOrEmpty(previewPath))
            {
                GravityLevelDefinition preview = AssetDatabase.LoadAssetAtPath<GravityLevelDefinition>(previewPath);
                if (preview != null)
                {
                    previewLaunchRequested = true;
                    int previewIndex = Array.IndexOf(levels, preview);
                    if (previewIndex >= 0)
                    {
                        currentLevelIndex = previewIndex;
                    }
                    else
                    {
                        // A level does not need to belong to the campaign sequence
                        // to be launched from the level editor's Play Preview button.
                        levels = new[] { preview };
                        currentLevelIndex = 0;
                    }
                }
            }
#endif

            return CurrentLevel;
        }

        public static bool HasNextLevel => currentLevelIndex >= 0 && currentLevelIndex + 1 < levels.Length;

        public static int CurrentLevelNumber => Mathf.Max(1, currentLevelIndex + 1);

        /// <summary>
        /// Requests that the next scene load immediately starts the active level without showing the main menu.
        /// </summary>
        public static void RequestRestart()
        {
            previewLaunchRequested = true;
        }

        internal static bool ConsumePreviewLaunchRequest()
        {
            bool requested = previewLaunchRequested;
            previewLaunchRequested = false;
            return requested;
        }

        public static bool TryAdvanceToNextLevel()
        {
            if (!HasNextLevel)
                return false;

            currentLevelIndex++;
            return true;
        }

        private static GravityLevelDefinition CurrentLevel =>
            currentLevelIndex >= 0 && currentLevelIndex < levels.Length
                ? levels[currentLevelIndex]
                : null;

        private static GravityLevelDefinition[] FindAllLevels()
        {
            GravityLevelSequence sequence = Resources.Load<GravityLevelSequence>(SequenceResourcePath);
            if (sequence != null)
            {
                List<GravityLevelDefinition> arrangedLevels = new List<GravityLevelDefinition>();
                foreach (GravityLevelDefinition level in sequence.levels)
                {
                    if (level != null)
                        arrangedLevels.Add(level);
                }

                if (arrangedLevels.Count > 0)
                    return arrangedLevels.ToArray();
            }

#if UNITY_EDITOR
            string[] levelGuids = AssetDatabase.FindAssets("t:GravityLevelDefinition");
            List<GravityLevelDefinition> foundLevels = new List<GravityLevelDefinition>();
            foreach (string guid in levelGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GravityLevelDefinition foundLevel = AssetDatabase.LoadAssetAtPath<GravityLevelDefinition>(path);
                if (foundLevel != null)
                    foundLevels.Add(foundLevel);
            }

            levels = foundLevels.ToArray();
#else
            levels = Resources.LoadAll<GravityLevelDefinition>("Levels");
#endif

            Array.Sort(levels, CompareLevels);
            return levels;
        }

        private static int CompareLevels(GravityLevelDefinition first, GravityLevelDefinition second)
        {
            return string.Compare(first.name, second.name, StringComparison.OrdinalIgnoreCase);
        }

        public static void Build(GravityLevelDefinition level)
        {
            float halfHeight = level.boardRows * .5f;
            float cameraSize = 10f;
            PrototypeBootstrap.ConfigureCamera(cameraSize, level.backgroundColor);

            GameObject board = new GameObject($"Level - {level.levelName}");
            PrototypeBoard boardState = board.AddComponent<PrototypeBoard>();
            boardState.SetRemovalHeight(-halfHeight - 15f);
            boardState.SetTimeLimit(level.timeLimit);
            boardState.EnableSequentialLevels();
            boardState.InitializeBoardSnapshot(LevelBoardSnapshotBuilder.Build(level));
            board.AddComponent<PuzzleDragController>();

            float frameThickness = GravityGridMetrics.FrameThicknessInCells;
            float exitWidth = Mathf.Clamp(level.exitWidth, .75f, level.boardColumns - frameThickness * 2f);
            CreateBoardBackground(level);
            CreateBoardFrame(level, exitWidth);

            CreateShredders(level, halfHeight, exitWidth);

            foreach (ObstacleDefinition obstacle in level.obstacles)
                CreateObstacle(level, obstacle);

            foreach (PinDefinition pin in level.pins)
                CreatePin(level, pin);

            for (int pieceIndex = 0; pieceIndex < level.pieces.Count; pieceIndex++)
                RuntimePieceFactory.Create(level, level.pieces[pieceIndex], pieceIndex);

            ValidateLevelSnapshotRuntimeState(level, boardState);

            // The manager creates its UI fallback when this scene does not provide one.
            LevelProgressManager.EnsureInstance().InitializeLevelProgress(level);
            SettingsPanelButton.EnsureConnected();
        }

        private static float ResolveCameraSize(GravityLevelDefinition level)
        {
            if (!level.useAutomaticCameraFit)
                return level.fixedCameraSize;

            float safeWidthFraction = level.useRuntimeSafeAreaForCameraFit
                ? SafeAreaWidthFraction()
                : 1f;
            float safeHeightFraction = level.useRuntimeSafeAreaForCameraFit
                ? SafeAreaHeightFraction()
                : 1f;

            return GravityGridMetrics.CameraSize(
                level.boardColumns,
                level.boardRows,
                CameraAspect(),
                safeWidthFraction,
                safeHeightFraction,
                level.cameraViewportWidth,
                level.cameraViewportHeight);
        }

        private static void ValidateLevelSnapshotRuntimeState(
            GravityLevelDefinition level,
            PrototypeBoard boardState)
        {
            LevelBoardSnapshotRuntimeValidator.Validate(
                level,
                boardState.BoardSnapshot,
                PuzzlePiece.ActivePieces,
                boardState);
        }

        private static void CreateBoardBackground(GravityLevelDefinition level)
        {
            GameObject background = new GameObject("Board Background");
            Color alternate = Color.Lerp(level.backgroundColor, Color.white, .08f);
            float fineCellSize = 1f / level.subdivisions;

            for (int boardY = 0; boardY < level.boardRows; boardY++)
            {
                for (int boardX = 0; boardX < level.boardColumns; boardX++)
                {
                    Vector2Int boardCell = new Vector2Int(boardX, boardY);
                    Color color = (boardX + boardY) % 2 == 0
                        ? level.backgroundColor
                        : alternate;

                    if (IsBoardCellFullyActive(level, boardCell))
                    {
                        GameObject square = PrototypeBootstrap.CreateVisualBlock(
                            $"Background Grid Cell {boardX}, {boardY}",
                            GridCellWorldPosition(level, boardCell),
                            Vector2.one,
                            color);
                        square.transform.SetParent(background.transform, true);
                        square.GetComponent<SpriteRenderer>().sortingOrder = -10;
                        continue;
                    }

                    Vector2Int fineOrigin = boardCell * level.subdivisions;
                    for (int fineY = 0; fineY < level.subdivisions; fineY++)
                    for (int fineX = 0; fineX < level.subdivisions; fineX++)
                    {
                        Vector2Int fineCell = fineOrigin + new Vector2Int(fineX, fineY);
                        if (!IsFineCellActive(level, fineCell))
                            continue;

                        GameObject fragment = PrototypeBootstrap.CreateVisualBlock(
                            $"Legacy Background Fragment {fineCell.x}, {fineCell.y}",
                            CellWorldPosition(level, fineCell),
                            Vector2.one * fineCellSize,
                            color);
                        fragment.transform.SetParent(background.transform, true);
                        fragment.GetComponent<SpriteRenderer>().sortingOrder = -10;
                    }
                }
            }
        }

        private static void CreateBoardFrame(GravityLevelDefinition level, float exitWidth)
        {
            float fineCellSize = 1f / level.subdivisions;
            float thickness = GravityGridMetrics.FrameThicknessInCells;
            int edgeIndex = 0;
            GameObject frameRoot = new GameObject("Composite Board Frame");

            for (int y = 0; y < level.FineRows; y++)
            {
                for (int x = 0; x < level.FineColumns; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!IsFineCellActive(level, cell))
                        continue;

                    Vector2 centre = CellWorldPosition(level, cell);
                    float edgeOffset = fineCellSize * .5f + thickness * .5f;

                    if (!IsFineCellActive(level, cell + Vector2Int.left))
                        CreateFrameEdge(frameRoot.transform, $"Frame Edge {++edgeIndex}", centre + Vector2.left * edgeOffset, new Vector2(thickness, fineCellSize + thickness), level.frameColor);
                    if (!IsFineCellActive(level, cell + Vector2Int.right))
                        CreateFrameEdge(frameRoot.transform, $"Frame Edge {++edgeIndex}", centre + Vector2.right * edgeOffset, new Vector2(thickness, fineCellSize + thickness), level.frameColor);
                    if (!IsFineCellActive(level, cell + Vector2Int.up))
                        CreateFrameEdge(frameRoot.transform, $"Frame Edge {++edgeIndex}", centre + Vector2.up * edgeOffset, new Vector2(fineCellSize + thickness, thickness), level.frameColor);

                    if (!IsFineCellActive(level, cell + Vector2Int.down))
                    {
                        if (y == 0)
                            CreateBottomEdgeWithExit(frameRoot.transform, level, centre, fineCellSize, exitWidth, thickness, ref edgeIndex);
                        else
                            CreateFrameEdge(frameRoot.transform, $"Frame Edge {++edgeIndex}", centre + Vector2.down * edgeOffset, new Vector2(fineCellSize + thickness, thickness), level.frameColor);
                    }
                }
            }

            CreateMapCollisionBlocks(level, exitWidth, fineCellSize);
        }

        private static void CreateMapCollisionBlocks(
            GravityLevelDefinition level,
            float exitWidth,
            float fineCellSize)
        {
            GameObject collisionRoot = new GameObject("Map Collision - Obstacle Boxes");
            Rigidbody2D collisionBody = collisionRoot.AddComponent<Rigidbody2D>();
            collisionBody.bodyType = RigidbodyType2D.Static;
            CompositeCollider2D composite = collisionRoot.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Synchronous;
            composite.edgeRadius = 0f;

            HashSet<Vector2Int> blockingCells = new HashSet<Vector2Int>();
            Vector2Int[] neighbours =
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.up,
                Vector2Int.down
            };

            for (int y = 0; y < level.FineRows; y++)
            {
                for (int x = 0; x < level.FineColumns; x++)
                {
                    Vector2Int activeCell = new Vector2Int(x, y);
                    if (!IsFineCellActive(level, activeCell))
                        continue;

                    foreach (Vector2Int direction in neighbours)
                    {
                        Vector2Int blockingCell = activeCell + direction;
                        if (IsFineCellActive(level, blockingCell))
                            continue;

                        if (IsBottomExitCell(level, blockingCell, exitWidth, fineCellSize))
                            continue;

                        blockingCells.Add(blockingCell);
                    }
                }
            }

            foreach (Vector2Int cell in blockingCells)
            {
                GameObject block = new GameObject($"Map Collision Cell {cell.x}, {cell.y}");
                block.transform.SetParent(collisionRoot.transform, false);
                block.transform.position = CellWorldPosition(level, cell);
                block.transform.localScale = Vector3.one * fineCellSize;

                BoxCollider2D collider = block.AddComponent<BoxCollider2D>();
                collider.edgeRadius = 0f;
                collider.usedByComposite = true;
            }

            // One merged outline has no fine-cell seams for gravity to push
            // against. A visually flat grid shelf is now physically flat too.
            composite.GenerateGeometry();
        }

        private static bool IsBottomExitCell(
            GravityLevelDefinition level,
            Vector2Int cell,
            float exitWidth,
            float fineCellSize)
        {
            if (cell.y >= 0)
                return false;

            float centreX = CellWorldPosition(level, cell).x;
            float cellLeft = centreX - fineCellSize * .5f;
            float cellRight = centreX + fineCellSize * .5f;
            float exitLeft = -exitWidth * .5f;
            float exitRight = exitWidth * .5f;
            return cellRight > exitLeft && cellLeft < exitRight;
        }

        private static void CreateBottomEdgeWithExit(
            Transform frameRoot,
            GravityLevelDefinition level,
            Vector2 cellCentre,
            float fineCellSize,
            float exitWidth,
            float thickness,
            ref int edgeIndex)
        {
            float cellLeft = cellCentre.x - fineCellSize * .5f;
            float cellRight = cellCentre.x + fineCellSize * .5f;
            float exitLeft = -exitWidth * .5f;
            float exitRight = exitWidth * .5f;
            float y = cellCentre.y - fineCellSize * .5f - thickness * .5f;

            float leftLength = Mathf.Max(0f, Mathf.Min(cellRight, exitLeft) - cellLeft);
            if (leftLength > .001f)
            {
                float centreX = cellLeft + leftLength * .5f;
                CreateFrameEdge(frameRoot, $"Frame Edge {++edgeIndex}", new Vector2(centreX, y), new Vector2(leftLength, thickness), level.frameColor);
            }

            float rightStart = Mathf.Max(cellLeft, exitRight);
            float rightLength = Mathf.Max(0f, cellRight - rightStart);
            if (rightLength > .001f)
            {
                float centreX = rightStart + rightLength * .5f;
                CreateFrameEdge(frameRoot, $"Frame Edge {++edgeIndex}", new Vector2(centreX, y), new Vector2(rightLength, thickness), level.frameColor);
            }
        }

        private static void CreateFrameEdge(Transform frameRoot, string name, Vector2 position, Vector2 size, Color color)
        {
            GameObject edge = PrototypeBootstrap.CreateVisualBlock(name, position, size, color);
            edge.transform.SetParent(frameRoot, true);
        }

        private static bool IsFineCellActive(GravityLevelDefinition level, Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= level.FineColumns || cell.y >= level.FineRows)
                return false;

            if (level.inactiveFineCells != null && level.inactiveFineCells.Contains(cell))
                return false;

            Vector2Int coarseCell = new Vector2Int(cell.x / level.subdivisions, cell.y / level.subdivisions);
            return level.inactiveBoardCells == null || !level.inactiveBoardCells.Contains(coarseCell);
        }

        private static bool IsBoardCellFullyActive(GravityLevelDefinition level, Vector2Int boardCell)
        {
            Vector2Int fineOrigin = boardCell * level.subdivisions;
            for (int y = 0; y < level.subdivisions; y++)
            for (int x = 0; x < level.subdivisions; x++)
            {
                if (!IsFineCellActive(level, fineOrigin + new Vector2Int(x, y)))
                    return false;
            }

            return true;
        }

        private static void CreateShredders(GravityLevelDefinition level, float halfHeight, float exitWidth)
        {
            if (level.shredders != null && level.shredders.Count > 0)
            {
                float largestRadius = 0f;
                float leftEdge = float.PositiveInfinity;
                float rightEdge = float.NegativeInfinity;
                float highestShredderTop = float.NegativeInfinity;
                foreach (ShredderDefinition definition in level.shredders)
                {
                    float authoredRadius = definition.radiusInFineCells / level.subdivisions;
                    largestRadius = Mathf.Max(largestRadius, authoredRadius);
                    Vector2 position = CellWorldPosition(level, definition.cell);
                    leftEdge = Mathf.Min(leftEdge, position.x - authoredRadius);
                    rightEdge = Mathf.Max(rightEdge, position.x + authoredRadius);
                    highestShredderTop = Mathf.Max(highestShredderTop, position.y + authoredRadius);
                    float authoredSpeed = definition.clockwise
                        ? -definition.rotationSpeed
                        : definition.rotationSpeed;
                    CreateShredder(
                        definition.name,
                        position,
                        authoredRadius,
                        authoredSpeed);
                }

                CreateShredderCatchZone(
                    leftEdge,
                    rightEdge,
                    highestShredderTop,
                    Mathf.Max(.2f, largestRadius));
                return;
            }

            int count = Mathf.Clamp(Mathf.RoundToInt(exitWidth), 1, 6);
            float radius = Mathf.Clamp(level.shredderRadius, .2f, exitWidth / (count + .5f));
            float spacing = count == 1 ? 0f : exitWidth / count;
            float startX = -(count - 1) * spacing * .5f;
            
            // Lower the shredders so their top edge (y + radius) is exactly at the bottom of the board (-halfHeight).
            // This prevents blocks resting on the shredder from being pushed vertically out of the grid alignment.
            float y = -halfHeight - radius;

            for (int i = 0; i < count; i++)
            {
                float direction = i % 2 == 0 ? -1f : 1f;
                CreateShredder(
                    $"Shredder {i + 1}",
                    new Vector2(startX + i * spacing, y),
                    radius,
                    level.shredderRotationSpeed * direction);
            }

            CreateShredderCatchZone(
                startX - radius,
                startX + (count - 1) * spacing + radius,
                y + radius,
                radius);
        }

        private static void CreateShredder(string name, Vector2 position, float radius, float speed)
        {
            GameObject shredder = new GameObject(name);
            shredder.transform.position = position;
            ShredderWheel wheel = shredder.AddComponent<ShredderWheel>();
            wheel.Build(radius, speed);
        }

        private static void CreateShredderCatchZone(
            float leftEdge,
            float rightEdge,
            float topY,
            float radius)
        {
            GameObject catchZone = new GameObject("Shredder Catch Zone");
            float thickness = 10f;
            float width = Mathf.Max(radius * 2f, rightEdge - leftEdge + radius * .5f);
            catchZone.transform.position = new Vector2(
                (leftEdge + rightEdge) * .5f,
                topY - thickness * .5f);
            BoxCollider2D catchTrigger = catchZone.AddComponent<BoxCollider2D>();
            catchTrigger.size = new Vector2(width, thickness);
            catchTrigger.isTrigger = true;
            ShredderCatchZone zone = catchZone.AddComponent<ShredderCatchZone>();
            zone.shredY = topY;
        }

        private static void CreateObstacle(GravityLevelDefinition level, ObstacleDefinition obstacle)
        {
            if (obstacle.usesGridCells)
            {
                Vector2Int authoredSize = new Vector2Int(
                    Mathf.Max(1, obstacle.sizeInGridCells.x),
                    Mathf.Max(1, obstacle.sizeInGridCells.y));
                Vector2Int rotatedSize = obstacle.quarterTurns % 2 == 0
                    ? authoredSize
                    : new Vector2Int(authoredSize.y, authoredSize.x);
                Vector2 centre = new Vector2(
                    -level.boardColumns * .5f + obstacle.gridCell.x + rotatedSize.x * .5f,
                    -level.boardRows * .5f + obstacle.gridCell.y + rotatedSize.y * .5f);
                PrototypeBootstrap.CreateStaticBlock(
                    obstacle.name,
                    centre,
                    rotatedSize,
                    obstacle.color,
                    false);
                return;
            }

            Vector2Int fineSize = obstacle.quarterTurns % 2 == 0
                ? obstacle.sizeInFineCells
                : new Vector2Int(obstacle.sizeInFineCells.y, obstacle.sizeInFineCells.x);
            Vector2 worldSize = (Vector2)fineSize / level.subdivisions;
            PrototypeBootstrap.CreateStaticBlock(
                obstacle.name,
                CellWorldPosition(level, obstacle.centreCell),
                worldSize,
                obstacle.color,
                false);
        }

        private static void CreatePin(GravityLevelDefinition level, PinDefinition pin)
        {
            PrototypeBootstrap.CreateStaticCircle(
                pin.name,
                CellWorldPosition(level, pin.cell),
                pin.radiusInFineCells / level.subdivisions,
                pin.color);
        }

        private static Vector2 CellWorldPosition(GravityLevelDefinition level, Vector2Int cell)
        {
            float fineCellSize = 1f / level.subdivisions;
            return new Vector2(
                -level.boardColumns * .5f + (cell.x + .5f) * fineCellSize,
                -level.boardRows * .5f + (cell.y + .5f) * fineCellSize);
        }

        private static Vector2 GridCellWorldPosition(GravityLevelDefinition level, Vector2Int cell)
        {
            return new Vector2(
                -level.boardColumns * .5f + cell.x + .5f,
                -level.boardRows * .5f + cell.y + .5f);
        }

        private static float CameraAspect()
        {
            return Screen.height > 0 ? (float)Screen.width / Screen.height : 9f / 16f;
        }

        private static float SafeAreaWidthFraction()
        {
            return Screen.width > 0 ? Screen.safeArea.width / Screen.width : 1f;
        }

        private static float SafeAreaHeightFraction()
        {
            return Screen.height > 0 ? Screen.safeArea.height / Screen.height : 1f;
        }
    }

    public sealed class ShredderWheel : MonoBehaviour
    {
        private const int ToothCount = 12;
        private float rotationSpeed;

        public void Build(float radius, float speed)
        {
            rotationSpeed = speed;

            CircleCollider2D trigger = gameObject.AddComponent<CircleCollider2D>();
            trigger.radius = radius * .92f;
            trigger.isTrigger = true;

            GameObject disc = new GameObject("Shredder Disc");
            disc.transform.SetParent(transform, false);
            disc.transform.localScale = Vector3.one * (radius * 1.65f);
            SpriteRenderer discRenderer = disc.AddComponent<SpriteRenderer>();
            discRenderer.sprite = PrototypeBootstrap.GetCircleSprite();
            discRenderer.color = new Color(.32f, .36f, .48f);
            discRenderer.sortingOrder = 25; // In front of feeding blocks

            GameObject hub = new GameObject("Shredder Hub");
            hub.transform.SetParent(transform, false);
            hub.transform.localScale = Vector3.one * (radius * .48f);
            SpriteRenderer hubRenderer = hub.AddComponent<SpriteRenderer>();
            hubRenderer.sprite = PrototypeBootstrap.GetCircleSprite();
            hubRenderer.color = new Color(.1f, .12f, .18f);
            hubRenderer.sortingOrder = 27; // In front of feeding blocks

            for (int i = 0; i < ToothCount; i++)
            {
                float angle = i * 360f / ToothCount;
                GameObject tooth = PrototypeBootstrap.CreateVisualBlock(
                    $"Tooth {i + 1}",
                    Vector2.zero,
                    new Vector2(radius * .42f, radius * .24f),
                    new Color(.75f, .8f, .92f));
                tooth.transform.SetParent(transform, false);
                tooth.transform.localPosition = Quaternion.Euler(0f, 0f, angle) * Vector3.up * (radius * .86f);
                tooth.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                tooth.GetComponent<SpriteRenderer>().sortingOrder = 26; // In front of feeding blocks
            }
        }

        private void Update()
        {
            transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryShred(other, transform.position);
        }

        internal static void TryShred(Collider2D other, Vector2 shredderCentre)
        {
            BlockShredder shredder = BlockShredder.Instance ?? UnityEngine.Object.FindObjectOfType<BlockShredder>();
            if (shredder != null)
            {
                shredder.TryShredBlock(other, shredderCentre);
                return;
            }

            PuzzlePiece piece = other.GetComponentInParent<PuzzlePiece>();
            if (piece == null || !piece.TryBeginShredding())
                return;

            Vector2 contactPoint = other.ClosestPoint(shredderCentre);
            CreateFragments(piece, contactPoint);
            piece.ReleaseInstance();
        }

        private static void CreateFragments(PuzzlePiece piece, Vector2 contactPoint)
        {
            SpriteRenderer[] renderers = piece.GetComponentsInChildren<SpriteRenderer>();
            Color pieceColor = Color.white;
            int originalPartCount = 0;
            foreach (SpriteRenderer renderer in renderers)
            {
                if (renderer.gameObject.name.StartsWith("Selected Fill") ||
                    renderer.gameObject.name.StartsWith("White Selection Outline") ||
                    renderer.gameObject.name.StartsWith("Ice ") ||
                    renderer.gameObject.name.StartsWith("Block Border"))
                    continue;

                pieceColor = renderer.color;
                originalPartCount++;
            }

            ShredderParticleEffects.SpawnBurst(contactPoint, pieceColor, 8, 4, 3);
            int fragmentCount = Mathf.Clamp(originalPartCount * 5, 18, 40);
            for (int i = 0; i < fragmentCount; i++)
            {
                float size = UnityEngine.Random.Range(.055f, .13f);
                Color fragmentColor = Color.Lerp(pieceColor, Color.white, UnityEngine.Random.Range(0f, .2f));
                GameObject fragment = PrototypeBootstrap.CreateVisualBlock(
                    "Shredded Fragment",
                    contactPoint + UnityEngine.Random.insideUnitCircle * .12f,
                    Vector2.one * size,
                    fragmentColor);
                fragment.GetComponent<SpriteRenderer>().sortingOrder = 20;

                Rigidbody2D body = fragment.AddComponent<Rigidbody2D>();
                body.gravityScale = .9f;
                body.velocity = new Vector2(
                    UnityEngine.Random.Range(-2.0f, 2.0f),
                    UnityEngine.Random.Range(-3.8f, -1.2f));
                body.angularVelocity = UnityEngine.Random.Range(-720f, 720f);

                ShredderFragment fragmentLife = fragment.AddComponent<ShredderFragment>();
                fragmentLife.SetLifetime(UnityEngine.Random.Range(.65f, 1.15f));
            }
        }
    }

    public sealed class ShredderCatchZone : MonoBehaviour
    {
        public static readonly List<ShredderCatchZone> ActiveZones = new List<ShredderCatchZone>();

        public float shredY;

        private void OnEnable()
        {
            if (!ActiveZones.Contains(this))
                ActiveZones.Add(this);
        }

        private void OnDisable()
        {
            ActiveZones.Remove(this);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            ShredderWheel.TryShred(other, new Vector2(other.transform.position.x, shredY));
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // Safety net for a body which enters the large catch trigger in the same
            // physics step as a fast rotation or contact change.
            ShredderWheel.TryShred(other, new Vector2(other.transform.position.x, shredY));
        }
    }

    public sealed class ShredderFragment : MonoBehaviour
    {
        private float lifetime;
        private float remaining;
        private SpriteRenderer fragmentRenderer;

        public void SetLifetime(float seconds)
        {
            lifetime = seconds;
            remaining = seconds;
            fragmentRenderer = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            remaining -= Time.deltaTime;
            if (remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            if (fragmentRenderer != null && remaining < lifetime * .35f)
            {
                Color color = fragmentRenderer.color;
                color.a = remaining / (lifetime * .35f);
                fragmentRenderer.color = color;
            }
        }
    }
}
