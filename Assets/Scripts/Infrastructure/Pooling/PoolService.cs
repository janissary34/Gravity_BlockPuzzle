using System;
using System.Collections.Generic;

namespace GravityPuzzle.Infrastructure.Pooling
{
    public sealed class PoolService
    {
        private readonly Dictionary<Type, object> pools = new Dictionary<Type, object>();

        public void Register<T>(IPool<T> pool)
        {
            pools[typeof(T)] = pool;
        }

        public bool TryGet<T>(out IPool<T> pool)
        {
            if (pools.TryGetValue(typeof(T), out object registeredPool) &&
                registeredPool is IPool<T> typedPool)
            {
                pool = typedPool;
                return true;
            }

            pool = null;
            return false;
        }
    }
}
