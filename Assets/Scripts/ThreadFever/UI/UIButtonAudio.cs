using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ThreadFever.UI
{
    /// <summary>
    /// Eklendiği butona tıklandığında ses çalan ve asıl geçiş olaylarını ses bitene kadar geciktiren bileşen.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class UIButtonAudio : MonoBehaviour
    {
        [Header("Audio Settings")]
        [Tooltip("Butona tıklandığında çalınacak ses efekti.")]
        [SerializeField] private AudioClip _clickSound;

        [Tooltip("Sesin seviyesi (0 ile 1 arasında).")]
        [Range(0f, 1f)]
        [SerializeField] private float _volume = 1f;

        [Header("Delay & Sync Settings")]
        [Tooltip("Geçiş işlemi başlamadan önce sesin çalması için beklenecek mi?")]
        [SerializeField] private bool _waitForSoundCompletion = true;

        [Tooltip("Geçiş süresi çarpanı (Örn: 1.0 = Sesin tamamı bitince geçiş yapar).")]
        [Range(0.1f, 3f)]
        [SerializeField] private float _completionRatio = 1f;

        [Tooltip("Ses süresinin ÜZERİNE eklenecek ekstra bekleme süresi (Saniye). Daha fazla delay istiyorsan burayı arttır!")]
        [Range(0f, 2f)]
        [SerializeField] private float _extraDelayInSeconds = 0.2f;

        [Header("Events")]
        [Tooltip("Butonun normal OnClick eventi yerine sahne geçişi/kapanış fonksiyonlarını BURAYA bağlayın!")]
        public UnityEvent OnDelayedClick;

        private Button _button;
        private Coroutine _routine;

        private void Awake()
        {
            // Objenin üzerindeki Button bileşenini al
            _button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            // Butonun tıklama event'ine abone ol
            if (_button != null)
            {
                _button.onClick.AddListener(OnButtonClicked);
            }
        }

        private void OnDisable()
        {
            // Obje kapatıldığında dinleyiciyi ve coroutine'i kaldır
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnButtonClicked);
            }

            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        private void OnDestroy()
        {
            // Bellek sızıntılarını önlemek için
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnButtonClicked);
            }
        }

        /// <summary>
        /// Butona tıklandığında süreci başlatan tetikleyici.
        /// </summary>
        private void OnButtonClicked()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
            }
            
            _routine = StartCoroutine(PlaySoundAndTransitionRoutine());
        }

        /// <summary>
        /// Sesi çalar, belirlediğimiz süre kadar bekler ve ardından asıl geçişi (Event) tetikler.
        /// </summary>
        private IEnumerator PlaySoundAndTransitionRoutine()
        {
            // 1. Önce Sesi Oynat
            if (_clickSound != null)
            {
                Vector3 playPosition = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                AudioSource.PlayClipAtPoint(_clickSound, playPosition, _volume);

                // 2. Bekleme Süresi (Delay)
                if (_waitForSoundCompletion)
                {
                    // Ses uzunluğu * çarpan + kullanıcının istediği ekstra saniye
                    float delayTime = (_clickSound.length * _completionRatio) + _extraDelayInSeconds;
                    yield return new WaitForSecondsRealtime(delayTime);
                }
            }
            else
            {
                Debug.LogWarning($"UIButtonAudio: '{gameObject.name}' objesinde AudioClip eksik!", this);
            }

            // 3. Asıl Sahne Geçişini / Kapanma İşlemini Tetikle
            OnDelayedClick?.Invoke();
            _routine = null;
        }
    }
}
