using System.Collections;
using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Core.StateMachine;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Gravity;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Gameplay.Reveal;
using GravityPuzzle.Gameplay.Tutorial;
using GravityPuzzle.Infrastructure.Services;
using GravityPuzzle.Presentation.Views;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GravityPuzzle
{
    /// <summary>
    /// Builds our first playable test board when Play starts.
    /// Keeping this in code makes the prototype quick to reset and iterate on.
    /// </summary>
    public static class PrototypeBootstrap
    {
        internal const float ColliderCornerRadius = .025f;
        private static Sprite squareSprite;
        private static Sprite circleSprite;
        private static Camera sceneCamera;
        private static bool sceneStartupHandled;
        private static readonly List<Collider2D> authoredObstacleColliders = new List<Collider2D>();
        // Only static colliders whose occupied space is already represented by
        // blocked grid cells may constrain a temporary shredder physics island.
        // Pins remain outside this list because they have no blocked-grid model.
        private static readonly List<Collider2D> physicsIslandGridBlockerColliders =
            new List<Collider2D>();
        // This material gives casts/contact resolution a stable, non-bouncy surface.
        private static PhysicsMaterial2D puzzleContactMaterial;

        /// <summary>
        /// The authored gameplay camera supplied by the scene composition root.
        /// Runtime systems consume this cached reference instead of performing
        /// independent Camera.main lookups.
        /// </summary>
        internal static Camera SceneCamera => sceneCamera;

        /// <summary>
        /// Invoked by the scene composition root after inspector-provided configs and pools are ready.
        /// </summary>
        internal static void StartForActiveScene()
        {
            if (sceneStartupHandled)
                return;

            sceneStartupHandled = true;

            GravityLevelDefinition authoredLevel = GravityLevelRuntime.FindLevelToPlay();
            if (authoredLevel != null)
            {
                GravityLevelRuntime.ConsumePreviewLaunchRequest();
                StartAuthoredLevel(authoredLevel);
                return;
            }

            Debug.LogError("[Bootstrap] No authored level was resolved. Assign a populated Level Sequence to Runtime Piece Factory Bootstrap.");
        }

        internal static void ConfigureSceneCamera(Camera camera)
        {
            sceneCamera = camera;
            sceneStartupHandled = false;
            authoredObstacleColliders.Clear();
            physicsIslandGridBlockerColliders.Clear();
        }

        internal static void StartAuthoredLevel(GravityLevelDefinition level)
        {
            GravityLevelRuntime.Build(level);
            ConfigurePuzzleColliders();
        }

        private static void ConfigurePuzzleColliders()
        {
            EnsurePhysicsMaterials();
            EnsurePuzzleObstacleCollisionLayers();
            ConfigurePhysicsIslandGridBlockers();
            Physics2D.velocityIterations = Mathf.Max(Physics2D.velocityIterations, 10);
            Physics2D.positionIterations = Mathf.Max(Physics2D.positionIterations, 10);

            ApplyPieceMaterials();
            ApplyObstacleMaterials();
        }

        private static void EnsurePhysicsMaterials()
        {
            if (puzzleContactMaterial == null)
            {
                puzzleContactMaterial = new PhysicsMaterial2D("Puzzle Contact")
                {
                    friction = .4f,
                    bounciness = 0f
                };
            }
        }

        private static void EnsurePuzzleObstacleCollisionLayers()
        {
            int pieceLayer = LayerMask.NameToLayer("PuzzlePiece");
            int obstacleLayer = LayerMask.NameToLayer("Obstacle");

            // Projects without named custom layers keep Unity's Default layer; make
            // that pair explicit as well so a stale matrix cannot disable contacts.
            if (pieceLayer < 0 || obstacleLayer < 0)
            {
                Physics2D.IgnoreLayerCollision(0, 0, false);
                return;
            }

            Physics2D.IgnoreLayerCollision(pieceLayer, obstacleLayer, false);
            IReadOnlyList<PuzzlePiece> activePieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < activePieces.Count; index++)
            {
                PuzzlePiece piece = activePieces[index];
                if (piece == null)
                    continue;

                SetLayerRecursively(piece.gameObject, pieceLayer);
            }

            for (int index = 0; index < authoredObstacleColliders.Count; index++)
            {
                Collider2D collider = authoredObstacleColliders[index];
                if (collider != null)
                    SetLayerRecursively(collider.gameObject, obstacleLayer);
            }
        }

        private static void ApplyPieceMaterials()
        {
            IReadOnlyList<PuzzlePiece> activePieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < activePieces.Count; index++)
            {
                PuzzlePiece piece = activePieces[index];
                if (piece != null)
                    ApplyPieceMaterial(piece, puzzleContactMaterial);
            }
        }

        private static void ApplyObstacleMaterials()
        {
            for (int index = 0; index < authoredObstacleColliders.Count; index++)
            {
                Collider2D collider = authoredObstacleColliders[index];
                if (collider != null)
                    collider.sharedMaterial = puzzleContactMaterial;
            }
        }

        /// <summary>
        /// Registers a static collider whose geometry is backed by the board's
        /// blocked-cell model. GravityLevelRuntime calls this explicitly for
        /// its composite topology boundary; CreateStaticBlock registers authored
        /// grid obstacles on the same path.
        /// </summary>
        internal static void RegisterPhysicsIslandGridBlocker(Collider2D collider)
        {
            if (collider != null && !physicsIslandGridBlockerColliders.Contains(collider))
                physicsIslandGridBlockerColliders.Add(collider);
        }

        private static void ConfigurePhysicsIslandGridBlockers()
        {
            int blockerLayer = LayerMask.NameToLayer(ShredderPhysicsLayers.BlockerLayerName);
            if (blockerLayer < 0)
            {
                Debug.LogError(
                    $"[ShredderPhysicsIsland] Required layer '{ShredderPhysicsLayers.BlockerLayerName}' is missing. " +
                    "Grid-backed obstacle collision is unavailable for the temporary physics island.");
                return;
            }

            for (int index = 0; index < physicsIslandGridBlockerColliders.Count; index++)
            {
                Collider2D collider = physicsIslandGridBlockerColliders[index];
                if (collider == null)
                    continue;

                SetLayerRecursively(collider.gameObject, blockerLayer);
                collider.sharedMaterial = puzzleContactMaterial;
            }
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;
            foreach (Transform child in target.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private static void ApplyPieceMaterial(PuzzlePiece piece, PhysicsMaterial2D material)
        {
            foreach (Collider2D pieceCollider in piece.GetComponentsInChildren<Collider2D>())
                pieceCollider.sharedMaterial = material;
        }

        internal static void ConfigureCamera()
        {
            ConfigureCamera(5.4f, new Color(.06f, .07f, .14f));
        }

        internal static void ConfigureCamera(float orthographicSize, Color backgroundColor)
        {
            if (sceneCamera == null)
            {
                Debug.LogError("[Bootstrap] No gameplay camera was supplied by RuntimePieceFactoryBootstrap.");
                return;
            }

            sceneCamera.orthographic = true;
            sceneCamera.orthographicSize = orthographicSize;
            sceneCamera.transform.position = new Vector3(0f, 0f, -10f);
            sceneCamera.backgroundColor = backgroundColor;
        }

        internal static void CreateStaticBlock(
            string blockName,
            Vector2 position,
            Vector2 size,
            Color color,
            bool roundCorners = true,
            Material presentationMaterial = null,
            Sprite presentationSprite = null,
            bool renderVisual = true)
        {
            GameObject block = CreateVisualBlock(
                blockName,
                position,
                size,
                color,
                presentationMaterial,
                0,
                presentationSprite,
                presentationSprite != null,
                renderVisual);
            BoxCollider2D collider = block.AddComponent<BoxCollider2D>();
            if (roundCorners)
                RoundColliderCorners(collider, size);
            authoredObstacleColliders.Add(collider);
            RegisterPhysicsIslandGridBlocker(collider);
        }

        internal static void CreateStaticCircle(string obstacleName, Vector2 position, float radius, Color color)
        {
            GameObject obstacle = new GameObject(obstacleName);
            obstacle.transform.position = position;
            obstacle.transform.localScale = Vector3.one * (radius * 2f);

            SpriteRenderer renderer = obstacle.AddComponent<SpriteRenderer>();
            renderer.sprite = GetCircleSprite();
            renderer.color = color;

            CircleCollider2D collider = obstacle.AddComponent<CircleCollider2D>();
            authoredObstacleColliders.Add(collider);
        }

        internal static void RoundColliderCorners(BoxCollider2D collider, Vector2 size)
        {
            // Two perfectly sharp Box2D corners can interlock when a dragged
            // piece reaches the end of an obstacle. This sub-pixel rounding is
            // visually imperceptible but lets the contact normal turn smoothly.
            collider.edgeRadius = Mathf.Min(
                ColliderCornerRadius,
                Mathf.Min(size.x, size.y) * .25f);
        }

        internal static void RegisterAuthoredObstacleCollider(Collider2D collider)
        {
            if (collider != null && !authoredObstacleColliders.Contains(collider))
                authoredObstacleColliders.Add(collider);
        }

        internal static GameObject CreateVisualBlock(
            string blockName,
            Vector2 position,
            Vector2 size,
            Color color,
            Material presentationMaterial = null,
            int sortingOrder = 0,
            Sprite presentationSprite = null,
            bool fitSpriteToSize = false,
            bool renderVisual = true,
            bool sliceSpriteToSize = false,
            bool flipX = false,
            bool flipY = false)
        {
            GameObject block = new GameObject(blockName);
            block.transform.position = position;
            block.transform.localScale = new Vector3(size.x, size.y, 1f);

            SpriteRenderer renderer = block.AddComponent<SpriteRenderer>();
            renderer.sprite = presentationSprite != null ? presentationSprite : GetSquareSprite();
            renderer.color = color;
            renderer.sharedMaterial = presentationMaterial;
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = renderVisual;
            renderer.flipX = flipX;
            renderer.flipY = flipY;

            if (sliceSpriteToSize && presentationSprite != null)
            {
                // Preserve authored corners while allowing arbitrary solid
                // obstacle rectangles. Sprite border data owns the fixed
                // edge thickness; only the centre region stretches.
                block.transform.localScale = Vector3.one;
                renderer.drawMode = SpriteDrawMode.Sliced;
                renderer.size = size;
            }
            else if (fitSpriteToSize && presentationSprite != null)
            {
                Vector2 spriteSize = presentationSprite.bounds.size;
                if (spriteSize.x > 0f && spriteSize.y > 0f)
                {
                    block.transform.localScale = new Vector3(
                        size.x / spriteSize.x,
                        size.y / spriteSize.y,
                        1f);
                }
            }

            return block;
        }

        internal static Sprite GetSquareSprite()
        {
            if (squareSprite != null)
                return squareSprite;

            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            squareSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
            return squareSprite;
        }

        internal static Sprite GetCircleSprite()
        {
            if (circleSprite != null)
                return circleSprite;

            const int textureSize = 64;
            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;

            Vector2 centre = Vector2.one * ((textureSize - 1) * .5f);
            float radius = textureSize * .48f;

            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), centre);
                    float alpha = Mathf.Clamp01(radius - distance + 1f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            circleSprite = Sprite.Create(
                texture,
                new Rect(0, 0, textureSize, textureSize),
                new Vector2(.5f, .5f),
                textureSize);
            return circleSprite;
        }
    }

    /// <summary>
    /// Lightweight responsive home screen. It intentionally uses generated UI
    /// textures so the prototype has no external asset or Canvas dependency.
    /// </summary>
    public sealed class GravityMainMenu : MonoBehaviour
    {
        private GravityLevelDefinition level;
        private int levelNumber;
        private bool settingsOpen;
        private bool soundEnabled = true;
        private bool hapticsEnabled = true;
        private bool startingLevel;

        private Texture2D backgroundTexture;
        private Texture2D cardTexture;
        private Texture2D pillTexture;
        private Texture2D greenButtonTexture;
        private Texture2D greenButtonHoverTexture;
        private Texture2D purpleButtonTexture;
        private Texture2D profileTexture;
        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle pillStyle;
        private GUIStyle levelButtonStyle;
        private GUIStyle settingsButtonStyle;
        private GUIStyle cardStyle;
        private GUIStyle profileLabelStyle;

        public void Initialize(GravityLevelDefinition levelToPlay, int nextLevelNumber)
        {
            level = levelToPlay;
            levelNumber = Mathf.Max(1, nextLevelNumber);
        }

        private void OnGUI()
        {
            if (level == null || startingLevel)
                return;

            EnsureStyles();
            float scale = Mathf.Clamp(
                Mathf.Min(Screen.width / 390f, Screen.height / 844f),
                .65f,
                1.6f);
            UpdateFontSizes(scale);

            Rect safe = Screen.safeArea;
            float safeTop = Screen.height - safe.yMax;
            float margin = 16f * scale;
            float top = safeTop + margin;

            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), backgroundTexture);

            float profileSize = 64f * scale;
            Rect profileRect = new Rect(safe.xMin + margin, top, profileSize, profileSize);
            GUI.DrawTexture(profileRect, profileTexture, ScaleMode.ScaleToFit, true);
            GUI.Label(
                new Rect(profileRect.x - 6f * scale, profileRect.yMax + 2f * scale,
                    profileRect.width + 12f * scale, 20f * scale),
                "PLAYER",
                profileLabelStyle);

            float pillHeight = 42f * scale;
            float firstPillX = profileRect.xMax + 12f * scale;
            GUI.Box(new Rect(firstPillX, top + 4f * scale, 100f * scale, pillHeight), "HP  5 / 5", pillStyle);
            GUI.Box(new Rect(firstPillX + 108f * scale, top + 4f * scale, 112f * scale, pillHeight), "GOLD  2000", pillStyle);

            Rect settingsRect = new Rect(
                safe.xMax - margin - 48f * scale,
                top,
                48f * scale,
                48f * scale);
            if (GUI.Button(settingsRect, "SET", settingsButtonStyle))
                settingsOpen = !settingsOpen;

            float cardWidth = Mathf.Min(330f * scale, Screen.width - margin * 2f);
            float cardHeight = 360f * scale;
            Rect card = new Rect(
                (Screen.width - cardWidth) * .5f,
                Mathf.Max(top + 110f * scale, Screen.height * .24f),
                cardWidth,
                cardHeight);
            GUI.Box(card, GUIContent.none, cardStyle);

            GUI.Label(
                new Rect(card.x + 20f * scale, card.y + 34f * scale,
                    card.width - 40f * scale, 100f * scale),
                "GRAVITY\nPUZZLE",
                titleStyle);
            GUI.Label(
                new Rect(card.x + 28f * scale, card.y + 142f * scale,
                    card.width - 56f * scale, 54f * scale),
                "Move the pieces, escape the map,\nand clear every puzzle.",
                subtitleStyle);

            // Keep the primary action large and centred in the usable screen,
            // independent of the decorative card's position or phone aspect.
            float safeCentreY = safeTop + safe.height * .5f;
            float levelButtonWidth = Mathf.Min(310f * scale, safe.width - margin * 2f);
            float levelButtonHeight = 96f * scale;
            Rect levelButton = new Rect(
                safe.center.x - levelButtonWidth * .5f,
                safeCentreY - levelButtonHeight * .5f,
                levelButtonWidth,
                levelButtonHeight);
            if (GUI.Button(levelButton, $"LEVEL {levelNumber}", levelButtonStyle))
                StartLevel();

            if (settingsOpen)
                DrawSettingsPanel(settingsRect, scale, safe);
        }

        private void DrawSettingsPanel(Rect settingsButton, float scale, Rect safe)
        {
            float width = 190f * scale;
            Rect panel = new Rect(
                Mathf.Max(safe.xMin + 8f * scale, settingsButton.xMax - width),
                settingsButton.yMax + 10f * scale,
                width,
                154f * scale);
            GUI.Box(panel, "SETTINGS", cardStyle);

            Rect soundButton = new Rect(
                panel.x + 18f * scale,
                panel.y + 46f * scale,
                panel.width - 36f * scale,
                40f * scale);
            if (GUI.Button(soundButton, $"SOUND: {(soundEnabled ? "ON" : "OFF")}", settingsButtonStyle))
                soundEnabled = !soundEnabled;

            Rect hapticsButton = new Rect(
                soundButton.x,
                soundButton.yMax + 10f * scale,
                soundButton.width,
                soundButton.height);
            if (GUI.Button(hapticsButton, $"HAPTICS: {(hapticsEnabled ? "ON" : "OFF")}", settingsButtonStyle))
                hapticsEnabled = !hapticsEnabled;
        }

        private void StartLevel()
        {
            if (startingLevel)
                return;

            startingLevel = true;
            PrototypeBootstrap.StartAuthoredLevel(level);
            // The legacy IMGUI menu is scene-owned. Disable it after selection so
            // the authored level can take over without a runtime Destroy call.
            gameObject.SetActive(false);
        }

        private void EnsureStyles()
        {
            if (backgroundTexture != null)
                return;

            backgroundTexture = CreateSolidTexture(new Color(.075f, .035f, .16f));
            cardTexture = CreateRoundedTexture(64, 15, new Color(.14f, .075f, .27f, .98f));
            pillTexture = CreateRoundedTexture(64, 22, new Color(.24f, .12f, .39f, .98f));
            greenButtonTexture = CreateRoundedTexture(64, 18, new Color(.16f, .78f, .28f));
            greenButtonHoverTexture = CreateRoundedTexture(64, 18, new Color(.24f, .92f, .38f));
            purpleButtonTexture = CreateRoundedTexture(64, 18, new Color(.37f, .12f, .68f));
            profileTexture = CreateProfileTexture(96);

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            titleStyle.normal.textColor = Color.white;

            subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            subtitleStyle.normal.textColor = new Color(.86f, .79f, 1f);

            pillStyle = CreateTexturedStyle(pillTexture, TextAnchor.MiddleCenter);
            levelButtonStyle = CreateTexturedStyle(greenButtonTexture, TextAnchor.MiddleCenter);
            levelButtonStyle.hover.background = greenButtonHoverTexture;
            levelButtonStyle.active.background = greenButtonHoverTexture;
            settingsButtonStyle = CreateTexturedStyle(purpleButtonTexture, TextAnchor.MiddleCenter);
            cardStyle = CreateTexturedStyle(cardTexture, TextAnchor.UpperCenter);
            cardStyle.padding = new RectOffset(12, 12, 13, 10);
            profileLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            profileLabelStyle.normal.textColor = new Color(1f, .82f, .2f);
        }

        private void UpdateFontSizes(float scale)
        {
            titleStyle.fontSize = Mathf.RoundToInt(34f * scale);
            subtitleStyle.fontSize = Mathf.RoundToInt(15f * scale);
            pillStyle.fontSize = Mathf.RoundToInt(14f * scale);
            levelButtonStyle.fontSize = Mathf.RoundToInt(30f * scale);
            settingsButtonStyle.fontSize = Mathf.RoundToInt(12f * scale);
            cardStyle.fontSize = Mathf.RoundToInt(17f * scale);
            profileLabelStyle.fontSize = Mathf.RoundToInt(11f * scale);
        }

        private static GUIStyle CreateTexturedStyle(Texture2D texture, TextAnchor alignment)
        {
            GUIStyle style = new GUIStyle(GUI.skin.button)
            {
                alignment = alignment,
                fontStyle = FontStyle.Bold,
                border = new RectOffset(18, 18, 18, 18)
            };
            style.normal.background = texture;
            style.hover.background = texture;
            style.active.background = texture;
            style.normal.textColor = Color.white;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            return style;
        }

        private static Texture2D CreateSolidTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.hideFlags = HideFlags.DontSave;
            return texture;
        }

        private static Texture2D CreateRoundedTexture(int size, int radius, Color color)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nearestX = Mathf.Clamp(x, radius, size - 1 - radius);
                float nearestY = Mathf.Clamp(y, radius, size - 1 - radius);
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(nearestX, nearestY));
                Color pixel = color;
                pixel.a *= Mathf.Clamp01(radius - distance + 1f);
                texture.SetPixel(x, y, pixel);
            }

            texture.Apply();
            texture.hideFlags = HideFlags.DontSave;
            return texture;
        }

        private static Texture2D CreateProfileTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            Vector2 centre = Vector2.one * ((size - 1) * .5f);
            float outerRadius = size * .48f;
            float faceRadius = size * .39f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 point = new Vector2(x, y);
                float distance = Vector2.Distance(point, centre);
                Color pixel = Color.clear;
                if (distance <= outerRadius)
                    pixel = distance > faceRadius
                        ? new Color(1f, .72f, .12f)
                        : new Color(.18f, .78f, .63f);

                float eyeY = centre.y + size * .08f;
                bool leftEye = Vector2.Distance(point, new Vector2(centre.x - size * .13f, eyeY)) < size * .045f;
                bool rightEye = Vector2.Distance(point, new Vector2(centre.x + size * .13f, eyeY)) < size * .045f;
                bool smile = y < centre.y - size * .08f && y > centre.y - size * .14f &&
                             Mathf.Abs(x - centre.x) < size * .14f;
                if (distance <= faceRadius && (leftEye || rightEye || smile))
                    pixel = new Color(.08f, .05f, .16f);

                texture.SetPixel(x, y, pixel);
            }

            texture.Apply();
            texture.hideFlags = HideFlags.DontSave;
            return texture;
        }
    }

    /// <summary>
    /// Removes pieces that fall through the open bottom and detects the win state.
    /// </summary>
    public sealed class PrototypeBoard : MonoBehaviour
    {
        public static PrototypeBoard Active { get; private set; }

        private const float NextLevelDelay = 1.25f;
        private float finalShredderGraceSeconds = 1f;
        private float removalHeight = -5.5f;
        private bool finalShredderOutcomeLocked;
        private bool boardCleared;
        private bool boardClearCompletionPending;
        private bool boardFailed;
        private float bombElapsedSeconds;
        private bool sequentialLevelsEnabled = true;
        private bool awaitingBoosterRewardDismissal;
        private BoosterRewardConfig pendingBoosterRewardConfig;
        private BoosterFirstUseTutorialCoordinator boosterFirstUseTutorial;
        private readonly HashSet<object> timerPauseOwners = new HashSet<object>();
        private readonly Dictionary<string, IRevealAreaPresentation> revealPresentations =
            new Dictionary<string, IRevealAreaPresentation>();
        // Board-owned runtime state for combined-piece edges. The serialized
        // level only provides joints; movement groups are derived here.
        private readonly PieceJointGraph pieceJointGraph = new PieceJointGraph();
        private readonly List<int> jointGroupMemberIds = new List<int>();
        private readonly List<PieceModel> jointGroupModels = new List<PieceModel>();
        private readonly List<GridCoordinate> jointGroupTargetAnchors = new List<GridCoordinate>();
        private readonly List<PuzzlePiece> jointGroupPieceBuffer = new List<PuzzlePiece>();
        private readonly List<PieceJointGraph.Joint> jointBuffer = new List<PieceJointGraph.Joint>();
        private int jointBlockSize = GravityGridMetrics.FineCellsPerGridCell;
        private RevealAreaCoordinator revealAreaCoordinator;
        private readonly StateMachine<GameState> gameStateMachine = new StateMachine<GameState>(
            GameState.Initialize,
            GameStateTransitionRules.Create());

        public static event System.Action OnLevelCleared;
        public event System.Action<GameState, GameState> GameStateChanged;
        public event System.Action<int, PieceState, PieceState> PieceStateChanged;
        public event System.Action<int> BlockShredded;
        public event System.Action BoardOccupancyChanged;
        public event System.Action LevelCleared;
        public event System.Action LevelFailed;

        [Header("Win UI Scene Configuration")]
        [Tooltip("If set, this scene will be loaded when the level is cleared instead of the default auto-reload behavior.")]
        public string winSceneName = "";

        [Tooltip("Canvas altında bulunan, level tamamlandığında gösterilecek win paneli.")]
        [SerializeField] private GameObject winPanel;

        [Tooltip("Win Panel içindeki, sonraki leveli yükleyecek buton.")]
        [SerializeField] private UnityEngine.UI.Button nextLevelButton;

        [Tooltip("Next Level Button içindeki, sıradaki level numarasını gösteren TMP metni.")]
        [SerializeField] private TMP_Text nextLevelButtonLabel;

        [Tooltip("Win Panel içindeki, tamamlanan levelin coin ödülünü gösteren TMP metni.")]
        [SerializeField] private TMP_Text winPanelCoinAmountText;

        [Tooltip("Win Panel içindeki, tamamlanan campaign levelini gösteren TMP metni.")]
        [SerializeField] private TMP_Text completedLevelText;

        [Header("Win Audio")]
        [Tooltip("Audio clip played when the win panel opens (e.g. Level Up Fanfare).")]
        [SerializeField] private AudioClip winFanfareAudioClip;
        [SerializeField] private AudioSource winAudioSource;

        [Header("Booster Reward Unlock UI")]
        [SerializeField] private NewBoosterPanelView newBoosterPanel;
        [SerializeField] private BoosterRewardConfig[] boosterRewardConfigs;
        [SerializeField] private GameObject winVfx;
        private ParticleSystem[] winVfxParticleSystems;

        [Header("Level Clear Effects")]
        [Tooltip("Scene instance of the first one-shot particle effect to play when the level is cleared.")]
        [SerializeField] private ParticleSystem levelClearParticleEffectA;

        [Tooltip("Scene instance of the second one-shot particle effect to play when the level is cleared.")]
        [SerializeField] private ParticleSystem levelClearParticleEffectB;

        [Tooltip("Scene instance of an additional one-shot particle effect to play when the level is cleared.")]
        [SerializeField] private ParticleSystem levelClearParticleEffectC;

        [Tooltip("Scene instance of an additional one-shot particle effect to play when the level is cleared.")]
        [SerializeField] private ParticleSystem levelClearParticleEffectD;

        [Tooltip("Scene instance of an additional one-shot particle effect to play when the level is cleared.")]
        [SerializeField] private ParticleSystem levelClearParticleEffectE;

        [Tooltip("Scene instance of an additional one-shot particle effect to play when the level is cleared.")]
        [SerializeField] private ParticleSystem levelClearParticleEffectF;

        [Tooltip("Scene instance of an additional one-shot particle effect to play when the level is cleared.")]
        [SerializeField] private ParticleSystem levelClearParticleEffectG;

        public float TimeLimit { get; private set; }
        public float TimeRemaining { get; private set; }
        public int DestroyedPieceCount { get; private set; }
        public bool IsTimerStarted { get; private set; }
        public bool IsTimerActive => TimeLimit > 0f && IsTimerStarted && !boardCleared && !boardFailed;
        public bool IsTimerPaused => timerPauseOwners.Count > 0;
        public bool IsLevelRunning => GameState == GameState.Ready || GameState == GameState.Playing;
        public bool HasFailed => boardFailed;
        public bool WasTimerExpiryFailure { get; private set; }
        public bool HasUsedTimerExpiryContinue { get; private set; }
        public GameState GameState => gameStateMachine.Current;
        public LevelBoardSnapshot BoardSnapshot { get; private set; }
        public PieceJointGraph PieceJointGraph => pieceJointGraph;
        public Presentation.Views.JointSpringPresenter JointSpringPresenter { get; set; }

        private System.Func<PieceModel, GridCoordinate, bool> cachedCanMovePieceDelegate;
        public System.Func<PieceModel, GridCoordinate, bool> CanMovePieceDelegate =>
            cachedCanMovePieceDelegate ?? (cachedCanMovePieceDelegate = CanMovePiece);

        public bool CanMovePiece(PieceModel piece, GridCoordinate targetAnchor)
        {
            return revealAreaCoordinator == null || revealAreaCoordinator.CanMovePiece(piece, targetAnchor);
        }

        public bool CanPieceParticipateInGravity(PieceModel piece)
        {
            return revealAreaCoordinator == null || revealAreaCoordinator.CanParticipateInGravity(piece);
        }

        public bool CanUseTargetedBooster(PuzzlePiece piece)
        {
            return piece != null && TryGetPieceModel(piece, out PieceModel model) &&
                   (revealAreaCoordinator == null || revealAreaCoordinator.CanUseTargetedBooster(model));
        }

        public bool TryCancelPartialBoxContentDrag(PuzzlePiece piece)
        {
            return revealAreaCoordinator != null && revealAreaCoordinator.TryCancelPartialBoxContentDrag(piece);
        }

        public void WakeUpGravity()
        {
            PuzzleDragController.WakeUpGravity();
        }

        public void ConfigureRevealAreas(RevealAreaCoordinator coordinator)
        {
            if (revealAreaCoordinator != null)
            {
                BlockShredded -= revealAreaCoordinator.NotifyPieceShredded;
                BoardOccupancyChanged -= revealAreaCoordinator.NotifyBoardChanged;
            }

            revealAreaCoordinator = coordinator;
            if (revealAreaCoordinator != null)
            {
                BlockShredded += revealAreaCoordinator.NotifyPieceShredded;
                BoardOccupancyChanged += revealAreaCoordinator.NotifyBoardChanged;
            }
            foreach (KeyValuePair<string, IRevealAreaPresentation> entry in revealPresentations)
                revealAreaCoordinator?.RegisterPresentation(entry.Key, entry.Value);
        }

        public void RegisterRevealPresentation(string areaId, IRevealAreaPresentation presentation)
        {
            if (string.IsNullOrWhiteSpace(areaId) || presentation == null)
                return;

            revealPresentations[areaId] = presentation;
            revealAreaCoordinator?.RegisterPresentation(areaId, presentation);
        }

        public bool HasRevealPresentation(string areaId)
        {
            return !string.IsNullOrWhiteSpace(areaId) &&
                   revealPresentations.ContainsKey(areaId);
        }

        /// <summary>Called after regular level roots have been created and synchronized.</summary>
        public void InitializeRevealAreas(GravityLevelDefinition level)
        {
            revealAreaCoordinator?.Initialize(this, level);
        }

        /// <summary>
        /// Ensures all authored bomb pieces have their countdown visuals and overlay
        /// rendered immediately on level open before any player action.
        /// </summary>
        public void RefreshInitialBombTimers()
        {
            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            for (int i = 0; i < pieces.Count; i++)
            {
                PuzzlePiece piece = pieces[i];
                if (piece != null && piece.IsBomb && !piece.IsBombDefused &&
                    !piece.IsShredderInteractionLocked)
                {
                    piece.RefreshBombState();
                }
            }
        }

        private void Awake()
        {
            if (winPanel != null)
                winPanel.SetActive(false);

            // Win VFX lives below the artist-authored result UI. Cache it at
            // composition time and explicitly stop it so Play On Awake cannot
            // consume its one-shot before the level is actually complete.
            if (winVfx != null)
            {
                winVfxParticleSystems = winVfx.GetComponentsInChildren<ParticleSystem>(true);
                StopParticleEffects(winVfxParticleSystems);
                winVfx.SetActive(false);
            }

            if (nextLevelButton != null)
                nextLevelButton.onClick.AddListener(LoadNextLevelFromWinPanel);
            if (winAudioSource == null)
                winAudioSource = GetComponent<AudioSource>();
#if UNITY_EDITOR
            if (winFanfareAudioClip == null)
                winFanfareAudioClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/Level Up Fanfare.mp3");
#endif
            boosterFirstUseTutorial = new BoosterFirstUseTutorialCoordinator(this);
            BoosterFirstUseTutorialRuntime.Configure(boosterFirstUseTutorial);
            if (newBoosterPanel != null)
                newBoosterPanel.Claimed += ClaimQueuedBoosterReward;
        }

        private void OnEnable()
        {
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this)
                Active = null;
        }

        private void OnDestroy()
        {
            if (nextLevelButton != null)
                nextLevelButton.onClick.RemoveListener(LoadNextLevelFromWinPanel);
            if (newBoosterPanel != null)
                newBoosterPanel.Claimed -= ClaimQueuedBoosterReward;
        }

        public void SetTimeLimit(float timeLimit)
        {
            TimeLimit = timeLimit;
            TimeRemaining = timeLimit;
            IsTimerStarted = false;
            DestroyedPieceCount = 0;
            boardClearCompletionPending = false;
            timerPauseOwners.Clear();
            finalShredderOutcomeLocked = false;
            WasTimerExpiryFailure = false;
            HasUsedTimerExpiryContinue = false;
            bombElapsedSeconds = 0f;
        }

        public void SetFinalShredderGraceSeconds(float seconds)
        {
            finalShredderGraceSeconds = Mathf.Max(0f, seconds);
        }

        /// <summary>
        /// Locks the result to a win when the final live piece reaches the
        /// shredder inside the configured final-seconds window. The allowance
        /// is an entry window, not a second timer running beside the shred
        /// animation: once earned, the feed and its progress voxels may finish.
        /// </summary>
        public void TryLockFinalShredderOutcome(PuzzlePiece shredderPiece)
        {
            if (shredderPiece == null ||
                TimeLimit <= 0f ||
                !IsTimerStarted ||
                TimeRemaining > finalShredderGraceSeconds)
                return;

            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            int livePieceCount = 0;
            for (int index = 0; index < pieces.Count; index++)
            {
                if (pieces[index] != null)
                    livePieceCount++;
            }

            if (livePieceCount == 1)
                finalShredderOutcomeLocked = true;
        }

        // Phase 2 keeps this snapshot parallel to the legacy physics runtime.
        // A later phase will make it the sole gameplay authority.
        public void InitializeBoardSnapshot(LevelBoardSnapshot snapshot)
        {
            BoardSnapshot = snapshot;
            TryTransitionGameState(GameState.Ready);
        }

        /// <summary>
        /// Resolves authored joint endpoints to the initial board model ids.
        /// Only active level pieces participate; editor validation prevents
        /// joints from crossing reveal-area ownership boundaries.
        /// </summary>
        public void ConfigurePieceJoints(GravityLevelDefinition level)
        {
            List<int> memberIds = new List<int>();
            List<PieceJointGraph.Joint> runtimeJoints = new List<PieceJointGraph.Joint>();
            if (level == null || BoardSnapshot == null || level.pieces == null)
            {
                pieceJointGraph.Configure(memberIds, runtimeJoints);
                return;
            }

            Dictionary<string, int> sourceIdByPieceId = new Dictionary<string, int>();
            Dictionary<string, PieceDefinition> definitionByPieceId = new Dictionary<string, PieceDefinition>();
            jointBlockSize = Mathf.Max(1, level.subdivisions);
            for (int index = 0; index < level.pieces.Count; index++)
            {
                if (!BoardSnapshot.TryGetPiece(index, out _) ||
                    level.pieces[index] == null)
                {
                    continue;
                }

                memberIds.Add(index);
                if (!string.IsNullOrEmpty(level.pieces[index].pieceId))
                {
                    sourceIdByPieceId[level.pieces[index].pieceId] = index;
                    definitionByPieceId[level.pieces[index].pieceId] = level.pieces[index];
                }
            }

            if (level.joints != null)
            {
                for (int index = 0; index < level.joints.Count; index++)
                {
                    JointDefinition joint = level.joints[index];
                    if (joint == null || joint.endpointA == null || joint.endpointB == null ||
                        !sourceIdByPieceId.TryGetValue(joint.endpointA.pieceId, out int memberA) ||
                        !sourceIdByPieceId.TryGetValue(joint.endpointB.pieceId, out int memberB) ||
                        !definitionByPieceId.TryGetValue(joint.endpointA.pieceId, out PieceDefinition definitionA) ||
                        !definitionByPieceId.TryGetValue(joint.endpointB.pieceId, out PieceDefinition definitionB) ||
                        !BoardSnapshot.TryGetPiece(memberA, out PieceModel modelA) ||
                        !BoardSnapshot.TryGetPiece(memberB, out PieceModel modelB) ||
                        !TryResolveJointLocalBlock(definitionA, modelA, joint.endpointA.localBlockOrigin, out GridCoordinate blockA) ||
                        !TryResolveJointLocalBlock(definitionB, modelB, joint.endpointB.localBlockOrigin, out GridCoordinate blockB) ||
                        memberA == memberB)
                    {
                        continue;
                    }

                    runtimeJoints.Add(new PieceJointGraph.Joint(
                        index,
                        memberA,
                        memberB,
                        blockA,
                        blockB));
                }
            }

            pieceJointGraph.Configure(memberIds, runtimeJoints);
        }

        public bool TryGetPieceModel(PuzzlePiece piece, out PieceModel model)
        {
            model = null;
            return piece != null &&
                   BoardSnapshot != null &&
                   BoardSnapshot.TryGetPiece(piece.SourcePieceId, out model);
        }

        public bool TrySetPieceState(PuzzlePiece piece, PieceState state)
        {
            if (!TryGetPieceModel(piece, out PieceModel model))
                return false;

            return TryTransitionPieceState(model, state);
        }

        public bool TryClearPieceFromGrid(PuzzlePiece piece, PieceState state)
        {
            if (!TryGetPieceModel(piece, out PieceModel model))
                return false;

            // State and occupancy must change as one logical transaction.
            // Clearing first used to leave a still-falling piece absent from
            // the authoritative grid when Dragging was rejected. Targeted
            // boosters then had no cell to resolve even though the piece was
            // still visible on the board.
            if (!TryTransitionPieceState(model, state))
                return false;

            BoardSnapshot.Grid.ClearPiece(model);
            return true;
        }

        /// <summary>
        /// Begins a drag without vacating the piece's grid cells. Unlike
        /// TryClearPieceFromGrid, the footprint stays Occupied for the whole
        /// drag so a falling piece above can never enter it. Movement during
        /// the drag goes through TryMoveIgnoringPiece, which already ignores
        /// this piece's own cells while checking each step.
        /// </summary>
        public bool TryBeginPieceDrag(PuzzlePiece piece)
        {
            if (!TryGetPieceModel(piece, out PieceModel model))
                return false;

            if (!TryCollectJointGroupModels(model.Id, jointGroupModels))
                return false;

            for (int index = 0; index < jointGroupModels.Count; index++)
            {
                if (jointGroupModels[index].State != PieceState.Placed &&
                    jointGroupModels[index].State != PieceState.Dragging)
                {
                    return false;
                }
            }

            for (int index = 0; index < jointGroupModels.Count; index++)
            {
                if (!TryTransitionPieceState(jointGroupModels[index], PieceState.Dragging))
                    return false;
            }

            revealAreaCoordinator?.NotifyPieceDragStarted(piece);
            return true;
        }

        /// <summary>
        /// Keeps a piece's current footprint occupied while it is travelling
        /// through the shredder.  Reserved cells block placement and grid
        /// gravity, but are released normally when the pooled piece despawns.
        /// </summary>
        public bool TryReservePieceInGrid(PuzzlePiece piece, PieceState state)
        {
            if (!TryGetPieceModel(piece, out PieceModel model))
                return false;

            bool wasAlreadyReserved = BoardSnapshot.Grid.IsFullyReservedBy(model);
            if (!BoardSnapshot.Grid.TryReserve(model))
                return false;

            if (TryTransitionPieceState(model, state))
                return true;

            // A rejected lifecycle transition must not leave an otherwise
            // playable piece behind as a permanent Reserved grid footprint.
            if (!wasAlreadyReserved)
                BoardSnapshot.Grid.TryRestoreReservedPiece(model);
            return false;
        }

        /// <summary>
        /// Atomically reserves a root and its board-derived support chain for
        /// the temporary shredder physics island. The grid remains authoritative:
        /// every footprint stays Reserved while Unity physics renders only the
        /// short contact reaction between these pieces.
        /// </summary>
        public bool TryReserveShredderPhysicsIsland(IReadOnlyList<PuzzlePiece> pieces)
        {
            if (BoardSnapshot == null || pieces == null || pieces.Count == 0)
                return false;

            List<PieceModel> models = new List<PieceModel>(pieces.Count);
            HashSet<int> ids = new HashSet<int>();
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                if (piece == null ||
                    !TryGetPieceModel(piece, out PieceModel model) ||
                    !ids.Add(model.Id) ||
                    !CanEnterPhysicsIsland(model.State))
                {
                    return false;
                }

                for (int cellIndex = 0; cellIndex < model.LocalCells.Count; cellIndex++)
                {
                    GridCoordinate coordinate = model.GetWorldCell(cellIndex);
                    if (!BoardSnapshot.Grid.TryGetOccupantId(coordinate, out int occupantId) ||
                        occupantId != model.Id)
                    {
                        return false;
                    }
                }

                models.Add(model);
            }

            // Prevalidation above makes these mutations all-or-nothing for a
            // stable board snapshot; no visual code is allowed between them.
            for (int index = 0; index < models.Count; index++)
            {
                if (!BoardSnapshot.Grid.TryReserve(models[index]) ||
                    !TryTransitionPieceState(models[index], PieceState.HandoffToPhysics))
                {
                    RollBackPartialShredderPhysicsIslandReservation(models);
                    Debug.LogError(
                        "[ShredderPhysicsIsland] Reservation transaction changed unexpectedly after prevalidation.",
                        this);
                    return false;
                }
            }

            BoardOccupancyChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Reverts a physics-island reservation before any member becomes a
        /// Dynamic Rigidbody or starts shred presentation. This explicit
        /// transaction recovery keeps board occupancy and lifecycle state in
        /// sync if an invariant is broken after preflight.
        /// </summary>
        public bool TryAbortShredderPhysicsIsland(IReadOnlyList<PuzzlePiece> pieces)
        {
            if (BoardSnapshot == null || pieces == null || pieces.Count == 0)
                return false;

            List<PieceModel> models = new List<PieceModel>(pieces.Count);
            HashSet<int> ids = new HashSet<int>();
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                if (piece == null ||
                    !TryGetPieceModel(piece, out PieceModel model) ||
                    !ids.Add(model.Id) ||
                    model.State != PieceState.HandoffToPhysics ||
                    !BoardSnapshot.Grid.IsFullyReservedBy(model))
                {
                    return false;
                }

                models.Add(model);
            }

            if (!RollBackPartialShredderPhysicsIslandReservation(models))
                return false;

            BoardOccupancyChanged?.Invoke();
            PuzzleDragController.WakeUpGravity();
            return true;
        }

        private bool RollBackPartialShredderPhysicsIslandReservation(
            IReadOnlyList<PieceModel> models)
        {
            if (models == null)
                return false;

            for (int index = 0; index < models.Count; index++)
            {
                PieceModel model = models[index];
                if (model == null)
                    return false;

                bool isReserved = BoardSnapshot.Grid.IsFullyReservedBy(model);
                if (!isReserved && model.State != PieceState.Placed)
                    return false;
            }

            for (int index = models.Count - 1; index >= 0; index--)
            {
                PieceModel model = models[index];
                if (!BoardSnapshot.Grid.IsFullyReservedBy(model))
                    continue;

                if (!BoardSnapshot.Grid.TryRestoreReservedPiece(model))
                    return false;

                if (model.State == PieceState.HandoffToPhysics &&
                    !TryTransitionPieceState(model, PieceState.Placed))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CanEnterPhysicsIsland(PieceState state)
        {
            // A grid gravity move commits its target before its view tween
            // reaches that target. Only a settled Placed model is therefore
            // safe to mirror into a temporary Dynamic Rigidbody.
            return state == PieceState.Placed;
        }

        /// <summary>
        /// Releases only the portion of a shredder reservation that has passed
        /// through the cutter. This keeps the visible upper portion authoritative
        /// until it is actually removed.
        /// </summary>
        public bool TryReleaseShredderGridCells(
            PuzzlePiece piece,
            IReadOnlyList<GridCoordinate> localCells)
        {
            if (!TryGetPieceModel(piece, out PieceModel model) ||
                !BoardSnapshot.Grid.TryReleaseReservedCells(model, localCells))
                return false;

            PuzzleDragController.WakeUpGravity();
            BoardOccupancyChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Restores the authoritative occupancy for a model that is still
        /// present in the snapshot but was temporarily cleared by an earlier
        /// drag or presentation transition. Targeted boosters use this small
        /// recovery transaction before acting, so their board decision remains
        /// model/grid based instead of falling back to collider hit testing.
        /// </summary>
        public bool TryRestorePieceGridOccupancy(PuzzlePiece piece)
        {
            if (!TryGetPieceModel(piece, out PieceModel model))
                return false;

            GridPlacementResult placement = BoardSnapshot.Grid.CheckPlacementIgnoringPiece(
                model,
                model.Anchor,
                model.Id);
            if (!placement.IsSuccess)
                return false;

            BoardSnapshot.Grid.ClearPiece(model);
            return BoardSnapshot.Grid.TryPlace(model);
        }

        /// <summary>
        /// Releases a runtime piece's occupied cells before its prefab root is
        /// returned to the typed pool.  A later rent can therefore never
        /// inherit a stale grid reservation from its previous lifetime.
        /// </summary>
        public bool TryDespawnPieceFromGrid(PuzzlePiece piece)
        {
            if (!TryGetPieceModel(piece, out PieceModel model))
                return false;

            BoardSnapshot.Grid.ClearPiece(model);
            bool despawned = TryTransitionPieceState(model, PieceState.Despawned);
            if (despawned)
                BoardOccupancyChanged?.Invoke();
            return despawned;
        }

        /// <summary>
        /// Replaces a damaged source model with the connected visual fragments
        /// created by the hammer. The grid is updated before gravity is woken,
        /// so fragments immediately block one another and every other piece.
        /// </summary>
        public bool TryRegisterHammerFragments(
            IReadOnlyList<PuzzlePiece> fragments,
            PieceState initialState)
        {
            if (BoardSnapshot == null || fragments == null || fragments.Count == 0)
                return false;

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null || !TryGetPieceModel(fragments[0], out PieceModel sourceModel))
                return false;

            // The retained runtime root keeps the source id.  Leaving the old
            // model in the snapshot and assigning every fragment a new id made
            // the grid and visual roots disagree after a hammer split.
            List<PieceModel> fragmentModels = new List<PieceModel>(fragments.Count);
            int nextId = BoardSnapshot.NextPieceId;
            for (int index = 0; index < fragments.Count; index++)
            {
                PuzzlePiece fragment = fragments[index];
                int fragmentId = index == 0
                    ? sourceModel.Id
                    : nextId + index - 1;
                if (fragment == null ||
                    !fragment.TryCreateGridModel(level, fragmentId, out PieceModel model))
                    return false;

                if (!TryTransitionPieceState(model, initialState))
                    return false;
                fragmentModels.Add(model);
            }

            BoardSnapshot.Grid.ClearPiece(sourceModel);
            int placedCount = 0;
            for (; placedCount < fragmentModels.Count; placedCount++)
            {
                if (BoardSnapshot.Grid.TryPlace(fragmentModels[placedCount]))
                    continue;

                for (int rollbackIndex = 0; rollbackIndex < placedCount; rollbackIndex++)
                    BoardSnapshot.Grid.ClearPiece(fragmentModels[rollbackIndex]);

                BoardSnapshot.Grid.TryPlace(sourceModel);
                Debug.LogWarning(
                    "[GridSplit] Hammer fragments could not be registered; restored the source grid model.",
                    this);
                return false;
            }

            // Replace the source entry first, then append only newly created
            // roots. This makes the snapshot's model id match the retained
            // PuzzlePiece and lets gravity resolve every remainder separately.
            if (!BoardSnapshot.TryReplacePlacedPiece(fragmentModels[0]))
            {
                for (int rollbackIndex = 0; rollbackIndex < fragmentModels.Count; rollbackIndex++)
                    BoardSnapshot.Grid.ClearPiece(fragmentModels[rollbackIndex]);

                BoardSnapshot.Grid.TryPlace(sourceModel);
                Debug.LogError("[GridSplit] The retained fragment could not replace its source model.", this);
                return false;
            }

            for (int index = 1; index < fragmentModels.Count; index++)
            {
                if (BoardSnapshot.TryRegisterPlacedPiece(fragmentModels[index]))
                    continue;

                Debug.LogError("[GridSplit] Registered fragment id sequence was invalid.", this);
                return false;
            }

            for (int index = 0; index < fragmentModels.Count; index++)
            {
                fragments[index].ConfigureSourcePieceId(fragmentModels[index].Id);
                pieceJointGraph.RegisterMember(fragmentModels[index].Id);
            }

            TransferJointsToHammerFragments(sourceModel, fragmentModels);

            BoardOccupancyChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Safe interim hammer behaviour: one runtime root keeps its identity,
        /// while the grid footprint is rebuilt from its remaining cells.  This
        /// deliberately avoids creating disconnected runtime roots until the
        /// full split lifecycle has a dedicated grid transaction.
        /// </summary>
        public bool TryRefreshHammerPieceGeometry(PuzzlePiece piece)
        {
            if (BoardSnapshot == null || piece == null ||
                !TryGetPieceModel(piece, out PieceModel existingModel))
                return false;

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null ||
                !piece.TryCreateGridModel(level, existingModel.Id, out PieceModel refreshedModel))
                return false;

            List<GridCoordinate> previousCells = new List<GridCoordinate>(existingModel.LocalCells);
            GridCoordinate previousAnchor = existingModel.Anchor;
            GridCoordinate previousPivotOffset = existingModel.PivotOffset;

            if (!TryTransitionPieceState(existingModel, PieceState.Placed))
                return false;

            // The snapshot owns the stable PieceModel instance for this runtime
            // root.  Replacing it with a new instance after the grid update can
            // fail even though the visual edit is valid, leaving the booster
            // with no authoritative target. Keep that identity and update only
            // its geometry as one grid transaction instead.
            BoardSnapshot.Grid.ClearPiece(existingModel);
            existingModel.ReplaceGeometry(
                refreshedModel.Anchor,
                refreshedModel.PivotOffset,
                new List<GridCoordinate>(refreshedModel.LocalCells));

            if (!BoardSnapshot.Grid.TryPlace(existingModel))
            {
                existingModel.ReplaceGeometry(
                    previousAnchor,
                    previousPivotOffset,
                    previousCells);
                BoardSnapshot.Grid.TryPlace(existingModel);
                Debug.LogWarning(
                    "[GridHammer] The edited piece footprint could not be committed; restored its previous grid shape.",
                    this);
                return false;
            }

            PuzzleDragController.WakeUpGravity();
            BoardOccupancyChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Aligns an authored piece model with the footprint assembled on its
        /// pooled runtime root. This is a setup-time integrity transaction:
        /// grid rules remain model-driven after the footprint has been
        /// committed. It prevents a generated slot layout from rendering a
        /// different silhouette than the one gravity reserves on the board.
        /// </summary>
        public bool TrySynchronizeRuntimePieceGeometry(PuzzlePiece piece)
        {
            if (BoardSnapshot == null || piece == null ||
                !TryGetPieceModel(piece, out PieceModel existingModel))
                return false;

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null ||
                !piece.TryCreateGridModel(level, existingModel.Id, out PieceModel runtimeModel))
                return false;

            if (HasMatchingGeometry(existingModel, runtimeModel))
                return true;

            if (existingModel.State != PieceState.Placed)
            {
                Debug.LogWarning(
                    $"[GridFootprint] Cannot synchronize '{piece.name}' while it is {existingModel.State}.",
                    this);
                return false;
            }

            List<GridCoordinate> previousCells = new List<GridCoordinate>(existingModel.LocalCells);
            GridCoordinate previousAnchor = existingModel.Anchor;
            GridCoordinate previousPivotOffset = existingModel.PivotOffset;

            BoardSnapshot.Grid.ClearPiece(existingModel);
            existingModel.ReplaceGeometry(
                runtimeModel.Anchor,
                runtimeModel.PivotOffset,
                new List<GridCoordinate>(runtimeModel.LocalCells));

            if (BoardSnapshot.Grid.TryPlace(existingModel))
            {
                Debug.LogWarning(
                    $"[GridFootprint] Synchronized runtime footprint for '{piece.name}' (id={existingModel.Id}).",
                    this);
                BoardOccupancyChanged?.Invoke();
                return true;
            }

            existingModel.ReplaceGeometry(
                previousAnchor,
                previousPivotOffset,
                previousCells);
            BoardSnapshot.Grid.TryPlace(existingModel);
            Debug.LogError(
                $"[GridFootprint] Runtime footprint for '{piece.name}' conflicts with the board; restored its authored footprint.",
                this);
            return false;
        }

        /// <summary>
        /// Adds hidden reveal content to the existing board snapshot. The
        /// roots are rented from the typed BlockPiece pool and the method
        /// rolls back the complete batch if any authored footprint is invalid.
        /// </summary>
        public bool TryActivateRevealPieces(IReadOnlyList<PieceDefinition> definitions)
        {
            if (BoardSnapshot == null || definitions == null)
                return false;

            List<PuzzlePiece> createdPieces = new List<PuzzlePiece>(definitions.Count);
            List<PieceModel> createdModels = new List<PieceModel>(definitions.Count);
            for (int index = 0; index < definitions.Count; index++)
            {
                PieceDefinition definition = definitions[index];
                if (!HasBlockCells(definition))
                    continue;

                if (!TryCreateAndRegisterRevealPiece(definition, out PuzzlePiece piece, out PieceModel model))
                {
                    RollbackRevealActivation(createdPieces, createdModels);
                    Debug.LogWarning(
                        $"[Reveal] Could not activate hidden piece '{definition.name}'.",
                        this);
                    return false;
                }

                createdPieces.Add(piece);
                createdModels.Add(model);
            }

            for (int index = 0; index < createdModels.Count; index++)
                pieceJointGraph.RegisterMember(createdModels[index].Id);

            BoardOccupancyChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Activates one exposed Box definition through the same pooled-root
        /// and authoritative-grid transaction used by final reveal activation.
        /// </summary>
        public bool TryActivateRevealPiece(PieceDefinition definition, out PuzzlePiece piece)
        {
            piece = null;
            if (!HasBlockCells(definition) ||
                !TryCreateAndRegisterRevealPiece(definition, out PuzzlePiece createdPiece, out PieceModel model))
                return false;

            pieceJointGraph.RegisterMember(model.Id);
            piece = createdPiece;
            BoardOccupancyChanged?.Invoke();
            return true;
        }

        private bool TryCreateAndRegisterRevealPiece(
            PieceDefinition definition,
            out PuzzlePiece piece,
            out PieceModel model)
        {
            piece = null;
            model = null;
            if (BoardSnapshot == null || definition == null)
                return false;

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null)
                return false;

            int pieceId = BoardSnapshot.NextPieceId;
            PieceModel authoredModel = LevelBoardSnapshotBuilder.CreatePieceModel(pieceId, definition);
            piece = RuntimePieceFactory.Create(level, definition, pieceId);
            if (piece == null)
                return false;

            Physics2D.SyncTransforms();
            if (piece.TryCreateGridModel(level, pieceId, out model) &&
                HasMatchingGeometry(authoredModel, model) &&
                BoardSnapshot.Grid.TryPlace(model) &&
                BoardSnapshot.TryRegisterPlacedPiece(model))
            {
                return true;
            }

            if (model != null)
                BoardSnapshot.Grid.ClearPiece(model);
            piece.ReleaseInstance();
            piece = null;
            model = null;
            return false;
        }

        private static bool HasBlockCells(PieceDefinition definition)
        {
            if (definition == null || definition.cells == null)
                return false;

            for (int index = 0; index < definition.cells.Count; index++)
            {
                if (definition.cells[index].type == PieceCellType.Block)
                    return true;
            }

            return false;
        }

        private void RollbackRevealActivation(
            IReadOnlyList<PuzzlePiece> createdPieces,
            IReadOnlyList<PieceModel> createdModels)
        {
            for (int index = 0; index < createdModels.Count; index++)
            {
                PieceModel model = createdModels[index];
                BoardSnapshot.Grid.ClearPiece(model);
                BoardSnapshot.TryRemoveRegisteredPiece(model);
            }

            for (int index = 0; index < createdPieces.Count; index++)
                createdPieces[index].ReleaseInstance();
        }

        private static bool HasMatchingGeometry(PieceModel authored, PieceModel runtime)
        {
            if (!authored.Anchor.Equals(runtime.Anchor) ||
                !authored.PivotOffset.Equals(runtime.PivotOffset) ||
                authored.LocalCells.Count != runtime.LocalCells.Count)
                return false;

            // Runtime factory may compact a complete module to a single
            // collider and expand it back to fine cells when rebuilding the
            // model. That is the same footprint, but it does not guarantee
            // the authored serialization order. Compare the coordinate set so
            // a valid compacted module is accepted while any missing or extra
            // collider cell still rejects the reveal transaction.
            HashSet<GridCoordinate> authoredCells = new HashSet<GridCoordinate>(authored.LocalCells);
            if (authoredCells.Count != authored.LocalCells.Count)
                return false;

            for (int index = 0; index < runtime.LocalCells.Count; index++)
            {
                if (!authoredCells.Remove(runtime.LocalCells[index]))
                    return false;
            }

            return authoredCells.Count == 0;
        }

        public bool TryMovePieceOnGrid(
            PuzzlePiece piece,
            GridCoordinate targetAnchor,
            out GridPlacementResult result)
        {
            result = GridPlacementResult.Failure(
                GridPlacementFailureReason.EmptyPiece,
                targetAnchor,
                GridCellState.Empty,
                default);

            return TryMovePieceGroupOnGrid(piece, targetAnchor, true, out result);
        }

        /// <summary>
        /// Commits one selected-piece drag step without changing its Dragging
        /// lifecycle state. Input owns the visual handoff; the board owns the
        /// reveal-area rule check and authoritative grid mutation.
        /// </summary>
        public bool TryMoveDraggingPieceOnGrid(
            PuzzlePiece piece,
            GridCoordinate targetAnchor,
            out GridPlacementResult result)
        {
            result = GridPlacementResult.Failure(
                GridPlacementFailureReason.EmptyPiece,
                targetAnchor,
                GridCellState.Empty,
                default);

            return TryMovePieceGroupOnGrid(piece, targetAnchor, false, out result);
        }

        /// <summary>
        /// Checks a translation without committing it. Drag presentation uses
        /// this for its sub-cell cursor offset so every member of a combined
        /// group follows the pointer together without visually entering a
        /// blocked footprint.
        /// </summary>
        public bool CanMovePieceGroupBy(PuzzlePiece piece, GridCoordinate delta)
        {
            if (!TryGetPieceModel(piece, out PieceModel source) ||
                !TryCollectJointGroupModels(source.Id, jointGroupModels))
                return false;

            jointGroupTargetAnchors.Clear();
            for (int index = 0; index < jointGroupModels.Count; index++)
            {
                PieceModel member = jointGroupModels[index];
                GridCoordinate targetAnchor = member.Anchor.Offset(delta);
                if (revealAreaCoordinator != null &&
                    !revealAreaCoordinator.CanMovePiece(member, targetAnchor))
                {
                    return false;
                }

                jointGroupTargetAnchors.Add(targetAnchor);
            }

            return BoardSnapshot.Grid.CheckGroupPlacementIgnoringMembers(
                jointGroupModels,
                jointGroupTargetAnchors).IsSuccess;
        }

        /// <summary>
        /// Supplies one stable pivot for an entire joint graph component. Input
        /// uses it only to preserve relative presentation and snap offsets;
        /// grid ownership remains on each member model.
        /// </summary>
        public bool TryGetJointGroupDragReference(
            PuzzlePiece piece,
            out GridCoordinate groupPivot,
            out GridCoordinate sourcePivotOffset)
        {
            groupPivot = default;
            sourcePivotOffset = default;
            if (!TryGetPieceModel(piece, out PieceModel source) ||
                !TryCollectJointGroupModels(source.Id, jointGroupModels) ||
                jointGroupModels.Count == 0)
            {
                return false;
            }

            PieceModel reference = jointGroupModels[0];
            for (int index = 1; index < jointGroupModels.Count; index++)
            {
                PieceModel candidate = jointGroupModels[index];
                if (candidate.Id < reference.Id)
                    reference = candidate;
            }

            groupPivot = new GridCoordinate(
                reference.Anchor.X - reference.PivotOffset.X,
                reference.Anchor.Y - reference.PivotOffset.Y);
            GridCoordinate sourcePivot = new GridCoordinate(
                source.Anchor.X - source.PivotOffset.X,
                source.Anchor.Y - source.PivotOffset.Y);
            sourcePivotOffset = new GridCoordinate(
                sourcePivot.X - groupPivot.X,
                sourcePivot.Y - groupPivot.Y);
            return true;
        }

        /// <summary>
        /// Validates the orthogonal route used by release snapping. It mirrors
        /// single-piece snap safety but checks each intermediate footprint as
        /// one graph-derived group.
        /// </summary>
        public bool CanTraversePieceGroupTo(PuzzlePiece piece, GridCoordinate targetAnchor)
        {
            if (!TryGetPieceModel(piece, out PieceModel source) ||
                !TryCollectJointGroupModels(source.Id, jointGroupModels))
            {
                return false;
            }

            jointGroupTargetAnchors.Clear();
            for (int index = 0; index < jointGroupModels.Count; index++)
                jointGroupTargetAnchors.Add(jointGroupModels[index].Anchor);

            GridCoordinate sourceAnchor = source.Anchor;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            int traversalBudget = level != null
                ? Mathf.Max(1, level.FineColumns + level.FineRows)
                : int.MaxValue;
            for (int stepIndex = 0; stepIndex < traversalBudget; stepIndex++)
            {
                if (!FineGridDragStepper.TryGetTraversalSteps(
                        sourceAnchor,
                        targetAnchor,
                        out GridCoordinate primaryDelta,
                        out GridCoordinate fallbackDelta))
                {
                    return true;
                }

                if (TryAdvanceJointGroupTraversal(primaryDelta))
                {
                    sourceAnchor = sourceAnchor.Offset(primaryDelta);
                    continue;
                }

                if ((fallbackDelta.X != 0 || fallbackDelta.Y != 0) &&
                    TryAdvanceJointGroupTraversal(fallbackDelta))
                {
                    sourceAnchor = sourceAnchor.Offset(fallbackDelta);
                    continue;
                }

                return false;
            }

            return sourceAnchor.Equals(targetAnchor);
        }

        public bool CanBeginPieceInteraction(PuzzlePiece piece)
        {
            if (!TryGetPieceModel(piece, out PieceModel model) ||
                !TryCollectJointGroupModels(model.Id, jointGroupModels))
            {
                return false;
            }

            CollectJointGroupPieces(piece, jointGroupPieceBuffer);
            if (jointGroupPieceBuffer.Count != jointGroupModels.Count)
                return false;

            for (int index = 0; index < jointGroupModels.Count; index++)
            {
                if (jointGroupPieceBuffer[index].IsFrozen ||
                    jointGroupPieceBuffer[index].IsShredderInteractionLocked ||
                    (revealAreaCoordinator != null &&
                     !revealAreaCoordinator.CanBeginInteraction(jointGroupModels[index])))
                {
                    return false;
                }
            }

            return true;
        }

        public void CollectJointGroupPieces(PuzzlePiece piece, List<PuzzlePiece> results)
        {
            if (results == null)
                return;

            results.Clear();
            if (!TryGetPieceModel(piece, out PieceModel model))
                return;

            pieceJointGraph.CollectGroupMembers(model.Id, jointGroupMemberIds);
            if (jointGroupMemberIds.Count == 0)
            {
                // A board can be interacted with during an editor domain
                // reload before its level graph has been reconfigured. A
                // missing graph must never make an otherwise valid normal
                // piece undraggable.
                results.Add(piece);
                return;
            }

            bool containsSelectedPiece = false;
            for (int memberIndex = 0; memberIndex < jointGroupMemberIds.Count; memberIndex++)
            {
                PuzzlePiece runtimePiece = FindActivePiece(jointGroupMemberIds[memberIndex]);
                if (runtimePiece != null)
                {
                    results.Add(runtimePiece);
                    containsSelectedPiece |= runtimePiece == piece;
                }
            }

            // The caller already holds a valid live reference to this root.
            // Keep a transient static-registry miss from invalidating normal
            // drag selection; a missing *other* combined member still fails
            // the group-count validation in CanBeginPieceInteraction.
            if (!containsSelectedPiece)
            {
                int selectedMemberIndex = 0;
                for (; selectedMemberIndex < jointGroupMemberIds.Count; selectedMemberIndex++)
                {
                    if (jointGroupMemberIds[selectedMemberIndex] == model.Id)
                        break;
                }

                results.Insert(Mathf.Min(selectedMemberIndex, results.Count), piece);
            }
        }

        public int BreakJointsForPiece(PuzzlePiece piece)
        {
            return TryGetPieceModel(piece, out PieceModel model)
                ? pieceJointGraph.RemoveJointsForMember(model.Id)
                : 0;
        }

        /// <summary>
        /// Commits the joint portion of a successful hammer hit. The grid cell
        /// is resolved before presentation mutates the modular piece, so only
        /// edges attached to the struck complete block are removed.
        /// </summary>
        public int BreakJointsAtPieceCell(int sourcePieceId, GridCoordinate worldCell)
        {
            jointGroupMemberIds.Clear();
            CollectJointIdsAtPieceCell(sourcePieceId, worldCell, jointGroupMemberIds);
            return BreakJointsById(jointGroupMemberIds);
        }

        /// <summary>
        /// Resolves joint ids against the unmodified grid model. Callers that
        /// change a piece's topology must capture this before the edit and
        /// commit it only after the edit succeeds, because a split can move an
        /// endpoint to a fragment with a different runtime identity.
        /// </summary>
        public void CollectJointIdsAtPieceCell(
            int sourcePieceId,
            GridCoordinate worldCell,
            List<int> results)
        {
            if (results == null)
                return;

            results.Clear();
            if (BoardSnapshot == null || !BoardSnapshot.TryGetPiece(sourcePieceId, out PieceModel model))
                return;

            GridCoordinate localCell = new GridCoordinate(
                worldCell.X - model.Anchor.X,
                worldCell.Y - model.Anchor.Y);
            pieceJointGraph.CollectJoints(jointBuffer);
            for (int index = 0; index < jointBuffer.Count; index++)
            {
                PieceJointGraph.Joint joint = jointBuffer[index];
                if ((joint.MemberA == sourcePieceId && IsInsideJointBlock(localCell, joint.BlockA)) ||
                    (joint.MemberB == sourcePieceId && IsInsideJointBlock(localCell, joint.BlockB)))
                {
                    results.Add(joint.Id);
                }
            }
        }

        public int BreakJointsById(IReadOnlyList<int> jointIds)
        {
            return pieceJointGraph.RemoveJointsById(jointIds);
        }

        public bool TryPlanJointGroupGravityMove(
            int sourcePieceId,
            List<GridGravityMove> moves)
        {
            if (moves == null || !TryCollectJointGroupModels(sourcePieceId, jointGroupModels))
                return false;

            moves.Clear();
            if (!BoardSnapshot.Grid.TryGetGroupFallTarget(jointGroupModels, jointGroupTargetAnchors, CanMovePieceDelegate))
                return false;

            for (int index = 0; index < jointGroupModels.Count; index++)
            {
                PieceModel member = jointGroupModels[index];
                moves.Add(new GridGravityMove(member.Id, member.Anchor, jointGroupTargetAnchors[index]));
            }

            return moves.Count > 0;
        }

        public bool TryCommitJointGroupGravityMove(
            IReadOnlyList<GridGravityMove> moves,
            out GridPlacementResult result)
        {
            result = GridPlacementResult.Failure(
                GridPlacementFailureReason.EmptyPiece,
                default,
                GridCellState.Empty,
                default);
            if (moves == null || moves.Count == 0 ||
                !TryCollectJointGroupModels(moves[0].PieceId, jointGroupModels) ||
                jointGroupModels.Count != moves.Count)
            {
                return false;
            }

            jointGroupTargetAnchors.Clear();
            for (int index = 0; index < jointGroupModels.Count; index++)
            {
                PieceModel member = jointGroupModels[index];
                GridGravityMove move = moves[index];
                if (member.Id != move.PieceId || !member.Anchor.Equals(move.FromAnchor) ||
                    member.State != PieceState.Placed ||
                    (revealAreaCoordinator != null &&
                     !revealAreaCoordinator.CanMovePiece(member, move.ToAnchor)))
                {
                    return false;
                }

                jointGroupTargetAnchors.Add(move.ToAnchor);
            }

            if (!BoardSnapshot.Grid.TryMoveGroupIgnoringMembers(
                    jointGroupModels,
                    jointGroupTargetAnchors,
                    out result))
            {
                return false;
            }

            for (int index = 0; index < jointGroupModels.Count; index++)
                TryTransitionPieceState(jointGroupModels[index], PieceState.Falling);

            BoardOccupancyChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Commits an independently supported stack without changing its joint graph.
        /// All members must be ordinary pieces and travel by the same grid delta.
        /// </summary>
        public bool TryCommitCoordinatedGravityMove(
            IReadOnlyList<GridGravityMove> moves, out GridPlacementResult result)
        {
            result = GridPlacementResult.Failure(
                GridPlacementFailureReason.EmptyPiece, default, GridCellState.Empty, default);
            if (BoardSnapshot == null || moves == null || moves.Count < 2)
                return false;

            jointGroupModels.Clear();
            jointGroupTargetAnchors.Clear();
            int distance = moves[0].FromAnchor.Y - moves[0].ToAnchor.Y;
            if (distance <= 0)
                return false;

            for (int index = 0; index < moves.Count; index++)
            {
                GridGravityMove move = moves[index];
                if (!BoardSnapshot.TryGetPiece(move.PieceId, out PieceModel member) ||
                    member.State != PieceState.Placed || !member.Anchor.Equals(move.FromAnchor) ||
                    move.ToAnchor.X != move.FromAnchor.X ||
                    move.FromAnchor.Y - move.ToAnchor.Y != distance ||
                    (revealAreaCoordinator != null && !revealAreaCoordinator.CanMovePiece(member, move.ToAnchor)))
                    return false;

                pieceJointGraph.CollectGroupMembers(member.Id, jointGroupMemberIds);
                if (jointGroupMemberIds.Count > 1)
                    return false;
                jointGroupModels.Add(member);
                jointGroupTargetAnchors.Add(move.ToAnchor);
            }

            if (!BoardSnapshot.Grid.TryGetGroupFallTarget(jointGroupModels, jointGroupTargetAnchors, CanMovePieceDelegate) ||
                jointGroupTargetAnchors[0].Y != moves[0].ToAnchor.Y ||
                !BoardSnapshot.Grid.TryMoveGroupIgnoringMembers(jointGroupModels, jointGroupTargetAnchors, out result))
                return false;

            for (int index = 0; index < jointGroupModels.Count; index++)
                TryTransitionPieceState(jointGroupModels[index], PieceState.Falling);
            BoardOccupancyChanged?.Invoke();
            return true;
        }

        public void CollectJointGroupPieces(int sourcePieceId, List<PuzzlePiece> results)
        {
            if (results == null)
                return;

            results.Clear();
            pieceJointGraph.CollectGroupMembers(sourcePieceId, jointGroupMemberIds);
            if (jointGroupMemberIds.Count == 0)
            {
                PuzzlePiece fallbackPiece = FindActivePiece(sourcePieceId);
                if (fallbackPiece != null)
                    results.Add(fallbackPiece);
                return;
            }

            for (int memberIndex = 0; memberIndex < jointGroupMemberIds.Count; memberIndex++)
            {
                PuzzlePiece runtimePiece = FindActivePiece(jointGroupMemberIds[memberIndex]);
                if (runtimePiece != null)
                    results.Add(runtimePiece);
            }

            if (results.Count == 0)
            {
                PuzzlePiece fallbackPiece = FindActivePiece(sourcePieceId);
                if (fallbackPiece != null)
                    results.Add(fallbackPiece);
            }
        }

        private bool TryMovePieceGroupOnGrid(
            PuzzlePiece piece,
            GridCoordinate targetAnchor,
            bool transitionToPlaced,
            out GridPlacementResult result)
        {
            result = GridPlacementResult.Failure(
                GridPlacementFailureReason.EmptyPiece,
                targetAnchor,
                GridCellState.Empty,
                default);
            if (!TryGetPieceModel(piece, out PieceModel source) ||
                !TryCollectJointGroupModels(source.Id, jointGroupModels))
            {
                return false;
            }

            GridCoordinate delta = new GridCoordinate(
                targetAnchor.X - source.Anchor.X,
                targetAnchor.Y - source.Anchor.Y);

            // Preserve the established single-piece movement contract. Only
            // an actual joint group needs the simultaneous-footprint path;
            // routing every ordinary drag through that new path made normal
            // drag behaviour depend on combined-block setup.
            if (jointGroupModels.Count == 1)
            {
                bool revealOk = revealAreaCoordinator == null ||
                                revealAreaCoordinator.CanMovePiece(source, targetAnchor);
                if (!revealOk)
                {
                    result = GridPlacementResult.Failure(
                        GridPlacementFailureReason.EmptyPiece,
                        targetAnchor,
                        GridCellState.Empty,
                        default);
                    LogGroupMoveFailure(delta, source, false, result);
                    return false;
                }

                if (!BoardSnapshot.Grid.TryMoveIgnoringPiece(
                        source,
                        targetAnchor,
                        source.Id,
                        out result))
                {
                    LogGroupMoveFailure(delta, source, true, result);
                    return false;
                }

                if (transitionToPlaced)
                    TryTransitionPieceState(source, PieceState.Placed);

                BoardOccupancyChanged?.Invoke();
                return true;
            }

            jointGroupTargetAnchors.Clear();
            for (int index = 0; index < jointGroupModels.Count; index++)
            {
                PieceModel member = jointGroupModels[index];
                GridCoordinate memberTarget = member.Anchor.Offset(delta);
                bool revealOk = revealAreaCoordinator == null ||
                                revealAreaCoordinator.CanMovePiece(member, memberTarget);
                if (!revealOk)
                {
                    result = GridPlacementResult.Failure(
                        GridPlacementFailureReason.EmptyPiece,
                        memberTarget,
                        GridCellState.Empty,
                        default);
                    LogGroupMoveFailure(delta, member, false, result);
                    return false;
                }

                jointGroupTargetAnchors.Add(memberTarget);
            }

            bool moved = BoardSnapshot.Grid.TryMoveGroupIgnoringMembers(
                jointGroupModels,
                jointGroupTargetAnchors,
                out result);
            if (!moved)
            {
                LogGroupMoveFailure(
                    delta,
                    FindGroupMemberAtTargetCoordinate(result.Coordinate, jointGroupTargetAnchors),
                    true,
                    result);
                return false;
            }

            if (transitionToPlaced)
            {
                for (int index = 0; index < jointGroupModels.Count; index++)
                    TryTransitionPieceState(jointGroupModels[index], PieceState.Placed);
            }

            BoardOccupancyChanged?.Invoke();
            return true;
        }

        private bool TryAdvanceJointGroupTraversal(GridCoordinate delta)
        {
            for (int index = 0; index < jointGroupModels.Count; index++)
            {
                GridCoordinate nextAnchor = jointGroupTargetAnchors[index].Offset(delta);
                PieceModel member = jointGroupModels[index];
                bool revealOk = revealAreaCoordinator == null ||
                                revealAreaCoordinator.CanMovePiece(member, nextAnchor);
                if (!revealOk)
                {
                    GridPlacementResult revealFailure = GridPlacementResult.Failure(
                        GridPlacementFailureReason.EmptyPiece,
                        nextAnchor,
                        GridCellState.Empty,
                        default);
                    LogGroupMoveFailure(delta, member, false, revealFailure);
                    return false;
                }
            }

            for (int index = 0; index < jointGroupTargetAnchors.Count; index++)
                jointGroupTargetAnchors[index] = jointGroupTargetAnchors[index].Offset(delta);

            GridPlacementResult result = BoardSnapshot.Grid.CheckGroupPlacementIgnoringMembers(
                jointGroupModels,
                jointGroupTargetAnchors);
            if (!result.IsSuccess)
            {
                LogGroupMoveFailure(
                    delta,
                    FindGroupMemberAtTargetCoordinate(result.Coordinate, jointGroupTargetAnchors),
                    true,
                    result);
                // A rejected candidate must not become the basis for the
                // orthogonal slide fallback. Keep traversal state equal to the
                // last confirmed anchor for every group member.
                for (int index = 0; index < jointGroupTargetAnchors.Count; index++)
                    jointGroupTargetAnchors[index] = jointGroupTargetAnchors[index].Offset(
                        new GridCoordinate(-delta.X, -delta.Y));
                return false;
            }

            return true;
        }

        private PieceModel FindGroupMemberAtTargetCoordinate(
            GridCoordinate coordinate,
            IReadOnlyList<GridCoordinate> targetAnchors)
        {
            for (int memberIndex = 0; memberIndex < jointGroupModels.Count; memberIndex++)
            {
                PieceModel member = jointGroupModels[memberIndex];
                if (member == null || memberIndex >= targetAnchors.Count)
                    continue;

                GridCoordinate memberAnchor = targetAnchors[memberIndex];
                for (int cellIndex = 0; cellIndex < member.LocalCells.Count; cellIndex++)
                {
                    if (memberAnchor.Offset(member.LocalCells[cellIndex]).Equals(coordinate))
                        return member;
                }
            }

            return jointGroupModels.Count > 0 ? jointGroupModels[0] : null;
        }

        private void LogGroupMoveFailure(
            GridCoordinate delta,
            PieceModel member,
            bool revealOk,
            GridPlacementResult result)
        {
            int memberId = member != null ? member.Id : default;
            Debug.Log(
                $"[GroupMove] FAIL delta={delta} member={memberId} " +
                $"reveal={revealOk} reason={result.Reason} cell={result.Coordinate} " +
                $"cellState={result.CellState} occupant={result.OccupantId} " +
                $"groupIds=[{string.Join(",", jointGroupModels.ConvertAll(model => model.Id))}]");
        }

        private bool TryCollectJointGroupModels(int sourceId, List<PieceModel> results)
        {
            results.Clear();
            if (BoardSnapshot == null)
                return false;

            pieceJointGraph.CollectGroupMembers(sourceId, jointGroupMemberIds);
            if (jointGroupMemberIds.Count == 0)
            {
                if (!BoardSnapshot.TryGetPiece(sourceId, out PieceModel source))
                    return false;

                results.Add(source);
                return true;
            }

            for (int index = 0; index < jointGroupMemberIds.Count; index++)
            {
                if (!BoardSnapshot.TryGetPiece(jointGroupMemberIds[index], out PieceModel member))
                {
                    results.Clear();
                    return false;
                }

                results.Add(member);
            }

            return results.Count > 0;
        }

        public PuzzlePiece FindActivePiece(int sourcePieceId)
        {
            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                if (piece != null && piece.SourcePieceId == sourcePieceId)
                    return piece;
            }

            return null;
        }

        private void TransferJointsToHammerFragments(
            PieceModel sourceModel,
            IReadOnlyList<PieceModel> fragments)
        {
            if (sourceModel == null || fragments == null)
                return;

            pieceJointGraph.CollectJoints(jointBuffer);
            for (int jointIndex = 0; jointIndex < jointBuffer.Count; jointIndex++)
            {
                PieceJointGraph.Joint joint = jointBuffer[jointIndex];
                TransferJointEndpointToHammerFragment(
                    sourceModel,
                    fragments,
                    joint.MemberA,
                    joint.BlockA);
                TransferJointEndpointToHammerFragment(
                    sourceModel,
                    fragments,
                    joint.MemberB,
                    joint.BlockB);
            }
        }

        private void TransferJointEndpointToHammerFragment(
            PieceModel sourceModel,
            IReadOnlyList<PieceModel> fragments,
            int endpointMemberId,
            GridCoordinate endpointBlock)
        {
            if (endpointMemberId != sourceModel.Id)
                return;

            GridCoordinate worldBlock = sourceModel.Anchor.Offset(endpointBlock);
            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                PieceModel fragment = fragments[fragmentIndex];
                if (!ContainsCompleteJointBlock(fragment, worldBlock))
                    continue;

                GridCoordinate fragmentBlock = new GridCoordinate(
                    worldBlock.X - fragment.Anchor.X,
                    worldBlock.Y - fragment.Anchor.Y);
                pieceJointGraph.TransferEndpoint(
                    endpointMemberId,
                    endpointBlock,
                    fragment.Id,
                    fragmentBlock,
                    true);
                return;
            }

            pieceJointGraph.TransferEndpoint(
                endpointMemberId,
                endpointBlock,
                endpointMemberId,
                endpointBlock,
                false);
        }

        private bool ContainsCompleteJointBlock(PieceModel model, GridCoordinate worldBlock)
        {
            if (model == null)
                return false;

            for (int y = 0; y < jointBlockSize; y++)
            for (int x = 0; x < jointBlockSize; x++)
            {
                GridCoordinate expected = new GridCoordinate(worldBlock.X + x, worldBlock.Y + y);
                bool contains = false;
                for (int localIndex = 0; localIndex < model.LocalCells.Count; localIndex++)
                {
                    if (model.GetWorldCell(localIndex).Equals(expected))
                    {
                        contains = true;
                        break;
                    }
                }

                if (!contains)
                    return false;
            }

            return true;
        }

        private bool IsInsideJointBlock(GridCoordinate localCell, GridCoordinate blockOrigin)
        {
            return localCell.X >= blockOrigin.X && localCell.X < blockOrigin.X + jointBlockSize &&
                   localCell.Y >= blockOrigin.Y && localCell.Y < blockOrigin.Y + jointBlockSize;
        }

        private static bool TryResolveJointLocalBlock(
            PieceDefinition definition,
            PieceModel model,
            Vector2Int localBlockOrigin,
            out GridCoordinate resolvedBlock)
        {
            resolvedBlock = default;
            if (definition == null || definition.cells == null || definition.cells.Count == 0 || model == null)
                return false;

            Vector2Int pieceMinimum = new Vector2Int(int.MaxValue, int.MaxValue);
            for (int index = 0; index < definition.cells.Count; index++)
            {
                Vector2Int rotated = QuarterTurnUtility.Rotate(
                    definition.cells[index].localCell,
                    definition.quarterTurns);
                pieceMinimum = Vector2Int.Min(pieceMinimum, rotated);
            }

            Vector2Int blockMinimum = new Vector2Int(int.MaxValue, int.MaxValue);
            int subdivisions = GravityGridMetrics.FineCellsPerGridCell;
            for (int y = 0; y < subdivisions; y++)
            for (int x = 0; x < subdivisions; x++)
            {
                Vector2Int rotated = QuarterTurnUtility.Rotate(
                    localBlockOrigin + new Vector2Int(x, y),
                    definition.quarterTurns);
                blockMinimum = Vector2Int.Min(blockMinimum, rotated);
            }

            resolvedBlock = new GridCoordinate(
                blockMinimum.x - pieceMinimum.x,
                blockMinimum.y - pieceMinimum.y);
            return true;
        }

        public bool TryCommitGridGravityMove(
            GridGravityMove move,
            out GridPlacementResult result)
        {
            result = GridPlacementResult.Failure(
                GridPlacementFailureReason.EmptyPiece,
                move.ToAnchor,
                GridCellState.Empty,
                default);

            if (BoardSnapshot == null ||
                !BoardSnapshot.TryGetPiece(move.PieceId, out PieceModel piece))
                return false;

            // Group gravity is committed exclusively through
            // TryCommitJointGroupGravityMove. Retargeting one member during a
            // presentation tween would otherwise split its authoritative
            // footprint away from the rest of the connected component.
            if (TryCollectJointGroupModels(piece.Id, jointGroupModels) &&
                jointGroupModels.Count > 1)
            {
                return false;
            }

            if (revealAreaCoordinator != null &&
                !revealAreaCoordinator.CanMovePiece(piece, move.ToAnchor))
                return false;

            bool moved = BoardSnapshot.Grid.TryMoveIgnoringPiece(
                piece,
                move.ToAnchor,
                move.PieceId,
                out result);
            if (moved)
            {
                TryTransitionPieceState(piece, PieceState.Falling);
                BoardOccupancyChanged?.Invoke();
            }

            return moved;
        }

        private bool TryTransitionPieceState(PieceModel model, PieceState nextState)
        {
            if (model == null)
                return false;

            PieceState previousState = model.State;
            if (model.TrySetState(nextState))
            {
                if (previousState != nextState)
                    PieceStateChanged?.Invoke(model.Id, previousState, nextState);
                return true;
            }

            Debug.LogWarning(
                $"[PieceState] Rejected transition for piece {model.Id}: {previousState} -> {nextState}.",
                this);
            return false;
        }

        public void StartTimer()
        {
            // Every drag/tap path may request a start. The first accepted
            // player action owns the bomb countdown's epoch; later requests
            // must never reset it.
            if (IsTimerStarted)
                return;

            if (!TryTransitionGameState(GameState.Playing))
                return;

            IsTimerStarted = true;
            bombElapsedSeconds = 0f;
        }

        public void NotifyPieceDestroyed(PuzzlePiece destroyedPiece)
        {
            if (!IsLevelRunning || destroyedPiece == null)
                return;

            // A bomb's contract is stricter than ordinary piece removal: it
            // must enter a shredder or be explicitly defused by a hammer.
            // Falling off the board or another destruction path cannot
            // silently satisfy its objective.
            if (destroyedPiece.IsBomb && !destroyedPiece.IsBombDefused && !destroyedPiece.IsBeingShredded)
            {
                Debug.Log($"[BombBlock] '{destroyedPiece.name}' was removed without entering a shredder.", destroyedPiece);
                FailLevel();
            }

            // The handoff reserves the piece while it is still visibly entering
            // the shredder. Individual grid cells are released only as the
            // cutter removes them from the presentation.
            bool keepsGridReservation = destroyedPiece.IsBeingShredded;
            if (keepsGridReservation)
                TryReservePieceInGrid(destroyedPiece, PieceState.Shredding);
            else
                TryClearPieceFromGrid(destroyedPiece, PieceState.Shredding);
            if (destroyedPiece.IsBeingShredded)
                BlockShredded?.Invoke(destroyedPiece.SourcePieceId);
            DestroyedPieceCount++;
            if (!keepsGridReservation)
                PuzzleDragController.WakeUpGravity();
            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            for (int i = 0; i < pieces.Count; i++)
            {
                PuzzlePiece piece = pieces[i];
                if (piece != null && piece != destroyedPiece)
                    piece.RefreshFreezeState(DestroyedPieceCount);
            }
        }

        public void AddTime(float seconds)
        {
            if (seconds > 0f && TimeLimit > 0f)
            {
                TimeRemaining += seconds;
            }
        }

        /// <summary>
        /// Restores a board that failed solely because its countdown reached
        /// zero. The board authorizes only one recovery per level and owns
        /// both the state transition and timer mutation.
        /// </summary>
        public bool TryResumeAfterTimerExpiry(float seconds)
        {
            if (seconds <= 0f ||
                TimeLimit <= 0f ||
                !boardFailed ||
                boardCleared ||
                GameState != GameState.Result ||
                !WasTimerExpiryFailure ||
                HasUsedTimerExpiryContinue ||
                TimeRemaining > 0f)
            {
                Debug.LogWarning("[Timer] Continue was rejected because the board is not awaiting a timer-expiry recovery.", this);
                return false;
            }

            boardFailed = false;
            WasTimerExpiryFailure = false;
            finalShredderOutcomeLocked = false;
            timerPauseOwners.Clear();
            AddTime(seconds);

            if (TryTransitionGameState(GameState.Playing))
            {
                HasUsedTimerExpiryContinue = true;
                return true;
            }

            boardFailed = true;
            WasTimerExpiryFailure = true;
            TimeRemaining = 0f;
            return false;
        }

        /// <summary>
        /// Adds an owner-specific timer pause. Multiple systems may pause the
        /// timer safely; it resumes only after every owner releases its pause.
        /// </summary>
        public bool TryPauseTimer(object owner)
        {
            if (owner == null || !IsTimerActive || TimeRemaining <= 0f)
                return false;

            return timerPauseOwners.Add(owner);
        }

        /// <summary>
        /// Releases only the pause belonging to the supplied owner.
        /// </summary>
        public void ResumeTimer(object owner)
        {
            if (owner != null)
                timerPauseOwners.Remove(owner);
        }

        /// <summary>
        /// Atomically hands an existing timer pause to another owner. This is
        /// used when a presentation sequence starts an effect whose gameplay
        /// component must own the remainder of the pause window.
        /// </summary>
        public bool TryTransferTimerPause(object currentOwner, object nextOwner)
        {
            if (currentOwner == null || nextOwner == null ||
                !timerPauseOwners.Contains(currentOwner))
                return false;

            timerPauseOwners.Add(nextOwner);
            timerPauseOwners.Remove(currentOwner);
            return true;
        }

        public void SetRemovalHeight(float height)
        {
            removalHeight = height;
        }

        public void EnableSequentialLevels()
        {
            sequentialLevelsEnabled = true;
        }

        private void Update()
        {
            // The scene-authored board exists before the selected level has
            // supplied its snapshot and pooled pieces. Keep it inert during
            // Initialize so an empty runtime list is not treated as a win.
            if (!IsLevelRunning)
                return;

            if (IsTimerActive && !IsTimerPaused)
            {
                TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);
            }

            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            if (IsTimerStarted && !boardCleared && !boardFailed)
            {
                bombElapsedSeconds += Time.deltaTime;
                if (UpdateBombTimers(pieces))
                    return;
            }

            for (int i = 0; i < pieces.Count; i++)
            {
                PuzzlePiece piece = pieces[i];
                if (piece == null)
                    continue;

                // A shredder feed owns its own completion and pooled release.
                // The generic off-board cleanup must not race that coroutine.
                if (piece.IsShredderInteractionLocked)
                    continue;

                if (piece.transform.position.y < removalHeight)
                {
                    piece.ReportDestroyed();
                    piece.ReleaseInstance();
                }
            }

            if (!boardCleared && !boardFailed)
            {
                LevelProgressManager progress = LevelProgressManager.Instance;
                bool revealReady = revealAreaCoordinator == null ||
                                   !revealAreaCoordinator.HasPendingRevealContent;
                if (!boardClearCompletionPending && !HasRemainingBoardPieces() && revealReady)
                {
                    // Completion starts only after the final shredder feed has
                    // fully released its pooled piece. Shredding is an active
                    // lifecycle state: its feed still owns renderer clipping,
                    // progress scheduling, audio and the Despawn transition.
                    boardClearCompletionPending = true;
                    progress?.CompleteForBoardClear();
                }

                if (boardClearCompletionPending &&
                    (progress == null || progress.IsCompletionPresentationFinished))
                {
                    boardClearCompletionPending = false;
                    boardCleared = true;
                    TryTransitionGameState(GameState.LevelComplete);
                    ShowWinPanel();
                    PlayLevelClearParticleEffects();
                    Debug.Log("LEVEL CLEARED!");

                    OnLevelCleared?.Invoke();
                    LevelCleared?.Invoke();

                    if (winPanel != null)
                    {
                        // The panel owns the player's next-level decision.
                    }
                    else if (!string.IsNullOrEmpty(winSceneName))
                    {
                        StartCoroutine(LoadWinScene(winSceneName));
                    }
                    else if (sequentialLevelsEnabled && GravityLevelRuntime.HasNextLevel)
                    {
                        StartCoroutine(LoadNextLevel());
                    }
                    else
                    {
                        StartCoroutine(EnterResultAfterDelay());
                    }
                }
                else if (TimeLimit > 0f && TimeRemaining <= 0f)
                {
                    // The final piece entered a shredder during the configured
                    // final-seconds window. It has earned its completion even
                    // if its physical feed and flying progress voxels continue
                    // after the display reaches 00:00.
                    if (finalShredderOutcomeLocked)
                    {
                        return;
                    }

                    bool allSettled = true;
                    for (int i = 0; i < pieces.Count; i++)
                    {
                        PuzzlePiece piece = pieces[i];
                        if (piece == null)
                            continue;

                        if (piece.Body != null && (piece.Body.velocity.sqrMagnitude > 0.01f || Mathf.Abs(piece.Body.angularVelocity) > 1f))
                        {
                            allSettled = false;
                            break;
                        }
                    }

                    if (allSettled)
                    {
                        FailLevel(true);
                    }
                }
            }
        }

        private bool HasRemainingBoardPieces()
        {
            if (BoardSnapshot == null)
                return true;

            IReadOnlyList<PieceModel> models = BoardSnapshot.Pieces;
            for (int index = 0; index < models.Count; index++)
            {
                PieceModel model = models[index];
                if (model == null)
                    continue;

                if (model.State != PieceState.Despawned)
                    return true;
            }

            return false;
        }

        private bool UpdateBombTimers(IReadOnlyList<PuzzlePiece> pieces)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                PuzzlePiece piece = pieces[i];
                if (piece == null || !piece.IsBomb || piece.IsBombDefused ||
                    piece.IsShredderInteractionLocked)
                    continue;

                float remainingSeconds = piece.BombTimerSeconds - bombElapsedSeconds;
                piece.RefreshBombTimer(remainingSeconds);
                if (remainingSeconds > 0f)
                    continue;

                Debug.Log($"[BombBlock] '{piece.name}' was not shredded before its timer expired.", piece);
                FailLevel();
                return true;
            }

            return false;
        }

        private void FailLevel(bool wasTimerExpiry = false)
        {
            if (boardFailed || boardCleared)
                return;

            boardFailed = true;
            WasTimerExpiryFailure = wasTimerExpiry;
            TryTransitionGameState(GameState.Result);
            Debug.Log("LEVEL FAILED!");
            LevelFailed?.Invoke();
        }

        private void PlayLevelClearParticleEffects()
        {
            PlayParticleEffect(levelClearParticleEffectA);
            PlayParticleEffect(levelClearParticleEffectB);
            PlayParticleEffect(levelClearParticleEffectC);
            PlayParticleEffect(levelClearParticleEffectD);
            PlayParticleEffect(levelClearParticleEffectE);
            PlayParticleEffect(levelClearParticleEffectF);
            PlayParticleEffect(levelClearParticleEffectG);
        }

        private void ShowWinPanel()
        {
            if (winPanel == null)
                return;

            bool hasNextLevel = GravityLevelRuntime.HasNextLevel;
            if (nextLevelButton != null)
                nextLevelButton.interactable = hasNextLevel;

            if (nextLevelButtonLabel != null)
            {
                nextLevelButtonLabel.text = hasNextLevel
                    ? $"LEVEL {GravityLevelRuntime.CurrentLevelNumber + 1}"
                    : "COMPLETE";
            }

            if (winPanelCoinAmountText != null)
                winPanelCoinAmountText.text = GravityLevelRuntime.CurrentLevelCoinAmount.ToString();

            if (completedLevelText != null)
                completedLevelText.SetText("Level {0} Completed!", GravityLevelRuntime.CurrentLevelNumber);

            winPanel.SetActive(true);
            RestartWinParticleEffects();
            PlayWinFanfare();
        }

        private void PlayWinFanfare()
        {
            if (winFanfareAudioClip == null)
                return;

            if (winAudioSource == null)
                winAudioSource = GetComponent<AudioSource>();

            if (winAudioSource != null)
                winAudioSource.PlayOneShot(winFanfareAudioClip);
            else
                AudioSource.PlayClipAtPoint(
                    winFanfareAudioClip,
                    PrototypeBootstrap.SceneCamera != null
                        ? PrototypeBootstrap.SceneCamera.transform.position
                        : Vector3.zero);
        }

        private bool nextLevelTransitionPending;

        private void LoadNextLevelFromWinPanel()
        {
            if (!boardCleared || nextLevelTransitionPending)
                return;

            nextLevelTransitionPending = true;
            if (nextLevelButton != null)
                nextLevelButton.interactable = false;

            StartCoroutine(ExecuteNextLevelFromWinPanel());
        }

        private IEnumerator ExecuteNextLevelFromWinPanel()
        {
            // Give a crisp, brief window (0.12s) so the button's click feedback
            // completes naturally rather than killing tweens abruptly on frame 0.
            yield return new WaitForSecondsRealtime(0.12f);

            if (TryQueueBoosterRewardForCompletedLevel())
            {
                if (winPanel != null)
                    winPanel.SetActive(false);
                if (winVfx != null)
                    winVfx.SetActive(false);
            }

            LoadNextLevelAfterWinPanel();
            nextLevelTransitionPending = false;
        }

        private bool TryQueueBoosterRewardForCompletedLevel()
        {
            if (boosterRewardConfigs == null || !GravityLevelRuntime.HasNextLevel)
                return false;

            int completedLevel = GravityLevelRuntime.CurrentLevelNumber;
            for (int index = 0; index < boosterRewardConfigs.Length; index++)
            {
                BoosterRewardConfig config = boosterRewardConfigs[index];
                if (config == null || config.PresentationLevel != completedLevel + 1)
                    continue;

                if (BoosterFirstUseTutorialCoordinator.Supports(config.BoosterType))
                {
                    if (!Application.isEditor &&
                        BoosterFirstUseTutorialProgress.GetState(config) !=
                        BoosterFirstUseTutorialState.NotStarted)
                        continue;
                }
                else if (BoosterRewardUnlockState.HasBeenPresented(config))
                {
                    continue;
                }

                GravityLevelRuntime.QueueBoosterReward(config);
                return true;
            }

            return false;
        }

        /// <summary>Shows a queued unlock after the destination level is ready.</summary>
        public void ShowQueuedBoosterReward()
        {
            if (newBoosterPanel != null &&
                GravityLevelRuntime.TryTakeQueuedBoosterReward(out BoosterRewardConfig rewardConfig))
            {
                awaitingBoosterRewardDismissal = true;
                pendingBoosterRewardConfig = rewardConfig;

                int grant = rewardConfig.GrantAmount > 0 ? rewardConfig.GrantAmount : 1;
                if (BoosterInventoryRuntime.Current.GetCount(rewardConfig.BoosterType) < grant)
                {
                    BoosterInventoryRuntime.Current.Grant(rewardConfig.BoosterType, grant - BoosterInventoryRuntime.Current.GetCount(rewardConfig.BoosterType));
                }

                newBoosterPanel.Show(rewardConfig);
                return;
            }

            boosterFirstUseTutorial?.ResumePending(boosterRewardConfigs);
        }

        private void ClaimQueuedBoosterReward(BoosterRewardConfig claimedConfig)
        {
            if (!awaitingBoosterRewardDismissal || claimedConfig == null ||
                claimedConfig != pendingBoosterRewardConfig)
                return;

            awaitingBoosterRewardDismissal = false;
            boosterFirstUseTutorial?.BeginClaim(pendingBoosterRewardConfig);
            pendingBoosterRewardConfig = null;
        }

        private void LoadNextLevelAfterWinPanel()
        {
            if (!boardCleared)
                return;

            if (!GravityLevelRuntime.TryAdvanceToNextLevel())
            {
                Debug.Log("[LevelSequence] No next level is available.", this);
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }

        private static void PlayParticleEffect(ParticleSystem particleEffect)
        {
            if (particleEffect == null)
                return;

            particleEffect.gameObject.SetActive(true);
            particleEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleEffect.Play(true);
        }

        private void RestartWinParticleEffects()
        {
            if (winVfx == null)
                return;

            winVfx.SetActive(true);
            if (winVfxParticleSystems == null)
                return;

            for (int index = 0; index < winVfxParticleSystems.Length; index++)
                PlayParticleEffect(winVfxParticleSystems[index]);
        }

        private static void StopParticleEffects(ParticleSystem[] particleEffects)
        {
            if (particleEffects == null)
                return;

            for (int index = 0; index < particleEffects.Length; index++)
            {
                ParticleSystem particleEffect = particleEffects[index];
                if (particleEffect != null)
                    particleEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private IEnumerator LoadWinScene(string sceneName)
        {
            yield return new WaitForSecondsRealtime(NextLevelDelay);
            TryTransitionGameState(GameState.Result);
            GravityLevelRuntime.TryAdvanceToNextLevel();
            SceneManager.LoadScene(sceneName);
        }

        private IEnumerator LoadNextLevel()
        {
            yield return new WaitForSecondsRealtime(NextLevelDelay);
            TryTransitionGameState(GameState.Result);

            if (!GravityLevelRuntime.TryAdvanceToNextLevel())
                yield break;

            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }

        private IEnumerator EnterResultAfterDelay()
        {
            yield return new WaitForSecondsRealtime(NextLevelDelay);
            TryTransitionGameState(GameState.Result);
        }

        private bool TryTransitionGameState(GameState nextState)
        {
            if (GameState == nextState)
                return true;

            GameState previousState = GameState;
            if (gameStateMachine.TryTransition(nextState))
            {
                GameStateChanged?.Invoke(previousState, nextState);
                return true;
            }

            Debug.LogWarning(
                $"[GameState] Rejected transition: {previousState} -> {nextState}.",
                this);
            return false;
        }

    }
}
