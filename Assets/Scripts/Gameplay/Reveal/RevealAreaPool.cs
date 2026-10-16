using System.Collections.Generic;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Reveal
{
    public static class RevealAreaPool
    {
        private static GameObjectPool<BoxRevealAreaView> boxPool;
        private static GameObjectPool<ElevatorRevealAreaView> elevatorPool;
        private static readonly List<BoxRevealAreaView> activeBoxes = new List<BoxRevealAreaView>();
        private static readonly List<ElevatorRevealAreaView> activeElevators = new List<ElevatorRevealAreaView>();

        public static void Configure(
            BoxRevealAreaView boxPrefab,
            int boxCapacity,
            ElevatorRevealAreaView elevatorPrefab,
            int elevatorCapacity,
            Transform parent)
        {
            ResetAllActive();

            if (boxPrefab != null && boxCapacity > 0)
            {
                boxPool = new GameObjectPool<BoxRevealAreaView>(boxPrefab, parent, boxCapacity);
                boxPool.Prewarm();
            }
            else
            {
                boxPool = null;
            }

            if (elevatorPrefab != null && elevatorCapacity > 0)
            {
                elevatorPool = new GameObjectPool<ElevatorRevealAreaView>(elevatorPrefab, parent, elevatorCapacity);
                elevatorPool.Prewarm();
            }
            else
            {
                elevatorPool = null;
            }
        }

        public static bool TryRentBox(out BoxRevealAreaView box)
        {
            if (boxPool != null && boxPool.TryRent(out box))
            {
                activeBoxes.Add(box);
                return true;
            }

            box = null;
            return false;
        }

        public static bool TryRentElevator(out ElevatorRevealAreaView elevator)
        {
            if (elevatorPool != null && elevatorPool.TryRent(out elevator))
            {
                activeElevators.Add(elevator);
                return true;
            }

            elevator = null;
            return false;
        }

        public static void ResetAllActive()
        {
            for (int i = activeBoxes.Count - 1; i >= 0; i--)
            {
                if (activeBoxes[i] != null)
                    activeBoxes[i].Despawn();
            }
            activeBoxes.Clear();

            for (int i = activeElevators.Count - 1; i >= 0; i--)
            {
                if (activeElevators[i] != null)
                    activeElevators[i].Despawn();
            }
            activeElevators.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool()
        {
            boxPool = null;
            elevatorPool = null;
            activeBoxes.Clear();
            activeElevators.Clear();
        }
    }
}
