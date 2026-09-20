using System.Threading;
using Cysharp.Threading.Tasks;

namespace Mtafasahin.MobileServices
{
    /// <summary>
    /// Seam between the coin economy and whatever decides a <see cref="PurchaseReceipt"/> is genuine.
    /// The gate every real-money credit passes through: no coin is minted for a purchase until something
    /// behind this interface has said the receipt is real and said what it is worth.
    /// <para>
    /// Its own seam rather than a step inside <see cref="ICoinPurchaseService"/>, because the two answer
    /// to different authorities and will not live in the same place. The purchase service is the store
    /// on the device; a validator is — eventually — a server that does not trust the device at all.
    /// Splitting them means the store integration can be swapped per platform without touching
    /// validation, and validation can move off the device without touching the store integration.
    /// </para>
    /// <para>
    /// Asynchronous and cancellable for that same reason: the shipped implementation
    /// (<see cref="DeterministicPurchaseReceiptValidator"/>) answers instantly and locally, but the
    /// implementation that replaces it is a network round trip, and a seam that could not await one
    /// would have to be rewritten to get one.
    /// </para>
    /// </summary>
    public interface IPurchaseReceiptValidator
    {
        /// <summary>
        /// Judges <paramref name="receipt"/> and reports what it is worth. A rejection is an ordinary
        /// outcome (<see cref="ValidatedPurchase.Rejected"/>), not an exception: a forged or unknown
        /// receipt is exactly what this exists to catch, so it must not read as a malfunction.
        /// </summary>
        UniTask<ValidatedPurchase> ValidateAsync(
            PurchaseReceipt receipt, CancellationToken cancellationToken);
    }
}
