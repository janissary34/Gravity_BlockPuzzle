using System.Reflection;
using GravityPuzzle.Presentation.Views;
using NUnit.Framework;
using ThreadFever.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class AreYouSurePanelTests
    {
        private const string RetryPanelPrefabPath = "Assets/MainPrefabs/UI/Retry_Panel.prefab";
        private const string InGameUiPrefabPath = "Assets/MainPrefabs/UI/InGameUI.prefab";

        [Test]
        public void RetryPanelPrefab_WiresImmediateHeartAndReviveHeartbeatWithGlow()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RetryPanelPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            AssertHeartGlowAndImmediatePulse(prefab.transform);

            Transform heartContent = prefab.transform.Find("BrokenHeart/HeartContent");
            Transform heartGlow = heartContent.Find("HeartGlow");
            Transform reviveBtnContent = prefab.transform.Find("Revive_btn/BtnContent");
            Assert.That(reviveBtnContent, Is.Not.Null);
            RewardChestJuice heartJuice = heartContent.GetComponent<RewardChestJuice>();
            RewardChestJuice glowJuice = heartGlow.GetComponent<RewardChestJuice>();
            RewardChestJuice reviveJuice = reviveBtnContent.GetComponent<RewardChestJuice>();
            Assert.That(reviveJuice, Is.Not.Null);
            Assert.That(reviveJuice.enabled, Is.True);

            float heartDuration = GetField<float>(heartJuice, "_duration");
            float glowDuration = GetField<float>(glowJuice, "_duration");
            float reviveDuration = GetField<float>(reviveJuice, "_duration");
            Assert.That(heartDuration, Is.EqualTo(reviveDuration).Within(0.0001f));
            Assert.That(glowDuration, Is.EqualTo(reviveDuration).Within(0.0001f));
        }

        [Test]
        public void InGameUiRetryPanel_WiresSameHeartGlowAndImmediateHeartbeat()
        {
            GameObject inGameUi = AssetDatabase.LoadAssetAtPath<GameObject>(InGameUiPrefabPath);
            Assert.That(inGameUi, Is.Not.Null);

            Transform retryPanel = inGameUi.transform.Find("Retry_Panel");
            Assert.That(retryPanel, Is.Not.Null);

            AssertHeartGlowAndImmediatePulse(retryPanel);
        }

        private static void AssertHeartGlowAndImmediatePulse(Transform panelRoot)
        {
            PanelEntrancePresentation entrance = panelRoot.GetComponent<PanelEntrancePresentation>();
            Assert.That(entrance, Is.Not.Null);

            FieldInfo pulseComponentsField = typeof(PanelEntrancePresentation).GetField(
                "pulseComponentsAfterEntrance",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(pulseComponentsField, Is.Not.Null);
            MonoBehaviour[] delayedPulses = pulseComponentsField.GetValue(entrance) as MonoBehaviour[];
            Assert.That(delayedPulses == null || delayedPulses.Length == 0, Is.True,
                "Heart heartbeat must not be delayed or stopped after panel entrance.");

            Transform brokenHeart = panelRoot.Find("BrokenHeart");
            Assert.That(brokenHeart, Is.Not.Null);
            Assert.That(brokenHeart.GetComponent<RewardChestJuice>(), Is.Null,
                "Heartbeat juice must live on a child container so PanelEntrancePresentation does not fight it.");

            Transform heartContent = brokenHeart.Find("HeartContent");
            Assert.That(heartContent, Is.Not.Null);
            RewardChestJuice heartJuice = heartContent.GetComponent<RewardChestJuice>();
            Assert.That(heartJuice, Is.Not.Null);
            Assert.That(heartJuice.enabled, Is.True);

            Transform heartGlow = heartContent.Find("HeartGlow");
            Transform heartGlowCore = heartContent.Find("HeartGlowCore");
            Transform heartIcon = heartContent.Find("HeartIcon");
            Assert.That(heartGlow, Is.Not.Null);
            Assert.That(heartGlowCore, Is.Not.Null);
            Assert.That(heartIcon, Is.Not.Null);
            Assert.That(heartGlow.GetSiblingIndex(), Is.LessThan(heartIcon.GetSiblingIndex()));
            Assert.That(heartGlowCore.GetSiblingIndex(), Is.LessThan(heartIcon.GetSiblingIndex()));

            Image glowImage = heartGlow.GetComponent<Image>();
            Image coreImage = heartGlowCore.GetComponent<Image>();
            Image iconImage = heartIcon.GetComponent<Image>();
            Assert.That(glowImage, Is.Not.Null);
            Assert.That(coreImage, Is.Not.Null);
            Assert.That(iconImage, Is.Not.Null);
            Assert.That(glowImage.sprite != null && glowImage.sprite.name == "heart-white-soft-glow", Is.True);
            Assert.That(coreImage.sprite != null && coreImage.sprite.name == "heart-white-soft-glow", Is.True);
            Assert.That(iconImage.sprite != null && iconImage.sprite.name == "Icon_Heart_Broken", Is.True);
            Assert.That(glowImage.raycastTarget, Is.False);
            Assert.That(coreImage.raycastTarget, Is.False);
            Assert.That(iconImage.raycastTarget, Is.False);

            RewardChestJuice glowJuice = heartGlow.GetComponent<RewardChestJuice>();
            Assert.That(glowJuice, Is.Not.Null);
            Assert.That(glowJuice.enabled, Is.True);
            Assert.That(GetField<bool>(glowJuice, "_pulseAlpha"), Is.True);
            Assert.That(GetField<Graphic>(glowJuice, "_alphaTarget"), Is.SameAs(glowImage));
        }

        private static T GetField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}' on {target.GetType().Name}.");
            return (T)field.GetValue(target);
        }

        private static void SetField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
