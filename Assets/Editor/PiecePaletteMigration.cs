#if UNITY_EDITOR
using GravityPuzzle.Config;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// One-time authoring migration from legacy per-piece tint values to the
    /// shared Scene_Tuna material palette. Runtime never guesses a palette
    /// from a colour; new levels must select their semantic palette directly.
    /// </summary>
    public static class PiecePaletteMigration
    {
        private const string VisualConfigPath = "Assets/PieceVisualConfig.asset";

        [MenuItem("Gravity Puzzle/Art/Migrate Legacy Piece Colours To Material Palette")]
        private static void MigrateAllLevels()
        {
            MigrateAllLevels(false);
        }

        [MenuItem("Gravity Puzzle/Art/Rebuild Automatic Material Palette Assignments")]
        private static void RebuildAutomaticAssignments()
        {
            MigrateAllLevels(true);
        }

        private static void MigrateAllLevels(bool replaceExistingAssignments)
        {
            PieceVisualConfig visualConfig =
                AssetDatabase.LoadAssetAtPath<PieceVisualConfig>(VisualConfigPath);
            if (visualConfig == null || visualConfig.PaletteDefinitions.Count == 0)
            {
                Debug.LogError("[PiecePaletteMigration] PieceVisualConfig has no configured material palette.");
                return;
            }

            // The runtime sequence contains both campaign assets under
            // Assets/ordered and newer authoring assets under Assets/Levels.
            // Restricting this migration to one folder leaves live campaign
            // levels on the legacy tint/fallback path, so search the project
            // for the explicit level type instead. Existing semantic choices
            // are still preserved unless the deliberate rebuild menu is used.
            string[] levelGuids = AssetDatabase.FindAssets("t:GravityLevelDefinition", new[] { "Assets" });
            int migratedPieces = 0;
            int changedLevels = 0;
            for (int levelIndex = 0; levelIndex < levelGuids.Length; levelIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(levelGuids[levelIndex]);
                GravityLevelDefinition level =
                    AssetDatabase.LoadAssetAtPath<GravityLevelDefinition>(assetPath);
                if (level == null)
                    continue;

                bool changed = false;
                foreach (PieceDefinition piece in level.EnumerateAllPieceDefinitions())
                {
                    if (piece == null ||
                        (!replaceExistingAssignments && !string.IsNullOrWhiteSpace(piece.paletteId)))
                        continue;

                    if (!TryResolveClosestPalette(piece.color, visualConfig, out string paletteId))
                    {
                        Debug.LogWarning(
                            $"[PiecePaletteMigration] '{level.name}/{piece.name}' has no usable palette match.",
                            level);
                        continue;
                    }

                    if (piece.paletteId == paletteId)
                        continue;

                    piece.paletteId = paletteId;
                    changed = true;
                    migratedPieces++;
                }

                if (!changed)
                    continue;

                EditorUtility.SetDirty(level);
                changedLevels++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[PiecePaletteMigration] Migrated {migratedPieces} pieces across {changedLevels} level assets to shared material palettes.");
        }

        private static bool TryResolveClosestPalette(
            Color source,
            PieceVisualConfig visualConfig,
            out string paletteId)
        {
            Color.RGBToHSV(source, out float hue, out float saturation, out _);
            if (saturation < .12f)
            {
                paletteId = "BlueDark";
                return visualConfig.TryGetPaletteMaterial(paletteId, out _);
            }

            // Legacy levels use lightened source tints, while the final
            // materials are deeply saturated. Map by the deliberately
            // authored hue bands rather than nearest RGB distance, which
            // incorrectly turns legacy red pieces orange.
            if (hue < .045f)
                paletteId = source.g < .4f ? "Red" : "Orange";
            else if (hue < .16f)
                paletteId = "Orange";
            else if (hue < .26f)
                paletteId = "Yellow";
            else if (hue < .45f)
                paletteId = "GreenLight";
            else if (hue < .63f)
                paletteId = "BlueLight";
            else if (hue < .84f)
                paletteId = "Purple";
            else
                paletteId = "Pink";

            return visualConfig.TryGetPaletteMaterial(paletteId, out _);
        }
    }
}
#endif
