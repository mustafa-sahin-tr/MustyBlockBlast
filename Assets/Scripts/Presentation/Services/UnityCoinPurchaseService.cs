using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.Purchasing;

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// Unity In-App Purchasing implementation of <see cref="ICoinPurchaseService"/>. The only type in
    /// the project that touches the purchasing SDK, so swapping stores — or stubbing one out in a test —
    /// is one binding in the LifetimeScope, exactly as it is for <see cref="UnityAuthService"/>.
    /// <para>
    /// The SDK's API is event-driven (a store connection, a product fetch and a purchase each complete
    /// on a callback) while the seam above is a single awaitable call, so the whole of this class is that
    /// translation: one completion source per outstanding operation, completed from the SDK's handlers.
    /// Nothing else lives here — no coin arithmetic, no persistence, no decision about whether a receipt
    /// is worth anything. Those belong to <see cref="CurrencySystem"/> and
    /// <see cref="IPurchaseReceiptValidator"/>, and keeping them out is what lets this file be the only
    /// one that has to be reviewed when the SDK changes.
    /// </para>
    /// <para>
    /// One purchase at a time. The stores enforce this themselves (Unity IAP reports
    /// <see cref="PurchaseFailureReason.ExistingPurchasePending"/> for a second concurrent order), so a
    /// second overlapping ask is refused here rather than handed down to fail less clearly — and a
    /// single in-flight slot is also what makes matching a callback back to its awaiter unambiguous,
    /// since the SDK's own purchase events carry no correlation id.
    /// </para>
    /// <para>
    /// Products are configured from <see cref="CoinBundleConfig"/> and
    /// <see cref="RemoveAdsProductConfig"/>, so the line-up the storefronts show and the line-up fetched
    /// from the store come from those same two assets. The coin bundles are all
    /// <see cref="ProductType.Consumable"/>: a coin bundle is spent on receipt and is meant to be
    /// buyable again. The ad-removal product is the one exception and is
    /// <see cref="ProductType.NonConsumable"/> — it is bought once and owned forever, and registering it
    /// as a consumable would let the store sell it twice.
    /// </para>
    /// <para>
    /// Two configs rather than one, and two kinds of product rather than one, is the whole of what this
    /// class knows about the difference between them. It still holds no notion of what either purchase
    /// is <em>worth</em>: the coins belong to <see cref="CurrencySystem"/> and the ad-removal flag to
    /// <see cref="AdRemovalSystem"/>, and both go through <see cref="IPurchaseReceiptValidator"/> first.
    /// A second implementation of the seam for the second product was the alternative, and it would have
    /// meant a second <see cref="StoreController"/> connection to the same store — which is why the
    /// product line-up widened here instead.
    /// </para>
    /// </summary>
    public sealed class UnityCoinPurchaseService : ICoinPurchaseService, IDisposable
    {
        private readonly CoinBundleConfig _bundleConfig;

        /// <summary>
        /// The ad-removal product, or null when this build does not sell one. Held for exactly two
        /// questions: whether to register it with the store, and whether a SKU handed to
        /// <see cref="PurchaseAsync"/> is it.
        /// </summary>
        private readonly RemoveAdsProductConfig _removeAdsConfig;

        /// <summary>
        /// Pending orders awaiting <see cref="CompletePurchase"/>, keyed by the transaction id the seam
        /// hands back and forth. Held because the SDK confirms a purchase by handing back the very
        /// <see cref="PendingOrder"/> object it delivered, which is an SDK type and therefore cannot
        /// cross the seam — the transaction id stands in for it on the far side.
        /// </summary>
        private readonly Dictionary<string, PendingOrder> _unconfirmedOrders =
            new Dictionary<string, PendingOrder>();

        private StoreController _storeController;

        /// <summary>
        /// Completion of the one-time connect-and-fetch, cached so every later purchase awaits the same
        /// result instead of reconnecting. Null until the first purchase asks for it.
        /// </summary>
        private UniTaskCompletionSource<bool> _readySource;

        private UniTaskCompletionSource<CoinPurchaseResult> _pendingPurchaseSource;
        private UniTaskCompletionSource<bool> _productsFetchedSource;

        public UnityCoinPurchaseService(
            CoinBundleConfig bundleConfig, RemoveAdsProductConfig removeAdsConfig)
        {
            _bundleConfig = bundleConfig;
            _removeAdsConfig = removeAdsConfig;
        }

        public async UniTask<CoinPurchaseResult> PurchaseAsync(
            string sku, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // An unknown SKU is an authoring or caller mistake, not a store problem, and it is caught
            // here so the store is never asked for a product this build does not sell. "Known" means
            // known to either config — a coin bundle row or the ad-removal product — because those two
            // together are exactly the line-up BuildProductDefinitions registers, and a guard narrower
            // than the line-up would refuse a product the store is quite happy to sell.
            if (!IsKnownSku(sku))
            {
                Debug.LogError(
                    $"{nameof(UnityCoinPurchaseService)} was asked for the unknown SKU '{sku}'. " +
                    $"Add it to {nameof(CoinBundleConfig)} or {nameof(RemoveAdsProductConfig)} and " +
                    "register it with the platform stores.");
                return CoinPurchaseResult.Failed;
            }

            if (_pendingPurchaseSource != null)
            {
                return CoinPurchaseResult.Failed;
            }

            bool isReady = await EnsureReadyAsync(cancellationToken);
            if (!isReady)
            {
                return CoinPurchaseResult.Failed;
            }

            // Resolved to a Product rather than purchased by id, so a SKU the store did not return —
            // typically one missing from App Store Connect or the Play Console — fails as a readable
            // configuration problem instead of an opaque store error.
            Product product = _storeController.GetProductById(sku);
            if (product == null)
            {
                Debug.LogError(
                    $"The store returned no product for SKU '{sku}'. Is it registered and active in " +
                    "App Store Connect / the Play Console?");
                return CoinPurchaseResult.Failed;
            }

            var purchaseSource = new UniTaskCompletionSource<CoinPurchaseResult>();
            _pendingPurchaseSource = purchaseSource;

            try
            {
                _storeController.PurchaseProduct(product);

                // AttachExternalCancellation rather than a cancellable await: the store prompt cannot be
                // dismissed from code, so cancelling abandons the await (nothing touches a disposed
                // scope) and leaves the transaction to the store. An abandoned purchase that still
                // completes arrives unconfirmed and is replayed on the next launch.
                return await purchaseSource.Task.AttachExternalCancellation(cancellationToken);
            }
            finally
            {
                // Cleared even on cancellation, or the single in-flight slot would stay occupied for the
                // rest of the session and refuse every later purchase.
                if (ReferenceEquals(_pendingPurchaseSource, purchaseSource))
                {
                    _pendingPurchaseSource = null;
                }
            }
        }

        public void CompletePurchase(PurchaseReceipt receipt)
        {
            if (_storeController == null || string.IsNullOrEmpty(receipt.TransactionId))
            {
                return;
            }

            if (!_unconfirmedOrders.TryGetValue(receipt.TransactionId, out PendingOrder order))
            {
                return;
            }

            _unconfirmedOrders.Remove(receipt.TransactionId);
            _storeController.ConfirmPurchase(order);
        }

        public void Dispose()
        {
            if (_storeController == null)
            {
                return;
            }

            _storeController.OnStoreConnected -= OnStoreConnected;
            _storeController.OnStoreDisconnected -= OnStoreDisconnected;
            _storeController.OnProductsFetched -= OnProductsFetched;
            _storeController.OnProductsFetchFailed -= OnProductsFetchFailed;
            _storeController.OnPurchasePending -= OnPurchasePending;
            _storeController.OnPurchaseFailed -= OnPurchaseFailed;
            _storeController = null;
        }

        /// <summary>
        /// Connects to the store and fetches the bundle line-up, once. Every caller after the first
        /// awaits the same cached result, so the seam's promise that a purchase connects "on demand and
        /// idempotently" holds however many times the storefront is opened.
        /// <para>
        /// Deliberately not retried on failure beyond the SDK's own connection retry policy: a store
        /// that is unreachable now is the player's "try again later", and pinning that answer for the
        /// session would be worse than letting the next tap ask again — which is why a failed attempt
        /// clears the cache rather than caching the failure.
        /// </para>
        /// </summary>
        private async UniTask<bool> EnsureReadyAsync(CancellationToken cancellationToken)
        {
            if (_readySource != null)
            {
                return await _readySource.Task.AttachExternalCancellation(cancellationToken);
            }

            var readySource = new UniTaskCompletionSource<bool>();
            _readySource = readySource;

            bool isReady = false;
            try
            {
                isReady = await ConnectAndFetchAsync(cancellationToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                // The SDK throws from Connect on some platform misconfigurations. Swallowed into a
                // refusal because a storefront that cannot open is not a crash — the player simply
                // cannot buy anything right now.
                Debug.LogError($"Connecting to the store failed: {exception.Message}");
            }
            finally
            {
                readySource.TrySetResult(isReady);
                if (!isReady && ReferenceEquals(_readySource, readySource))
                {
                    _readySource = null;
                }
            }

            return isReady;
        }

        private async UniTask<bool> ConnectAndFetchAsync(CancellationToken cancellationToken)
        {
            _storeController = UnityIAPServices.StoreController();

            // Purchases left unconfirmed by a killed session are not replayed into the purchase
            // callbacks here. They stay unconfirmed at the store — and therefore recoverable by a
            // restore flow, which is a follow-up — rather than arriving with no awaiter, where they
            // would either be dropped (coins the player paid for, lost) or auto-confirmed (the same,
            // with the store's replay thrown away too).
            _storeController.ProcessPendingOrdersOnPurchasesFetched(false);

            _storeController.OnStoreConnected += OnStoreConnected;
            _storeController.OnStoreDisconnected += OnStoreDisconnected;
            _storeController.OnProductsFetched += OnProductsFetched;
            _storeController.OnProductsFetchFailed += OnProductsFetchFailed;
            _storeController.OnPurchasePending += OnPurchasePending;
            _storeController.OnPurchaseFailed += OnPurchaseFailed;

            // AttachExternalCancellation for the reason UnityAuthService uses it: the SDK call itself
            // takes no token, so cancelling abandons the await and leaves the request to finish.
            await _storeController.Connect().AsUniTask().AttachExternalCancellation(cancellationToken);

            var fetchSource = new UniTaskCompletionSource<bool>();
            _productsFetchedSource = fetchSource;

            _storeController.FetchProducts(BuildProductDefinitions());

            return await fetchSource.Task.AttachExternalCancellation(cancellationToken);
        }

        /// <summary>
        /// Whether either config names <paramref name="sku"/>. The one place "does this build sell it?"
        /// is answered, so <see cref="PurchaseAsync"/>'s guard and
        /// <see cref="BuildProductDefinitions"/>'s line-up cannot drift apart into a SKU that is
        /// registered but refused, or refused but registered.
        /// </summary>
        private bool IsKnownSku(string sku)
        {
            if (_removeAdsConfig != null && _removeAdsConfig.Matches(sku))
            {
                return true;
            }

            return _bundleConfig != null && _bundleConfig.TryGetBundle(sku, out CoinBundle unused);
        }

        /// <summary>
        /// One <see cref="ProductDefinition"/> per authored bundle, walked by index so the line-up is
        /// built without an enumerator, plus the ad-removal product when one is configured.
        /// <para>
        /// The bundles are consumable — a coin bundle is spent the moment it is credited and must be
        /// buyable again. The ad-removal product is non-consumable, which is not a detail: a consumable
        /// is consumed on confirmation and the store will sell it again, so registering a
        /// bought-once-owned-forever product as one would let a player pay twice for something they
        /// already own, and would leave the store with no record of the entitlement for a restore to
        /// find.
        /// </para>
        /// </summary>
        private List<ProductDefinition> BuildProductDefinitions()
        {
            int bundleCount = _bundleConfig == null ? 0 : _bundleConfig.BundleCount;
            var definitions = new List<ProductDefinition>(bundleCount + 1);

            for (int bundleIndex = 0; bundleIndex < bundleCount; bundleIndex++)
            {
                CoinBundle bundle = _bundleConfig.BundleAt(bundleIndex);
                if (bundle.IsValid)
                {
                    definitions.Add(new ProductDefinition(bundle.Sku, ProductType.Consumable));
                }
            }

            if (_removeAdsConfig != null && _removeAdsConfig.IsValid)
            {
                definitions.Add(new ProductDefinition(_removeAdsConfig.Sku, ProductType.NonConsumable));
            }

            return definitions;
        }

        /// <summary>
        /// The store took the money. The receipt is read off the <see cref="PendingOrder"/> here and
        /// nowhere later, because the SDK documents both the receipt and the transaction id of a
        /// consumable as being available only while the order is pending — once confirmed they are gone.
        /// <para>
        /// The order itself is kept, unconfirmed, until <see cref="CompletePurchase"/>: confirming here
        /// would tell the store the coins had been delivered before they had been, and a crash in that
        /// window would lose them with no replay to recover them.
        /// </para>
        /// </summary>
        private void OnPurchasePending(PendingOrder order)
        {
            string transactionId = order.Info.TransactionID;
            var receipt = new PurchaseReceipt(transactionId, ResolveOrderedSku(order), order.Info.Receipt);

            if (!string.IsNullOrEmpty(transactionId))
            {
                _unconfirmedOrders[transactionId] = order;
            }

            UniTaskCompletionSource<CoinPurchaseResult> source = _pendingPurchaseSource;
            if (source == null)
            {
                // No awaiter: an order that arrived after its purchase was abandoned. Left unconfirmed
                // on purpose, for the reason stated on ProcessPendingOrdersOnPurchasesFetched above.
                Debug.LogWarning(
                    $"A purchase completed with no request awaiting it (transaction '{transactionId}'). " +
                    "It is left unconfirmed so the store can replay it.");
                return;
            }

            source.TrySetResult(CoinPurchaseResult.FromReceipt(receipt));
        }

        /// <summary>
        /// The store refused. A dismissal is separated from every other failure because the two are
        /// different sentences to the player — one of them does not even need saying.
        /// </summary>
        private void OnPurchaseFailed(FailedOrder order)
        {
            UniTaskCompletionSource<CoinPurchaseResult> source = _pendingPurchaseSource;
            if (source == null)
            {
                return;
            }

            bool wasCancelled = order.FailureReason == PurchaseFailureReason.UserCancelled;
            if (!wasCancelled)
            {
                Debug.LogWarning($"Purchase failed: {order.FailureReason} — {order.Details}");
            }

            source.TrySetResult(
                wasCancelled ? CoinPurchaseResult.Cancelled : CoinPurchaseResult.Failed);
        }

        /// <summary>
        /// Which SKU an order was for, read off the first cart item. Coin bundles are only ever ordered
        /// one at a time by <see cref="PurchaseAsync"/>, so there is never a second item — and the id is
        /// read from the order rather than remembered from the request so that what is credited is what
        /// the store says was bought.
        /// </summary>
        private static string ResolveOrderedSku(Order order)
        {
            IReadOnlyList<CartItem> items = order.CartOrdered == null ? null : order.CartOrdered.Items();
            if (items == null || items.Count == 0)
            {
                return string.Empty;
            }

            CartItem firstItem = items[0];
            if (firstItem == null || firstItem.Product == null)
            {
                return string.Empty;
            }

            return firstItem.Product.definition.id;
        }

        private void OnProductsFetched(List<Product> products)
        {
            UniTaskCompletionSource<bool> source = _productsFetchedSource;
            _productsFetchedSource = null;
            if (source != null)
            {
                source.TrySetResult(true);
            }
        }

        private void OnProductsFetchFailed(ProductFetchFailed failure)
        {
            Debug.LogError(
                $"Fetching {failure.FailedFetchProducts.Count} store products failed: " +
                $"{failure.FailureReason}. Are the SKUs registered with the platform stores?");

            UniTaskCompletionSource<bool> source = _productsFetchedSource;
            _productsFetchedSource = null;
            if (source != null)
            {
                source.TrySetResult(false);
            }
        }

        /// <summary>Subscribed to because the SDK warns when <c>Connect</c> is called without a listener
        /// on it. The connection itself is awaited through <c>Connect</c>'s own task.</summary>
        private static void OnStoreConnected()
        {
        }

        /// <summary>
        /// Subscribed to for the same reason, and it does have one job: a disconnection invalidates the
        /// cached readiness, so the next purchase reconnects rather than ordering against a store that
        /// is no longer there.
        /// </summary>
        private void OnStoreDisconnected(StoreConnectionFailureDescription description)
        {
            Debug.LogWarning($"The store disconnected: {description.message}");
            _readySource = null;
        }
    }
}
