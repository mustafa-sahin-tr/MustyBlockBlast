using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Systems;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Store stand-in for the real-money purchase path: returns a scripted outcome and records what it
    /// was asked to do. A shared top-level helper rather than a nested class, for the reason
    /// <see cref="TestMessageBroker{T}"/> is one — two fixtures need it, and a second copy of a stub is
    /// a second thing that can drift away from the interface.
    /// <para>
    /// The real <c>UnityCoinPurchaseService</c> cannot be exercised in EditMode at all: there is no
    /// store to connect to, so a purchase would fail for reasons that say nothing about the code under
    /// test. What is testable — and what these stubs exist to test — is the orchestration above the
    /// seam: whether coins are credited, once, for the right amount, and only when they should be.
    /// </para>
    /// </summary>
    internal sealed class StubCoinPurchaseService : ICoinPurchaseService
    {
        private readonly CoinPurchaseResult _result;

        /// <summary>A store that completes the purchase and hands back
        /// <paramref name="receipt"/>.</summary>
        internal StubCoinPurchaseService(PurchaseReceipt receipt)
        {
            _result = CoinPurchaseResult.FromReceipt(receipt);
        }

        /// <summary>A store that refuses, with <paramref name="outcome"/> as the reason.</summary>
        internal StubCoinPurchaseService(CoinPurchaseOutcome outcome)
        {
            _result = outcome == CoinPurchaseOutcome.Cancelled
                ? CoinPurchaseResult.Cancelled
                : CoinPurchaseResult.Failed;
        }

        /// <summary>How often the store was asked to sell something, so a test can assert that a
        /// refused ask never reached it at all.</summary>
        internal int PurchaseCount { get; private set; }

        internal string LastSku { get; private set; }

        /// <summary>
        /// Every transaction the credit path acknowledged, in order. Recorded because the ordering
        /// matters: the store must only be told the goods were delivered *after* the coins are banked,
        /// and an already-consumed replay must still be acknowledged so the store stops replaying it.
        /// </summary>
        internal List<string> ConfirmedTransactionIds { get; } = new List<string>();

        public UniTask<CoinPurchaseResult> PurchaseAsync(string sku, CancellationToken cancellationToken)
        {
            PurchaseCount++;
            LastSku = sku;
            return UniTask.FromResult(_result);
        }

        public void CompletePurchase(PurchaseReceipt receipt)
        {
            ConfirmedTransactionIds.Add(receipt.TransactionId);
        }
    }
}
