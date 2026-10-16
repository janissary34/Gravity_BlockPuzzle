using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Reveal
{
    public static class RevealAreaPool
    {
        private static GameObjectPool<BoxRevealAreaView> boxPool;
        private static GameObjectPool<ElevatorRevealAreaView> elevatorPool;

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
                return true;

            box = null;
            return false;
        }

        public static bool TryRentElevator(out ElevatorRevealAreaView elevator)
        {
            if (elevatorPool != null && elevatorPool.TryRent(out elevator))
                return true;

            elevator = null;
            return false;
        }

        public static void ResetAllActive()
        {
            boxPool?.ReturnAllRented();
            elevatorPool?.ReturnAllRented();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool()
        {
            boxPool = null;
            elevatorPool = null;
        }
    }
}
