using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ThreadFever.UI
{
    /// <summary>
    /// Sahne yüklenince otomatik olarak iplik sökülmesi (açılış) animasyonu oynatır.
    /// Race_Scene'deki herhangi bir GameObject'e bu bileşeni ekleyin.
    /// Shader materyal ataması yapmadan çalışır; her şeyi kod içinde oluşturur.
    /// </summary>
    public class SceneRevealTransition : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("ThreadWipe materyalini içeren geçiş Image'ı. Boş bırakılırsa otomatik oluşturulur.")]
        [SerializeField] private Image _transitionOverlay;

        [Header("Settings")]
        [Tooltip("Açılış animasyonunun süresi (saniye).")]
        [SerializeField] private float _duration = 0.5f;

        [Tooltip("Animasyon başlamadan önce bekleme süresi (Race Scene'in initialize olması için).")]
        [SerializeField] private float _startDelay = 0.05f;

        private Material _mat;

        private void Start()
        {
            if (_transitionOverlay == null)
            {
                Debug.LogWarning("[SceneRevealTransition] TransitionOverlay referansı atanmamış! " +
                                 "Inspector'dan bir Image bileşeni atayın.", this);
                return;
            }

            // Sadece FirstScene'den gelindiyse (ThreadTransitionManager tetiklediyse) animasyonu oynat
            if (!ThreadTransitionManager.ShouldPlayRevealNextScene)
            {
                _transitionOverlay.gameObject.SetActive(false);
                Destroy(this);
                return;
            }

            // Bayrağı sıfırla ki Level_Scene'den falan gelindiğinde oynamasın
            ThreadTransitionManager.ShouldPlayRevealNextScene = false;

            // Materyalin kopyasını al (projeyi bozmasın)
            _mat = new Material(_transitionOverlay.material);
            _transitionOverlay.material = _mat;

            // Ekranı tamamen kaplı başlat (Cutoff = 0 → tüm pikseller görünür/kaplı)
            _mat.SetFloat("_Cutoff", 0f);
            _transitionOverlay.gameObject.SetActive(true);

            StartCoroutine(RevealRoutine());
        }

        private IEnumerator RevealRoutine()
        {
            // Sahnenin tam initialize olması için kısa bekle
            yield return new WaitForSecondsRealtime(_startDelay);

            // İplik sökülmesi: Cutoff 0'dan 1'e çık
            float elapsed = 0f;
            while (elapsed < _duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _duration);
                _mat.SetFloat("_Cutoff", Mathf.Lerp(0f, 1f, t));
                yield return null;
            }

            _mat.SetFloat("_Cutoff", 1f);
            _transitionOverlay.gameObject.SetActive(false);

            // Kendini temizle
            Destroy(this);
        }
    }
}
