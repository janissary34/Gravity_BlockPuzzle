namespace Arda.GameFoundation.Pooling
{
    /// <summary>
    /// Owns a reusable collection of a single component type.
    /// </summary>
    public interface IPool<T>
    {
        bool TryRent(out T item);
        void Return(T item);
        int AvailableCount { get; }
        int Capacity { get; }
    }
}
