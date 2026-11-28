using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GravityPuzzle.Tests.PlayMode
{
    public sealed class FailPanelButtonVisibilityPlayModeTests
    {
        private const string FailPanelPrefabPath = "Assets/MainPrefabs/UI/Fail_Panel.prefab";

        private static readonly Type JuiceType =
            Type.GetType("ThreadFever.UI.RewardChestJuice, Assembly-CSharp");

        private static readonly Type TmpTextType =
            Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");

        private GameObject testRoot;

        [TearDown]
        public void TearDown()
        {
            if (testRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(testRoot);
                testRoot = null;
            }
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator FailPanel_SequentialEntrance_KeepsTryAgainButtonVisibleAfterPulseStarts()
        {
            Assert.That(JuiceType, Is.Not.Null, "RewardChestJuice type must exist in Assembly-CSharp.");
            Assert.That(TmpTextType, Is.Not.Null, "TMP_Text type must exist.");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FailPanelPrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Expected prefab at '{FailPanelPrefabPath}'.");

            // Mirror InGameUI/Scene1 startup state: Fail_Panel starts inactive before its first show.
            testRoot = new GameObject("FailPanelTestRoot", typeof(Canvas));
            testRoot.SetActive(false);

            GameObject failPanel = UnityEngine.Object.Instantiate(prefab, testRoot.transform);
            failPanel.SetActive(false);
            testRoot.SetActive(true);

            Transform buttonTransform = failPanel.transform.Find("Button");
            Assert.That(buttonTransform, Is.Not.Null, "Fail_Panel must have a Button child.");

            Transform labelTransform = buttonTransform.Find("\u0130mg_Button/Text (TMP)");
            Assert.That(labelTransform, Is.Not.Null, "Fail_Panel/Button must contain Text (TMP).");

            Component labelTmp = labelTransform.GetComponent(TmpTextType);
            Assert.That(labelTmp, Is.Not.Null);
            PropertyInfo textProperty = TmpTextType.GetProperty("text");
            Assert.That(textProperty, Is.Not.Null);
            Assert.That(textProperty.GetValue(labelTmp) as string, Is.EqualTo("Try Again"));

            Behaviour juice = buttonTransform.GetComponent(JuiceType) as Behaviour;
            Assert.That(juice, Is.Not.Null, "Fail_Panel/Button must have RewardChestJuice.");
            Assert.That(juice.enabled, Is.False, "RewardChestJuice must start disabled until entrance finishes.");

            // First activation triggers PanelEntrancePresentation.OnEnable before child Awake.
            failPanel.SetActive(true);
            Assert.That(buttonTransform.localScale, Is.EqualTo(Vector3.zero),
                "Sequential entrance should start Try Again hidden at zero scale.");

            yield return WaitForPulseToActivate(juice, timeoutSeconds: 2.5f);

            Assert.That(juice.enabled, Is.True, "RewardChestJuice should enable once entrance finishes.");

            // Advance a few frames so pulse OnEnable + tween ticks take effect.
            for (int frame = 0; frame < 5; frame++)
                yield return null;

            Assert.That(buttonTransform.localScale.x, Is.GreaterThanOrEqualTo(0.99f),
                "Try Again button must not snap back to zero scale when RewardChestJuice starts pulsing.");
            Assert.That(buttonTransform.localScale.y, Is.GreaterThanOrEqualTo(0.99f));
            Assert.That(labelTransform.gameObject.activeInHierarchy, Is.True);
            Assert.That(labelTransform.lossyScale.x, Is.GreaterThan(0.5f));

            // Re-opening Fail_Panel after closing must preserve the same visible button behavior.
            failPanel.SetActive(false);
            failPanel.SetActive(true);

            yield return WaitForPulseToActivate(juice, timeoutSeconds: 2.5f);
            for (int frame = 0; frame < 5; frame++)
                yield return null;

            Assert.That(juice.enabled, Is.True);
            Assert.That(buttonTransform.localScale.x, Is.GreaterThanOrEqualTo(0.99f),
                "Try Again button must remain visible when Fail_Panel is reopened.");
            Assert.That(buttonTransform.localScale.y, Is.GreaterThanOrEqualTo(0.99f));
        }

        private static IEnumerator WaitForPulseToActivate(Behaviour juice, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!juice.enabled && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
#endif
    }
}
