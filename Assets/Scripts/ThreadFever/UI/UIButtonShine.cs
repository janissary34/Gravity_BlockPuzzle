using UnityEngine;
using DG.Tweening;

namespace ThreadFever.UI
{
    /// <summary>
    /// Butona child obje olarak eklenecek olan parıltı (shine) efekti.
    /// Belirli aralıklarla soldan sağa geçen bir şerit animasyonu oynatır,
    /// aynı zamanda köşede bir Star Flare (yıldız parlaması) efekti çalıştırır.
    ///
    /// Shine ve Star Flare birbirinden BAĞIMSIZ döngülerde çalışır;
    /// her birinin interval'ı Inspector'dan ayrı ayrı ayarlanabilir.
    ///
    /// Star Flare pozisyonu: Inspector/Scene'de _starFlareImage'ı istediğin yere koy,
    /// script pozisyona hiç dokunmaz — sadece scale ve rotation animasyonu yapar.
    /// </summary>
    public class UIButtonShine : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Butonun child'ı olan parıltı şeridinin RectTransform'u.")]
        [SerializeField] private RectTransform _shineImage;

        [Tooltip("Yıldız parlaması görselinin RectTransform'u (isteğe bağlı). " +
                 "Scene'de istediğin yere yerleştir — script pozisyona dokunmaz.")]
        [SerializeField] private RectTransform _starFlareImage;

        [Header("Shine Settings")]
        [Tooltip("Parıltının soldan sağa geçme süresi (saniye).")]
        [SerializeField] private float _shineDuration = 0.6f;

        [Tooltip("Parıltılar arasındaki bekleme süresi (saniye).")]
        [SerializeField] private float _shineInterval = 3f;

        [Header("Star Flare Settings")]
        [Tooltip("Oyun başlayınca hem Shine hem Flare'in ilk tetiklenmeden önce bekleyeceği süre (saniye). " +
                 "Sahne açıldıktan hemen sonra bir kez çalıştırılır, sonra normal interval'lar devam eder.")]
        [SerializeField] private float _initialDelay = 0.5f;

        [Tooltip("Star Flare animasyonları arasındaki bekleme süresi (saniye). " +
                 "Shine interval'ından bağımsız çalışır.")]
        [SerializeField] private float _flareInterval = 2f;

        [Tooltip("Yıldızın scale tepe noktası. 0.4 = minimal/şık, 1.0 = normal boyut.")]
        [SerializeField] private float _flareMaxScale = 0.4f;

        [Tooltip("Büyüme süresi (saniye).")]
        [SerializeField] private float _flareGrowDuration = 0.2f;

        [Tooltip("Küçülme süresi (saniye).")]
        [SerializeField] private float _flareShrinkDuration = 0.2f;

        // Shine ve Flare birbirinden bağımsız iki sequence.
        private Sequence _shineSequence;
        private Sequence _flareSequence;

        // Butonun genişliğini bir kez hesaplayıp saklıyoruz.
        private float _buttonWidth;

        // ─────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────

        private void Start()
        {
            if (_shineImage == null)
            {
                Debug.LogWarning("UIButtonShine: _shineImage referansı atanmamış!", this);
                return;
            }

            // Ebeveyn butonun genişliğini al
            var parentRect = _shineImage.parent as RectTransform;
            _buttonWidth = parentRect != null ? parentRect.rect.width : 200f;

            // Star Flare: sadece scale'i sıfırla, pozisyona dokunma
            if (_starFlareImage != null)
            {
                _starFlareImage.localScale    = Vector3.zero;
                _starFlareImage.localRotation = Quaternion.identity;
            }

            // İki bağımsız döngüyü başlat
            ResetShinePosition();
            PlayShineLoop();
            PlayFlareLoop();
        }

        private void OnDestroy()
        {
            // Sahne/obje yok edildiğinde arkada aktif tween bırakmıyoruz.
            _shineSequence?.Kill();
            _flareSequence?.Kill();

            if (_shineImage != null)
                _shineImage.DOKill();

            if (_starFlareImage != null)
                _starFlareImage.DOKill();
        }

        // ─────────────────────────────────────────────────────────────
        // Private Helpers
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Parıltı şeridini butonun sol dışına konumlandırır.
        /// </summary>
        private void ResetShinePosition()
        {
            _shineImage.anchoredPosition = new Vector2(-_buttonWidth, 0f);
        }

