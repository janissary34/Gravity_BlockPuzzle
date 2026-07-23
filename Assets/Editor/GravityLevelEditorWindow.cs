using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    public sealed class GravityLevelEditorWindow : EditorWindow
    {
        private enum EditTool
        {
            Select,
            MovePiece,
            MapShape,
            ThinMapShape,
            PaintBlock,
            PaintHook,
            Pin,
            Obstacle,
            ThinObstacle,
            Shredder,
            Erase
        }

        private const float SidebarWidth = 310f;
        private const float ToolbarHeight = 38f;
        private const string PreviewPathKey = "GravityPuzzle.PreviewLevelPath";

        private GravityLevelDefinition level;
        private EditTool tool = EditTool.Select;
        private int selectedPiece = -1;
        private int selectedPin = -1;
        private int selectedObstacle = -1;
        private int selectedShredder = -1;
        private Vector2Int dragOffset;
        private Vector2 sidebarScroll;
        private Vector2Int lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
        private bool mapShapeStrokeActive;
        private bool mapShapeStrokeMakesInactive;
        private bool validationIsCurrent;
        private readonly List<string> validationMessages = new List<string>();
        private static readonly Color InactiveMapShapeColor = new Color(1f, .22f, .58f);

        [MenuItem("Gravity Puzzle/Level Editor")]
        private static void Open()
        {
            GetWindow<GravityLevelEditorWindow>("Gravity Level Editor");
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (level == null)
            {
                EditorGUI.HelpBox(
                    new Rect(24f, ToolbarHeight + 24f, position.width - 48f, 54f),
                    "Create a level or choose an existing GravityLevel asset.",
                    MessageType.Info);
                return;
            }

            Rect sidebar = new Rect(0f, ToolbarHeight, SidebarWidth, position.height - ToolbarHeight);
            Rect canvas = new Rect(SidebarWidth, ToolbarHeight, position.width - SidebarWidth, position.height - ToolbarHeight);
            DrawSidebar(sidebar);
            DrawCanvas(canvas);
        }

        private void DrawToolbar()
        {
            GUILayout.BeginArea(new Rect(0f, 0f, position.width, ToolbarHeight), EditorStyles.toolbar);
            GUILayout.BeginHorizontal();

            GravityLevelDefinition chosen = (GravityLevelDefinition)EditorGUILayout.ObjectField(
                level,
                typeof(GravityLevelDefinition),
                false,
                GUILayout.Width(260f));
            if (chosen != level)
            {
                level = chosen;
                ClearSelection();
            }

            if (GUILayout.Button("New Level", EditorStyles.toolbarButton, GUILayout.Width(86f)))
                CreateNewLevel();

            using (new EditorGUI.DisabledScope(level == null))
            {
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                    SaveLevel();

                if (GUILayout.Button("Play Preview", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                    PlayPreview();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Map + Block = 1 grid cell   Hook = fine detail", EditorStyles.miniLabel);
            GUILayout.Space(8f);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawSidebar(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);
            sidebarScroll = GUILayout.BeginScrollView(sidebarScroll);

            GUILayout.Label("Map", EditorStyles.boldLabel);
            DrawLevelProperties();
            GUILayout.Space(8f);

            GUILayout.Label("Tools", EditorStyles.boldLabel);
            DrawToolButtons();
            GUILayout.Space(10f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Puzzle Pieces", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Piece", GUILayout.Width(70f)))
                AddPiece();
            GUILayout.EndHorizontal();

            for (int i = 0; i < level.pieces.Count; i++)
            {
                Color previous = GUI.backgroundColor;
                if (i == selectedPiece)
                    GUI.backgroundColor = new Color(.55f, .8f, 1f);
                string pieceLabel = level.pieces[i].frozenMoveCount > 0
                    ? $"{level.pieces[i].name} [Ice {level.pieces[i].frozenMoveCount}]"
                    : level.pieces[i].name;
                if (GUILayout.Button(pieceLabel))
                    SelectPiece(i);
                GUI.backgroundColor = previous;
            }

            GUILayout.Space(8f);
            DrawSelectedInspector();
            GUILayout.Space(12f);
            DrawValidation();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawLevelProperties()
        {
            string newName = EditorGUILayout.TextField("Name", level.levelName);
            float newTimeLimit = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Time Limit (0=infinite)", "Time limit in seconds. 0 for unlimited time."), level.timeLimit));
            int newColumns = Mathf.Max(3, EditorGUILayout.IntField("Columns", level.boardColumns));
            int newRows = Mathf.Max(3, EditorGUILayout.IntField("Rows", level.boardRows));
            float newGravity = Mathf.Max(.1f, EditorGUILayout.FloatField("Gravity", level.gravityScale));
            int newExitWidthCells = EditorGUILayout.IntSlider(
                "Exit Width (cells)",
                Mathf.Clamp(Mathf.RoundToInt(level.exitWidth), 1, newColumns),
                1,
                newColumns);
            float newExitWidth = newExitWidthCells;
            Color newBackground = EditorGUILayout.ColorField("Background", level.backgroundColor);
            Color newFrame = EditorGUILayout.ColorField("Frame", level.frameColor);

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntField("Internal Fine Grid", level.subdivisions);

            if (level.subdivisions != GravityGridMetrics.FineCellsPerGridCell)
            {
                EditorGUILayout.HelpBox(
                    $"Modular authoring expects {GravityGridMetrics.FineCellsPerGridCell} internal fine cells per grid cell. " +
                    "This legacy level should be rebuilt or migrated before modular editing.",
                    MessageType.Warning);
            }

            float estimatedCellPoints = GravityGridMetrics.EstimatedCellSizeInPoints(newColumns, newRows);
            EditorGUILayout.LabelField("Approx. Cell on Phone", $"{estimatedCellPoints:0} pt");

            if (newName != level.levelName || newColumns != level.boardColumns ||
                newRows != level.boardRows ||
                !Mathf.Approximately(newTimeLimit, level.timeLimit) ||
                !Mathf.Approximately(newGravity, level.gravityScale) ||
                !Mathf.Approximately(newExitWidth, level.exitWidth) ||
                newBackground != level.backgroundColor || newFrame != level.frameColor)
            {
                Undo.RecordObject(level, "Edit map settings");
                level.levelName = newName;
                level.timeLimit = newTimeLimit;
                level.boardColumns = newColumns;
                level.boardRows = newRows;
                level.gravityScale = newGravity;
                level.exitWidth = newExitWidth;
                level.backgroundColor = newBackground;
                level.frameColor = newFrame;
                MarkDirty();
            }
        }

        private void DrawToolButtons()
        {
            string[] labels = { "Select", "Move", "Map Cell", "Thin Map Cell", "Block", "Hook", "Pin", "Obstacle", "Thin Obstacle", "Shredder", "Erase" };
            int chosen = GUILayout.SelectionGrid((int)tool, labels, 2);
            if (chosen != (int)tool)
            {
                tool = (EditTool)chosen;
                mapShapeStrokeActive = false;
            }
        }

        private void DrawSelectedInspector()
        {
            if (selectedPiece >= 0 && selectedPiece < level.pieces.Count)
            {
                PieceDefinition piece = level.pieces[selectedPiece];
                GUILayout.Label("Selected Piece", EditorStyles.boldLabel);
                string newName = EditorGUILayout.TextField("Name", piece.name);
                Color newColor = EditorGUILayout.ColorField("Color", piece.color);
                Vector2Int newOrigin = EditorGUILayout.Vector2IntField("Origin", piece.origin);
                int newRotation = EditorGUILayout.Popup("Start Rotation", piece.quarterTurns, new[] { "0°", "90°", "180°", "270°" });
                int newFrozenMoveCount = Mathf.Max(0, EditorGUILayout.IntField(
                    new GUIContent(
                        "Frozen Move Count",
                        "The piece unlocks after this many pieces have been destroyed. Use 0 for a normal piece."),
                    piece.frozenMoveCount));
                float newCounterFontSize = piece.iceCounterFontSize;
                Color newCounterTextColor = piece.iceCounterTextColor;
                Color newCounterOutlineColor = piece.iceCounterOutlineColor;
                float newCounterOutlineWidth = piece.iceCounterOutlineWidth;
                Vector2 newCounterOffset = piece.iceCounterOffset;
                if (newFrozenMoveCount > 0)
                {
                    GUILayout.Label("Ice Counter Text", EditorStyles.boldLabel);
                    newCounterFontSize = Mathf.Max(1f,
                        EditorGUILayout.FloatField("Font Size", piece.iceCounterFontSize));
                    newCounterTextColor = EditorGUILayout.ColorField(
                        "Text Color", piece.iceCounterTextColor);
                    newCounterOutlineColor = EditorGUILayout.ColorField(
                        "Outline Color", piece.iceCounterOutlineColor);
                    newCounterOutlineWidth = EditorGUILayout.Slider(
                        "Outline Width", piece.iceCounterOutlineWidth, 0f, 1f);
                    newCounterOffset = EditorGUILayout.Vector2Field(
                        "Position Offset", piece.iceCounterOffset);
                }

                if (newName != piece.name || newColor != piece.color ||
                    newOrigin != piece.origin || newRotation != piece.quarterTurns ||
                    newFrozenMoveCount != piece.frozenMoveCount ||
                    !Mathf.Approximately(newCounterFontSize, piece.iceCounterFontSize) ||
                    newCounterTextColor != piece.iceCounterTextColor ||
                    newCounterOutlineColor != piece.iceCounterOutlineColor ||
                    !Mathf.Approximately(newCounterOutlineWidth, piece.iceCounterOutlineWidth) ||
                    newCounterOffset != piece.iceCounterOffset)
                {
                    Undo.RecordObject(level, "Edit puzzle piece");
                    piece.name = newName;
                    piece.color = newColor;
                    piece.origin = newOrigin;
                    piece.quarterTurns = newRotation;
                    piece.frozenMoveCount = newFrozenMoveCount;
                    piece.iceCounterFontSize = newCounterFontSize;
                    piece.iceCounterTextColor = newCounterTextColor;
                    piece.iceCounterOutlineColor = newCounterOutlineColor;
                    piece.iceCounterOutlineWidth = newCounterOutlineWidth;
                    piece.iceCounterOffset = newCounterOffset;
                    MarkDirty();
                }

                EditorGUILayout.LabelField("Fine cells", piece.cells.Count.ToString());
                if (GUILayout.Button("Delete Piece"))
                {
                    Undo.RecordObject(level, "Delete puzzle piece");
                    level.pieces.RemoveAt(selectedPiece);
                    ClearSelection();
                    MarkDirty();
                }
                return;
            }

            if (selectedPin >= 0 && selectedPin < level.pins.Count)
            {
                PinDefinition pin = level.pins[selectedPin];
                GUILayout.Label("Selected Pin", EditorStyles.boldLabel);
                string newName = EditorGUILayout.TextField("Name", pin.name);
                Vector2Int newCell = EditorGUILayout.Vector2IntField("Cell", pin.cell);
                float newRadius = Mathf.Max(.1f, EditorGUILayout.FloatField("Radius (fine cells)", pin.radiusInFineCells));
                Color newColor = EditorGUILayout.ColorField("Color", pin.color);
                if (newName != pin.name || newCell != pin.cell || !Mathf.Approximately(newRadius, pin.radiusInFineCells) || newColor != pin.color)
                {
                    Undo.RecordObject(level, "Edit pin");
                    pin.name = newName;
                    pin.cell = newCell;
                    pin.radiusInFineCells = newRadius;
                    pin.color = newColor;
                    MarkDirty();
                }
                if (GUILayout.Button("Delete Pin"))
                {
                    Undo.RecordObject(level, "Delete pin");
                    level.pins.RemoveAt(selectedPin);
                    ClearSelection();
                    MarkDirty();
                }
                return;
            }

            if (selectedObstacle >= 0 && selectedObstacle < level.obstacles.Count)
            {
                ObstacleDefinition obstacle = level.obstacles[selectedObstacle];
                GUILayout.Label("Selected Obstacle", EditorStyles.boldLabel);
                string newName = EditorGUILayout.TextField("Name", obstacle.name);
                int newRotation = EditorGUILayout.Popup("Rotation", obstacle.quarterTurns, new[] { "0°", "90°", "180°", "270°" });
                Color newColor = EditorGUILayout.ColorField("Color", obstacle.color);

                if (obstacle.usesGridCells)
                {
                    Vector2Int newGridCell = EditorGUILayout.Vector2IntField("Grid Cell", obstacle.gridCell);
                    Vector2Int newGridSize = EditorGUILayout.Vector2IntField("Size (cells)", obstacle.sizeInGridCells);
                    newGridSize.x = Mathf.Max(1, newGridSize.x);
                    newGridSize.y = Mathf.Max(1, newGridSize.y);

                    if (newName != obstacle.name || newGridCell != obstacle.gridCell ||
                        newGridSize != obstacle.sizeInGridCells || newRotation != obstacle.quarterTurns ||
                        newColor != obstacle.color)
                    {
                        Undo.RecordObject(level, "Edit modular obstacle");
                        obstacle.name = newName;
                        obstacle.gridCell = newGridCell;
                        obstacle.sizeInGridCells = newGridSize;
                        obstacle.quarterTurns = newRotation;
                        obstacle.color = newColor;
                        MarkDirty();
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "This is a fine-grid obstacle. (Thin obstacle or legacy)",
                        MessageType.Info);
                    
                    Vector2Int newCentre = EditorGUILayout.Vector2IntField("Fine Grid Centre", obstacle.centreCell);
                    Vector2Int newFineSize = EditorGUILayout.Vector2IntField("Fine Grid Size", obstacle.sizeInFineCells);
                    
                    if (newName != obstacle.name || newRotation != obstacle.quarterTurns || newColor != obstacle.color ||
                        newCentre != obstacle.centreCell || newFineSize != obstacle.sizeInFineCells)
                    {
                        Undo.RecordObject(level, "Edit fine-grid obstacle");
                        obstacle.name = newName;
                        obstacle.quarterTurns = newRotation;
                        obstacle.color = newColor;
                        obstacle.centreCell = newCentre;
                        obstacle.sizeInFineCells = newFineSize;
                        MarkDirty();
                    }
                    if (GUILayout.Button("Convert Obstacle to Grid Cells"))
                    {
                        Undo.RecordObject(level, "Convert obstacle to modular grid");
                        ConvertObstacleToGrid(obstacle);
                        MarkDirty();
                    }
                }
                if (GUILayout.Button("Delete Obstacle"))
                {
                    Undo.RecordObject(level, "Delete obstacle");
                    level.obstacles.RemoveAt(selectedObstacle);
                    ClearSelection();
                    MarkDirty();
                }
                return;
            }

            if (selectedShredder >= 0 && selectedShredder < level.shredders.Count)
            {
                ShredderDefinition shredder = level.shredders[selectedShredder];
                GUILayout.Label("Selected Shredder", EditorStyles.boldLabel);
                string newName = EditorGUILayout.TextField("Name", shredder.name);
                Vector2Int newCell = EditorGUILayout.Vector2IntField("Cell", shredder.cell);
                float newRadius = Mathf.Max(.5f, EditorGUILayout.FloatField("Radius (fine cells)", shredder.radiusInFineCells));
                float newSpeed = Mathf.Max(0f, EditorGUILayout.FloatField("Rotation Speed", shredder.rotationSpeed));
                bool newClockwise = EditorGUILayout.Toggle("Clockwise", shredder.clockwise);

                if (newName != shredder.name || newCell != shredder.cell ||
                    !Mathf.Approximately(newRadius, shredder.radiusInFineCells) ||
                    !Mathf.Approximately(newSpeed, shredder.rotationSpeed) || newClockwise != shredder.clockwise)
                {
                    Undo.RecordObject(level, "Edit shredder");
                    shredder.name = newName;
                    shredder.cell = newCell;
                    shredder.radiusInFineCells = newRadius;
                    shredder.rotationSpeed = newSpeed;
                    shredder.clockwise = newClockwise;
                    MarkDirty();
                }

                if (GUILayout.Button("Delete Shredder"))
                {
                    Undo.RecordObject(level, "Delete shredder");
                    level.shredders.RemoveAt(selectedShredder);
                    ClearSelection();
                    MarkDirty();
                }
                return;
            }

            EditorGUILayout.HelpBox("Select a piece, pin, obstacle, or shredder to edit its properties.", MessageType.None);
        }

        private void DrawValidation()
        {
            GUILayout.Label("Modular Validation", EditorStyles.boldLabel);
            if (GUILayout.Button(validationIsCurrent ? "Re-run Validation" : "Run Validation"))
                RunValidation();

            if (!validationIsCurrent)
            {
                EditorGUILayout.HelpBox(
                    "Run validation after editing to check modular blocks, touch size, exits, and piece routes.",
                    MessageType.None);
                return;
            }

            if (validationMessages.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Modular grid, touch size, and exit paths are valid.",
                    MessageType.Info);
                return;
            }

            foreach (string message in validationMessages)
                EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        private void RunValidation()
        {
            validationMessages.Clear();
            validationIsCurrent = true;

            if (level.subdivisions != GravityGridMetrics.FineCellsPerGridCell)
            {
                validationMessages.Add(
                    $"Internal fine grid is {level.subdivisions}; modular levels use {GravityGridMetrics.FineCellsPerGridCell}.");
            }

            float estimatedCellPoints = GravityGridMetrics.EstimatedCellSizeInPoints(
                level.boardColumns,
                level.boardRows);
            if (estimatedCellPoints < GravityGridMetrics.MinimumTouchTargetPoints)
            {
                validationMessages.Add(
                    $"A 1-cell block is approximately {estimatedCellPoints:0} pt on a portrait phone. " +
                    $"Keep it at least {GravityGridMetrics.MinimumTouchTargetPoints:0} pt.");
            }

            if (!Mathf.Approximately(level.exitWidth, Mathf.Round(level.exitWidth)))
                validationMessages.Add("Exit width must be a whole number of grid cells.");

            int exitCells = Mathf.Clamp(Mathf.RoundToInt(level.exitWidth), 1, level.boardColumns);
            if ((level.boardColumns - exitCells) % 2 != 0)
            {
                validationMessages.Add(
                    "The centred exit cuts through a grid cell. Use an exit width with the same odd/even parity as Columns.");
            }

            // Validation for legacy fine-cell cut-outs was removed since Thin Map Cell is supported.

            // Validation for legacy fine-grid obstacles was removed 
            // since fine-grid obstacles are now used as thin obstacles.

            foreach (PieceDefinition piece in level.pieces)
            {
                ValidatePieceModules(piece);

                if (!HasExitPath(piece, 0))
                {
                    validationMessages.Add($"{piece.name} has no collision-free route to the bottom exit.");
                }
            }
        }

        private void ValidatePieceModules(PieceDefinition piece)
        {
            Dictionary<Vector2Int, HashSet<Vector2Int>> blocksByGridCell =
                new Dictionary<Vector2Int, HashSet<Vector2Int>>();
            int blockCellCount = 0;

            foreach (PieceCellDefinition cell in piece.cells)
            {
                if (cell.type != PieceCellType.Block)
                    continue;

                blockCellCount++;
                Vector2Int absolute = piece.origin + QuarterTurnUtility.Rotate(cell.localCell, piece.quarterTurns);
                Vector2Int gridCell = FineToGridCell(absolute);
                if (!blocksByGridCell.TryGetValue(gridCell, out HashSet<Vector2Int> occupied))
                {
                    occupied = new HashSet<Vector2Int>();
                    blocksByGridCell.Add(gridCell, occupied);
                }
                occupied.Add(absolute);
            }

            if (blockCellCount == 0)
            {
                validationMessages.Add($"{piece.name} has no full-cell block core; it is made only from fine hook cells.");
                return;
            }

            int expectedCells = level.subdivisions * level.subdivisions;
            foreach (KeyValuePair<Vector2Int, HashSet<Vector2Int>> module in blocksByGridCell)
            {
                if (module.Value.Count != expectedCells)
                {
                    validationMessages.Add(
                        $"{piece.name} has a partial block at grid cell {module.Key}. Repaint or erase the complete module.");
                }
            }

            if (piece.cells.Count == 0)
                return;

            GetPieceFineBounds(piece, out Vector2Int minimum, out Vector2Int maximum);
            float estimatedCellPoints = GravityGridMetrics.EstimatedCellSizeInPoints(
                level.boardColumns,
                level.boardRows);
            float shortSideInCells = Mathf.Min(
                maximum.x - minimum.x + 1,
                maximum.y - minimum.y + 1) / (float)level.subdivisions;
            if (shortSideInCells * estimatedCellPoints < GravityGridMetrics.MinimumTouchTargetPoints)
            {
                validationMessages.Add(
                    $"{piece.name}'s short side is too small for comfortable touch selection at this grid density.");
            }
        }

        private void DrawCanvas(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(.095f, .1f, .13f));
            float cellSize = Mathf.Min((area.width - 56f) / level.FineColumns, (area.height - 56f) / level.FineRows);
            cellSize = Mathf.Max(4f, cellSize);
            Vector2 boardSize = new Vector2(level.FineColumns * cellSize, level.FineRows * cellSize);
            Rect board = new Rect(
                area.center.x - boardSize.x * .5f,
                area.center.y - boardSize.y * .5f,
                boardSize.x,
                boardSize.y);

            DrawBoardShape(board, cellSize);
            DrawObstacles(board, cellSize);
            DrawPins(board, cellSize);
            DrawShredders(board, cellSize);
            DrawPieces(board, cellSize);
            DrawGrid(board, cellSize);
            HandleCanvasInput(board, cellSize);
        }

        private void DrawBoardShape(Rect board, float cellSize)
        {
            EditorGUI.DrawRect(board, new Color(.035f, .04f, .055f));
            Color alternate = Color.Lerp(level.backgroundColor, Color.white, .08f);

            for (int y = 0; y < level.FineRows; y++)
            {
                for (int x = 0; x < level.FineColumns; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!IsFineCellActive(cell))
                    {
                        EditorGUI.DrawRect(CellRect(board, cellSize, cell), InactiveMapShapeColor);
                        continue;
                    }

                    Rect rect = CellRect(board, cellSize, cell);
                    EditorGUI.DrawRect(
                        rect,
                        (x / level.subdivisions + y / level.subdivisions) % 2 == 0
                            ? level.backgroundColor
                            : alternate);
                }
            }

            Handles.BeginGUI();
            Handles.color = level.frameColor;
            for (int y = 0; y < level.FineRows; y++)
            {
                for (int x = 0; x < level.FineColumns; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!IsFineCellActive(cell))
                        continue;

                    Rect rect = CellRect(board, cellSize, cell);
                    if (!IsFineCellActive(cell + Vector2Int.left))
                        Handles.DrawAAPolyLine(3f, new Vector3(rect.x, rect.y), new Vector3(rect.x, rect.yMax));
                    if (!IsFineCellActive(cell + Vector2Int.right))
                        Handles.DrawAAPolyLine(3f, new Vector3(rect.xMax, rect.y), new Vector3(rect.xMax, rect.yMax));
                    if (!IsFineCellActive(cell + Vector2Int.up))
                        Handles.DrawAAPolyLine(3f, new Vector3(rect.x, rect.y), new Vector3(rect.xMax, rect.y));
                    if (!IsFineCellActive(cell + Vector2Int.down) &&
                        !(cell.y == 0 && IsFineColumnInsideExit(cell.x)))
                        Handles.DrawAAPolyLine(3f, new Vector3(rect.x, rect.yMax), new Vector3(rect.xMax, rect.yMax));
                }
            }
            Handles.EndGUI();
        }

        private void DrawGrid(Rect board, float cellSize)
        {
            Handles.BeginGUI();
            for (int x = 0; x <= level.FineColumns; x++)
            {
                bool major = x % level.subdivisions == 0;
                Handles.color = major ? new Color(1f, 1f, 1f, .28f) : new Color(1f, 1f, 1f, .09f);
                float px = board.x + x * cellSize;
                Handles.DrawLine(new Vector3(px, board.y), new Vector3(px, board.yMax));
            }
            for (int y = 0; y <= level.FineRows; y++)
            {
                bool major = y % level.subdivisions == 0;
                Handles.color = major ? new Color(1f, 1f, 1f, .28f) : new Color(1f, 1f, 1f, .09f);
                float py = board.y + y * cellSize;
                Handles.DrawLine(new Vector3(board.x, py), new Vector3(board.xMax, py));
            }
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(2f,
                new Vector3(board.x, board.y), new Vector3(board.xMax, board.y),
                new Vector3(board.xMax, board.yMax), new Vector3(board.x, board.yMax),
                new Vector3(board.x, board.y));
            Handles.EndGUI();
        }

        private void DrawPieces(Rect board, float cellSize)
        {
            for (int pieceIndex = 0; pieceIndex < level.pieces.Count; pieceIndex++)
            {
                PieceDefinition piece = level.pieces[pieceIndex];
                Rect pieceBounds = default;
                bool hasVisibleCell = false;
                foreach (PieceCellDefinition cell in piece.cells)
                {
                    Vector2Int absolute = piece.origin + QuarterTurnUtility.Rotate(cell.localCell, piece.quarterTurns);
                    if (!IsInside(absolute))
                        continue;

                    Rect rect = CellRect(board, cellSize, absolute);
                    if (hasVisibleCell)
                    {
                        float xMin = Mathf.Min(pieceBounds.xMin, rect.xMin);
                        float yMin = Mathf.Min(pieceBounds.yMin, rect.yMin);
                        float xMax = Mathf.Max(pieceBounds.xMax, rect.xMax);
                        float yMax = Mathf.Max(pieceBounds.yMax, rect.yMax);
                        pieceBounds = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                    }
                    else
                    {
                        pieceBounds = rect;
                        hasVisibleCell = true;
                    }

                    Color color = cell.type == PieceCellType.Hook
                        ? Color.Lerp(piece.color, Color.white, .22f)
                        : piece.color;
                    EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), color);
                    if (piece.frozenMoveCount > 0)
                    {
                        Color ice = new Color(.55f, .9f, 1f, .48f);
                        EditorGUI.DrawRect(
                            new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f),
                            ice);
                    }
                }

                if (piece.frozenMoveCount > 0 && hasVisibleCell)
                {
                    float worldToEditorPixels = cellSize * level.subdivisions;
                    pieceBounds.position += new Vector2(
                        piece.iceCounterOffset.x * worldToEditorPixels,
                        -piece.iceCounterOffset.y * worldToEditorPixels);
                    DrawIceCounterPreview(pieceBounds, piece);
                }

                if (pieceIndex == selectedPiece && IsInside(piece.origin))
                {
                    Rect origin = CellRect(board, cellSize, piece.origin);
                    EditorGUI.DrawRect(new Rect(origin.center.x - 3f, origin.center.y - 3f, 6f, 6f), Color.white);
                }
            }
        }

        private static void DrawIceCounterPreview(
            Rect bounds,
            PieceDefinition piece)
        {
            // The Level Editor is the visual source of truth: this value is a
            // normal GUI point size. Runtime converts the same value to TMP's
            // much larger world-space typography scale.
            int previewFontSize = Mathf.Clamp(
                Mathf.RoundToInt(piece.iceCounterFontSize), 8, 96);
            GUIStyle textStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = previewFontSize
            };

            string count = piece.frozenMoveCount.ToString();
            int outlinePixels = Mathf.Max(
                1,
                Mathf.RoundToInt(previewFontSize * piece.iceCounterOutlineWidth * .08f));
            textStyle.normal.textColor = piece.iceCounterOutlineColor;
            GUI.Label(new Rect(bounds.x - outlinePixels, bounds.y, bounds.width, bounds.height), count, textStyle);
            GUI.Label(new Rect(bounds.x + outlinePixels, bounds.y, bounds.width, bounds.height), count, textStyle);
            GUI.Label(new Rect(bounds.x, bounds.y - outlinePixels, bounds.width, bounds.height), count, textStyle);
            GUI.Label(new Rect(bounds.x, bounds.y + outlinePixels, bounds.width, bounds.height), count, textStyle);

            textStyle.normal.textColor = piece.iceCounterTextColor;
            GUI.Label(bounds, count, textStyle);
        }

        private void DrawPins(Rect board, float cellSize)
        {
            for (int i = 0; i < level.pins.Count; i++)
            {
                PinDefinition pin = level.pins[i];
                Vector2 centre = CellRect(board, cellSize, pin.cell).center;
                float diameter = pin.radiusInFineCells * cellSize * 2f;
                Rect pinRect = new Rect(centre.x - diameter * .5f, centre.y - diameter * .5f, diameter, diameter);
                Handles.BeginGUI();
                Handles.color = pin.color;
                Handles.DrawSolidDisc(centre, Vector3.forward, diameter * .5f);
                if (i == selectedPin)
                {
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(centre, Vector3.forward, diameter * .5f + 2f);
                }
                Handles.EndGUI();
            }
        }

        private void DrawShredders(Rect board, float cellSize)
        {
            for (int i = 0; i < level.shredders.Count; i++)
            {
                ShredderDefinition shredder = level.shredders[i];
                Vector2 centre = CellRect(board, cellSize, shredder.cell).center;
                float radius = shredder.radiusInFineCells * cellSize;

                Handles.BeginGUI();
                Handles.color = new Color(.32f, .36f, .48f);
                Handles.DrawSolidDisc(centre, Vector3.forward, radius * .78f);
                Handles.color = new Color(.75f, .8f, .92f);
                Handles.DrawWireDisc(centre, Vector3.forward, radius);

                for (int tooth = 0; tooth < 12; tooth++)
                {
                    float angle = tooth * Mathf.PI * 2f / 12f;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Handles.DrawLine(centre + direction * radius * .72f, centre + direction * radius);
                }

                if (i == selectedShredder)
                {
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(centre, Vector3.forward, radius + 3f);
                }
                Handles.EndGUI();
            }
        }

        private void DrawObstacles(Rect board, float cellSize)
        {
            for (int i = 0; i < level.obstacles.Count; i++)
            {
                ObstacleDefinition obstacle = level.obstacles[i];
                Rect rect;
                if (obstacle.usesGridCells)
                {
                    Vector2Int gridSize = RotatedGridSize(obstacle);
                    rect = GridRect(board, cellSize, obstacle.gridCell, gridSize);
                }
                else
                {
                    Vector2Int fineSize = RotatedFineSize(obstacle);
                    Vector2 centre = CellRect(board, cellSize, obstacle.centreCell).center;
                    rect = new Rect(
                        centre.x - fineSize.x * cellSize * .5f,
                        centre.y - fineSize.y * cellSize * .5f,
                        fineSize.x * cellSize,
                        fineSize.y * cellSize);
                }
                EditorGUI.DrawRect(rect, obstacle.color);
                if (i == selectedObstacle)
                {
                    Handles.BeginGUI();
                    Handles.color = Color.white;
                    Handles.DrawAAPolyLine(2f,
                        new Vector3(rect.x, rect.y), new Vector3(rect.xMax, rect.y),
                        new Vector3(rect.xMax, rect.yMax), new Vector3(rect.x, rect.yMax),
                        new Vector3(rect.x, rect.y));
                    Handles.EndGUI();
                }
            }
        }

        private void HandleCanvasInput(Rect board, float cellSize)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseUp && current.button == 0)
            {
                mapShapeStrokeActive = false;
                lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
                return;
            }

            bool pointerEvent = current.type == EventType.MouseDown || current.type == EventType.MouseDrag;
            if (!pointerEvent || current.button != 0 || !board.Contains(current.mousePosition))
                return;

            Vector2Int cell = MouseToCell(board, cellSize, current.mousePosition);
            if (current.type == EventType.MouseDrag && cell == lastPaintedCell)
                return;
            lastPaintedCell = cell;

            switch (tool)
            {
                case EditTool.Select:
                case EditTool.MovePiece:
                    if (current.type == EventType.MouseDown)
                        SelectAt(cell);
                    else if (current.type == EventType.MouseDrag)
                        MoveSelectedItemTo(cell + dragOffset);
                    break;
                case EditTool.MapShape:
                    if (current.type == EventType.MouseDown)
                    {
                        mapShapeStrokeActive = true;
                        mapShapeStrokeMakesInactive = IsFineCellActive(cell);
                    }
                    if (mapShapeStrokeActive)
                        PaintBoardCell(cell, mapShapeStrokeMakesInactive);
                    break;
                case EditTool.ThinMapShape:
                    if (current.type == EventType.MouseDown)
                    {
                        mapShapeStrokeActive = true;
                        mapShapeStrokeMakesInactive = IsFineCellActive(cell);
                    }
                    if (mapShapeStrokeActive)
                        PaintFineBoardCell(cell, mapShapeStrokeMakesInactive);
                    break;
                case EditTool.PaintBlock: PaintBlock(cell); break;
                case EditTool.PaintHook: PaintFineCell(cell, PieceCellType.Hook); break;
                case EditTool.Pin: if (current.type == EventType.MouseDown) AddPin(cell); break;
                case EditTool.Obstacle: AddObstacle(cell); break;
                case EditTool.ThinObstacle: AddThinObstacle(cell); break;
                case EditTool.Shredder: if (current.type == EventType.MouseDown) AddShredder(cell); break;
                case EditTool.Erase: EraseAt(cell); break;
            }

            current.Use();
            Repaint();
        }

        private void PaintBlock(Vector2Int clickedCell)
        {
            if (!HasSelectedPiece() || !IsInside(clickedCell))
                return;

            Vector2Int coarseOrigin = new Vector2Int(
                clickedCell.x / level.subdivisions * level.subdivisions,
                clickedCell.y / level.subdivisions * level.subdivisions);
            Undo.RecordObject(level, "Paint block");
            for (int y = 0; y < level.subdivisions; y++)
            for (int x = 0; x < level.subdivisions; x++)
            {
                Vector2Int fineCell = coarseOrigin + new Vector2Int(x, y);
                if (IsInside(fineCell))
                    SetPieceCell(level.pieces[selectedPiece], fineCell, PieceCellType.Block);
            }
            MarkDirty();
        }

        private void PaintFineCell(Vector2Int cell, PieceCellType type)
        {
            if (!HasSelectedPiece() || !IsInside(cell))
                return;
            Undo.RecordObject(level, "Paint hook");
            SetPieceCell(level.pieces[selectedPiece], cell, type);
            MarkDirty();
        }

        private void PaintBoardCell(Vector2Int fineCell, bool makeInactive)
        {
            Undo.RecordObject(level, "Edit map shape");

            if (level.inactiveFineCells == null)
                level.inactiveFineCells = new List<Vector2Int>();
            if (level.inactiveBoardCells == null)
                level.inactiveBoardCells = new List<Vector2Int>();

            Vector2Int coarseCell = new Vector2Int(
                fineCell.x / level.subdivisions,
                fineCell.y / level.subdivisions);
            Vector2Int coarseOrigin = coarseCell * level.subdivisions;
            level.inactiveFineCells.RemoveAll(cell =>
                cell.x >= coarseOrigin.x && cell.x < coarseOrigin.x + level.subdivisions &&
                cell.y >= coarseOrigin.y && cell.y < coarseOrigin.y + level.subdivisions);

            if (makeInactive)
            {
                if (!level.inactiveBoardCells.Contains(coarseCell))
                    level.inactiveBoardCells.Add(coarseCell);
            }
            else
            {
                level.inactiveBoardCells.Remove(coarseCell);
            }

            ClearSelection();
            MarkDirty();
        }

        private void PaintFineBoardCell(Vector2Int fineCell, bool makeInactive)
        {
            Undo.RecordObject(level, "Edit fine map shape");

            if (level.inactiveFineCells == null)
                level.inactiveFineCells = new List<Vector2Int>();
            if (level.inactiveBoardCells == null)
                level.inactiveBoardCells = new List<Vector2Int>();

            Vector2Int coarseCell = new Vector2Int(
                fineCell.x / level.subdivisions,
                fineCell.y / level.subdivisions);

            if (level.inactiveBoardCells.Contains(coarseCell))
            {
                level.inactiveBoardCells.Remove(coarseCell);
                Vector2Int coarseOrigin = coarseCell * level.subdivisions;
                for (int y = 0; y < level.subdivisions; y++)
                for (int x = 0; x < level.subdivisions; x++)
                {
                    Vector2Int f = coarseOrigin + new Vector2Int(x, y);
                    if (!level.inactiveFineCells.Contains(f))
                        level.inactiveFineCells.Add(f);
                }
            }

            if (makeInactive)
            {
                if (!level.inactiveFineCells.Contains(fineCell))
                    level.inactiveFineCells.Add(fineCell);
            }
            else
            {
                level.inactiveFineCells.Remove(fineCell);
            }

            ClearSelection();
            MarkDirty();
        }

        private void SetPieceCell(PieceDefinition piece, Vector2Int absoluteCell, PieceCellType type)
        {
            Vector2Int local = QuarterTurnUtility.InverseRotate(absoluteCell - piece.origin, piece.quarterTurns);
            PieceCellDefinition existing = piece.cells.Find(c => c.localCell == local);
            if (existing != null)
                existing.type = type;
            else
                piece.cells.Add(new PieceCellDefinition(local, type));
        }

        private void EraseAt(Vector2Int absoluteCell)
        {
            Undo.RecordObject(level, "Erase level item");
            for (int i = level.pieces.Count - 1; i >= 0; i--)
            {
                PieceDefinition piece = level.pieces[i];
                Vector2Int local = QuarterTurnUtility.InverseRotate(absoluteCell - piece.origin, piece.quarterTurns);
                PieceCellDefinition hitCell = piece.cells.Find(c => c.localCell == local);
                if (hitCell == null)
                    continue;

                int removed;
                if (hitCell.type == PieceCellType.Hook)
                {
                    removed = piece.cells.RemoveAll(c => c.localCell == local);
                }
                else
                {
                    Vector2Int gridCell = FineToGridCell(absoluteCell);
                    removed = piece.cells.RemoveAll(c =>
                    {
                        Vector2Int absolute = piece.origin + QuarterTurnUtility.Rotate(c.localCell, piece.quarterTurns);
                        return c.type == PieceCellType.Block && FineToGridCell(absolute) == gridCell;
                    });
                }
                if (removed > 0)
                {
                    SelectPiece(i);
                    MarkDirty();
                    return;
                }
            }

            int pinIndex = level.pins.FindIndex(p => p.cell == absoluteCell);
            if (pinIndex >= 0)
            {
                level.pins.RemoveAt(pinIndex);
                ClearSelection();
                MarkDirty();
                return;
            }

            int shredderIndex = FindShredderAt(absoluteCell);
            if (shredderIndex >= 0)
            {
                level.shredders.RemoveAt(shredderIndex);
                ClearSelection();
                MarkDirty();
                return;
            }

            int obstacleIndex = level.obstacles.FindIndex(o => ObstacleContains(o, absoluteCell));
            if (obstacleIndex >= 0)
            {
                level.obstacles.RemoveAt(obstacleIndex);
                ClearSelection();
                MarkDirty();
            }
        }

        private void SelectAt(Vector2Int cell)
        {
            for (int i = level.pieces.Count - 1; i >= 0; i--)
            {
                PieceDefinition piece = level.pieces[i];
                Vector2Int local = QuarterTurnUtility.InverseRotate(cell - piece.origin, piece.quarterTurns);
                if (piece.cells.Exists(c => c.localCell == local))
                {
                    SelectPiece(i);
                    dragOffset = piece.origin - cell;
                    return;
                }
            }

            int pin = level.pins.FindIndex(p => p.cell == cell);
            if (pin >= 0)
            {
                selectedPiece = -1;
                selectedObstacle = -1;
                selectedShredder = -1;
                selectedPin = pin;
                dragOffset = level.pins[pin].cell - cell;
                return;
            }

            int shredder = FindShredderAt(cell);
            if (shredder >= 0)
            {
                ClearSelection();
                selectedShredder = shredder;
                dragOffset = level.shredders[shredder].cell - cell;
                return;
            }

            int obstacle = level.obstacles.FindIndex(o => ObstacleContains(o, cell));
            ClearSelection();
            if (obstacle >= 0)
            {
                selectedObstacle = obstacle;
                var obs = level.obstacles[obstacle];
                dragOffset = (obs.usesGridCells ? (obs.gridCell * level.subdivisions) : obs.centreCell) - cell;
            }
        }

        private void MoveSelectedItemTo(Vector2Int targetFineCell)
        {
            if (HasSelectedPiece())
            {
                Undo.RecordObject(level, "Move puzzle piece");
                PieceDefinition piece = level.pieces[selectedPiece];
                piece.origin = targetFineCell;
                MarkDirty();
            }
            else if (selectedPin >= 0 && selectedPin < level.pins.Count)
            {
                Undo.RecordObject(level, "Move pin");
                level.pins[selectedPin].cell = targetFineCell;
                MarkDirty();
            }
            else if (selectedObstacle >= 0 && selectedObstacle < level.obstacles.Count)
            {
                Undo.RecordObject(level, "Move obstacle");
                ObstacleDefinition obstacle = level.obstacles[selectedObstacle];
                if (obstacle.usesGridCells)
                    obstacle.gridCell = FineToGridCell(targetFineCell);
                else
                    obstacle.centreCell = targetFineCell;
                MarkDirty();
            }
            else if (selectedShredder >= 0 && selectedShredder < level.shredders.Count)
            {
                Undo.RecordObject(level, "Move shredder");
                level.shredders[selectedShredder].cell = targetFineCell;
                MarkDirty();
            }
        }

        private void AddPin(Vector2Int cell)
        {
            if (!IsInside(cell))
                return;

            Undo.RecordObject(level, "Add pin");
            level.pins.Add(new PinDefinition { name = $"Pin {level.pins.Count + 1}", cell = cell });
            selectedPiece = -1;
            selectedObstacle = -1;
            selectedShredder = -1;
            selectedPin = level.pins.Count - 1;
            MarkDirty();
        }

        private void AddObstacle(Vector2Int cell)
        {
            if (!IsInside(cell))
                return;

            Vector2Int gridCell = FineToGridCell(cell);
            if (level.obstacles.Exists(o => o.usesGridCells && o.gridCell == gridCell))
                return;

            Undo.RecordObject(level, "Add obstacle");
            level.obstacles.Add(new ObstacleDefinition
            {
                name = $"Obstacle {level.obstacles.Count + 1}",
                usesGridCells = true,
                gridCell = gridCell,
                sizeInGridCells = Vector2Int.one
            });
            selectedPiece = -1;
            selectedPin = -1;
            selectedShredder = -1;
            selectedObstacle = level.obstacles.Count - 1;
            MarkDirty();
        }

        private void AddThinObstacle(Vector2Int cell)
        {
            if (!IsInside(cell))
                return;

            if (level.obstacles.Exists(o => !o.usesGridCells && o.centreCell == cell))
                return;

            Undo.RecordObject(level, "Add thin obstacle");
            level.obstacles.Add(new ObstacleDefinition
            {
                name = $"Thin Obstacle {level.obstacles.Count + 1}",
                usesGridCells = false,
                centreCell = cell,
                sizeInFineCells = Vector2Int.one
            });
            selectedPiece = -1;
            selectedPin = -1;
            selectedShredder = -1;
            selectedObstacle = level.obstacles.Count - 1;
            MarkDirty();
        }

        private void AddShredder(Vector2Int cell)
        {
            if (!IsInside(cell))
                return;

            Undo.RecordObject(level, "Add shredder");
            level.shredders.Add(new ShredderDefinition
            {
                name = $"Shredder {level.shredders.Count + 1}",
                cell = cell,
                clockwise = level.shredders.Count % 2 == 0
            });
            selectedPiece = -1;
            selectedPin = -1;
            selectedObstacle = -1;
            selectedShredder = level.shredders.Count - 1;
            MarkDirty();
        }

        private void AddPiece()
        {
            Undo.RecordObject(level, "Add puzzle piece");
            PieceDefinition piece = new PieceDefinition
            {
                name = $"Piece {level.pieces.Count + 1}",
                origin = new Vector2Int(level.FineColumns / 2, level.FineRows / 2),
                color = Color.HSVToRGB((level.pieces.Count * .19f) % 1f, .7f, 1f)
            };
            level.pieces.Add(piece);
            SelectPiece(level.pieces.Count - 1);
            tool = EditTool.PaintBlock;
            MarkDirty();
        }

        private void CreateNewLevel()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Gravity Puzzle Level",
                "GravityLevel_01",
                "asset",
                "Choose where to save the level asset.");
            if (string.IsNullOrEmpty(path))
                return;

            GravityLevelDefinition created = CreateInstance<GravityLevelDefinition>();
            created.subdivisions = GravityGridMetrics.FineCellsPerGridCell;
            int modularExitWidth = Mathf.Clamp(Mathf.RoundToInt(created.exitWidth), 1, created.boardColumns);
            if ((created.boardColumns - modularExitWidth) % 2 != 0)
                modularExitWidth = Mathf.Min(created.boardColumns, modularExitWidth + 1);
            created.exitWidth = modularExitWidth;
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            level = created;
            ClearSelection();
            AddPiece();
            Selection.activeObject = created;
        }

        private void SaveLevel()
        {
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
        }

        private void PlayPreview()
        {
            SaveLevel();
            EditorPrefs.SetString(PreviewPathKey, AssetDatabase.GetAssetPath(level));
            EditorApplication.isPlaying = true;
        }

        private void SelectPiece(int index)
        {
            selectedPiece = index;
            selectedPin = -1;
            selectedObstacle = -1;
            selectedShredder = -1;
        }

        private void ClearSelection()
        {
            selectedPiece = -1;
            selectedPin = -1;
            selectedObstacle = -1;
            selectedShredder = -1;
        }

        private bool HasSelectedPiece()
        {
            return selectedPiece >= 0 && selectedPiece < level.pieces.Count;
        }

        private bool IsInside(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= level.FineColumns || cell.y >= level.FineRows)
                return false;

            return IsFineCellActive(cell);
        }

        private bool IsFineCellActive(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= level.FineColumns || cell.y >= level.FineRows)
                return false;

            if (level.inactiveFineCells != null && level.inactiveFineCells.Contains(cell))
                return false;

            Vector2Int coarseCell = new Vector2Int(cell.x / level.subdivisions, cell.y / level.subdivisions);
            return level.inactiveBoardCells == null || !level.inactiveBoardCells.Contains(coarseCell);
        }

        private bool ObstacleContains(ObstacleDefinition obstacle, Vector2Int cell)
        {
            if (obstacle.usesGridCells)
            {
                Vector2Int gridCell = FineToGridCell(cell);
                Vector2Int gridSize = RotatedGridSize(obstacle);
                return gridCell.x >= obstacle.gridCell.x &&
                       gridCell.y >= obstacle.gridCell.y &&
                       gridCell.x < obstacle.gridCell.x + gridSize.x &&
                       gridCell.y < obstacle.gridCell.y + gridSize.y;
            }

            Vector2Int fineSize = RotatedFineSize(obstacle);
            Vector2 delta = (Vector2)cell - obstacle.centreCell;
            return Mathf.Abs(delta.x) <= fineSize.x * .5f && Mathf.Abs(delta.y) <= fineSize.y * .5f;
        }

        private int FindShredderAt(Vector2Int cell)
        {
            for (int i = level.shredders.Count - 1; i >= 0; i--)
            {
                ShredderDefinition shredder = level.shredders[i];
                if (Vector2.Distance(shredder.cell, cell) <= shredder.radiusInFineCells)
                    return i;
            }

            return -1;
        }

        private static Vector2Int RotatedFineSize(ObstacleDefinition obstacle)
        {
            return obstacle.quarterTurns % 2 == 0
                ? obstacle.sizeInFineCells
                : new Vector2Int(obstacle.sizeInFineCells.y, obstacle.sizeInFineCells.x);
        }

        private static Vector2Int RotatedGridSize(ObstacleDefinition obstacle)
        {
            Vector2Int size = new Vector2Int(
                Mathf.Max(1, obstacle.sizeInGridCells.x),
                Mathf.Max(1, obstacle.sizeInGridCells.y));
            return obstacle.quarterTurns % 2 == 0
                ? size
                : new Vector2Int(size.y, size.x);
        }

        private Vector2Int FineToGridCell(Vector2Int fineCell)
        {
            return new Vector2Int(
                Mathf.FloorToInt((float)fineCell.x / level.subdivisions),
                Mathf.FloorToInt((float)fineCell.y / level.subdivisions));
        }

        private Vector2Int MouseToCell(Rect board, float cellSize, Vector2 mouse)
        {
            int x = Mathf.FloorToInt((mouse.x - board.x) / cellSize);
            int rowFromTop = Mathf.FloorToInt((mouse.y - board.y) / cellSize);
            return new Vector2Int(x, level.FineRows - 1 - rowFromTop);
        }

        private Rect CellRect(Rect board, float cellSize, Vector2Int cell)
        {
            return new Rect(
                board.x + cell.x * cellSize,
                board.y + (level.FineRows - 1 - cell.y) * cellSize,
                cellSize,
                cellSize);
        }

        private Rect GridRect(Rect board, float fineCellSize, Vector2Int gridCell, Vector2Int gridSize)
        {
            float width = gridSize.x * level.subdivisions * fineCellSize;
            float height = gridSize.y * level.subdivisions * fineCellSize;
            return new Rect(
                board.x + gridCell.x * level.subdivisions * fineCellSize,
                board.y + (level.FineRows - (gridCell.y + gridSize.y) * level.subdivisions) * fineCellSize,
                width,
                height);
        }

        private void ConvertObstacleToGrid(ObstacleDefinition obstacle)
        {
            Vector2Int fineSize = RotatedFineSize(obstacle);
            Vector2 fineMinimum = (Vector2)obstacle.centreCell - (Vector2)fineSize * .5f;
            obstacle.gridCell = new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt(fineMinimum.x / level.subdivisions), 0, level.boardColumns - 1),
                Mathf.Clamp(Mathf.RoundToInt(fineMinimum.y / level.subdivisions), 0, level.boardRows - 1));
            obstacle.sizeInGridCells = new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt((float)fineSize.x / level.subdivisions)),
                Mathf.Max(1, Mathf.RoundToInt((float)fineSize.y / level.subdivisions)));
            obstacle.quarterTurns = 0;
            obstacle.usesGridCells = true;
        }

        private bool HasExitPath(PieceDefinition piece, int clearanceInFineCells)
        {
            if (piece.cells == null || piece.cells.Count == 0)
                return false;

            HashSet<Vector2Int> pieceCells = new HashSet<Vector2Int>();
            foreach (PieceCellDefinition cell in piece.cells)
            {
                pieceCells.Add(
                    piece.origin + QuarterTurnUtility.Rotate(cell.localCell, piece.quarterTurns));
            }

            HashSet<Vector2Int> blockedCells = BuildObstacleCells();
            Queue<Vector2Int> frontier = new Queue<Vector2Int>();
            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            Vector2Int start = Vector2Int.zero;
            if (!CanPlacePiece(pieceCells, start, clearanceInFineCells, blockedCells))
                return false;

            frontier.Enqueue(start);
            visited.Add(start);
            Vector2Int[] directions =
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.up,
                Vector2Int.down
            };

            while (frontier.Count > 0)
            {
                Vector2Int translation = frontier.Dequeue();
                bool belowBoard = true;
                foreach (Vector2Int cell in pieceCells)
                {
                    if (cell.y + translation.y >= 0)
                    {
                        belowBoard = false;
                        break;
                    }
                }
                if (belowBoard)
                    return true;

                foreach (Vector2Int direction in directions)
                {
                    Vector2Int next = translation + direction;
                    if (visited.Contains(next))
                        continue;
                    if (!CanPlacePiece(pieceCells, next, clearanceInFineCells, blockedCells))
                        continue;

                    visited.Add(next);
                    frontier.Enqueue(next);
                }
            }

            return false;
        }

        private bool CanPlacePiece(
            HashSet<Vector2Int> pieceCells,
            Vector2Int translation,
            int clearance,
            HashSet<Vector2Int> blockedCells)
        {
            foreach (Vector2Int sourceCell in pieceCells)
            {
                Vector2Int translatedCell = sourceCell + translation;
                for (int y = -clearance; y <= clearance; y++)
                for (int x = -clearance; x <= clearance; x++)
                {
                    Vector2Int testCell = translatedCell + new Vector2Int(x, y);
                    if (testCell.x < 0 || testCell.x >= level.FineColumns || testCell.y >= level.FineRows)
                        return false;

                    if (testCell.y < 0)
                    {
                        if (!IsFineColumnInsideExit(testCell.x))
                            return false;
                        continue;
                    }

                    if (!IsFineCellActive(testCell) || blockedCells.Contains(testCell))
                        return false;
                }
            }

            return true;
        }

        private HashSet<Vector2Int> BuildObstacleCells()
        {
            HashSet<Vector2Int> blockedCells = new HashSet<Vector2Int>();
            foreach (ObstacleDefinition obstacle in level.obstacles)
            {
                if (obstacle.usesGridCells)
                {
                    Vector2Int size = RotatedGridSize(obstacle);
                    Vector2Int fineOrigin = obstacle.gridCell * level.subdivisions;
                    Vector2Int fineSize = size * level.subdivisions;
                    for (int y = 0; y < fineSize.y; y++)
                    for (int x = 0; x < fineSize.x; x++)
                        blockedCells.Add(fineOrigin + new Vector2Int(x, y));
                    continue;
                }

                for (int y = 0; y < level.FineRows; y++)
                for (int x = 0; x < level.FineColumns; x++)
                {
                    Vector2Int fineCell = new Vector2Int(x, y);
                    if (ObstacleContains(obstacle, fineCell))
                        blockedCells.Add(fineCell);
                }
            }
            return blockedCells;
        }

        private bool IsFineColumnInsideExit(int fineColumn)
        {
            float fineCellSize = 1f / level.subdivisions;
            float cellLeft = -level.boardColumns * .5f + fineColumn * fineCellSize;
            float cellRight = cellLeft + fineCellSize;
            float halfExit = Mathf.Clamp(level.exitWidth, 1f, level.boardColumns) * .5f;
            const float tolerance = .0001f;
            return cellLeft >= -halfExit - tolerance && cellRight <= halfExit + tolerance;
        }

        private static void GetPieceFineBounds(
            PieceDefinition piece,
            out Vector2Int minimum,
            out Vector2Int maximum)
        {
            minimum = new Vector2Int(int.MaxValue, int.MaxValue);
            maximum = new Vector2Int(int.MinValue, int.MinValue);
            foreach (PieceCellDefinition cell in piece.cells)
            {
                Vector2Int absolute = piece.origin + QuarterTurnUtility.Rotate(cell.localCell, piece.quarterTurns);
                minimum = Vector2Int.Min(minimum, absolute);
                maximum = Vector2Int.Max(maximum, absolute);
            }
        }

        private void MarkDirty()
        {
            validationIsCurrent = false;
            EditorUtility.SetDirty(level);
            Repaint();
        }
    }
}
