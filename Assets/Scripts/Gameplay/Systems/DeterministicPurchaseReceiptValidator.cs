using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Settings;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Stand-in receipt validator used until a validation backend exists: it reads the SKU the device
    /// claims, looks its coin value up in <see cref="CoinBundleConfig"/>, and approves. The purchase
    /// counterpart of <see cref="DeterministicCoinRewardSource"/>, and deterministic for the same
    /// reason — the rest of the purchase flow has to be buildable and playable against it.
    /// <para>
    /// <b>This is not validation, and it must not ship as one.</b> It never reads
    /// <see cref="PurchaseReceipt.RawReceiptJson"/>, checks no signature, contacts nothing, and keeps no
    /// record of what has already been honoured. It trusts the claimed SKU completely, which means a
    /// tampered client could present any SKU it liked — including one it never paid for — and be
    /// approved for that bundle's coins. Everything real-money validation is for is absent here;
    /// what is present is the seam it will arrive through.
    /// </para>
    /// <para>
    /// Swapping it is one line in <c>GameLifetimeScope</c>, exactly as it is for the two ad stubs. A
    /// real implementation forwards the raw receipt to a server, has the store's own API confirm the
    /// transaction, resolves the coin amount from the server's copy of the line-up rather than from the
    /// device's claim, and records the transaction as spent in an account-level ledger. Before real
    /// money is taken from a real player, that implementation has to be the one bound.
    /// </para>
    /// <para>
    /// What is *not* deferred to that day: the client-side duplicate check in
    /// <see cref="CurrencySystem.PurchaseCoinBundleAsync"/>. That needs no backend, so it is built for
    /// real, and it holds even against this validator — a receipt replayed twice is approved twice here
    /// and still credited exactly once.
    /// </para>
    /// </summary>
    public sealed class DeterministicPurchaseReceiptValidator : IPurchaseReceiptValidator
    {
        private readonly CoinBundleConfig _bundleConfig;

        public DeterministicPurchaseReceiptValidator(CoinBundleConfig bundleConfig)
        {
            _bundleConfig = bundleConfig;
        }

        /// <summary>
        /// Approves any receipt naming a SKU the config knows, for that SKU's authored coin amount, and
        /// rejects one it does not.
        /// <para>
        /// The unknown SKU is rejected rather than approved for zero coins, because the two are not the
        /// same outcome upstream: a zero-coin approval would let the credit path mark a real transaction
        /// id as consumed while paying nothing, burning it for good. A rejection leaves the transaction
        /// unconsumed and unconfirmed, which is the recoverable state.
        /// </para>
        /// </summary>
        public UniTask<ValidatedPurchase> ValidateAsync(
            PurchaseReceipt receipt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_bundleConfig == null || !_bundleConfig.TryGetBundle(receipt.Sku, out CoinBundle bundle))
            {
                return UniTask.FromResult(ValidatedPurchase.Rejected);
            }

            return UniTask.FromResult(new ValidatedPurchase(bundle.CoinAmount, isValid: true));
        }
    }
}
