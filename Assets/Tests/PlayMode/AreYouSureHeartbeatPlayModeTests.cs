using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GravityPuzzle.Tests.PlayMode
{
    public sealed class AreYouSureHeartbeatPlayModeTests
    {
        private const string RetryPanelPrefabPath = "Assets/MainPrefabs/UI/Retry_Panel.prefab";

        private static readonly Type JuiceType = Type.GetType("ThreadFever.UI.RewardChestJuice, Assembly-CSharp");
        private static readonly Type EntranceType = Type.GetType("GravityPuzzle.Presentation.Views.PanelEntrancePresentation, Assembly-CSharp");
        private static readonly Type ImageType = Type.GetType("UnityEngine.UI.Image, UnityEngine.UI");

        private GameObject canvasRoot;

        [TearDown]
        public void TearDown()
        {
            if (canvasRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
                canvasRoot = null;
            }
        }

        [UnityTest]
        public IEnumerator AreYouSurePanel_StartsHeartAndReviveHeartbeatSynchronouslyWithGlowOnOpen()
        {
            Assert.That(JuiceType, Is.Not.Null);
            Assert.That(EntranceType, Is.Not.Null);
            Assert.That(ImageType, Is.Not.Null);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RetryPanelPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            canvasRoot = new GameObject("TestCanvas", typeof(Canvas));
            canvasRoot.SetActive(false);

            GameObject panel = UnityEngine.Object.Instantiate(prefab, canvasRoot.transform, false);
            panel.SetActive(false);
            canvasRoot.SetActive(true);

            Transform heartContent = panel.transform.Find("BrokenHeart/HeartContent");
            Transform heartGlow = heartContent != null ? heartContent.Find("HeartGlow") : null;
            Transform reviveContent = panel.transform.Find("Revive_btn/BtnContent");

            Assert.That(heartContent, Is.Not.Null);
            Assert.That(heartGlow, Is.Not.Null);
            Assert.That(reviveContent, Is.Not.Null);

            Component heartJuice = heartContent.GetComponent(JuiceType);
            Component glowJuice = heartGlow.GetComponent(JuiceType);
            Component reviveJuice = reviveContent.GetComponent(JuiceType);
            Component glowImage = heartGlow.GetComponent(ImageType);

            Assert.That(heartJuice, Is.Not.Null);
            Assert.That(glowJuice, Is.Not.Null);
            Assert.That(reviveJuice, Is.Not.Null);
            Assert.That(glowImage, Is.Not.Null);

            // Wait a bit before opening so Time.unscaledTime is non-zero (catching any phase-teleport bug).
            yield return new WaitForSecondsRealtime(0.15f);

            panel.SetActive(true);

            // Immediately upon opening: all three pulse components remain enabled and start at rest (1.0), not snapped mid-cycle.
            Assert.That(GetEnabled(heartJuice), Is.True);
            Assert.That(GetEnabled(glowJuice), Is.True);
            Assert.That(GetEnabled(reviveJuice), Is.True);
            Assert.That(heartContent.localScale.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(heartGlow.localScale.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(reviveContent.localScale.x, Is.EqualTo(1f).Within(0.001f));

            float restAlpha = GetGraphicAlpha(glowImage);

            // Advance into the first beat (~0.25s, overlapping/just after entrance) and verify both heart and button are already beating in sync.
            yield return new WaitForSecondsRealtime(0.28f);

            float heartNormEarly = NormalizedPulse(heartContent.localScale.x, 1.08f);
            float reviveNormEarly = NormalizedPulse(reviveContent.localScale.x, 1.045f);
            float glowNormEarly = NormalizedPulse(heartGlow.localScale.x, 1.10f);
            Assert.That(heartNormEarly, Is.GreaterThan(0.08f));
            Assert.That(reviveNormEarly, Is.GreaterThan(0.08f));
            Assert.That(glowNormEarly, Is.GreaterThan(0.08f));
            Assert.That(Mathf.Abs(heartNormEarly - reviveNormEarly), Is.LessThan(0.04f));
            Assert.That(Mathf.Abs(heartNormEarly - glowNormEarly), Is.LessThan(0.04f));
            Assert.That(GetGraphicAlpha(glowImage), Is.GreaterThan(restAlpha + 0.02f));

            // Advance near peak (~0.70s total) and confirm both approach beat apex together.
            yield return new WaitForSecondsRealtime(0.42f);

            float heartNormPeak = NormalizedPulse(heartContent.localScale.x, 1.08f);
            float reviveNormPeak = NormalizedPulse(reviveContent.localScale.x, 1.045f);
            Assert.That(heartNormPeak, Is.GreaterThan(0.75f));
            Assert.That(reviveNormPeak, Is.GreaterThan(0.75f));
            Assert.That(Mathf.Abs(heartNormPeak - reviveNormPeak), Is.LessThan(0.04f));
            Assert.That(GetGraphicAlpha(glowImage), Is.GreaterThan(restAlpha + 0.15f));

            // Advance into return half of the beat (~1.25s total) and verify synchronized contraction.
            yield return new WaitForSecondsRealtime(0.55f);

            float heartNormReturn = NormalizedPulse(heartContent.localScale.x, 1.08f);
            float reviveNormReturn = NormalizedPulse(reviveContent.localScale.x, 1.045f);
            Assert.That(heartNormReturn, Is.LessThan(heartNormPeak));
            Assert.That(reviveNormReturn, Is.LessThan(reviveNormPeak));
            Assert.That(Mathf.Abs(heartNormReturn - reviveNormReturn), Is.LessThan(0.04f));

            // Deactivate and re-activate: verify clean reset and synchronized restart.
            panel.SetActive(false);
            yield return null;
            Assert.That(heartContent.localScale.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(heartGlow.localScale.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(reviveContent.localScale.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(GetGraphicAlpha(glowImage), Is.EqualTo(restAlpha).Within(0.001f));

            panel.SetActive(true);
            Assert.That(heartContent.localScale.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(reviveContent.localScale.x, Is.EqualTo(1f).Within(0.001f));
            yield return new WaitForSecondsRealtime(0.35f);
            float reopenHeartNorm = NormalizedPulse(heartContent.localScale.x, 1.08f);
            float reopenReviveNorm = NormalizedPulse(reviveContent.localScale.x, 1.045f);
            Assert.That(reopenHeartNorm, Is.GreaterThan(0.15f));
            Assert.That(Mathf.Abs(reopenHeartNorm - reopenReviveNorm), Is.LessThan(0.04f));
        }

        [UnityTest]
        public IEnumerator RewardChestJuice_StartsAtZeroPhaseWithoutJumpingAndRestoresRestStateOnDisable()
        {
            Assert.That(JuiceType, Is.Not.Null);
            Assert.That(ImageType, Is.Not.Null);

            FieldInfo pulseField = JuiceType.GetField("_pulseTween", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo alphaField = JuiceType.GetField("_alphaTween", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(pulseField, Is.Not.Null);
            Assert.That(alphaField, Is.Not.Null);

            Type tweenType = pulseField.FieldType;
            Type tweenExtensionsType = tweenType.Assembly.GetType("DG.Tweening.TweenExtensions");
            Assert.That(tweenExtensionsType, Is.Not.Null);

            MethodInfo isActiveMethod = tweenExtensionsType.GetMethod("IsActive", BindingFlags.Public | BindingFlags.Static, null, new[] { tweenType }, null);
            MethodInfo elapsedMethod = tweenExtensionsType.GetMethod("Elapsed", BindingFlags.Public | BindingFlags.Static, null, new[] { tweenType, typeof(bool) }, null);
            Assert.That(isActiveMethod, Is.Not.Null);
            Assert.That(elapsedMethod, Is.Not.Null);

            GameObject host = new GameObject("JuiceTest", typeof(RectTransform), ImageType);
            try
            {
                Component image = host.GetComponent(ImageType);
                PropertyInfo colorProp = ImageType.GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(colorProp, Is.Not.Null);
                colorProp.SetValue(image, new Color(1f, 0.25f, 0.3f, 0.6f));

                host.SetActive(false);
                Component juice = host.AddComponent(JuiceType);
                SetField(juice, "_pulseScale", 1.12f);
                SetField(juice, "_duration", 0.75f);
                SetField(juice, "_pulseAlpha", true);
                SetField(juice, "_alphaTarget", image);
                SetField(juice, "_peakAlpha", 0.95f);

                host.SetActive(true);
                Assert.That(host.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(((Color)colorProp.GetValue(image)).a, Is.EqualTo(0.6f).Within(0.0001f));

                object scaleTween = pulseField.GetValue(juice);
                object alphaTween = alphaField.GetValue(juice);
                Assert.That(scaleTween, Is.Not.Null);
                Assert.That((bool)isActiveMethod.Invoke(null, new[] { scaleTween }), Is.True);
                Assert.That((float)elapsedMethod.Invoke(null, new[] { scaleTween, true }), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(alphaTween, Is.Not.Null);
                Assert.That((bool)isActiveMethod.Invoke(null, new[] { alphaTween }), Is.True);
                Assert.That((float)elapsedMethod.Invoke(null, new[] { alphaTween, true }), Is.EqualTo(0f).Within(0.0001f));
                yield return null;

                host.transform.localScale = new Vector3(1.07f, 1.07f, 1.07f);
                colorProp.SetValue(image, new Color(1f, 0.25f, 0.3f, 0.9f));

                host.SetActive(false);
                yield return null;
                Assert.That(host.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(((Color)colorProp.GetValue(image)).a, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(pulseField.GetValue(juice), Is.Null);
                Assert.That(alphaField.GetValue(juice), Is.Null);

                host.SetActive(true);
                Assert.That(host.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(((Color)colorProp.GetValue(image)).a, Is.EqualTo(0.6f).Within(0.0001f));
                object restartedScaleTween = pulseField.GetValue(juice);
                object restartedAlphaTween = alphaField.GetValue(juice);
                Assert.That(restartedScaleTween, Is.Not.Null);
                Assert.That((float)elapsedMethod.Invoke(null, new[] { restartedScaleTween, true }), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(restartedAlphaTween, Is.Not.Null);
                Assert.That((float)elapsedMethod.Invoke(null, new[] { restartedAlphaTween, true }), Is.EqualTo(0f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void SetField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static float NormalizedPulse(float scale, float pulseScale)
        {
            return (scale - 1f) / (pulseScale - 1f);
        }

        private static bool GetEnabled(Component component)
        {
            return ((Behaviour)component).enabled;
        }

        private static float GetGraphicAlpha(Component graphic)
        {
            PropertyInfo colorProp = graphic.GetType().GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(colorProp, Is.Not.Null);
            Color c = (Color)colorProp.GetValue(graphic);
            return c.a;
        }
    }
}
