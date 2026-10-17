using System;
using System.Collections.Generic;
using System.IO;
using GravityPuzzle.Bootstrap;
using GravityPuzzle.Config;
using GravityPuzzle.Presentation.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GravityPuzzle.Editor
{
    public static class LevelRuntimeRootAuthoring
    {
        private const string PrefabPath = "Assets/Prefabs/LevelRuntimeRoot.prefab";
        private const string SceneTunaBoardFramePrefabPath = "Assets/Prefabs/SceneTunaBoardFrame.prefab";
        private const string FallbackSpritePath = "Assets/Sprites/Generated/LevelRuntimeSquare.png";
        private const int MinimumVisualCapacity = 512;
        private const int MinimumMapCollisionCapacity = 256;
        private const int MinimumObstacleCollisionCapacity = 64;
        private const int CapacitySafetyMargin = 32;
        private const int MaximumCapacityAttempts = 5;

        [MenuItem("Gravity Puzzle/Level Authoring/Rebuild Level Runtime Root")]
        public static void RebuildLevelRuntimeRoot()
        {
            RuntimePieceFactoryBootstrap bootstrap = FindBootstrapInActiveScene();
            if (bootstrap == null)
            {
                Debug.LogError("[LevelAuthoring] The active scene has no RuntimePieceFactoryBootstrap.");
                return;
            }

            SerializedObject bootstrapObject = new SerializedObject(bootstrap);
            GravityLevelSequence sequence = bootstrapObject.FindProperty("levelSequence").objectReferenceValue
                as GravityLevelSequence;
            BoardPresentationConfig presentationConfig = bootstrapObject.FindProperty("boardPresentationConfig")
                .objectReferenceValue as BoardPresentationConfig;
            if (sequence == null || sequence.levels == null || sequence.levels.Count == 0)
            {
                Debug.LogError("[LevelAuthoring] RuntimePieceFactoryBootstrap needs a non-empty Level Sequence.", bootstrap);
                return;
            }

            Sprite fallbackSprite = LoadOrCreateFallbackSprite();
            EstimateCapacities(
                sequence.levels,
                out int visualCapacity,
                out int obstacleCapacity,
                out int mapCapacity,
                out int frameModuleCapacity);

            GameObject template = null;
            LevelRuntimeRoot root = null;
            for (int attempt = 0; attempt < MaximumCapacityAttempts; attempt++)
            {
                if (template != null)
                    Object.DestroyImmediate(template);

                template = CreateTemplate(
                    visualCapacity,
                    obstacleCapacity,
                    mapCapacity,
                    frameModuleCapacity,
                    fallbackSprite,
                    out root);

                try
                {
                    ValidateEveryLevel(
                        sequence.levels,
                        presentationConfig,
                        root,
                        out int peakVisualCount,
                        out int peakObstacleCount,
                        out int peakMapCount,
                        out int peakFrameModuleCount);
                    visualCapacity = peakVisualCount + CapacitySafetyMargin;
                    obstacleCapacity = peakObstacleCount + CapacitySafetyMargin;
                    mapCapacity = peakMapCount + CapacitySafetyMargin;
                    frameModuleCapacity = peakFrameModuleCount + CapacitySafetyMargin;

                    Object.DestroyImmediate(template);
                    template = CreateTemplate(
                        visualCapacity,
                        obstacleCapacity,
                        mapCapacity,
                        frameModuleCapacity,
                        fallbackSprite,
                        out root);
                    ValidateEveryLevel(
                        sequence.levels,
                        presentationConfig,
                        root,
                        out _,
                        out _,
                        out _,
                        out _);
                    break;
                }
                catch (InvalidOperationException exception)
                {
                    if (attempt == MaximumCapacityAttempts - 1)
                    {
                        Object.DestroyImmediate(template);
                        throw;
                    }

                    visualCapacity *= 2;
                    obstacleCapacity *= 2;
                    mapCapacity *= 2;
                    frameModuleCapacity *= 2;
                    Debug.LogWarning(
                        $"[LevelAuthoring] Expanding LevelRuntimeRoot capacities after validation: {exception.Message}");
                }
            }

            GravityLevelDefinition previewLevel = FirstValidLevel(sequence.levels);
            GravityLevelRuntime.BuildAuthoredGeometryPreview(previewLevel, presentationConfig, root);
            PrefabUtility.SaveAsPrefabAsset(template, PrefabPath);
            Object.DestroyImmediate(template);

            ReplaceSceneInstance(bootstrap, bootstrapObject);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

            Debug.Log(
                $"[LevelAuthoring] LevelRuntimeRoot authored with {visualCapacity} visual, " +
                $"{frameModuleCapacity} Scene Tuna frame modules, {obstacleCapacity} obstacle-collision " +
                $"and {mapCapacity} map-collision slots.",
                bootstrap);
        }

        private static GameObject CreateTemplate(
            int visualCapacity,
            int obstacleCapacity,
            int mapCapacity,
            int frameModuleCapacity,
            Sprite fallbackSprite,
            out LevelRuntimeRoot root)
        {
            GameObject template = new GameObject("Level Runtime Root");
            root = template.AddComponent<LevelRuntimeRoot>();

            Transform visualSlotRoot = CreateChild(template.transform, "Unused Visual Slots");
            Transform backgroundRoot = CreateChild(template.transform, "Board Background");
            Transform frameRoot = CreateChild(template.transform, "Composite Board Frame");
            FrameAuthoringSlots frameSlots = CreateSceneTunaFrameModules(
                frameRoot,
                frameModuleCapacity);
            Transform obstacleVisualRoot = CreateChild(template.transform, "Obstacle Visuals");
            Transform obstacleCollisionRoot = CreateChild(template.transform, "Obstacle Collisions");
            Transform mapCollisionRoot = CreateChild(template.transform, "Map Collision - Obstacle Boxes");

            Rigidbody2D mapBody = mapCollisionRoot.gameObject.AddComponent<Rigidbody2D>();
            mapBody.bodyType = RigidbodyType2D.Static;
            CompositeCollider2D composite = mapCollisionRoot.gameObject.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Synchronous;

            SpriteRenderer[] visualSlots = new SpriteRenderer[visualCapacity];
            for (int index = 0; index < visualSlots.Length; index++)
            {
                GameObject slot = new GameObject($"Visual Slot {index + 1:0000}");
                slot.transform.SetParent(visualSlotRoot, false);
                SpriteRenderer renderer = slot.AddComponent<SpriteRenderer>();
                renderer.enabled = false;
                visualSlots[index] = renderer;
            }

            BoxCollider2D[] obstacleSlots = CreateColliderSlots(
                obstacleCollisionRoot,
                "Obstacle Collision Slot",
                obstacleCapacity);
            BoxCollider2D[] mapSlots = CreateColliderSlots(
                mapCollisionRoot,
                "Map Collision Slot",
                mapCapacity);

            root.ConfigureAuthoredSlots(
                visualSlotRoot,
                backgroundRoot,
                frameRoot,
                obstacleVisualRoot,
                obstacleCollisionRoot,
                mapCollisionRoot,
                visualSlots,
                frameSlots.Pools,
                frameSlots.Backdrop,
                obstacleSlots,
                mapSlots,
                mapBody,
                composite);
            root.ConfigureFallbackSprite(fallbackSprite);
            return template;
        }

        private static FrameAuthoringSlots CreateSceneTunaFrameModules(
            Transform frameRoot,
            int requiredCapacity)
        {
            EnsureSceneTunaFramePrefabCapacity(requiredCapacity);
            GameObject framePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneTunaBoardFramePrefabPath);
            if (framePrefab == null)
            {
                Debug.LogWarning(
                    "[LevelAuthoring] SceneTunaBoardFrame prefab is missing; frame modules will use the legacy visual slots.");
                return new FrameAuthoringSlots(
                    Array.Empty<BoardFrameModulePool>(),
                    null);
            }

            GameObject frameInstance = PrefabUtility.InstantiatePrefab(framePrefab, frameRoot) as GameObject;
            if (frameInstance == null)
                throw new InvalidOperationException("[LevelAuthoring] Failed to nest SceneTunaBoardFrame in LevelRuntimeRoot.");

            frameInstance.name = "Scene Tuna Board Frame";
            BoardFrameModulePool[] pools = CreateFramePools(frameInstance.transform);
            SpriteRenderer backdrop = FindChildRenderer(frameInstance.transform, "Frame Glow Backdrop");
            return new FrameAuthoringSlots(pools, backdrop);
        }

        private static void EnsureSceneTunaFramePrefabCapacity(int requiredCapacity)
        {
            GameObject framePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneTunaBoardFramePrefabPath);
            if (framePrefab == null)
                return;

            GameObject contents = PrefabUtility.LoadPrefabContents(SceneTunaBoardFramePrefabPath);
            try
            {
                // Validation reports total modules across the four repeated-edge
                // roles.  Keep the prefab compact by translating that total into
                // a per-role capacity; validation retries if a particular edge
                // needs more slots than the estimate.
                int poolCapacity = Mathf.Max(8, Mathf.CeilToInt(requiredCapacity / 4f));
                BoardFrameModuleRole[] roles =
                {
                    BoardFrameModuleRole.TopLeftCorner,
                    BoardFrameModuleRole.TopRightCorner,
                    BoardFrameModuleRole.BottomLeftCorner,
                    BoardFrameModuleRole.BottomRightCorner,
                    BoardFrameModuleRole.TopEdgeModule,
                    BoardFrameModuleRole.BottomEdgeModule,
                    BoardFrameModuleRole.LeftEdgeModule,
                    BoardFrameModuleRole.RightEdgeModule
                };

                for (int roleIndex = 0; roleIndex < roles.Length; roleIndex++)
                {
                    BoardFrameModuleRole role = roles[roleIndex];
                    int roleCount = IsCornerRole(role) ? 1 : poolCapacity;
                    Transform roleRoot = FindOrCreateRoleRoot(contents.transform, role);
                    int existingCount = roleRoot.GetComponentsInChildren<SpriteRenderer>(true).Length;
                    for (int slotIndex = existingCount; slotIndex < roleCount; slotIndex++)
                    {
                        GameObject slot = new GameObject(roleCount == 1
                            ? role.ToString()
                            : $"{role} {slotIndex + 1:00}");
                        slot.transform.SetParent(roleRoot, false);
                        SpriteRenderer renderer = slot.AddComponent<SpriteRenderer>();
                        renderer.enabled = false;
                    }
                }

                EnsureBackdrop(contents.transform);
                PrefabUtility.SaveAsPrefabAsset(contents, SceneTunaBoardFramePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static BoardFrameModulePool[] CreateFramePools(Transform frameRoot)
        {
            BoardFrameModuleRole[] roles =
            {
                BoardFrameModuleRole.TopLeftCorner,
                BoardFrameModuleRole.TopRightCorner,
                BoardFrameModuleRole.BottomLeftCorner,
                BoardFrameModuleRole.BottomRightCorner,
                BoardFrameModuleRole.TopEdgeModule,
                BoardFrameModuleRole.BottomEdgeModule,
                BoardFrameModuleRole.LeftEdgeModule,
                BoardFrameModuleRole.RightEdgeModule
            };
            BoardFrameModulePool[] pools = new BoardFrameModulePool[roles.Length];
            for (int index = 0; index < roles.Length; index++)
            {
                Transform roleRoot = FindOrCreateRoleRoot(frameRoot, roles[index]);
                pools[index] = new BoardFrameModulePool(
                    roles[index],
                    roleRoot.GetComponentsInChildren<SpriteRenderer>(true));
            }

            return pools;
        }

        private static Transform FindOrCreateRoleRoot(Transform parent, BoardFrameModuleRole role)
        {
            string name = role + " Slots";
            for (int index = 0; index < parent.childCount; index++)
            {
                Transform child = parent.GetChild(index);
                if (child.name == name)
                    return child;
            }

            return CreateChild(parent, name);
        }

        private static SpriteRenderer EnsureBackdrop(Transform frameRoot)
        {
            SpriteRenderer existing = FindChildRenderer(frameRoot, "Frame Glow Backdrop");
            if (existing != null)
                return existing;

            GameObject backdrop = new GameObject("Frame Glow Backdrop");
            backdrop.transform.SetParent(frameRoot, false);
            SpriteRenderer renderer = backdrop.AddComponent<SpriteRenderer>();
            renderer.enabled = false;
            return renderer;
        }

        private static SpriteRenderer FindChildRenderer(Transform root, string name)
        {
            SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null && renderers[index].gameObject.name == name)
                    return renderers[index];
            }

            return null;
        }

        private static bool IsCornerRole(BoardFrameModuleRole role)
        {
            return role == BoardFrameModuleRole.TopLeftCorner ||
                   role == BoardFrameModuleRole.TopRightCorner ||
                   role == BoardFrameModuleRole.BottomLeftCorner ||
                   role == BoardFrameModuleRole.BottomRightCorner;
        }

        private readonly struct FrameAuthoringSlots
        {
            public readonly BoardFrameModulePool[] Pools;
            public readonly SpriteRenderer Backdrop;

            public FrameAuthoringSlots(BoardFrameModulePool[] pools, SpriteRenderer backdrop)
            {
                Pools = pools;
                Backdrop = backdrop;
            }
        }

        private static BoxCollider2D[] CreateColliderSlots(
            Transform parent,
            string name,
            int capacity)
        {
            BoxCollider2D[] slots = new BoxCollider2D[capacity];
            for (int index = 0; index < slots.Length; index++)
            {
                GameObject slot = new GameObject($"{name} {index + 1:0000}");
                slot.transform.SetParent(parent, false);
                BoxCollider2D collider = slot.AddComponent<BoxCollider2D>();
                collider.enabled = false;
                slots[index] = collider;
            }

            return slots;
        }

        private static void ValidateEveryLevel(
            IReadOnlyList<GravityLevelDefinition> levels,
            BoardPresentationConfig presentationConfig,
            LevelRuntimeRoot root,
            out int peakVisualCount,
            out int peakObstacleCount,
            out int peakMapCount,
            out int peakFrameModuleCount)
        {
            peakVisualCount = 0;
            peakObstacleCount = 0;
            peakMapCount = 0;
            peakFrameModuleCount = 0;
            for (int index = 0; index < levels.Count; index++)
            {
                GravityLevelDefinition level = levels[index];
                if (level != null)
                {
                    GravityLevelRuntime.BuildAuthoredGeometryPreview(level, presentationConfig, root);
                    peakVisualCount = Mathf.Max(peakVisualCount, root.UsedVisualSlotCount);
                    peakObstacleCount = Mathf.Max(
                        peakObstacleCount,
                        root.UsedObstacleCollisionSlotCount);
                    peakMapCount = Mathf.Max(peakMapCount, root.UsedMapCollisionSlotCount);
                    peakFrameModuleCount = Mathf.Max(
                        peakFrameModuleCount,
                        root.UsedFrameModuleSlotCount);
                }
            }
        }

        private static void ReplaceSceneInstance(
            RuntimePieceFactoryBootstrap bootstrap,
            SerializedObject bootstrapObject)
        {
            SerializedProperty rootProperty = bootstrapObject.FindProperty("levelRuntimeRoot");
            LevelRuntimeRoot previousRoot = rootProperty.objectReferenceValue as LevelRuntimeRoot;
            if (previousRoot != null)
                Undo.DestroyObjectImmediate(previousRoot.gameObject);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene()) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("[LevelAuthoring] Failed to instantiate LevelRuntimeRoot prefab.");

            Undo.RegisterCreatedObjectUndo(instance, "Create Level Runtime Root");
            rootProperty.objectReferenceValue = instance.GetComponent<LevelRuntimeRoot>();
            bootstrapObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrap);
        }

        private static RuntimePieceFactoryBootstrap FindBootstrapInActiveScene()
        {
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                RuntimePieceFactoryBootstrap bootstrap = roots[index]
                    .GetComponentInChildren<RuntimePieceFactoryBootstrap>(true);
                if (bootstrap != null)
                    return bootstrap;
            }

            return null;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static Sprite LoadOrCreateFallbackSprite()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(FallbackSpritePath);
            if (sprite != null)
                return sprite;

            string directory = Path.GetDirectoryName(FallbackSpritePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            File.WriteAllBytes(FallbackSpritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(FallbackSpritePath, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = AssetImporter.GetAtPath(FallbackSpritePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 1f;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(FallbackSpritePath);
        }

        private static GravityLevelDefinition FirstValidLevel(
            IReadOnlyList<GravityLevelDefinition> levels)
        {
            for (int index = 0; index < levels.Count; index++)
            {
                if (levels[index] != null)
                    return levels[index];
            }

            return null;
        }

        private static void EstimateCapacities(
            IReadOnlyList<GravityLevelDefinition> levels,
            out int visualCapacity,
            out int obstacleCapacity,
            out int mapCapacity,
            out int frameModuleCapacity)
        {
            visualCapacity = MinimumVisualCapacity;
            obstacleCapacity = MinimumObstacleCollisionCapacity;
            mapCapacity = MinimumMapCollisionCapacity;
            frameModuleCapacity = 0;

            for (int index = 0; index < levels.Count; index++)
            {
                GravityLevelDefinition level = levels[index];
                if (level == null)
                    continue;

                int boardAndLowerGridUpperBound = CountBoardVisuals(level) + level.boardColumns * 24;
                int frameUpperBound = (level.FineColumns + level.FineRows) * 2 + 24;
                frameModuleCapacity = Mathf.Max(frameModuleCapacity, frameUpperBound + CapacitySafetyMargin);
                int obstacleCellCount = CountObstacleCells(level);
                int obstacleVisualUpperBound = obstacleCellCount * 3 + level.obstacles.Count;
                visualCapacity = Mathf.Max(
                    visualCapacity,
                    boardAndLowerGridUpperBound + frameUpperBound +
                    obstacleVisualUpperBound + CapacitySafetyMargin);
                obstacleCapacity = Mathf.Max(
                    obstacleCapacity,
                    level.obstacles.Count + CapacitySafetyMargin);
                mapCapacity = Mathf.Max(
                    mapCapacity,
                    CountMapBlockingCells(level) + CapacitySafetyMargin);
            }
        }

        private static int CountBoardVisuals(GravityLevelDefinition level)
        {
            int count = 0;
            for (int boardY = 0; boardY < level.boardRows; boardY++)
            for (int boardX = 0; boardX < level.boardColumns; boardX++)
            {
                Vector2Int origin = new Vector2Int(boardX, boardY) * level.subdivisions;
                int activeFineCells = 0;
                for (int fineY = 0; fineY < level.subdivisions; fineY++)
                for (int fineX = 0; fineX < level.subdivisions; fineX++)
                {
                    if (IsFineCellActive(level, origin + new Vector2Int(fineX, fineY)))
                        activeFineCells++;
                }

                int fineCellCount = level.subdivisions * level.subdivisions;
                count += activeFineCells == fineCellCount ? 1 : activeFineCells;
            }

            return count;
        }

        private static int CountObstacleCells(GravityLevelDefinition level)
        {
            int count = 0;
            for (int index = 0; index < level.obstacles.Count; index++)
            {
                ObstacleDefinition obstacle = level.obstacles[index];
                if (!obstacle.usesGridCells)
                {
                    count++;
                    continue;
                }

                Vector2Int size = obstacle.sizeInGridCells;
                count += Mathf.Max(1, size.x) * Mathf.Max(1, size.y);
            }

            return count;
        }

        private static int CountMapBlockingCells(GravityLevelDefinition level)
        {
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            Vector2Int[] neighbours =
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.up,
                Vector2Int.down
            };
            for (int y = 0; y < level.FineRows; y++)
            for (int x = 0; x < level.FineColumns; x++)
            {
                Vector2Int activeCell = new Vector2Int(x, y);
                if (!IsFineCellActive(level, activeCell))
                    continue;

                for (int directionIndex = 0; directionIndex < neighbours.Length; directionIndex++)
                {
                    Vector2Int neighbour = activeCell + neighbours[directionIndex];
                    if (!IsFineCellActive(level, neighbour))
                        cells.Add(neighbour);
                }
            }

            return cells.Count;
        }

        private static bool IsFineCellActive(GravityLevelDefinition level, Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= level.FineColumns || cell.y >= level.FineRows)
                return false;
            if (level.inactiveFineCells != null && level.inactiveFineCells.Contains(cell))
                return false;

            Vector2Int boardCell = new Vector2Int(
                cell.x / level.subdivisions,
                cell.y / level.subdivisions);
            return level.inactiveBoardCells == null || !level.inactiveBoardCells.Contains(boardCell);
        }
    }
}
