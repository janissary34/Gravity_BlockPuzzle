using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace ThreadFever.UI
{
    /// <summary>
    /// İplik örme efektiyle sahne kapanma geçişini yöneten Singleton.
    /// Yalnızca KAPANMA animasyonunu oynatır ve ardından sahneyi yükler.
    /// Açılma animasyonu için hedef sahnede SceneRevealTransition kullanın.
    /// </summary>
    public class ThreadTransitionManager : MonoBehaviour
    {
        public static ThreadTransitionManager Instance { get; private set; }
        
        /// <summary>
        /// Hedef sahne yüklendiğinde açılış (sökülme) animasyonunun oynatılıp oynatılmayacağını belirler.
        /// </summary>
        public static bool ShouldPlayRevealNextScene { get; set; } = false;

        [Header("References")]
        [SerializeField] private Image _transitionOverlay;

        [Header("Settings")]
        [Tooltip("Kapanma animasyonunun süresi (saniye).")]
        [SerializeField] private float _transitionDuration = 0.5f;

        private Material _transitionMat;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (_transitionOverlay != null)
            {
                _transitionMat = new Material(_transitionOverlay.material);
                _transitionOverlay.material = _transitionMat;
                _transitionMat.SetFloat("_Cutoff", 1f);
                _transitionOverlay.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// İplik kapanma animasyonu oynatır, bittikten sonra hedef sahneyi yükler.
        /// </summary>
        public void TransitionToScene(string sceneName)
        {
            if (_transitionOverlay == null || _transitionMat == null)
            {
                SceneManager.LoadScene(sceneName);
                return;
            }

            StartCoroutine(CloseAndLoad(sceneName));
        }

        private IEnumerator CloseAndLoad(string sceneName)
        {
            // Yeni sahne yüklendiğinde açılış animasyonu oynaması için bayrağı true yap
            ShouldPlayRevealNextScene = true;

            // Kapanma animasyonu: Cutoff 1 → 0 (ekran ipliklerle dolar)
            _transitionMat.SetFloat("_Cutoff", 1f);
            _transitionOverlay.gameObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < _transitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _transitionDuration);
                _transitionMat.SetFloat("_Cutoff", Mathf.Lerp(1f, 0f, t));
                yield return null;
            }

            _transitionMat.SetFloat("_Cutoff", 0f);

            // Ekran tamamen kaplı → sahneyi yükle
            SceneManager.LoadScene(sceneName);
        }
    }
}
