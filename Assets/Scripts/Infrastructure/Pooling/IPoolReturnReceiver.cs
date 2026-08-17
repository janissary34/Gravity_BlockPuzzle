using System;

namespace GravityPuzzle.Infrastructure.Pooling
{
    /// <summary>
    /// Lets a typed pool install its return callback before a rented object is
    /// enabled. Pool ownership therefore cannot depend on a later factory step.
    /// </summary>
    public interface IPoolReturnReceiver<T>
    {
        void SetPoolReturnHandler(Action<T> returnHandler);
    }
}
