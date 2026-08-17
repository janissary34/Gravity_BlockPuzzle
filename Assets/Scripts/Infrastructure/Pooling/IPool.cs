namespace GravityPuzzle.Infrastructure.Pooling
{
    public interface IPool<T>
    {
        bool TryRent(out T item);
        void Return(T item);
        int AvailableCount { get; }
        int Capacity { get; }
    }
}
