using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Presentation.VFX
{
    public interface IIceBlockParticleVfx
    {
        IIceBlockParticleVfxHandle RentPair();
    }

    public interface IIceBlockParticleVfxHandle
    {
        void PlayCrack(Vector3 position, int sortingLayerId, int sortingOrder);
        void PlayBreak(Vector3 position, int sortingLayerId, int sortingOrder);
    }

    /// <summary>Bootstrap-owned prewarmed effects for simultaneous ice blocks.</summary>
    public sealed class IceBlockParticleVfxPool : IIceBlockParticleVfx
    {
        private readonly MonoBehaviour coroutineHost;
        private readonly List<EffectPair> effectPairs;
        private int nextPairIndex;

        public IceBlockParticleVfxPool(
            MonoBehaviour coroutineHost,
            ParticleSystem crackTemplate,
            ParticleSystem breakTemplate,
            Transform parent,
            int capacity)
        {
            this.coroutineHost = coroutineHost;
            effectPairs = Prewarm(crackTemplate, breakTemplate, parent, capacity);
        }

        public IIceBlockParticleVfxHandle RentPair()
        {
            if (nextPairIndex >= effectPairs.Count)
            {
                Debug.LogWarning("[IceBlockVFX] No prewarmed particle pair is available for this ice block.");
                return null;
            }

            return effectPairs[nextPairIndex++];
        }

        private void Play(ParticleSystem effect, Vector3 position, int sortingLayerId, int sortingOrder)
        {
            if (effect == null)
                return;

            effect.transform.position = position;
            ParticleSystemRenderer renderer = effect.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sortingLayerID = sortingLayerId;
                renderer.sortingOrder = sortingOrder;
            }
            effect.gameObject.SetActive(true);
            effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            effect.Play(true);
            coroutineHost.StartCoroutine(ReturnWhenFinished(effect));
        }

        private IEnumerator ReturnWhenFinished(ParticleSystem effect)
        {
            yield return null;
            while (effect != null && effect.IsAlive(true))
                yield return null;

            if (effect != null)
                effect.gameObject.SetActive(false);
        }

        private List<EffectPair> Prewarm(
            ParticleSystem crackTemplate,
            ParticleSystem breakTemplate,
            Transform parent,
            int capacity)
        {
            List<EffectPair> pairs = new List<EffectPair>(capacity);
            DisableSceneTemplate(crackTemplate);
            if (breakTemplate != crackTemplate)
                DisableSceneTemplate(breakTemplate);

            for (int index = 0; index < capacity; index++)
            {
                pairs.Add(new EffectPair(
                    this,
                    CreateEffect(crackTemplate, parent, "Ice Crack Effect"),
                    CreateEffect(breakTemplate, parent, "Ice Break Effect")));
            }

            return pairs;
        }

        private static void DisableSceneTemplate(ParticleSystem template)
        {
            if (template != null && template.gameObject.scene.IsValid())
            {
                template.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                template.gameObject.SetActive(false);
            }
        }

        private static ParticleSystem CreateEffect(ParticleSystem template, Transform parent, string effectName)
        {
            if (template == null)
                return null;

            ParticleSystem effect = Object.Instantiate(template, parent);
            effect.name = effectName;
            ParticleSystem.MainModule main = effect.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            effect.gameObject.SetActive(false);
            return effect;
        }

        private sealed class EffectPair : IIceBlockParticleVfxHandle
        {
            private readonly IceBlockParticleVfxPool owner;
            private readonly ParticleSystem crackEffect;
            private readonly ParticleSystem breakEffect;

            public EffectPair(IceBlockParticleVfxPool owner, ParticleSystem crackEffect, ParticleSystem breakEffect)
            {
                this.owner = owner;
                this.crackEffect = crackEffect;
                this.breakEffect = breakEffect;
            }

            public void PlayCrack(Vector3 position, int sortingLayerId, int sortingOrder) =>
                owner.Play(crackEffect, position, sortingLayerId, sortingOrder);

            public void PlayBreak(Vector3 position, int sortingLayerId, int sortingOrder) =>
                owner.Play(breakEffect, position, sortingLayerId, sortingOrder);
        }
    }
}
