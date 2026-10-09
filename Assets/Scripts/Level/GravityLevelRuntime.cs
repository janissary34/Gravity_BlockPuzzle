using System;
using System.Collections.Generic;
using GravityPuzzle.Config;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Gravity;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Gameplay.Reveal;
using GravityPuzzle.Presentation.Views;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GravityPuzzle
{
    public static class GravityLevelRuntime
    {
        private const string PreviewPathKey = "GravityPuzzle.PreviewLevelPath";
        private const int FrameSortingOrder = -8;
        private const int FrameCornerSortingOrder = FrameSortingOrder + 1;
        private static GravityLevelDefinition[] levels = Array.Empty<GravityLevelDefinition>();
        private static GravityLevelSequence configuredSequence;
        private static PrototypeBoard configuredBoard;
        private static PuzzleDragController configuredDragController;
        private static RevealPresentationConfig configuredRevealPresentationConfig;
        private static BoardPresentationConfig configuredBoardPresentationConfig;
        private static RevealPresentationConfig fallbackRevealPresentationConfig;
        private static Sprite obstacleJoinSprite;
        private static Sprite obstacleJoinSpriteSource;
        private static int currentLevelIndex = -1;
        private static BoosterRewardConfig queuedBoosterReward;
        private static bool levelSequenceInitialized;
        private static bool previewLaunchRequested;
        private static bool isEditorLevelPreview;

        /// <summary>
        /// True only for the level-editor's explicit Play Preview command.
        /// Normal Play Mode uses persistent player inventory, even in the editor.
        /// </summary>
        public static bool IsEditorLevelPreview => isEditorLevelPreview;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLevelSequence()
        {
            levels = Array.Empty<GravityLevelDefinition>();
            configuredSequence = null;
            configuredBoard = null;
            configuredDragController = null;
            configuredRevealPresentationConfig = null;
            configuredBoardPresentationConfig = null;
            fallbackRevealPresentationConfig = null;
            obstacleJoinSprite = null;
            obstacleJoinSpriteSource = null;
            currentLevelIndex = -1;
            queuedBoosterReward = null;
            levelSequenceInitialized = false;
            previewLaunchRequested = false;
            isEditorLevelPreview = false;
        }

        /// <summary>
        /// Supplies the authored campaign sequence from the scene composition root.
        /// Runtime level selection deliberately does not load assets by path.
        /// </summary>
        public static void ConfigureLevelSequence(GravityLevelSequence sequence)
        {
            if (sequence == null)
            {
                Debug.LogError("[LevelSequence] RuntimePieceFactoryBootstrap is missing its Level Sequence reference.");
                return;
            }

            if (levelSequenceInitialized && configuredSequence != sequence)
            {
                Debug.LogWarning("[LevelSequence] Sequence was configured after level selection had already started. The active sequence is unchanged.");
                return;
            }

            configuredSequence = sequence;
        }

        /// <summary>
        /// Supplies the scene-authored gameplay adapters. A level build must not
        /// create another board or input controller at runtime: both systems
        /// hold authoritative state and therefore have exactly one owner.
        /// </summary>
        public static void ConfigureSceneGameplayDependencies(
            PrototypeBoard board,
            PuzzleDragController dragController)
        {
            if (board == null || dragController == null)
            {
                Debug.LogError(
                    "[Bootstrap] RuntimePieceFactoryBootstrap needs authored PrototypeBoard and PuzzleDragController references.");
                return;
            }

            configuredBoard = board;
            configuredDragController = dragController;
        }

        /// <summary>Supplies optional presentation tuning for level-authored reveal areas.</summary>
        public static void ConfigureRevealPresentationConfig(RevealPresentationConfig config)
        {
            configuredRevealPresentationConfig = config;
        }

        /// <summary>
        /// Supplies the shared board skin. Gameplay level assets retain only
        /// topology and tuning, so future themes can swap the presentation
        /// without duplicating every level definition.
        /// </summary>
        public static void ConfigureBoardPresentationConfig(BoardPresentationConfig config)
        {
            configuredBoardPresentationConfig = config;
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
                    isEditorLevelPreview = true;
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

        /// <summary>Configured coin reward for the level currently being played.</summary>
        public static int CurrentLevelCoinAmount => CurrentLevel != null
            ? Mathf.Max(0, CurrentLevel.coinAmount)
            : 0;

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

        /// <summary>
        /// Carries an earned booster presentation across the scene reload so
        /// it can appear over the level that was just unlocked.
        /// </summary>
        public static void QueueBoosterReward(BoosterRewardConfig rewardConfig)
        {
            queuedBoosterReward = rewardConfig;
        }

        public static bool TryTakeQueuedBoosterReward(out BoosterRewardConfig rewardConfig)
        {
            rewardConfig = queuedBoosterReward;
            queuedBoosterReward = null;
            return rewardConfig != null;
        }

        private static GravityLevelDefinition CurrentLevel =>
            currentLevelIndex >= 0 && currentLevelIndex < levels.Length
                ? levels[currentLevelIndex]
                : null;

        private static GravityLevelDefinition[] FindAllLevels()
        {
            if (configuredSequence != null)
            {
                List<GravityLevelDefinition> arrangedLevels = new List<GravityLevelDefinition>();
                foreach (GravityLevelDefinition level in configuredSequence.levels)
                {
                    if (level != null)
                        arrangedLevels.Add(level);
                }

                if (arrangedLevels.Count > 0)
                    return arrangedLevels.ToArray();
            }

            Debug.LogError("[LevelSequence] No playable levels were supplied by RuntimePieceFactoryBootstrap.");
            return Array.Empty<GravityLevelDefinition>();
        }

        public static void Build(GravityLevelDefinition level)
        {
            if (level == null)
            {
                Debug.LogError("[LevelRuntime] Cannot build a null level.");
                return;
            }

            if (configuredBoard == null || configuredDragController == null)
            {
                Debug.LogError(
                    "[LevelRuntime] Scene gameplay dependencies were not configured. " +
                    "Assign PrototypeBoard and PuzzleDragController on RuntimePieceFactoryBootstrap.");
                return;
            }

            if (!TryValidateShredderPools(level, BlockShredder.Instance != null
                    ? BlockShredder.Instance.Config
                    : null))
            {
                // Do not initialize the board snapshot until every required
                // runtime object is available. A partial level build leaves no
                // pieces to count and would otherwise be interpreted as a win.
                return;
            }

            float halfHeight = level.boardRows * .5f;
            float cameraSize = ResolveCameraSize(level);
            PrototypeBootstrap.ConfigureCamera(cameraSize, ResolveExteriorColor(level));

            PrototypeBoard boardState = configuredBoard;
            boardState.SetRemovalHeight(-halfHeight - 15f);
            boardState.SetTimeLimit(level.timeLimit);
            boardState.EnableSequentialLevels();
            boardState.InitializeBoardSnapshot(LevelBoardSnapshotBuilder.Build(level));
            boardState.ConfigureRevealAreas(new RevealAreaCoordinator());

            float frameThickness = level.frameThickness;
            float exitWidth = Mathf.Clamp(level.exitWidth, .75f, level.boardColumns - frameThickness * 2f);
            CreateBoardBackground(level);
            CreateBoardFrame(level, exitWidth);
            CreateGeneratedRevealPresentations(level, boardState);

            ShredderConfig shredderConfig = BlockShredder.Instance.Config;
            if (shredderConfig != null)
                boardState.SetFinalShredderGraceSeconds(
                    shredderConfig.FinalPieceTimerGraceSeconds);
            CreateShredders(level, halfHeight, shredderConfig);

            CreateObstacles(level);

            foreach (PinDefinition pin in level.pins)
                CreatePin(level, pin);

            List<PuzzlePiece> runtimePieces = new List<PuzzlePiece>(level.pieces.Count);
            for (int pieceIndex = 0; pieceIndex < level.pieces.Count; pieceIndex++)
            {
                PuzzlePiece piece = RuntimePieceFactory.Create(level, level.pieces[pieceIndex], pieceIndex);
                if (piece != null)
                    runtimePieces.Add(piece);
            }

            // Generated part slots are the runtime visual footprint. Reconcile
            // it once before play starts so gravity reserves exactly what the
            // player sees, rather than relying on an authored approximation.
            Physics2D.SyncTransforms();
            for (int pieceIndex = 0; pieceIndex < runtimePieces.Count; pieceIndex++)
                boardState.TrySynchronizeRuntimePieceGeometry(runtimePieces[pieceIndex]);

            ValidateLevelSnapshotRuntimeState(level, boardState);

            boardState.InitializeRevealAreas(level);

            // Progress UI is authored in the scene; runtime never creates a fallback.
            LevelProgressManager progressManager = LevelProgressManager.EnsureInstance();
            if (progressManager != null)
                progressManager.InitializeLevelProgress(level);

            boardState.ShowQueuedBoosterReward();
        }

        private static float ResolveCameraSize(GravityLevelDefinition level)
        {
            float levelCameraSize;
            if (!level.useAutomaticCameraFit)
                levelCameraSize = level.fixedCameraSize;
            else
            {
                float safeWidthFraction = level.useRuntimeSafeAreaForCameraFit
                    ? SafeAreaWidthFraction()
                    : 1f;
                float safeHeightFraction = level.useRuntimeSafeAreaForCameraFit
                    ? SafeAreaHeightFraction()
                    : 1f;

                levelCameraSize = GravityGridMetrics.CameraSize(
                    level.boardColumns,
                    level.boardRows,
                    CameraAspect(),
                    safeWidthFraction,
                    safeHeightFraction,
                    level.cameraViewportWidth,
                    level.cameraViewportHeight);
            }

            float presentationMargin = configuredBoardPresentationConfig != null
                ? configuredBoardPresentationConfig.CameraSizeMultiplier
                : 1f;
            return levelCameraSize * presentationMargin;
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

        private static void CreateGeneratedRevealPresentations(
            GravityLevelDefinition level,
            PrototypeBoard boardState)
        {
            if (level == null || boardState == null)
                return;

            RevealPresentationConfig config = configuredRevealPresentationConfig;
            if (config == null)
            {
                if (fallbackRevealPresentationConfig == null)
                    fallbackRevealPresentationConfig = ScriptableObject.CreateInstance<RevealPresentationConfig>();
                config = fallbackRevealPresentationConfig;
            }

            CreateGeneratedRevealPresentations(
                level.boxes,
                RevealAreaKind.Box,
                level,
                boardState,
                config);
            CreateGeneratedRevealPresentations(
                level.elevators,
                RevealAreaKind.Elevator,
                level,
                boardState,
                config);
        }

        private static void CreateGeneratedRevealPresentations<TDefinition>(
            IReadOnlyList<TDefinition> definitions,
            RevealAreaKind kind,
            GravityLevelDefinition level,
            PrototypeBoard boardState,
            RevealPresentationConfig config)
            where TDefinition : RevealAreaDefinition
        {
            if (definitions == null)
                return;

            for (int index = 0; index < definitions.Count; index++)
            {
                TDefinition definition = definitions[index];
                if (definition == null || string.IsNullOrWhiteSpace(definition.areaId) ||
                    boardState.HasRevealPresentation(definition.areaId))
                    continue;

                boardState.RegisterRevealPresentation(
                    definition.areaId,
                    RuntimeRevealAreaPresentation.Create(level, definition, kind, config));
            }
        }

        private static void CreateBoardBackground(GravityLevelDefinition level)
        {
            GameObject background = new GameObject("Board Background");
            // The board uses its own background palette. Frame colour is
            // reserved for the exterior border created below.
            // Background alpha is not a meaningful board-design setting: a
            // transparent cell would reveal the camera and make the authored
            // chequered palette look like the fallback blue. Always render
            // the selected RGB value as an opaque board cell.
            Color baseColor = ResolveBoardBackgroundColor(level);
            baseColor.a = 1f;
            Color alternate = configuredBoardPresentationConfig != null &&
                              configuredBoardPresentationConfig.OverrideLevelBackground
                ? configuredBoardPresentationConfig.AlternateBackgroundColor
                : Color.Lerp(baseColor, Color.white, .08f);
            float fineCellSize = 1f / level.subdivisions;

            CreateViewportEnvironmentBackdrop(background.transform, level);
            CreateBoardInteriorBackdrop(background.transform, level);
            CreateLowerScreenBackground(background.transform, level);

            for (int boardY = 0; boardY < level.boardRows; boardY++)
            {
                for (int boardX = 0; boardX < level.boardColumns; boardX++)
                {
                    Vector2Int boardCell = new Vector2Int(boardX, boardY);
                    Color color = (boardX + boardY) % 2 == 0
                        ? baseColor
                        : alternate;
                    bool isAlternateCell = (boardX + boardY) % 2 != 0;
                    Material gridMaterial = GetGridMaterial(isAlternateCell);
                    Color presentationColor = configuredBoardPresentationConfig != null
                        ? configuredBoardPresentationConfig.GetGridTint(isAlternateCell)
                        : color;

                    if (IsBoardCellFullyActive(level, boardCell))
                    {
                        GameObject square = PrototypeBootstrap.CreateVisualBlock(
                            $"Background Grid Cell {boardX}, {boardY}",
                            GridCellWorldPosition(level, boardCell),
                            Vector2.one * ResolveGridCellVisualScale(),
                            presentationColor,
                            gridMaterial,
                            -10,
                            configuredBoardPresentationConfig != null
                                ? configuredBoardPresentationConfig.GridSprite
                                : null,
                            true);
                        square.transform.SetParent(background.transform, true);
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
                            Vector2.one * fineCellSize * ResolveGridCellVisualScale(),
                            presentationColor,
                            gridMaterial,
                            -10,
                            configuredBoardPresentationConfig != null
                                ? configuredBoardPresentationConfig.GridSprite
                                : null,
                            true);
                        fragment.transform.SetParent(background.transform, true);
                    }
                }
            }
        }

        private static void CreateViewportEnvironmentBackdrop(
            Transform parent,
            GravityLevelDefinition level)
        {
            if (configuredBoardPresentationConfig == null ||
                configuredBoardPresentationConfig.EnvironmentBackdropMaterial == null)
                return;

            float cameraHalfHeight = ResolveCameraSize(level) +
                                     configuredBoardPresentationConfig.ViewportBottomPadding;
            float height = cameraHalfHeight * 2f;
            float width = height * CameraAspect();
            GameObject backdrop = PrototypeBootstrap.CreateVisualBlock(
                "Viewport Environment Backdrop",
                Vector2.zero,
                new Vector2(width, height),
                configuredBoardPresentationConfig.EnvironmentTint,
                configuredBoardPresentationConfig.EnvironmentBackdropMaterial,
                -20);
            backdrop.transform.SetParent(parent, true);

            CreateViewportEdgeDarkening(parent, width, height);
        }

        private static void CreateViewportEdgeDarkening(
            Transform parent,
            float viewportWidth,
            float viewportHeight)
        {
            Material material = configuredBoardPresentationConfig.ExteriorEdgeDarkeningMaterial;
            float alpha = configuredBoardPresentationConfig.ExteriorEdgeDarkeningAlpha;
            if (material == null || alpha <= .001f)
                return;

            float fadeWidth = Mathf.Min(
                configuredBoardPresentationConfig.ExteriorEdgeDarkeningWidth,
                Mathf.Min(viewportWidth, viewportHeight) * .5f);
            Color tint = new Color(0f, 0f, 0f, alpha);
            float halfWidth = viewportWidth * .5f;
            float halfHeight = viewportHeight * .5f;

            CreateEdgeDarkeningStrip(
                parent,
                "Exterior Top Darkening",
                new Vector2(0f, halfHeight - fadeWidth * .5f),
                new Vector2(viewportWidth, fadeWidth),
                180f,
                tint,
                material);
            CreateEdgeDarkeningStrip(
                parent,
                "Exterior Bottom Darkening",
                new Vector2(0f, -halfHeight + fadeWidth * .5f),
                new Vector2(viewportWidth, fadeWidth),
                0f,
                tint,
                material);
            CreateEdgeDarkeningStrip(
                parent,
                "Exterior Left Darkening",
                new Vector2(-halfWidth + fadeWidth * .5f, 0f),
                new Vector2(fadeWidth, viewportHeight),
                -90f,
                tint,
                material);
            CreateEdgeDarkeningStrip(
                parent,
                "Exterior Right Darkening",
                new Vector2(halfWidth - fadeWidth * .5f, 0f),
                new Vector2(fadeWidth, viewportHeight),
                90f,
                tint,
                material);
        }

        private static void CreateEdgeDarkeningStrip(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size,
            float rotation,
            Color tint,
            Material material)
        {
            GameObject strip = PrototypeBootstrap.CreateVisualBlock(
                name,
                position,
                size,
                tint,
                material,
                -19);
            strip.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
            strip.transform.SetParent(parent, true);
        }

        private static void CreateBoardInteriorBackdrop(Transform parent, GravityLevelDefinition level)
        {
            if (configuredBoardPresentationConfig == null ||
                configuredBoardPresentationConfig.BoardInteriorMaterial == null)
                return;

            GameObject backdrop = PrototypeBootstrap.CreateVisualBlock(
                "Board Environment Backdrop",
                Vector2.zero,
                new Vector2(level.boardColumns, level.boardRows),
                configuredBoardPresentationConfig.BoardInteriorTint,
                configuredBoardPresentationConfig.BoardInteriorMaterial,
                -11);
            backdrop.transform.SetParent(parent, true);
        }

        private static void CreateLowerScreenBackground(Transform parent, GravityLevelDefinition level)
        {
            float extension = ResolveLowerPresentationExtent(level);
            if (extension <= .001f || configuredBoardPresentationConfig == null)
                return;

            float halfHeight = level.boardRows * .5f;
            GameObject backdrop = PrototypeBootstrap.CreateVisualBlock(
                "Lower Environment Backdrop",
                new Vector2(0f, -halfHeight - extension * .5f),
                new Vector2(level.boardColumns, extension),
                configuredBoardPresentationConfig.BoardInteriorTint,
                configuredBoardPresentationConfig.BoardInteriorMaterial,
                -11);
            backdrop.transform.SetParent(parent, true);

            if (configuredBoardPresentationConfig.RenderGridBelowShredder)
            {
                Color bottomColor = configuredBoardPresentationConfig.LowerGradientTint;
                bottomColor.a = 1f;
                int rowCount = Mathf.Max(1, Mathf.CeilToInt(extension));
                for (int row = 0; row < rowCount; row++)
                {
                    float normalizedDepth = Mathf.Clamp01((row + .5f) / extension);
                    float delayedDepth = normalizedDepth * normalizedDepth * normalizedDepth;
                    float smoothDepth = delayedDepth * delayedDepth * (3f - 2f * delayedDepth);
                    Color depthTint = Color.Lerp(Color.white, bottomColor, smoothDepth);
                    float centreY = -halfHeight - row - .5f;
                    for (int column = 0; column < level.boardColumns; column++)
                    {
                        bool isAlternateCell = (column - row - 1) % 2 != 0;
                        Color gridTint = configuredBoardPresentationConfig.GetGridTint(isAlternateCell);
                        Color presentationTint = new Color(
                            gridTint.r * depthTint.r,
                            gridTint.g * depthTint.g,
                            gridTint.b * depthTint.b,
                            gridTint.a * (1f - smoothDepth));
                        GameObject cell = PrototypeBootstrap.CreateVisualBlock(
                            $"Lower Grid Cell {column}, {-row - 1}",
                            new Vector2(-level.boardColumns * .5f + column + .5f, centreY),
                            Vector2.one * ResolveGridCellVisualScale(),
                            presentationTint,
                            GetGridMaterial(isAlternateCell),
                            -10,
                            configuredBoardPresentationConfig.GridSprite,
                            true);
                        cell.transform.SetParent(parent, true);
                    }
                }
            }

            Color overlayColor = new Color(
                0f,
                0f,
                0f,
                configuredBoardPresentationConfig.LowerGridBottomOverlayAlpha);
            GameObject overlay = PrototypeBootstrap.CreateVisualBlock(
                "Lower Grid Darkening Gradient",
                new Vector2(0f, -halfHeight - extension * .5f),
                new Vector2(level.boardColumns, extension + .02f),
                overlayColor,
                configuredBoardPresentationConfig.LowerGridDarkeningMaterial,
                -9);
            overlay.transform.SetParent(parent, true);
        }

        private static float ResolveGridCellVisualScale()
        {
            return configuredBoardPresentationConfig != null
                ? configuredBoardPresentationConfig.GridCellVisualScale
                : 1f;
        }

        private static Material GetGridMaterial(bool isAlternateCell)
        {
            return configuredBoardPresentationConfig != null
                ? configuredBoardPresentationConfig.GetGridMaterial(isAlternateCell)
                : null;
        }

        private static void CreateBoardFrame(GravityLevelDefinition level, float exitWidth)
        {
            float fineCellSize = 1f / level.subdivisions;
            float thickness = level.frameThickness;
            int edgeIndex = 0;
            GameObject frameRoot = new GameObject("Composite Board Frame");

            // Map Shape defines playable board topology, not decorative walls.
            // Only the outside perimeter is rendered as a frame; cut-outs stay
            // visually open while their collision geometry is still generated
            // below by CreateMapCollisionBlocks.
            HashSet<Vector2Int> outerTopEdges = new HashSet<Vector2Int>();
            HashSet<Vector2Int> outerLeftEdges = new HashSet<Vector2Int>();
            HashSet<Vector2Int> outerRightEdges = new HashSet<Vector2Int>();

            for (int y = 0; y < level.FineRows; y++)
            {
                for (int x = 0; x < level.FineColumns; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!IsFineCellActive(level, cell))
                        continue;

                    Vector2 centre = CellWorldPosition(level, cell);

                    if (!IsFineCellActive(level, cell + Vector2Int.left))
                    {
                        if (x == 0)
                            outerLeftEdges.Add(cell);
                    }

                    if (!IsFineCellActive(level, cell + Vector2Int.right))
                    {
                        if (x == level.FineColumns - 1)
                            outerRightEdges.Add(cell);
                    }

                    if (!IsFineCellActive(level, cell + Vector2Int.up))
                    {
                        if (y == level.FineRows - 1)
                            outerTopEdges.Add(cell);
                    }

                    if (!IsFineCellActive(level, cell + Vector2Int.down))
                    {
                        if (y == 0 &&
                            (configuredBoardPresentationConfig == null ||
                             configuredBoardPresentationConfig.RenderBottomEdgeSegments))
                        {
                            CreateBottomEdgeWithExit(frameRoot.transform, level, centre, fineCellSize, exitWidth, thickness, ref edgeIndex);
                        }
                    }
                }
            }

            CreateHorizontalFrameRuns(
                frameRoot.transform,
                level,
                outerTopEdges,
                fineCellSize,
                thickness,
                1f,
                BoardFrameEdge.Top,
                ref edgeIndex);
            CreateVerticalFrameRuns(
                frameRoot.transform,
                level,
                outerLeftEdges,
                fineCellSize,
                thickness,
                -1f,
                BoardFrameEdge.Left,
                ref edgeIndex);
            CreateVerticalFrameRuns(
                frameRoot.transform,
                level,
                outerRightEdges,
                fineCellSize,
                thickness,
                1f,
                BoardFrameEdge.Right,
                ref edgeIndex);
            CreateOuterFrameCorners(frameRoot.transform, level, fineCellSize, thickness, ref edgeIndex);
            CreateLowerFrameContinuation(frameRoot.transform, level, thickness, ref edgeIndex);

            CreateMapCollisionBlocks(level, exitWidth, fineCellSize);
        }

        private static void CreateLowerFrameContinuation(
            Transform frameRoot,
            GravityLevelDefinition level,
            float thickness,
            ref int edgeIndex)
        {
            float extension = ResolveLowerPresentationExtent(level);
            float visibleLength = extension - thickness * .5f;
            if (visibleLength <= .001f)
                return;

            float halfWidth = level.boardColumns * .5f;
            float halfHeight = level.boardRows * .5f;
            float top = -halfHeight - thickness * .5f;
            float bottom = top - visibleLength;
            float centreY = (top + bottom) * .5f;
            CreateFrameEdge(
                frameRoot,
                $"Lower Frame Edge {++edgeIndex}",
                new Vector2(-halfWidth - thickness * .5f, centreY),
                new Vector2(thickness, visibleLength),
                level.frameColor,
                BoardFrameEdge.Left);
            CreateFrameEdge(
                frameRoot,
                $"Lower Frame Edge {++edgeIndex}",
                new Vector2(halfWidth + thickness * .5f, centreY),
                new Vector2(thickness, visibleLength),
                level.frameColor,
                BoardFrameEdge.Right);
        }

        private static float ResolveLowerPresentationExtent(GravityLevelDefinition level)
        {
            if (level == null || configuredBoardPresentationConfig == null ||
                !configuredBoardPresentationConfig.ExtendPresentationToViewportBottom)
                return 0f;

            float halfHeight = level.boardRows * .5f;
            return Mathf.Max(
                0f,
                ResolveCameraSize(level) - halfHeight +
                configuredBoardPresentationConfig.ViewportBottomPadding);
        }

        private static void CreateHorizontalFrameRuns(
            Transform frameRoot,
            GravityLevelDefinition level,
            HashSet<Vector2Int> edges,
            float fineCellSize,
            float thickness,
            float verticalDirection,
            BoardFrameEdge frameEdge,
            ref int edgeIndex)
        {
            for (int y = 0; y < level.FineRows; y++)
            {
                for (int x = 0; x < level.FineColumns;)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!edges.Contains(cell))
                    {
                        x++;
                        continue;
                    }

                    int startX = x;
                    do
                    {
                        x++;
                    }
                    while (x < level.FineColumns && edges.Contains(new Vector2Int(x, y)));

                    int length = x - startX;
                    Vector2 firstCentre = CellWorldPosition(level, new Vector2Int(startX, y));
                    Vector2 lastCentre = CellWorldPosition(level, new Vector2Int(x - 1, y));
                    float leftTrim = 0f;
                    float rightTrim = 0f;
                    if (frameEdge == BoardFrameEdge.Top && y == level.FineRows - 1)
                    {
                        if (startX == 0 && HasOuterFrameCorner(
                                level,
                                new Vector2Int(0, level.FineRows - 1),
                                BoardFrameEdge.TopLeftCorner))
                        {
                            leftTrim = GetCornerJoinTrim(
                                fineCellSize,
                                thickness,
                                configuredBoardPresentationConfig.CornerOutset.x) +
                                configuredBoardPresentationConfig.TopCornerHorizontalInset;
                        }

                        if (x == level.FineColumns && HasOuterFrameCorner(
                                level,
                                new Vector2Int(level.FineColumns - 1, level.FineRows - 1),
                                BoardFrameEdge.TopRightCorner))
                        {
                            rightTrim = GetCornerJoinTrim(
                                fineCellSize,
                                thickness,
                                configuredBoardPresentationConfig.CornerOutset.x) +
                                configuredBoardPresentationConfig.TopCornerHorizontalInset;
                        }
                    }

                    float runLength = length * fineCellSize + thickness;
                    float visibleLength = Mathf.Max(.001f, runLength - leftTrim - rightTrim);
                    Vector2 position = new Vector2(
                        (firstCentre.x + lastCentre.x) * .5f + (leftTrim - rightTrim) * .5f,
                        firstCentre.y + verticalDirection * (fineCellSize + thickness) * .5f);
                    CreateFrameEdge(
                        frameRoot,
                        $"Frame Edge {++edgeIndex}",
                        position,
                        new Vector2(visibleLength, thickness),
                        level.frameColor,
                        frameEdge);
                }
            }
        }

        private static void CreateVerticalFrameRuns(
            Transform frameRoot,
            GravityLevelDefinition level,
            HashSet<Vector2Int> edges,
            float fineCellSize,
            float thickness,
            float horizontalDirection,
            BoardFrameEdge frameEdge,
            ref int edgeIndex)
        {
            for (int x = 0; x < level.FineColumns; x++)
            {
                for (int y = 0; y < level.FineRows;)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!edges.Contains(cell))
                    {
                        y++;
                        continue;
                    }

                    int startY = y;
                    do
                    {
                        y++;
                    }
                    while (y < level.FineRows && edges.Contains(new Vector2Int(x, y)));

                    int length = y - startY;
                    Vector2 firstCentre = CellWorldPosition(level, new Vector2Int(x, startY));
                    Vector2 lastCentre = CellWorldPosition(level, new Vector2Int(x, y - 1));
                    float bottomTrim = 0f;
                    float topTrim = 0f;
                    if (startY == 0)
                    {
                        BoardFrameEdge bottomCorner = frameEdge == BoardFrameEdge.Left
                            ? BoardFrameEdge.BottomLeftCorner
                            : BoardFrameEdge.BottomRightCorner;
                        if (HasOuterFrameCorner(level, new Vector2Int(x, 0), bottomCorner))
                        {
                            bottomTrim = GetCornerJoinTrim(
                                fineCellSize,
                                thickness,
                                configuredBoardPresentationConfig.CornerOutset.y);
                        }
                    }

                    if (y == level.FineRows)
                    {
                        BoardFrameEdge topCorner = frameEdge == BoardFrameEdge.Left
                            ? BoardFrameEdge.TopLeftCorner
                            : BoardFrameEdge.TopRightCorner;
                        if (HasOuterFrameCorner(level, new Vector2Int(x, level.FineRows - 1), topCorner))
                        {
                            topTrim = GetCornerJoinTrim(
                                fineCellSize,
                                thickness,
                                configuredBoardPresentationConfig.CornerOutset.y) +
                                configuredBoardPresentationConfig.TopCornerVerticalInset;
                        }
                        else if (configuredBoardPresentationConfig != null)
                        {
                            // With no authored corner cap, end the side rail at the same
                            // inset used by the top rail. Otherwise the collision-sized
                            // vertical run protrudes above the presentation-only top edge
                            // and creates the two antenna-like bars seen at the board top.
                            topTrim = thickness * .5f +
                                      configuredBoardPresentationConfig.TopEdgeInwardOverlap;
                        }
                    }

                    float runLength = length * fineCellSize + thickness;
                    float visibleLength = Mathf.Max(.001f, runLength - bottomTrim - topTrim);
                    Vector2 position = new Vector2(
                        firstCentre.x + horizontalDirection * (fineCellSize + thickness) * .5f,
                        (firstCentre.y + lastCentre.y) * .5f + (bottomTrim - topTrim) * .5f);
                    CreateFrameEdge(
                        frameRoot,
                        $"Frame Edge {++edgeIndex}",
                        position,
                        new Vector2(thickness, visibleLength),
                        level.frameColor,
                        frameEdge);
                }
            }
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
            float halfWidth = level.boardColumns * .5f;
            float cornerInset = Mathf.Max(
                0f,
                GetCornerJoinTrim(
                    fineCellSize,
                    thickness,
                    configuredBoardPresentationConfig != null
                        ? configuredBoardPresentationConfig.CornerOutset.x
                        : 0f) - thickness * .5f);

            if (Mathf.Approximately(cellLeft, -halfWidth) && HasOuterFrameCorner(
                    level,
                    Vector2Int.zero,
                    BoardFrameEdge.BottomLeftCorner))
            {
                cellLeft += cornerInset;
            }

            if (Mathf.Approximately(cellRight, halfWidth) && HasOuterFrameCorner(
                    level,
                    new Vector2Int(level.FineColumns - 1, 0),
                    BoardFrameEdge.BottomRightCorner))
            {
                cellRight -= cornerInset;
            }

            float leftLength = Mathf.Max(0f, Mathf.Min(cellRight, exitLeft) - cellLeft);
            if (leftLength > .001f)
            {
                float centreX = cellLeft + leftLength * .5f;
                CreateFrameEdge(
                    frameRoot,
                    $"Frame Edge {++edgeIndex}",
                    new Vector2(centreX, y),
                    new Vector2(leftLength, thickness),
                    level.frameColor,
                    BoardFrameEdge.Bottom);
            }

            float rightStart = Mathf.Max(cellLeft, exitRight);
            float rightLength = Mathf.Max(0f, cellRight - rightStart);
            if (rightLength > .001f)
            {
                float centreX = rightStart + rightLength * .5f;
                CreateFrameEdge(
                    frameRoot,
                    $"Frame Edge {++edgeIndex}",
                    new Vector2(centreX, y),
                    new Vector2(rightLength, thickness),
                    level.frameColor,
                    BoardFrameEdge.Bottom);
            }
        }

        private static void CreateFrameEdge(
            Transform frameRoot,
            string name,
            Vector2 position,
            Vector2 size,
            Color color,
            BoardFrameEdge frameEdge)
        {
            if (configuredBoardPresentationConfig != null &&
                configuredBoardPresentationConfig.GetEdgeSprite(frameEdge) == null)
                return;

            if (!IsFrameCorner(frameEdge) && TryCreateRepeatedFrameModules(
                    frameRoot,
                    name,
                    position,
                    size,
                    frameEdge))
                return;

            GameObject frameObject = PrototypeBootstrap.CreateVisualBlock(name, position, size, color);
            frameObject.transform.SetParent(frameRoot, true);

            ApplyFramePresentation(frameObject.GetComponent<SpriteRenderer>(), size, frameEdge);
        }

        private static bool TryCreateRepeatedFrameModules(
            Transform frameRoot,
            string name,
            Vector2 position,
            Vector2 size,
            BoardFrameEdge frameEdge)
        {
            if (configuredBoardPresentationConfig == null)
                return false;

            Sprite sprite = configuredBoardPresentationConfig.GetEdgeSprite(frameEdge);
            if (sprite == null)
                return false;

            bool horizontal = frameEdge == BoardFrameEdge.Top ||
                              frameEdge == BoardFrameEdge.Bottom;
            float thicknessMultiplier = frameEdge == BoardFrameEdge.Top
                ? configuredBoardPresentationConfig.TopEdgeVisualThicknessMultiplier
                : configuredBoardPresentationConfig.FrameVisualThicknessMultiplier;
            float originalThickness = horizontal ? size.y : size.x;
            Vector2 spriteSize = sprite.bounds.size;
            float nativeScale = configuredBoardPresentationConfig.FrameModuleScale;
            float visualThickness = (horizontal ? spriteSize.y : spriteSize.x) *
                                    nativeScale * thicknessMultiplier;

            // Authored Scene Tuna modules straddle the logical board boundary.
            // The former implementation aligned their inner transparent edge
            // with the boundary, which exposed the atlas padding as a gap and
            // produced the detached double-line visible above the grid.
            if (frameEdge == BoardFrameEdge.Top)
                position.y -= originalThickness * .5f +
                              configuredBoardPresentationConfig.TopEdgeInwardOverlap;
            else if (frameEdge == BoardFrameEdge.Bottom)
                position.y += originalThickness * .5f;
            else if (frameEdge == BoardFrameEdge.Left)
                position.x += originalThickness * .5f;
            else if (frameEdge == BoardFrameEdge.Right)
                position.x -= originalThickness * .5f;

            float nativeLength = (horizontal ? spriteSize.x : spriteSize.y) *
                                 nativeScale;
            float requestedLength = horizontal ? size.x : size.y;
            if (nativeLength <= .001f || requestedLength <= .001f)
                return false;

            int moduleCount = Mathf.Max(1, Mathf.RoundToInt(requestedLength / nativeLength));
            float moduleLength = requestedLength / moduleCount;
            for (int moduleIndex = 0; moduleIndex < moduleCount; moduleIndex++)
            {
                float offset = -requestedLength * .5f +
                               moduleLength * (moduleIndex + .5f);
                Vector2 modulePosition = position +
                                         (horizontal
                                             ? Vector2.right * offset
                                             : Vector2.up * offset);
                Vector2 moduleSize = horizontal
                    ? new Vector2(moduleLength, visualThickness)
                    : new Vector2(visualThickness, moduleLength);
                GameObject module = PrototypeBootstrap.CreateVisualBlock(
                    $"{name} Module {moduleIndex + 1}",
                    modulePosition,
                    moduleSize,
                    configuredBoardPresentationConfig.FrameTint,
                    configuredBoardPresentationConfig.FrameMaterial,
                    FrameSortingOrder,
                    sprite,
                    true,
                    true,
                    false,
                    false,
                    frameEdge == BoardFrameEdge.Top);
                module.transform.SetParent(frameRoot, true);
            }

            return true;
        }

        private static void CreateOuterFrameCorners(
            Transform frameRoot,
            GravityLevelDefinition level,
            float fineCellSize,
            float thickness,
            ref int edgeIndex)
        {
            if (configuredBoardPresentationConfig == null)
                return;

            float halfWidth = level.boardColumns * .5f;
            float halfHeight = level.boardRows * .5f;
            float cornerSize = GetOuterFrameCornerSize(fineCellSize, thickness);
            Vector2 cornerOutset = configuredBoardPresentationConfig.CornerOutset;
            float topCornerInset = configuredBoardPresentationConfig.TopCornerHorizontalInset;
            float topCornerVerticalInset = configuredBoardPresentationConfig.TopCornerVerticalInset;
            CreateFrameCorner(
                frameRoot,
                level,
                new Vector2Int(0, level.FineRows - 1),
                new Vector2(
                    -halfWidth - thickness * .5f - cornerOutset.x + topCornerInset,
                    halfHeight + thickness * .5f + cornerOutset.y - topCornerVerticalInset),
                cornerSize,
                BoardFrameEdge.TopLeftCorner,
                ref edgeIndex);
            CreateFrameCorner(
                frameRoot,
                level,
                new Vector2Int(level.FineColumns - 1, level.FineRows - 1),
                new Vector2(
                    halfWidth + thickness * .5f + cornerOutset.x - topCornerInset,
                    halfHeight + thickness * .5f + cornerOutset.y - topCornerVerticalInset),
                cornerSize,
                BoardFrameEdge.TopRightCorner,
                ref edgeIndex);
            CreateFrameCorner(
                frameRoot,
                level,
                Vector2Int.zero,
                new Vector2(
                    -halfWidth - thickness * .5f - cornerOutset.x,
                    -halfHeight - thickness * .5f - cornerOutset.y),
                cornerSize,
                BoardFrameEdge.BottomLeftCorner,
                ref edgeIndex);
            CreateFrameCorner(
                frameRoot,
                level,
                new Vector2Int(level.FineColumns - 1, 0),
                new Vector2(
                    halfWidth + thickness * .5f + cornerOutset.x,
                    -halfHeight - thickness * .5f - cornerOutset.y),
                cornerSize,
                BoardFrameEdge.BottomRightCorner,
                ref edgeIndex);
        }

        private static void CreateFrameCorner(
            Transform frameRoot,
            GravityLevelDefinition level,
            Vector2Int requiredCell,
            Vector2 position,
            float size,
            BoardFrameEdge corner,
            ref int edgeIndex)
        {
            if (!IsFineCellActive(level, requiredCell) ||
                configuredBoardPresentationConfig.GetEdgeSprite(corner) == null)
                return;

            CreateFrameEdge(
                frameRoot,
                $"Frame Corner {++edgeIndex}",
                position,
                Vector2.one * size,
                level.frameColor,
                corner);
        }

        private static bool HasOuterFrameCorner(
            GravityLevelDefinition level,
            Vector2Int requiredCell,
            BoardFrameEdge corner)
        {
            return configuredBoardPresentationConfig != null &&
                   IsFineCellActive(level, requiredCell) &&
                   configuredBoardPresentationConfig.GetEdgeSprite(corner) != null;
        }

        private static float GetOuterFrameCornerSize(float fineCellSize, float thickness)
        {
            // Scene Tuna authors each corner as one grid-sized atlas module.
            // Including frame thickness here enlarged the artwork and produced
            // solid rectangular caps above the board.
            return Mathf.Max(fineCellSize, thickness * 2f);
        }

        private static float GetCornerJoinTrim(
            float fineCellSize,
            float thickness,
            float cornerOutset)
        {
            return Mathf.Max(
                0f,
                GetOuterFrameCornerSize(fineCellSize, thickness) * .5f - cornerOutset);
        }

        private static void ApplyFramePresentation(
            SpriteRenderer renderer,
            Vector2 size,
            BoardFrameEdge edge)
        {
            if (renderer == null || configuredBoardPresentationConfig == null)
                return;

            Sprite sprite = configuredBoardPresentationConfig.GetEdgeSprite(edge);
            if (sprite == null)
                return;

            renderer.sprite = sprite;
            renderer.sharedMaterial = configuredBoardPresentationConfig.FrameMaterial;
            renderer.color = configuredBoardPresentationConfig.FrameTint;
            renderer.sortingOrder = IsFrameCorner(edge)
                ? FrameCornerSortingOrder
                : FrameSortingOrder;

            Vector2 spriteSize = sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f)
                return;

            renderer.transform.localScale = new Vector3(
                size.x / spriteSize.x,
                size.y / spriteSize.y,
                1f);
        }

        private static bool IsFrameCorner(BoardFrameEdge edge)
        {
            return edge == BoardFrameEdge.TopLeftCorner ||
                   edge == BoardFrameEdge.TopRightCorner ||
                   edge == BoardFrameEdge.BottomLeftCorner ||
                   edge == BoardFrameEdge.BottomRightCorner;
        }

        private static Color ResolveBoardBackgroundColor(GravityLevelDefinition level)
        {
            if (configuredBoardPresentationConfig != null &&
                configuredBoardPresentationConfig.OverrideLevelBackground)
                return configuredBoardPresentationConfig.BackgroundColor;

            return level != null ? level.backgroundColor : Color.black;
        }

        private static Color ResolveExteriorColor(GravityLevelDefinition level)
        {
            if (configuredBoardPresentationConfig != null)
                return configuredBoardPresentationConfig.ExteriorColor;

            return ResolveBoardBackgroundColor(level);
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

        private static void CreateShredders(
            GravityLevelDefinition level,
            float halfHeight,
            ShredderConfig shredderConfig)
        {
            float rotationMultiplier = shredderConfig != null
                ? shredderConfig.WheelRotationSpeedMultiplier
                : 1f;

            // The cutter is a continuous bottom-row hazard, independent of the
            // decorative frame exit. Cover every board column including both
            // outermost cells, so pieces cannot bypass it through a corner.
            float coverageWidth = level.boardColumns;
            int count = CalculateRequiredShredderWheelCount(level, shredderConfig);
            // Fit an integer number of touching wheels exactly across the full
            // board width. The first/last wheel edges land on the board edges.
            float radius = coverageWidth / (count * 2f);
            // Centre-to-centre spacing equals a wheel diameter: teeth touch with
            // no authored gaps, regardless of board size or old level metadata.
            float spacing = radius * 2f;
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
                    level.shredderRotationSpeed * rotationMultiplier * direction);
            }

            CreateShredderCatchZone(
                startX - radius,
                startX + (count - 1) * spacing + radius,
                y + radius,
                radius,
                shredderConfig);
        }

        private static void CreateShredder(string name, Vector2 position, float radius, float speed)
        {
            if (!ShredderWheelPool.TryRent(out ShredderWheel wheel))
                throw new InvalidOperationException("[ShredderPool] Pool is not configured or has insufficient capacity.");

            GameObject shredder = wheel.gameObject;
            shredder.name = name;
            shredder.transform.position = position;
            wheel.Configure(radius, speed);
        }

        private static bool TryValidateShredderPools(
            GravityLevelDefinition level,
            ShredderConfig shredderConfig)
        {
            if (shredderConfig == null)
            {
                Debug.LogError(
                    "[LevelRuntime] BlockShredder requires a ShredderConfig before a level can be built.");
                return false;
            }

            int requiredWheelCount = CalculateRequiredShredderWheelCount(level, shredderConfig);
            if (!ShredderWheelPool.HasCapacity(requiredWheelCount))
            {
                Debug.LogError(
                    $"[ShredderPool] Level requires {requiredWheelCount} wheel(s), but the configured wheel pool capacity is insufficient. " +
                    "Increase Wheel Pool Capacity in ShredderConfig before playing this level.");
                return false;
            }

            if (!ShredderCatchZonePool.HasCapacity(1))
            {
                Debug.LogError(
                    "[ShredderPool] Catch-zone pool is not configured. Create and assign the ShredderCatchZone prefab in ShredderConfig.");
                return false;
            }

            return true;
        }

        private static int CalculateRequiredShredderWheelCount(
            GravityLevelDefinition level,
            ShredderConfig shredderConfig)
        {
            float radiusMultiplier = shredderConfig != null
                ? shredderConfig.WheelRadiusMultiplier
                : 1f;
            float requestedRadius = Mathf.Max(.2f, level.shredderRadius * radiusMultiplier);
            return Mathf.Max(1, Mathf.CeilToInt(level.boardColumns / (requestedRadius * 2f)));
        }

        private static void CreateShredderCatchZone(
            float leftEdge,
            float rightEdge,
            float topY,
            float radius,
            ShredderConfig shredderConfig)
        {
            if (shredderConfig == null ||
                !ShredderCatchZonePool.TryRent(out ShredderCatchZone zone))
            {
                throw new InvalidOperationException(
                    "[ShredderPool] Catch-zone pool is not configured. Create and assign the ShredderCatchZone prefab in ShredderConfig.");
            }

            float thickness = 10f;
            float width = Mathf.Max(radius * 2f, rightEdge - leftEdge + radius * .5f);
            zone.Configure(
                new Vector2(
                (leftEdge + rightEdge) * .5f,
                topY - thickness * .5f),
                new Vector2(width, thickness),
                topY,
                shredderConfig.CaptureApproachDistance);
        }

        private static void CreateObstacles(GravityLevelDefinition level)
        {
            Dictionary<Vector2Int, Color> visualCells = new Dictionary<Vector2Int, Color>();
            for (int obstacleIndex = 0; obstacleIndex < level.obstacles.Count; obstacleIndex++)
            {
                ObstacleDefinition obstacle = level.obstacles[obstacleIndex];
                if (!obstacle.usesGridCells)
                {
                    CreateLegacyObstacle(level, obstacle);
                    continue;
                }

                Vector2Int authoredSize = new Vector2Int(
                    Mathf.Max(1, obstacle.sizeInGridCells.x),
                    Mathf.Max(1, obstacle.sizeInGridCells.y));
                Vector2Int rotatedSize = obstacle.quarterTurns % 2 == 0
                    ? authoredSize
                    : new Vector2Int(authoredSize.y, authoredSize.x);
                Vector2 centre = new Vector2(
                    -level.boardColumns * .5f + obstacle.gridCell.x + rotatedSize.x * .5f,
                    -level.boardRows * .5f + obstacle.gridCell.y + rotatedSize.y * .5f);

                // Keep collision ownership on the established static-block
                // path while presentation is rendered once per connected
                // component below. The transparent renderer is presentation-
                // inert and avoids creating a second collider authority.
                PrototypeBootstrap.CreateStaticBlock(
                    obstacle.name + " Collision",
                    centre,
                    rotatedSize,
                    Color.clear,
                    false,
                    renderVisual: false);

                for (int y = 0; y < rotatedSize.y; y++)
                for (int x = 0; x < rotatedSize.x; x++)
                {
                    Vector2Int cell = obstacle.gridCell + new Vector2Int(x, y);
                    if (!visualCells.ContainsKey(cell))
                        visualCells.Add(cell, ResolveStaticBlockColor(obstacle.color));
                }
            }

            CreateMergedObstacleVisuals(level, visualCells);
        }

        private static void CreateMergedObstacleVisuals(
            GravityLevelDefinition level,
            Dictionary<Vector2Int, Color> visualCells)
        {
            HashSet<Vector2Int> remaining = new HashSet<Vector2Int>(visualCells.Keys);
            List<Vector2Int> queue = new List<Vector2Int>();
            List<Vector2Int> component = new List<Vector2Int>();
            while (remaining.Count > 0)
            {
                Vector2Int seed = default;
                foreach (Vector2Int candidate in remaining)
                {
                    seed = candidate;
                    break;
                }

                queue.Clear();
                component.Clear();
                queue.Add(seed);
                remaining.Remove(seed);
                Color componentColor = visualCells[seed];
                for (int queueIndex = 0; queueIndex < queue.Count; queueIndex++)
                {
                    Vector2Int cell = queue[queueIndex];
                    component.Add(cell);
                    AddConnectedObstacleCell(
                        cell + Vector2Int.left,
                        componentColor,
                        visualCells,
                        remaining,
                        queue);
                    AddConnectedObstacleCell(
                        cell + Vector2Int.right,
                        componentColor,
                        visualCells,
                        remaining,
                        queue);
                    AddConnectedObstacleCell(
                        cell + Vector2Int.up,
                        componentColor,
                        visualCells,
                        remaining,
                        queue);
                    AddConnectedObstacleCell(
                        cell + Vector2Int.down,
                        componentColor,
                        visualCells,
                        remaining,
                        queue);
                }

                CreateMergedObstacleVisual(level, component, componentColor);
            }
        }

        private static void AddConnectedObstacleCell(
            Vector2Int cell,
            Color componentColor,
            Dictionary<Vector2Int, Color> visualCells,
            HashSet<Vector2Int> remaining,
            List<Vector2Int> queue)
        {
            if (!remaining.Contains(cell) ||
                !visualCells.TryGetValue(cell, out Color cellColor) ||
                cellColor != componentColor)
                return;

            remaining.Remove(cell);
            queue.Add(cell);
        }

        private static void CreateMergedObstacleVisual(
            GravityLevelDefinition level,
            List<Vector2Int> cells,
            Color color)
        {
            if (cells == null || cells.Count == 0)
                return;

            Vector2Int minimum = cells[0];
            Vector2Int maximum = cells[0];
            for (int index = 1; index < cells.Count; index++)
            {
                minimum = Vector2Int.Min(minimum, cells[index]);
                maximum = Vector2Int.Max(maximum, cells[index]);
            }

            Sprite shapeSprite = null;
            string shapeKey = BuildNormalizedShapeKey(cells, minimum);
            if (configuredBoardPresentationConfig != null)
                configuredBoardPresentationConfig.TryGetStaticBlockShape(shapeKey, out shapeSprite);

            Vector2Int boundsSize = maximum - minimum + Vector2Int.one;
            if (shapeSprite != null)
            {
                Vector2 centre = new Vector2(
                    -level.boardColumns * .5f + minimum.x + boundsSize.x * .5f,
                    -level.boardRows * .5f + minimum.y + boundsSize.y * .5f);
                PrototypeBootstrap.CreateVisualBlock(
                    "Merged Obstacle Visual",
                    centre,
                    boundsSize,
                    color,
                    configuredBoardPresentationConfig != null
                        ? configuredBoardPresentationConfig.BoardBlockMaterial
                        : null,
                    0,
                    shapeSprite,
                    true);
                return;
            }

            Sprite modularSprite = configuredBoardPresentationConfig != null
                ? configuredBoardPresentationConfig.StaticBlockSprite
                : null;
            if (modularSprite != null)
            {
                CreateSlicedObstacleRuns(level, cells, color, modularSprite);
                return;
            }

            // The map atlas does not cover every possible authored topology.
            // If the configured fallback sprite is missing, retain a visible
            // cell-by-cell fallback. Gameplay occupancy and collision sizes
            // remain unchanged in either presentation path.
            float modularScale = configuredBoardPresentationConfig != null
                ? configuredBoardPresentationConfig.ModularStaticBlockScale
                : 1f;
            for (int index = 0; index < cells.Count; index++)
            {
                PrototypeBootstrap.CreateVisualBlock(
                    "Obstacle Visual",
                    GridCellWorldPosition(level, cells[index]),
                    Vector2.one * modularScale,
                    color,
                    configuredBoardPresentationConfig != null
                        ? configuredBoardPresentationConfig.BoardBlockMaterial
                        : null,
                    0,
                    configuredBoardPresentationConfig != null
                        ? configuredBoardPresentationConfig.StaticBlockSprite
                        : null,
                    configuredBoardPresentationConfig != null &&
                    configuredBoardPresentationConfig.StaticBlockSprite != null);
            }
        }

        private static void CreateSlicedObstacleRuns(
            GravityLevelDefinition level,
            List<Vector2Int> cells,
            Color color,
            Sprite modularSprite)
        {
            // Exact atlas silhouettes remain the preferred path. For shapes
            // that the atlas does not contain, merge contiguous row spans and
            // then extend identical spans vertically. This turns an arbitrary
            // polyomino into a small set of connected 9-sliced rectangles
            // instead of exposing the bevel and transparent gutter of every
            // individual 1x1 sprite.
            List<Vector2Int> orderedCells = new List<Vector2Int>(cells);
            orderedCells.Sort((left, right) =>
            {
                int rowComparison = left.y.CompareTo(right.y);
                return rowComparison != 0 ? rowComparison : left.x.CompareTo(right.x);
            });

            List<RectInt> rectangles = new List<RectInt>();
            for (int cellIndex = 0; cellIndex < orderedCells.Count;)
            {
                int row = orderedCells[cellIndex].y;
                int runStart = orderedCells[cellIndex].x;
                int runEnd = runStart;
                cellIndex++;
                while (cellIndex < orderedCells.Count && orderedCells[cellIndex].y == row)
                {
                    int column = orderedCells[cellIndex].x;
                    if (column == runEnd + 1)
                    {
                        runEnd = column;
                        cellIndex++;
                        continue;
                    }

                    AddOrExtendObstacleRectangle(rectangles, runStart, runEnd, row);
                    runStart = column;
                    runEnd = column;
                    cellIndex++;
                }

                AddOrExtendObstacleRectangle(rectangles, runStart, runEnd, row);
            }

            float overlapScale = configuredBoardPresentationConfig != null
                ? configuredBoardPresentationConfig.ModularStaticBlockScale
                : 1f;
            Material material = configuredBoardPresentationConfig != null
                ? configuredBoardPresentationConfig.BoardBlockMaterial
                : null;
            for (int index = 0; index < rectangles.Count; index++)
            {
                RectInt rectangle = rectangles[index];
                Vector2 centre = new Vector2(
                    -level.boardColumns * .5f + rectangle.x + rectangle.width * .5f,
                    -level.boardRows * .5f + rectangle.y + rectangle.height * .5f);
                Vector2 visualSize = new Vector2(rectangle.width, rectangle.height) * overlapScale;
                PrototypeBootstrap.CreateVisualBlock(
                    "Sliced Obstacle Visual",
                    centre,
                    visualSize,
                    color,
                    material,
                    0,
                    modularSprite,
                    false,
                    true,
                    true);
            }

            CreateObstacleJoinVisuals(level, rectangles, color, material, modularSprite);
        }

        private static void CreateObstacleJoinVisuals(
            GravityLevelDefinition level,
            List<RectInt> rectangles,
            Color color,
            Material material,
            Sprite modularSprite)
        {
            // A fallback polyomino can require more than one sliced rectangle.
            // Their independently bevelled ends would otherwise remain visible
            // at shared edges and make one obstacle read as separate pieces.
            // These flat patches live wholly inside the occupied silhouette and
            // cover only those internal caps; gameplay geometry is untouched.
            float inset = configuredBoardPresentationConfig != null
                ? configuredBoardPresentationConfig.ModularStaticBlockJoinInset
                : .12f;
            float depth = configuredBoardPresentationConfig != null
                ? configuredBoardPresentationConfig.ModularStaticBlockJoinDepth
                : .42f;
            Sprite joinSprite = GetObstacleJoinSprite(modularSprite);

            for (int firstIndex = 0; firstIndex < rectangles.Count; firstIndex++)
            {
                RectInt first = rectangles[firstIndex];
                for (int secondIndex = firstIndex + 1; secondIndex < rectangles.Count; secondIndex++)
                {
                    RectInt second = rectangles[secondIndex];
                    if (first.yMax == second.yMin || second.yMax == first.yMin)
                    {
                        int overlapMin = Mathf.Max(first.xMin, second.xMin);
                        int overlapMax = Mathf.Min(first.xMax, second.xMax);
                        float width = overlapMax - overlapMin - inset * 2f;
                        if (width > 0f)
                        {
                            float boundary = first.yMax == second.yMin ? first.yMax : second.yMax;
                            CreateObstacleJoinVisual(
                                level,
                                new Vector2((overlapMin + overlapMax) * .5f, boundary),
                                new Vector2(width, depth),
                                color,
                                material,
                                joinSprite);
                        }
                    }

                    if (first.xMax != second.xMin && second.xMax != first.xMin)
                        continue;

                    int verticalOverlapMin = Mathf.Max(first.yMin, second.yMin);
                    int verticalOverlapMax = Mathf.Min(first.yMax, second.yMax);
                    float height = verticalOverlapMax - verticalOverlapMin - inset * 2f;
                    if (height <= 0f)
                        continue;

                    float verticalBoundary = first.xMax == second.xMin ? first.xMax : second.xMax;
                    CreateObstacleJoinVisual(
                        level,
                        new Vector2(verticalBoundary, (verticalOverlapMin + verticalOverlapMax) * .5f),
                        new Vector2(depth, height),
                        color,
                        material,
                        joinSprite);
                }
            }
        }

        private static Sprite GetObstacleJoinSprite(Sprite source)
        {
            if (obstacleJoinSprite != null && obstacleJoinSpriteSource == source)
                return obstacleJoinSprite;

            // The shader samples both the sprite atlas and its matching gloss
            // atlas. A generated white square therefore reads unrelated UVs
            // and appears as a dark rectangular artefact. Reusing the opaque
            // centre of the obstacle sprite keeps both texture lookups aligned
            // while omitting the rounded border that the join must cover.
            Rect sourceRect = source.rect;
            Vector4 border = source.border;
            Rect centreRect = Rect.MinMaxRect(
                sourceRect.xMin + border.x,
                sourceRect.yMin + border.y,
                sourceRect.xMax - border.z,
                sourceRect.yMax - border.w);
            if (centreRect.width < 1f || centreRect.height < 1f)
            {
                float inset = Mathf.Min(sourceRect.width, sourceRect.height) * .25f;
                centreRect = Rect.MinMaxRect(
                    sourceRect.xMin + inset,
                    sourceRect.yMin + inset,
                    sourceRect.xMax - inset,
                    sourceRect.yMax - inset);
            }

            obstacleJoinSpriteSource = source;
            obstacleJoinSprite = Sprite.Create(
                source.texture,
                centreRect,
                new Vector2(.5f, .5f),
                source.pixelsPerUnit,
                0,
                SpriteMeshType.FullRect);
            obstacleJoinSprite.name = source.name + " Join Centre";
            return obstacleJoinSprite;
        }

        private static void CreateObstacleJoinVisual(
            GravityLevelDefinition level,
            Vector2 boardPosition,
            Vector2 size,
            Color color,
            Material material,
            Sprite joinSprite)
        {
            Vector2 worldPosition = new Vector2(
                -level.boardColumns * .5f + boardPosition.x,
                -level.boardRows * .5f + boardPosition.y);
            PrototypeBootstrap.CreateVisualBlock(
                "Obstacle Join Visual",
                worldPosition,
                size,
                color,
                material,
                1,
                joinSprite,
                true);
        }

        private static void AddOrExtendObstacleRectangle(
            List<RectInt> rectangles,
            int runStart,
            int runEnd,
            int row)
        {
            int runWidth = runEnd - runStart + 1;
            for (int index = rectangles.Count - 1; index >= 0; index--)
            {
                RectInt rectangle = rectangles[index];
                if (rectangle.x != runStart || rectangle.width != runWidth || rectangle.yMax != row)
                    continue;

                rectangles[index] = new RectInt(
                    rectangle.x,
                    rectangle.y,
                    rectangle.width,
                    rectangle.height + 1);
                return;
            }

            rectangles.Add(new RectInt(runStart, row, runWidth, 1));
        }

        private static string BuildNormalizedShapeKey(
            List<Vector2Int> cells,
            Vector2Int minimum)
        {
            List<Vector2Int> normalizedCells = new List<Vector2Int>(cells.Count);
            for (int index = 0; index < cells.Count; index++)
                normalizedCells.Add(cells[index] - minimum);

            normalizedCells.Sort((left, right) =>
            {
                int rowComparison = left.y.CompareTo(right.y);
                return rowComparison != 0 ? rowComparison : left.x.CompareTo(right.x);
            });

            List<string> entries = new List<string>(normalizedCells.Count);
            for (int index = 0; index < normalizedCells.Count; index++)
                entries.Add(normalizedCells[index].x + "," + normalizedCells[index].y);

            return string.Join(";", entries);
        }

        private static void CreateLegacyObstacle(GravityLevelDefinition level, ObstacleDefinition obstacle)
        {
            Vector2Int fineSize = obstacle.quarterTurns % 2 == 0
                ? obstacle.sizeInFineCells
                : new Vector2Int(obstacle.sizeInFineCells.y, obstacle.sizeInFineCells.x);
            Vector2 worldSize = (Vector2)fineSize / level.subdivisions;
            PrototypeBootstrap.CreateStaticBlock(
                obstacle.name,
                CellWorldPosition(level, obstacle.centreCell),
                worldSize,
                ResolveStaticBlockColor(obstacle.color),
                false,
                configuredBoardPresentationConfig != null
                    ? configuredBoardPresentationConfig.BoardBlockMaterial
                    : null,
                configuredBoardPresentationConfig != null
                    ? configuredBoardPresentationConfig.StaticBlockSprite
                    : null);
        }

        private static Color ResolveStaticBlockColor(Color authoredColor)
        {
            return configuredBoardPresentationConfig != null
                ? configuredBoardPresentationConfig.StaticBlockTint
                : authoredColor;
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

}
