using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Systems;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Receipt-validation stand-in: approves or rejects on command, for a coin amount the test names,
    /// and counts how often it was consulted.
    /// <para>
    /// The amount is scripted separately from the SKU on purpose. A real validator prices a receipt from
    /// the server's own copy of the line-up rather than from what the device claims, so a test needs to
    /// be able to have it return a figure the client never asked for — which is the only way to prove
    /// the credit path banks the validator's number and not its own.
    /// </para>
    /// </summary>
    internal sealed class StubPurchaseReceiptValidator : IPurchaseReceiptValidator
    {
        private readonly ValidatedPurchase _verdict;

        internal StubPurchaseReceiptValidator(int coinAmount, bool isValid = true)
        {
            _verdict = new ValidatedPurchase(coinAmount, isValid);
        }

        /// <summary>A validator that rejects everything.</summary>
        internal static StubPurchaseReceiptValidator Rejecting()
            => new StubPurchaseReceiptValidator(0, isValid: false);

        /// <summary>How often a receipt reached the validator, so a test can assert that an
        /// already-consumed one was refused before the (potentially expensive) check.</summary>
        internal int ValidateCount { get; private set; }

        public UniTask<ValidatedPurchase> ValidateAsync(
            PurchaseReceipt receipt, CancellationToken cancellationToken)
        {
            ValidateCount++;
            return UniTask.FromResult(_verdict);
        }
    }
}
