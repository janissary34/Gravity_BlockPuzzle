using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using GravityPuzzle.Presentation.Views;

namespace GravityPuzzle.Tests.Editor
{
    public class ProgressSegmentedFillTests
    {
        [Test]
        public void SegmentedFillShaderAsset_ExistsAndCompilesWithoutErrors()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/ProgressSegmentedFill.shader");
            Assert.IsNotNull(shader, "Expected ProgressSegmentedFill shader asset at Assets/Shaders/ProgressSegmentedFill.shader.");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "ProgressSegmentedFill.shader should compile without shader errors.");
        }

        [Test]
        public void RecordSegment_WritesColorAcrossRequestedNormalizedRange_AndResetClears()
        {
            GameObject go = new GameObject("ProgressFillTest", typeof(RectTransform), typeof(Image), typeof(ProgressSegmentedFillView));
            try
            {
                ProgressSegmentedFillView fillView = go.GetComponent<ProgressSegmentedFillView>();
                Assert.IsNotNull(fillView);

                Color red = new Color(1f, 0f, 0f, 1f);
                Color green = new Color(0f, 1f, 0f, 1f);
                Color blue = new Color(0f, 0f, 1f, 1f);

                fillView.RecordSegment(0f, 0.25f, red);
                fillView.RecordSegment(0.25f, 0.5f, green);
                fillView.RecordSegment(0.5f, 1f, blue);

                Color32 s1 = fillView.SampleSegmentPixel(0.125f);
                Color32 s2 = fillView.SampleSegmentPixel(0.375f);
                Color32 s3 = fillView.SampleSegmentPixel(0.75f);

                Assert.Greater(s1.r, 200);
                Assert.Less(s1.g, 50);

                Assert.Greater(s2.g, 200);
                Assert.Less(s2.r, 50);

                Assert.Greater(s3.b, 200);
                Assert.Less(s3.r, 50);

                fillView.ResetSegments();
                Color32 resetSample = fillView.SampleSegmentPixel(0.125f);
                Color32 expectedDefault = fillView.DefaultFillColor;
                Assert.AreEqual(expectedDefault.r, resetSample.r);
                Assert.AreEqual(expectedDefault.g, resetSample.g);
                Assert.AreEqual(expectedDefault.b, resetSample.b);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SetUniformColor_OverridesIncomingSegmentsWithSingleColor()
        {
            GameObject go = new GameObject("ProgressUniformFillTest", typeof(RectTransform), typeof(Image), typeof(ProgressSegmentedFillView));
            try
            {
                ProgressSegmentedFillView fillView = go.GetComponent<ProgressSegmentedFillView>();
                Color uniformGreen = new Color(0.22f, 0.84f, 0.24f, 1f);
                fillView.SetUniformColor(true, uniformGreen);

                fillView.RecordSegment(0f, 0.5f, Color.red);
                fillView.RecordSegment(0.5f, 1f, Color.blue);

                Color32 expected = (Color32)uniformGreen;
                Color32 s1 = fillView.SampleSegmentPixel(0.25f);
                Color32 s2 = fillView.SampleSegmentPixel(0.75f);

                Assert.AreEqual(expected.r, s1.r);
                Assert.AreEqual(expected.g, s1.g);
                Assert.AreEqual(expected.b, s1.b);
                Assert.AreEqual(expected.r, s2.r);
                Assert.AreEqual(expected.g, s2.g);
                Assert.AreEqual(expected.b, s2.b);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
