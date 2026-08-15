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

        public int AvailableCount => available.Count;
        public int Capacity { get; }

        public GameObjectPool(T prefabReference, Transform poolParent, int capacity)
        {
            prefab = prefabReference;
            parent = poolParent;
            Capacity = Mathf.Max(0, capacity);
            available = new Stack<T>(Capacity);
            rented = new HashSet<T>();
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
            item.gameObject.SetActive(true);
            item.OnSpawn();
            return true;
        }

        public void Return(T item)
        {
            if (item == null || !rented.Remove(item))
                return;

            item.OnDespawn();
            item.transform.SetParent(parent, false);
            item.gameObject.SetActive(false);
            available.Push(item);
        }
    }
}
