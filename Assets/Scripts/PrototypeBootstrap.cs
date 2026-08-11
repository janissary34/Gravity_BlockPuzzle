using System.Collections;
using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
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
        private const float BoardWidth = 6f;
        private const float BoardHeight = 8f;
        internal const float ColliderCornerRadius = .025f;
        private static Sprite squareSprite;
        private static Sprite circleSprite;
        // This material gives casts/contact resolution a stable, non-bouncy surface.
        private static PhysicsMaterial2D puzzleContactMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoader()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Main_Menu" || scene.name == "MainMenu")
                return;

            CreatePrototype();
        }

        private static void CreatePrototype()
        {
            if (Object.FindObjectOfType<PrototypeBoard>() != null ||
                Object.FindObjectOfType<GravityMainMenu>() != null ||
                Object.FindObjectOfType<MainMenuUI>() != null)
                return;

            GravityLevelDefinition authoredLevel = GravityLevelRuntime.FindLevelToPlay();
            if (authoredLevel != null)
            {
                if (GravityLevelRuntime.ConsumePreviewLaunchRequest())
                {
                    StartAuthoredLevel(authoredLevel);
                    return;
                }

                ConfigureCamera(5.4f, new Color(.075f, .035f, .16f));
                GameObject menu = new GameObject("Gravity Puzzle Main Menu");
                menu.AddComponent<GravityMainMenu>().Initialize(
                    authoredLevel,
                    GravityLevelRuntime.CurrentLevelNumber);
                return;
            }

            ConfigureCamera();

            GameObject board = new GameObject("Gravity Puzzle Prototype");
            board.AddComponent<PrototypeBoard>();
            board.AddComponent<PuzzleDragController>();

            CreateLargeSquareBackground(
                (int)BoardWidth,
                (int)BoardHeight,
                1f,
                new Color(.075f, .09f, .17f),
                new Color(.095f, .115f, .205f));

            // Four fixed walls: this is the first version of our outer frame.
            CreateStaticBlock("Left Wall", new Vector2(-3.25f, 0f), new Vector2(.5f, BoardHeight + .5f), new Color(.16f, .18f, .32f));
            CreateStaticBlock("Right Wall", new Vector2(3.25f, 0f), new Vector2(.5f, BoardHeight + .5f), new Color(.16f, .18f, .32f));
            // No floor: solved pieces leave through this opening and are removed.
            CreateStaticBlock("Ceiling", new Vector2(0f, 4.1f), new Vector2(BoardWidth + .5f, .5f), new Color(.16f, .18f, .32f));

            // Wall pins sit UNDER the pieces, so gravity presses the pieces onto them.
            // The player must lift and guide each L-piece around its pin.
            CreateStaticCircle("Pink Support Pin", new Vector2(.45f, -.07f), .24f, new Color(1f, .74f, .12f));
            CreateStaticCircle("Blue Support Pin", new Vector2(-.85f, .83f), .24f, new Color(1f, .74f, .12f));

            // Two L-shaped blocks. Each has a small hook tip at the end of its arm.
            // The blue tip rests on the pink arm, so gravity keeps the pair connected.
            CreateHookPiece("Pink upper hook", new Vector2(.7f, 1.65f), true, new Color(1f, .32f, .62f));
            CreateHookPiece("Blue lower hook", new Vector2(-1.08f, -.05f), false, new Color(.18f, .65f, 1f));

            ConfigurePuzzleColliders();
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

            foreach (Collider2D sceneCollider in Object.FindObjectsOfType<Collider2D>())
                sceneCollider.sharedMaterial = puzzleContactMaterial;
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
            foreach (PuzzlePiece piece in Object.FindObjectsOfType<PuzzlePiece>())
                SetLayerRecursively(piece.gameObject, pieceLayer);

            foreach (Collider2D collider in Object.FindObjectsOfType<Collider2D>())
            {
                if (collider.GetComponentInParent<PuzzlePiece>() != null)
                    continue;

                Rigidbody2D body = collider.GetComponentInParent<Rigidbody2D>();
                if (body != null && body.bodyType == RigidbodyType2D.Static)
                    SetLayerRecursively(collider.gameObject, obstacleLayer);
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
            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
            }

            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor = backgroundColor;
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
        }

        internal static void CreateLargeSquareBackground(
            int columns,
            int rows,
            float squareSize,
            Color firstColor,
            Color secondColor)
        {
            GameObject background = new GameObject("Large Square Background");
            float startX = -(columns - 1) * squareSize * .5f;
            float startY = -(rows - 1) * squareSize * .5f;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    GameObject square = CreateVisualBlock(
                        $"Background Square {x}, {y}",
                        new Vector2(startX + x * squareSize, startY + y * squareSize),
                        Vector2.one * squareSize,
                        (x + y) % 2 == 0 ? firstColor : secondColor);

                    square.transform.SetParent(background.transform, true);
                    square.GetComponent<SpriteRenderer>().sortingOrder = -10;
                }
            }
        }

        internal static void CreateStaticCircle(string obstacleName, Vector2 position, float radius, Color color)
        {
            GameObject obstacle = new GameObject(obstacleName);
            obstacle.transform.position = position;
            obstacle.transform.localScale = Vector3.one * (radius * 2f);

            SpriteRenderer renderer = obstacle.AddComponent<SpriteRenderer>();
            renderer.sprite = GetCircleSprite();
            renderer.color = color;

            obstacle.AddComponent<CircleCollider2D>();
        }

        private static void CreateHookPiece(string pieceName, Vector2 position, bool opensDown, Color color)
        {
            GameObject piece = new GameObject(pieceName);
            piece.transform.position = position;

            Rigidbody2D body = piece.AddComponent<Rigidbody2D>();
            body.gravityScale = 1.5f;
            body.mass = 1f;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            piece.AddComponent<PuzzlePiece>();

            // The stem, arm, and tip together form a J-shaped hook.
            // A compound collider means all three child colliders act as one rigid piece.
            if (opensDown)
            {
                CreateHookPart(piece.transform, "Stem", new Vector2(0f, -.45f), new Vector2(.36f, 1.9f), color);
                CreateHookPart(piece.transform, "Arm", new Vector2(-.65f, -1.3f), new Vector2(1.65f, .36f), color);
                CreateHookPart(piece.transform, "Small Hook Tip", new Vector2(-1.38f, -1.58f), new Vector2(.36f, .9f), color);
            }
            else
            {
                CreateHookPart(piece.transform, "Stem", new Vector2(0f, .45f), new Vector2(.36f, 1.9f), color);
                CreateHookPart(piece.transform, "Arm", new Vector2(.65f, 1.3f), new Vector2(1.65f, .36f), color);
                CreateHookPart(piece.transform, "Small Hook Tip", new Vector2(1.38f, 1.02f), new Vector2(.36f, .9f), color);
            }
        }

        private static void CreateHookPart(Transform parent, string partName, Vector2 localPosition, Vector2 size, Color color)
        {
            GameObject part = CreateVisualBlock(partName, Vector2.zero, size, color);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            BoxCollider2D collider = part.AddComponent<BoxCollider2D>();
            RoundColliderCorners(collider, size);
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
            Destroy(gameObject);
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
        private float removalHeight = -5.5f;
        private bool boardCleared;
        private bool boardFailed;
        private bool sequentialLevelsEnabled = true;
        private readonly HashSet<object> timerPauseOwners = new HashSet<object>();

        public static event System.Action OnLevelCleared;

        [Header("Win UI Scene Configuration")]
        [Tooltip("If set, this scene will be loaded when the level is cleared instead of the default auto-reload behavior.")]
        public string winSceneName = "";

        [Tooltip("If true, renders the old debug OnGUI 'LEVEL CLEARED!' text overlay on screen.")]
        public bool showRuntimeWinGUI = true;

        public float TimeLimit { get; private set; }
        public float TimeRemaining { get; private set; }
        public int DestroyedPieceCount { get; private set; }
        public bool IsTimerStarted { get; private set; }
        public bool IsTimerActive => TimeLimit > 0f && IsTimerStarted && !boardCleared && !boardFailed;
        public bool IsTimerPaused => timerPauseOwners.Count > 0;
        public bool IsLevelRunning => !boardCleared && !boardFailed;
        public LevelBoardSnapshot BoardSnapshot { get; private set; }

        private void OnEnable()
        {
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this)
                Active = null;
        }

        public void SetTimeLimit(float timeLimit)
        {
            TimeLimit = timeLimit;
            TimeRemaining = timeLimit;
            IsTimerStarted = false;
            DestroyedPieceCount = 0;
            timerPauseOwners.Clear();
        }

        // Phase 2 keeps this snapshot parallel to the legacy physics runtime.
        // A later phase will make it the sole gameplay authority.
        public void InitializeBoardSnapshot(LevelBoardSnapshot snapshot)
        {
            BoardSnapshot = snapshot;
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

            model.SetState(state);
            return true;
        }

        public bool TryClearPieceFromGrid(PuzzlePiece piece, PieceState state)
        {
            if (!TryGetPieceModel(piece, out PieceModel model))
                return false;

            BoardSnapshot.Grid.ClearPiece(model);
            model.SetState(state);
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
                model.SetState(PieceState.Placed);

            return moved;
        }

        public void StartTimer()
        {
            IsTimerStarted = true;
        }

        public void NotifyPieceDestroyed(PuzzlePiece destroyedPiece)
        {
            if (!IsLevelRunning || destroyedPiece == null)
                return;

            TryClearPieceFromGrid(destroyedPiece, PieceState.Shredding);
            DestroyedPieceCount++;
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
                if (piece.transform.position.y < removalHeight)
                {
                    piece.ReportDestroyed();
                    Object.Destroy(piece.gameObject);
                }
            }

            if (!boardCleared && !boardFailed)
            {
                LevelProgressManager progress = LevelProgressManager.Instance;
                bool requiresProgress = progress != null && progress.TotalBlockUnits > 0;
                bool progressReady = !requiresProgress ||
                                     (progress.IsLevelComplete && !progress.HasPendingProgressPresentation);
                if (livePieceCount == 0 && !BlockShredder.HasActiveGemFlights && progressReady)
                {
                    boardCleared = true;
                    Debug.Log("LEVEL CLEARED!");

                    OnLevelCleared?.Invoke();

                    if (!string.IsNullOrEmpty(winSceneName))
                    {
                        StartCoroutine(LoadWinScene(winSceneName));
                    }
                    else if (sequentialLevelsEnabled && GravityLevelRuntime.HasNextLevel)
                    {
                        StartCoroutine(LoadNextLevel());
                    }
                }
                else if (TimeLimit > 0f && TimeRemaining <= 0f)
                {
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
                        Debug.Log("LEVEL FAILED!");
                        
                        PuzzleDragController dragController = GetComponent<PuzzleDragController>();
                        if (dragController != null)
                            dragController.enabled = false;
                            
                        LevelTimerUI timerUI = LevelTimerUI.Active;
                        if (timerUI != null)
                            timerUI.ShowFailPopup();
                    }
                }
            }
        }

        private IEnumerator LoadWinScene(string sceneName)
        {
            yield return new WaitForSecondsRealtime(NextLevelDelay);
            GravityLevelRuntime.TryAdvanceToNextLevel();
            SceneManager.LoadScene(sceneName);
        }

        private IEnumerator LoadNextLevel()
        {
            yield return new WaitForSecondsRealtime(NextLevelDelay);

            if (!GravityLevelRuntime.TryAdvanceToNextLevel())
                yield break;

            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
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