        /// <summary>
        /// Yıldız flare'i görünmez başlangıç durumuna sıfırlar.
        /// Pozisyona dokunmaz — sadece scale ve rotation sıfırlanır.
        /// </summary>
        private void ResetFlareState()
        {
            if (_starFlareImage == null) return;
            _starFlareImage.localScale    = Vector3.zero;
            _starFlareImage.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Shine çizgisinin döngüsü.
        /// İlk tetikleme _initialDelay sonra gelir, ardından normal _shineInterval ile döner.
        /// </summary>
        private void PlayShineLoop()
        {
            _shineSequence?.Kill();
            _shineSequence = DOTween.Sequence();

            // İlk tetikleme: kısa delay sonra hemen çalışır
            _shineSequence.AppendInterval(_initialDelay);
            _shineSequence.AppendCallback(ResetShinePosition);
            _shineSequence.Append(
                _shineImage.DOAnchorPosX(_buttonWidth, _shineDuration)
                           .SetEase(Ease.Linear)
            );

            // İlk play bittikten sonra normal interval'lı döngüye geç
            _shineSequence.OnComplete(StartShineLoopNormal);
            _shineSequence.SetUpdate(true);
        }

        /// <summary>
        /// İlk Shine'dan sonra devreye giren, tam _shineInterval'lı sonsuz döngü.
        /// </summary>
        private void StartShineLoopNormal()
        {
            _shineSequence?.Kill();
            _shineSequence = DOTween.Sequence();

            _shineSequence.AppendInterval(_shineInterval);
            _shineSequence.AppendCallback(ResetShinePosition);
            _shineSequence.Append(
                _shineImage.DOAnchorPosX(_buttonWidth, _shineDuration)
                           .SetEase(Ease.Linear)
            );

            _shineSequence.SetLoops(-1, LoopType.Restart);
            _shineSequence.SetUpdate(true);
        }

        /// <summary>
        /// Star Flare'in döngüsü.
        /// İlk tetikleme _initialDelay sonra gelir, ardından normal _flareInterval ile döner.
        /// </summary>
        private void PlayFlareLoop()
        {
            if (_starFlareImage == null) return;

            _flareSequence?.Kill();
            _flareSequence = DOTween.Sequence();

            // İlk tetikleme: kısa delay sonra hemen çalışır
            _flareSequence.AppendCallback(ResetFlareState);
            _flareSequence.AppendInterval(_initialDelay);
            _flareSequence.Append(
                _starFlareImage.DOScale(Vector3.one * _flareMaxScale, _flareGrowDuration)
                               .SetEase(Ease.OutQuad)
            );
            _flareSequence.Join(
                _starFlareImage.DOLocalRotate(
                    new Vector3(0f, 0f, 45f),
                    _flareGrowDuration + _flareShrinkDuration,
                    RotateMode.LocalAxisAdd)
                               .SetEase(Ease.InOutQuad)
            );
            _flareSequence.Append(
                _starFlareImage.DOScale(Vector3.zero, _flareShrinkDuration)
                               .SetEase(Ease.InQuad)
            );

            // İlk play bittikten sonra normal interval'lı döngüye geç
            _flareSequence.OnComplete(StartFlareLoopNormal);
            _flareSequence.SetUpdate(true);
        }

        /// <summary>
        /// İlk Flare'den sonra devreye giren, tam _flareInterval'lı sonsuz döngü.
        /// </summary>
        private void StartFlareLoopNormal()
        {
            if (_starFlareImage == null) return;

            _flareSequence?.Kill();
            _flareSequence = DOTween.Sequence();

            _flareSequence.AppendCallback(ResetFlareState);
            _flareSequence.AppendInterval(_flareInterval);
            _flareSequence.Append(
                _starFlareImage.DOScale(Vector3.one * _flareMaxScale, _flareGrowDuration)
                               .SetEase(Ease.OutQuad)
            );
            _flareSequence.Join(
                _starFlareImage.DOLocalRotate(
                    new Vector3(0f, 0f, 45f),
                    _flareGrowDuration + _flareShrinkDuration,
                    RotateMode.LocalAxisAdd)
                               .SetEase(Ease.InOutQuad)
            );
            _flareSequence.Append(
                _starFlareImage.DOScale(Vector3.zero, _flareShrinkDuration)
                               .SetEase(Ease.InQuad)
            );

            _flareSequence.SetLoops(-1, LoopType.Restart);
            _flareSequence.SetUpdate(true);
        }
    }
}
