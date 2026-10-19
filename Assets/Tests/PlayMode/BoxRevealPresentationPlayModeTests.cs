using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GravityPuzzle.Tests.PlayMode
{
    public sealed class BoxRevealPresentationPlayModeTests
    {
        private static readonly Type ViewType = Type.GetType("GravityPuzzle.Presentation.Views.BoxRevealAreaView, Assembly-CSharp");
        private static readonly Type KindType = Type.GetType("GravityPuzzle.Gameplay.Reveal.RevealAreaKind, Assembly-CSharp");
        private static readonly Type ConfigType = Type.GetType("GravityPuzzle.Config.RevealPresentationConfig, Assembly-CSharp");

        [UnityTest]
        public IEnumerator ProgressRetargetsWithoutMovingTheFrameAndCompletesBeforeUnlock()
        {
            Assert.That(ViewType, Is.Not.Null);
            Assert.That(KindType, Is.Not.Null);
            Assert.That(ConfigType, Is.Not.Null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BoxRevealArea.prefab");
            var config = AssetDatabase.LoadAssetAtPath("Assets/Config/RevealPresentationConfig.asset", ConfigType);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(config, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            Component view = instance.GetComponent(ViewType);
            var initialize = ViewType.GetMethod("Initialize");
            var setRemaining = ViewType.GetMethod("SetBoxRemaining");
            var playOpening = ViewType.GetMethod("PlayOpening");
            var frame = instance.transform.Find("Shutter/Box Frame").GetComponent<SpriteRenderer>();
            var panel = instance.transform.Find("Shutter/Box Moving Shutter").GetComponent<SpriteRenderer>();
            var backing = instance.transform.Find("Shutter/Box Opaque Backing").GetComponent<SpriteRenderer>();
            var properties = new MaterialPropertyBlock();
            bool completed = false;
            try
            {
                initialize.Invoke(view, new object[] { "test", Vector2.zero, new Vector2(3f, 2f), config });
                Vector2 frameSize = frame.size;
                Vector3 framePosition = frame.transform.localPosition;
                foreach (int remaining in new[] { 4, 3, 2, 1 })
                {
                    setRemaining.Invoke(view, new object[] { remaining, 5 });
                    yield return new WaitForSeconds(.6f);
                    panel.GetPropertyBlock(properties);
                    Assert.That(properties.GetFloat("_Opening"), Is.EqualTo((5f - remaining) / 5f).Within(.01f));
                    Assert.That(frame.size, Is.EqualTo(frameSize));
                    Assert.That(frame.transform.localPosition, Is.EqualTo(framePosition));
                    Assert.That(backing.gameObject.activeSelf, Is.True);
                }

                // Reinitialization also exercises the pooled visual reset. A
                // second target arrives while the first tween is in flight.
                ViewType.GetMethod("OnDespawn").Invoke(view, null);
                initialize.Invoke(view, new object[] { "test", Vector2.zero, new Vector2(2f, 3f), config });
                setRemaining.Invoke(view, new object[] { 2, 3 });
                yield return new WaitForSeconds(.1f);
                panel.GetPropertyBlock(properties);
                float interrupted = properties.GetFloat("_Opening");
                setRemaining.Invoke(view, new object[] { 1, 3 });
                panel.GetPropertyBlock(properties);
                Assert.That(properties.GetFloat("_Opening"), Is.EqualTo(interrupted).Within(.001f));
                yield return new WaitForSeconds(.6f);
                panel.GetPropertyBlock(properties);
                Assert.That(properties.GetFloat("_Opening"), Is.EqualTo(2f / 3f).Within(.01f));

                setRemaining.Invoke(view, new object[] { 0, 3 });
                playOpening.Invoke(view, new object[] { Enum.ToObject(KindType, 0), (Action)(() => completed = true) });
                Assert.That(completed, Is.False);
                Assert.That(backing.gameObject.activeSelf, Is.True);
                yield return new WaitForSeconds(1f);
                Assert.That(completed, Is.True);
            }
            finally
            {
                if (instance != null)
                    UnityEngine.Object.Destroy(instance);
            }
        }
    }
}
