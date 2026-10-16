namespace Arda.GameFoundation.Pooling
{
    /// <summary>
    /// Receives lifecycle callbacks immediately after renting and immediately
    /// before returning an object to its pool.
    /// </summary>
    public interface IPoolable
    {
        void OnSpawn();
        void OnDespawn();
    }
}
