using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns the ad-removal slice of <see cref="ProfileModel"/>: the single
    /// <see cref="ProfileModel.AdsRemoved"/> flag, and the one-time real-money purchase that sets it.
    /// The one and only writer of that field.
    /// <para>
    /// Its own System rather than a fifth faucet on <see cref="CurrencySystem"/>, which is scoped by its
    /// own class doc to "the currency slice of <see cref="ProfileModel"/>". Removing ads mints no coin,
    /// spends no coin and does not touch the convertible pool; the only thing it shares with a coin
    /// bundle is the store it is bought through. Folding it in would widen the one class in the project
    /// that is deliberately narrow — the single writer of the balance — to cover a flag that has nothing
    /// to do with a balance. The split is the same one-writer-per-slice arrangement
    /// <see cref="CurrencySystem"/> already has with <see cref="ProfileSystem"/> and
    /// <see cref="PowerUpSystem"/>, extended by one slice, and it extends to loading too: the flag is
    /// read back by this constructor, not by anyone else's, so "who writes it" and "who reads it back"
    /// are never two answers.
    /// </para>
    /// <para>
    /// It shares the purchase seams rather than introducing new ones. <see cref="ICoinPurchaseService"/>
    /// is the project's one store integration — the interface's implementation is the only type that
    /// touches the purchasing SDK — and a second seam for a second product would mean a second store
    /// connection, which the SDK does not cleanly allow and no store would thank us for. The same goes
    /// for <see cref="IPurchaseReceiptValidator"/>: a receipt is a receipt, and the day validation moves
    /// to a server it must move for both products at once.
    /// </para>
    /// <para>
    /// <b>What this flag gates today: nothing.</b> The game shows no forced or interstitial advertising
    /// at all (see <c>docs/game-design.md</c> — "no forced interstitials in v1"), so there is no ad
    /// display to suppress. The two ad seams that do exist, <see cref="IRewardSource"/> and
    /// <see cref="ICoinRewardSource"/>, are opt-in rewarded ads the player asks for in exchange for a
    /// power-up or coins, and they deliberately stay available after this purchase: taking them away
    /// would make "remove ads" remove a way to earn, which is the opposite of what was bought. So this
    /// is persisted state with no consumer yet, on purpose — the flag has to exist and survive before
    /// anything can honour it, and the first forced-ad placement to be added will read it here rather
    /// than inventing its own record of the purchase.
    /// </para>
    /// <para>
    /// Persistence is one flat PlayerPrefs key, exactly as <see cref="ProfileSystem"/>,
    /// <see cref="PowerUpSystem"/> and <see cref="CurrencySystem"/> write theirs.
    /// </para>
    /// <para>
    /// <b>No transaction-id ledger, deliberately.</b> <see cref="CurrencySystem"/> keeps one because
    /// crediting the same receipt's coins twice would really pay twice; setting a boolean that is
    /// already <c>true</c> to <c>true</c> again changes nothing, so the flag is idempotent by its own
    /// nature and needs no memory of which receipts it has seen. A store replaying the purchase after a
    /// crash, a restore or a reinstall lands on exactly the state it should — which is also why a
    /// restore flow, when one exists, needs no new bookkeeping here. This is a simplification made on
    /// purpose and not an omission: the dedupe machinery next door exists for a bug this path cannot
    /// have.
    /// </para>
    /// </summary>
    public sealed class AdRemovalSystem
    {
        private const string ADS_REMOVED_KEY = "Profile.AdsRemoved";

        private readonly ProfileModel _profileModel;
        private readonly RemoveAdsProductConfig _productConfig;
        private readonly ICoinPurchaseService _purchaseService;
        private readonly IPurchaseReceiptValidator _receiptValidator;

        public AdRemovalSystem(
            ProfileModel profileModel,
            RemoveAdsProductConfig productConfig,
            ICoinPurchaseService purchaseService,
            IPurchaseReceiptValidator receiptValidator)
        {
            _profileModel = profileModel;
            _productConfig = productConfig;
            _purchaseService = purchaseService;
            _receiptValidator = receiptValidator;

            Load();
        }

        /// <summary>
        /// Whether the player owns the ad-removal product. Read by the settings card so it can show the
        /// purchase as owned instead of offering it again — the flag itself is a
        /// <see cref="MustyBlockBlast.Gameplay.Reactive.ReactiveProperty{T}"/> on the model, which is
        /// what a View subscribes to; this is the one-shot answer for a caller that only needs it now.
        /// </summary>
        public bool AdsRemoved => _profileModel.AdsRemoved.Value;

        /// <summary>
        /// Buys the configured ad-removal product for real money and records it as owned. Returns
        /// whether the flag ended up set by this call; every refusal along the way persists nothing and
        /// leaves the flag exactly as it found it.
        /// <para>
        /// Three gates, and nothing is written until all three are passed: the config has to name a
        /// product, the store has to complete the purchase, and a validator has to vouch for the
        /// receipt. A dismissal or a store failure stops at the second (see
        /// <see cref="CoinPurchaseOutcome"/> — the caller can tell those apart for its messaging, but
        /// both mean "record nothing"), and an unvouched receipt stops at the third. That third gate is
        /// the whole point of the validation seam: the store saying money moved is not the same as
        /// anything having been verified, and a client that set the flag on the store's word alone would
        /// hand the product to whoever could fake a receipt.
        /// </para>
        /// <para>
        /// Unlike <see cref="CurrencySystem.PurchaseCoinBundleAsync"/> there is no duplicate check and no
        /// transaction id is required, because this outcome is idempotent — see the class doc. An
        /// already-owning player who somehow reaches this path re-buys nothing: the store refuses a
        /// second order for a non-consumable it has already sold, and if it did not, setting the flag
        /// again would be a no-op.
        /// </para>
        /// <para>
        /// The validator's <see cref="ValidatedPurchase.CoinAmount"/> is deliberately ignored. This
        /// product is worth no coins, so the only part of the verdict that means anything here is
        /// <see cref="ValidatedPurchase.IsValid"/> — and reading the amount would refuse the one valid
        /// verdict a non-coin product can ever produce.
        /// </para>
        /// <para>
        /// Then the ordering: write the flag, flush, and acknowledge the store last. Last for the reason
        /// <see cref="ICoinPurchaseService.CompletePurchase"/> gives — a store told "delivered" before
        /// the flag reaches the disk would stop replaying a purchase a crash swallowed, leaving the
        /// player out of pocket with nothing to show for it. Confirming after the flush makes that window
        /// recoverable: the store replays, and the replay sets an already-set flag.
        /// </para>
        /// </summary>
        public async UniTask<bool> PurchaseRemoveAdsAsync(CancellationToken cancellationToken)
        {
            if (_productConfig == null || !_productConfig.IsValid)
            {
                Debug.LogError(
                    $"{nameof(AdRemovalSystem)} has no product to buy. Set a SKU on " +
                    $"{nameof(RemoveAdsProductConfig)} and register it with the platform stores.");
                return false;
            }

            CoinPurchaseResult purchaseResult = await _purchaseService.PurchaseAsync(
                _productConfig.Sku, cancellationToken);
            if (!purchaseResult.Succeeded)
            {
                return false;
            }

            PurchaseReceipt receipt = purchaseResult.Receipt;

            ValidatedPurchase validated = await _receiptValidator.ValidateAsync(
                receipt, cancellationToken);
            if (!validated.IsValid)
            {
                return false;
            }

            SetAdsRemoved();
            PlayerPrefs.Save();

            _purchaseService.CompletePurchase(receipt);
            return true;
        }

        /// <summary>
        /// Records the product as owned, in the model and in PlayerPrefs. Not flushed here, matching how
        /// <see cref="CurrencySystem.CreditCoins"/> leaves its flush to the caller: the caller is the one
        /// that knows how many writes its own outcome is made of, even when — as here — the answer
        /// happens to be one.
        /// </summary>
        private void SetAdsRemoved()
        {
            _profileModel.AdsRemoved.Value = true;

            // An int rather than PlayerPrefs' absent bool overload, which is what every other flag in
            // this project persists as.
            PlayerPrefs.SetInt(ADS_REMOVED_KEY, 1);
        }

        /// <summary>
        /// Reads the flag back. Anything other than the exact value written counts as "not owned": a
        /// corrupt or hand-edited save must not be able to hand out a paid product, and the default for
        /// a missing key is the same answer a fresh install needs.
        /// </summary>
        private void Load()
        {
            _profileModel.AdsRemoved.Value = PlayerPrefs.GetInt(ADS_REMOVED_KEY, 0) == 1;
        }
    }
}
