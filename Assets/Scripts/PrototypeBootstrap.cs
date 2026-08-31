using System.Collections;
using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Core.StateMachine;
using GravityPuzzle.Gameplay.Gravity;
using GravityPuzzle.Gameplay.Pieces;
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
            Physics2D.velocityIterations = Mathf.Max(Physics2D.velocityIterations, 10);
            Physics2D.positionIterations = Mathf.Max(Physics2D.positionIterations, 10);

            ApplyPieceMaterials();
            ApplyObstacleMaterials();
        }

        internal static void SetDraggingFriction(PuzzlePiece piece, bool isDragging)
        {
            EnsurePhysicsMaterials();
            ApplyPieceMaterial(piece, puzzleContactMaterial);
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
            bool roundCorners = true)
        {
            GameObject block = CreateVisualBlock(blockName, position, size, color);
            BoxCollider2D collider = block.AddComponent<BoxCollider2D>();
            if (roundCorners)
                RoundColliderCorners(collider, size);
            authoredObstacleColliders.Add(collider);
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

        internal static GameObject CreateVisualBlock(string blockName, Vector2 position, Vector2 size, Color color)
        {
            GameObject block = new GameObject(blockName);
            block.transform.position = position;
            block.transform.localScale = new Vector3(size.x, size.y, 1f);

            SpriteRenderer renderer = block.AddComponent<SpriteRenderer>();
            renderer.sprite = GetSquareSprite();
            renderer.color = color;
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
        private bool boardFailed;
        private bool sequentialLevelsEnabled = true;
        private readonly HashSet<object> timerPauseOwners = new HashSet<object>();
        private readonly StateMachine<GameState> gameStateMachine = new StateMachine<GameState>(
            GameState.Initialize,
            GameStateTransitionRules.Create());

        public static event System.Action OnLevelCleared;
        public event System.Action<GameState, GameState> GameStateChanged;
        public event System.Action<int, PieceState, PieceState> PieceStateChanged;
        public event System.Action LevelCleared;
        public event System.Action LevelFailed;

        [Header("Win UI Scene Configuration")]
        [Tooltip("If set, this scene will be loaded when the level is cleared instead of the default auto-reload behavior.")]
        public string winSceneName = "";

        [Tooltip("If true, renders the old debug OnGUI 'LEVEL CLEARED!' text overlay on screen.")]
        public bool showRuntimeWinGUI = false;

        [Tooltip("Canvas altında bulunan, level tamamlandığında gösterilecek win paneli.")]
        [SerializeField] private GameObject winPanel;

        [Tooltip("Win Panel içindeki, sonraki leveli yükleyecek buton.")]
        [SerializeField] private UnityEngine.UI.Button nextLevelButton;

        [Tooltip("Next Level Button içindeki, sıradaki level numarasını gösteren TMP metni.")]
        [SerializeField] private TMP_Text nextLevelButtonLabel;

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
        public GameState GameState => gameStateMachine.Current;
        public LevelBoardSnapshot BoardSnapshot { get; private set; }

        private void Awake()
        {
            if (winPanel != null)
                winPanel.SetActive(false);

            if (nextLevelButton != null)
                nextLevelButton.onClick.AddListener(LoadNextLevelFromWinPanel);
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
        }

        public void SetTimeLimit(float timeLimit)
        {
            TimeLimit = timeLimit;
            TimeRemaining = timeLimit;
            IsTimerStarted = false;
            DestroyedPieceCount = 0;
            timerPauseOwners.Clear();
            finalShredderOutcomeLocked = false;
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

            return TryTransitionPieceState(model, PieceState.Dragging);
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

            if (!BoardSnapshot.Grid.TryReserve(model))
                return false;

            return TryTransitionPieceState(model, state);
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
            return TryTransitionPieceState(model, PieceState.Despawned);
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
                fragments[index].ConfigureSourcePieceId(fragmentModels[index].Id);

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
            return true;
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

            if (!TryGetPieceModel(piece, out PieceModel model))
                return false;

            bool moved = BoardSnapshot.Grid.TryMoveIgnoringPiece(
                model,
                targetAnchor,
                piece.SourcePieceId,
                out result);

            if (moved)
                TryTransitionPieceState(model, PieceState.Placed);

            return moved;
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

            bool moved = BoardSnapshot.Grid.TryMoveIgnoringPiece(
                piece,
                move.ToAnchor,
                move.PieceId,
                out result);
            if (moved)
                TryTransitionPieceState(piece, PieceState.Falling);

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
            if (!TryTransitionGameState(GameState.Playing))
                return;

            IsTimerStarted = true;
        }

        public void NotifyPieceDestroyed(PuzzlePiece destroyedPiece)
        {
            if (!IsLevelRunning || destroyedPiece == null)
                return;

            // The handoff reserves the piece while it is still visibly entering
            // the shredder. Individual grid cells are released only as the
            // cutter removes them from the presentation.
            bool keepsGridReservation = destroyedPiece.IsBeingShredded;
            if (keepsGridReservation)
                TryReservePieceInGrid(destroyedPiece, PieceState.Shredding);
            else
                TryClearPieceFromGrid(destroyedPiece, PieceState.Shredding);
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
            int livePieceCount = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                PuzzlePiece piece = pieces[i];
                if (piece == null)
                    continue;

                livePieceCount++;
                // A shredder feed owns its own completion and pooled release.
                // The generic off-board cleanup must not race that coroutine.
                if (piece.IsBeingShredded)
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
                bool requiresProgress = progress != null && progress.TotalBlockUnits > 0;
                bool progressReady = !requiresProgress ||
                                     (progress.IsLevelComplete && !progress.HasPendingProgressPresentation);
                if (livePieceCount == 0 && progressReady)
                {
                    boardCleared = true;
                    TryTransitionGameState(GameState.LevelComplete);
                    PlayLevelClearParticleEffects();
                    ShowWinPanel();
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
                        boardFailed = true;
                        TryTransitionGameState(GameState.Result);
                        Debug.Log("LEVEL FAILED!");
                        LevelFailed?.Invoke();
                    }
                }
            }
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

            winPanel.SetActive(true);
        }

        private void LoadNextLevelFromWinPanel()
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

        private void OnGUI()
        {
            if (!showRuntimeWinGUI || !boardCleared)
                return;

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 38,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = new Color(.35f, 1f, .55f);
            string message = sequentialLevelsEnabled && !GravityLevelRuntime.HasNextLevel
                ? "ALL LEVELS CLEARED!"
                : "LEVEL CLEARED!";
            GUI.Label(new Rect(0f, Screen.height * .42f, Screen.width, 70f), message, style);
        }
    }
}
