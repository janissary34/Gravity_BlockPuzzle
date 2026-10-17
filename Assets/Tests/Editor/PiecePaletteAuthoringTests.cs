using GravityPuzzle.Editor;
using NUnit.Framework;
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
        public void LegacyColoursSelectSceneTunaPalette(float r, float g, float b, string expected)
        {
            Assert.That(PiecePaletteMigration.ResolvePaletteId(new Color(r, g, b)), Is.EqualTo(expected));
        }
    }
}
