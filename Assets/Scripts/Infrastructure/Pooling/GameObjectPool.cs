using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Infrastructure.Pooling
{
    public sealed class GameObjectPool<T> : IPool<T> where T : MonoBehaviour, IPoolable
    {
        private readonly T prefab;
        private readonly Transform parent;
        private readonly Stack<T> available;
        private readonly HashSet<T> rented;
        private readonly List<T> rentedBuffer;

        public int AvailableCount => available.Count;
        public int Capacity { get; }

        public GameObjectPool(T prefabReference, Transform poolParent, int capacity)
        {
            prefab = prefabReference;
            parent = poolParent;
            Capacity = Mathf.Max(0, capacity);
            available = new Stack<T>(Capacity);
            rented = new HashSet<T>();
            rentedBuffer = new List<T>(Capacity);
        }

        // Called only during composition-root prewarm, before gameplay starts.
        public void Prewarm()
        {
            for (int index = available.Count + rented.Count; index < Capacity; index++)
            {
                T instance = Object.Instantiate(prefab, parent);
                instance.gameObject.SetActive(false);
                available.Push(instance);
            }
        }

        public bool TryRent(out T item)
        {
            if (available.Count == 0)
            {
                item = null;
                return false;
            }

            item = available.Pop();
            rented.Add(item);
            if (item is IPoolReturnReceiver<T> returnReceiver)
                returnReceiver.SetPoolReturnHandler(Return);
            item.gameObject.SetActive(true);
            item.OnSpawn();
            return true;
        }

        public void Return(T item)
        {
            if (item == null || !rented.Remove(item))
                return;

            item.OnDespawn();
            if (parent != null)
            {
                if (item.transform.parent != parent && parent.gameObject.scene.isLoaded)
                    item.transform.SetParent(parent, false);
            }
            else if (item.transform.parent != null)
            {
                item.transform.SetParent(null, false);
            }
            item.gameObject.SetActive(false);
            available.Push(item);
        }

        /// <summary>
        /// Returns every currently rented item without requiring presentation
        /// systems to maintain a second, potentially stale active list.
        /// </summary>
        public void ReturnAllRented()
        {
            if (rented.Count == 0)
                return;

            rentedBuffer.Clear();
            foreach (T item in rented)
                rentedBuffer.Add(item);

            for (int index = 0; index < rentedBuffer.Count; index++)
                Return(rentedBuffer[index]);

            rentedBuffer.Clear();
        }
    }
}
