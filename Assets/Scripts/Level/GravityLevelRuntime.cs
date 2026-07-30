using System;
using System.Collections.Generic;
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
            float cameraSize = GravityGridMetrics.CameraSize(
                level.boardColumns,
                level.boardRows,
                CameraAspect(),
                SafeAreaWidthFraction(),
                SafeAreaHeightFraction());
            PrototypeBootstrap.ConfigureCamera(cameraSize, level.backgroundColor);

            GameObject board = new GameObject($"Level - {level.levelName}");
            PrototypeBoard boardState = board.AddComponent<PrototypeBoard>();
            boardState.SetRemovalHeight(-halfHeight - 15f);
            boardState.SetTimeLimit(level.timeLimit);
            boardState.EnableSequentialLevels();
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

            foreach (PieceDefinition piece in level.pieces)
                CreatePiece(level, piece);
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
                foreach (ShredderDefinition definition in level.shredders)
                {
                    float authoredRadius = definition.radiusInFineCells / level.subdivisions;
                    largestRadius = Mathf.Max(largestRadius, authoredRadius);
                    float authoredSpeed = definition.clockwise
                        ? -definition.rotationSpeed
                        : definition.rotationSpeed;
                    CreateShredder(
                        definition.name,
                        CellWorldPosition(level, definition.cell),
                        authoredRadius,
                        authoredSpeed);
                }

                CreateShredderCatchZone(
                    halfHeight,
                    exitWidth,
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

            CreateShredderCatchZone(halfHeight, exitWidth, radius);
        }

        private static void CreateShredder(string name, Vector2 position, float radius, float speed)
        {
            GameObject shredder = new GameObject(name);
            shredder.transform.position = position;
            ShredderWheel wheel = shredder.AddComponent<ShredderWheel>();
            wheel.Build(radius, speed);
        }

        private static void CreateShredderCatchZone(float halfHeight, float exitWidth, float radius)
        {
            GameObject catchZone = new GameObject("Shredder Catch Zone");
            float thickness = 10f;
            float topY = -halfHeight - radius * .5f;
            catchZone.transform.position = new Vector2(0f, topY - thickness * .5f);
            BoxCollider2D catchTrigger = catchZone.AddComponent<BoxCollider2D>();
            catchTrigger.size = new Vector2(exitWidth + 2f, thickness);
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

        private static void CreatePiece(GravityLevelDefinition level, PieceDefinition definition)
        {
            GameObject piece = new GameObject(definition.name);
            piece.transform.position = CellWorldPosition(level, definition.origin);

            Rigidbody2D body = piece.AddComponent<Rigidbody2D>();
            body.gravityScale = level.gravityScale;
            body.mass = 1f;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.StartAwake;

            CompositeCollider2D pieceComposite = piece.AddComponent<CompositeCollider2D>();
            pieceComposite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            pieceComposite.generationType = CompositeCollider2D.GenerationType.Synchronous;
            pieceComposite.edgeRadius = 0f;

            float fineCellSize = 1f / level.subdivisions;

            List<PiecePartGeometry> parts = new List<PiecePartGeometry>();
            Dictionary<Vector2Int, int> blockCounts = new Dictionary<Vector2Int, int>();
            foreach (PieceCellDefinition cell in definition.cells)
            {
                Vector2Int rotated = QuarterTurnUtility.Rotate(cell.localCell, definition.quarterTurns);
                if (cell.type != PieceCellType.Block)
                    continue;

                Vector2Int absolute = definition.origin + rotated;
                Vector2Int gridCell = new Vector2Int(
                    Mathf.FloorToInt((float)absolute.x / level.subdivisions),
                    Mathf.FloorToInt((float)absolute.y / level.subdivisions));
                blockCounts.TryGetValue(gridCell, out int count);
                blockCounts[gridCell] = count + 1;
            }

            HashSet<Vector2Int> completeModules = new HashSet<Vector2Int>();
            int cellsPerModule = level.subdivisions * level.subdivisions;
            foreach (KeyValuePair<Vector2Int, int> blockCount in blockCounts)
            {
                if (blockCount.Value != cellsPerModule)
                    continue;

                completeModules.Add(blockCount.Key);
                Vector2 localPosition =
                    GridCellWorldPosition(level, blockCount.Key) -
                    CellWorldPosition(level, definition.origin);
                parts.Add(new PiecePartGeometry("Grid Block", localPosition, Vector2.one));
            }

            foreach (PieceCellDefinition cell in definition.cells)
            {
                Vector2Int rotated = QuarterTurnUtility.Rotate(cell.localCell, definition.quarterTurns);
                Vector2Int absolute = definition.origin + rotated;
                Vector2Int gridCell = new Vector2Int(
                    Mathf.FloorToInt((float)absolute.x / level.subdivisions),
                    Mathf.FloorToInt((float)absolute.y / level.subdivisions));
                if (cell.type == PieceCellType.Block && completeModules.Contains(gridCell))
                    continue;

                Vector2 localPosition = (Vector2)rotated * fineCellSize;
                string partName = cell.type == PieceCellType.Hook ? "Hook Cell" : "Block Cell";
                parts.Add(new PiecePartGeometry(
                    partName,
                    localPosition,
                    Vector2.one * fineCellSize));
            }

            GetPiecePartBounds(parts, out Vector2 minimum, out Vector2 maximum);
            Vector2 collisionCentre = (minimum + maximum) * .5f;

            GameObject collisionRootObject = new GameObject("Collision Geometry");
            collisionRootObject.transform.SetParent(piece.transform, false);
            collisionRootObject.transform.localPosition = collisionCentre;

            List<BoxCollider2D> collisionCells = new List<BoxCollider2D>(parts.Count);
            List<SpriteRenderer> collisionCellVisuals = new List<SpriteRenderer>(parts.Count);
            foreach (PiecePartGeometry part in parts)
            {
                collisionCells.Add(CreatePiecePart(
                    piece.transform,
                    collisionRootObject.transform,
                    collisionCentre,
                    part,
                    definition.color,
                    out SpriteRenderer cellVisual));
                collisionCellVisuals.Add(cellVisual);
            }

            pieceComposite.GenerateGeometry();

            // Draw a unified outline around the outer perimeter of the shape
            LineRenderer outline = piece.AddComponent<LineRenderer>();
            outline.useWorldSpace = false;
            outline.loop = true;
            outline.startWidth = 0.08f;
            outline.endWidth = 0.08f;
            outline.numCornerVertices = 4;
            outline.numCapVertices = 4;
            outline.sortingOrder = -1; // Draw just behind the colored blocks
            
            // Standard sprite material so it matches the 2D lighting/rendering
            outline.material = new Material(Shader.Find("Sprites/Default"));
            Color outlineColor = new Color(0.12f, 0.12f, 0.15f, 1f);
            outline.startColor = outlineColor;
            outline.endColor = outlineColor;

            if (pieceComposite.pathCount > 0)
            {
                int pointCount = pieceComposite.GetPathPointCount(0);
                outline.positionCount = pointCount;
                Vector2[] path = new Vector2[pointCount];
                pieceComposite.GetPath(0, path);
                for (int i = 0; i < pointCount; i++)
                {
                    outline.SetPosition(i, new Vector3(path[i].x, path[i].y, 0f));
                }
            }

            PuzzlePiece puzzlePiece = piece.AddComponent<PuzzlePiece>();
            puzzlePiece.ConfigureCollisionGeometry(
                pieceComposite,
                collisionCells,
                collisionCellVisuals);
            puzzlePiece.ConfigureFreeze(
                definition.frozenMoveCount,
                definition.iceCounterFontSize,
                definition.iceCounterTextColor,
                definition.iceCounterOutlineColor,
                definition.iceCounterOutlineWidth,
                definition.iceCounterOffset);
        }

        private static BoxCollider2D CreatePiecePart(
            Transform visualParent,
            Transform collisionRoot,
            Vector2 collisionCentre,
            PiecePartGeometry part,
            Color color,
            out SpriteRenderer cellVisual)
        {
            GameObject visual = new GameObject(part.name);
            visual.transform.SetParent(visualParent, false);
            visual.transform.localPosition = part.localPosition;
            
            // Add a disabled SpriteRenderer to satisfy PuzzlePiece's internal logic
            cellVisual = visual.AddComponent<SpriteRenderer>();
            cellVisual.sprite = PrototypeBootstrap.GetSquareSprite();
            cellVisual.color = color;
            cellVisual.enabled = false;
            
            // Build the actual visible 3x3 voxel grid
            VoxelBlockBuilder.BuildVoxelGrid(visual.transform, part.name, part.size, color);

            GameObject colliderObject = new GameObject($"{part.name} Collider");
            colliderObject.transform.SetParent(collisionRoot, false);
            colliderObject.transform.localPosition = part.localPosition - collisionCentre;
            BoxCollider2D partCollider = colliderObject.AddComponent<BoxCollider2D>();
            partCollider.size = part.size;
            // PuzzlePiece applies clearance to this individual modular cell.
            // Its centre never moves relative to the corresponding artwork.
            partCollider.edgeRadius = 0f;
            partCollider.usedByComposite = true;
            return partCollider;
        }

        private static void GetPiecePartBounds(
            List<PiecePartGeometry> parts,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (PiecePartGeometry part in parts)
            {
                Vector2 halfSize = part.size * .5f;
                minimum = Vector2.Min(minimum, part.localPosition - halfSize);
                maximum = Vector2.Max(maximum, part.localPosition + halfSize);
            }

            if (parts.Count == 0)
            {
                minimum = Vector2.zero;
                maximum = Vector2.one * .01f;
            }
        }

        private readonly struct PiecePartGeometry
        {
            public readonly string name;
            public readonly Vector2 localPosition;
            public readonly Vector2 size;

            public PiecePartGeometry(string name, Vector2 localPosition, Vector2 size)
            {
                this.name = name;
                this.localPosition = localPosition;
                this.size = size;
            }
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
            discRenderer.sortingOrder = 8;

            GameObject hub = new GameObject("Shredder Hub");
            hub.transform.SetParent(transform, false);
            hub.transform.localScale = Vector3.one * (radius * .48f);
            SpriteRenderer hubRenderer = hub.AddComponent<SpriteRenderer>();
            hubRenderer.sprite = PrototypeBootstrap.GetCircleSprite();
            hubRenderer.color = new Color(.1f, .12f, .18f);
            hubRenderer.sortingOrder = 10;

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
                tooth.GetComponent<SpriteRenderer>().sortingOrder = 9;
            }

            if (gameObject.GetComponent<BlockShredder>() == null)
            {
                gameObject.AddComponent<BlockShredder>();
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
            BlockShredder shredder = UnityEngine.Object.FindObjectOfType<BlockShredder>();
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
            Destroy(piece.gameObject);
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
                    UnityEngine.Random.Range(-2.8f, 2.8f),
                    UnityEngine.Random.Range(.8f, 3.8f));
                body.angularVelocity = UnityEngine.Random.Range(-720f, 720f);

                ShredderFragment fragmentLife = fragment.AddComponent<ShredderFragment>();
                fragmentLife.SetLifetime(UnityEngine.Random.Range(.65f, 1.15f));
            }
        }
    }

    public sealed class ShredderCatchZone : MonoBehaviour
    {
        public float shredY;

        private void OnTriggerEnter2D(Collider2D other)
        {
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
