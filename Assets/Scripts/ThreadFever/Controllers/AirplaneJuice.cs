using UnityEngine;
using DG.Tweening;

namespace ThreadFever.Controllers
{
    /// <summary>
    /// Herhangi bir uçak GameObject'ine (UI Image dahil) eklendiğinde,
    /// her 3–4 saniyede bir Z ekseninde 360° tam dönüş (takla) animasyonu oynatır.
    /// Coroutine yerine saf DOTween zinciri kullanır — daha güvenilir.
    /// </summary>
    public class AirplaneJuice : MonoBehaviour
    {
        [Header("Roll Settings")]
        [Tooltip("İki takla arasındaki minimum bekleme süresi (saniye).")]
        [SerializeField] private float _rollIntervalMin = 3f;

        [Tooltip("İki takla arasındaki maksimum bekleme süresi (saniye).")]
        [SerializeField] private float _rollIntervalMax = 4f;

        [Tooltip("360° dönüşün tamamlanma süresi (saniye).")]
        [SerializeField] private float _rollDuration = 0.6f;

        [Tooltip("İlk takladan önce beklenecek gecikme (saniye).")]
        [SerializeField] private float _initialDelay = 1.5f;

        // Aktif tween — OnDestroy'da güvenle temizlenecek.
        private Tween _activeTween;

        // ─────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────

        private void Start()
        {
            // İlk tetikleme: _initialDelay saniye sonra
            _activeTween = DOVirtual
                .DelayedCall(_initialDelay, SpinOnce, ignoreTimeScale: false);
        }

        private void OnDestroy()
        {
            _activeTween?.Kill();
            transform.DOKill();
        }

        // ─────────────────────────────────────────────────────────────
        // Spin Logic
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Tek bir 360° dönüş yapar. Bitince kendini rastgele interval ile yeniden planlar.
        /// </summary>
        private void SpinOnce()
        {
            // Temiz başlangıç — önceki rotasyon birikimini sıfırla
            transform.localRotation = Quaternion.identity;

            // 360° Z-ekseni spin
            _activeTween = transform
                .DOLocalRotate(new Vector3(0f, 0f, 360f), _rollDuration, RotateMode.FastBeyond360)
                .SetEase(Ease.InOutQuad)
                .SetUpdate(false)
                .OnComplete(() =>
                {
                    // Spin bitti — rotasyonu identity'e sıfırla
                    transform.localRotation = Quaternion.identity;

                    // Rastgele aralık sonra bir sonraki spin'i planla
                    float delay = Random.Range(_rollIntervalMin, _rollIntervalMax);
                    _activeTween = DOVirtual.DelayedCall(delay, SpinOnce, ignoreTimeScale: false);
                });
        }
    }
}
