using System;
using System.Collections.Generic;

namespace Arda.GameFoundation.Pooling
{
    /// <summary>
    /// Composition-root-owned registry for explicitly created typed pools.
    /// Do not expose this as a global service locator.
    /// </summary>
    public sealed class PoolRegistry
    {
        private readonly Dictionary<Type, object> pools = new Dictionary<Type, object>();

        public void Register<T>(IPool<T> pool)
        {
            if (pool == null)
                throw new ArgumentNullException(nameof(pool));

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
