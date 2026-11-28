using GravityPuzzle.Config;
using GravityPuzzle.Editor;
using GravityPuzzle.Gameplay.Pieces;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class PiecePaletteAuthoringTests
    {
        [TestCase(1f, .3f, .3f, "Red")]
        [TestCase(.902f, 1f, .3f, "Yellow")]
        [TestCase(.3f, 1f, .496f, "GreenLight")]
        [TestCase(.3f, .706f, 1f, "BlueLight")]
        [TestCase(.692f, .3f, 1f, "Purple")]
        [TestCase(1f, .3f, .51f, "Pink")]
        [TestCase(1f, .888f, .3f, "Orange")]
        [TestCase(0f, 0f, 0f, "Black")]
        [TestCase(.08f, .08f, .12f, "Black")]
        [TestCase(.2f, .2f, .2f, "Black")]
        [TestCase(.128f, .138f, .160f, "Black")]
        [TestCase(1f, 1f, 1f, "White")]
        [TestCase(.92f, .92f, .95f, "White")]
        public void LegacyColoursSelectSceneTunaPalette(float r, float g, float b, string expected)
        {
            Assert.That(PiecePaletteMigration.ResolvePaletteId(new Color(r, g, b)), Is.EqualTo(expected));
        }

        [TestCase("Red")]
        [TestCase("Yellow")]
        [TestCase("Orange")]
        [TestCase("GreenLight")]
        [TestCase("GreenDark")]
        [TestCase("BlueLight")]
        [TestCase("BlueDark")]
        [TestCase("Purple")]
        [TestCase("Pink")]
        [TestCase("Black")]
        [TestCase("White")]
        public void PaletteMaterialsResolveNonWhiteVoxelColors(string paletteId)
        {
            PieceVisualConfig config = AssetDatabase.LoadAssetAtPath<PieceVisualConfig>("Assets/PieceVisualConfig.asset");
            Assert.That(config, Is.Not.Null);
            Assert.That(config.TryGetPaletteMaterial(paletteId, out Material material), Is.True);

            Color resolved = RuntimePieceFactory.ResolvePresentationMaterialColor(material, Color.white);
            bool isNearWhite = resolved.r > .95f && resolved.g > .95f && resolved.b > .95f;
            Assert.That(isNearWhite, Is.False, $"Palette '{paletteId}' resolved to near-white {resolved}.");
            Assert.That(resolved.a, Is.EqualTo(1f).Within(1e-4f));
        }
    }
}
