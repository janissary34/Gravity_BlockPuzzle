using UnityEngine;
using ThreadFever.Controllers;
using ThreadFever.Events;
using ThreadFever.Models;

namespace ThreadFever.Test
{
    public class RaceTester : MonoBehaviour
    {
        [SerializeField] private RaceStateManager _stateManager;
        [SerializeField] private RaceController _raceController;

        private void OnEnable()
        {
            RaceEvents.OnRaceEnded += HandleRaceEnded;
            RaceEvents.OnComebackStateTriggered += HandleComeback;
        }

        private void OnDisable()
        {
            RaceEvents.OnRaceEnded -= HandleRaceEnded;
            RaceEvents.OnComebackStateTriggered -= HandleComeback;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.S))
            {
                Debug.Log("<color=green>Test: Yeni yarış başlatıldı!</color>");
                _stateManager.StartNewRace();
                RaceEvents.OnRaceContinued?.Invoke(); // UI'ı sıfırlamak için
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                Debug.Log("<color=cyan>Test: Oyuncu bir seviye geçti! (+1 Adım)</color>");
                _raceController.ProcessPlayerTurn(isSuccess: true, additionalSteps: 1);
            }
            
            if (Input.GetKeyDown(KeyCode.F))
            {
                Debug.Log("<color=red>Test: Oyuncu seviyeyi kaybetti! (Adım yok, AI'lar oynar)</color>");
                _raceController.ProcessPlayerTurn(isSuccess: false, additionalSteps: 0);
            }
        }

        private void HandleRaceEnded(RaceResult result)
        {
            if (result.IsWin)
            {
                Debug.Log($"<color=yellow>YARIŞ BİTTİ! KAZANDIN! Sıralama: {result.Rank}, Ödül: {result.Reward}</color>");
            }
            else
            {
                Debug.Log("<color=red>YARIŞ BİTTİ! ELENDİN!</color>");
            }
        }

        private void HandleComeback()
        {
            Debug.Log("<color=orange>COMEBACK DURUMU TETİKLENDİ! Liderden 3 adım geridesin.</color>");
        }
    }
}
