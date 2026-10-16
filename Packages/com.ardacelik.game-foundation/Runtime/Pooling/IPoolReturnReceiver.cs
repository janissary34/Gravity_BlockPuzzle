using System;

namespace Arda.GameFoundation.Pooling
{
    /// <summary>
    /// Lets a pool install a return callback before the rented object is
    /// enabled, so pool ownership never relies on later scene discovery.
    /// </summary>
    public interface IPoolReturnReceiver<T>
    {
        void SetPoolReturnHandler(Action<T> returnHandler);
    }
}
