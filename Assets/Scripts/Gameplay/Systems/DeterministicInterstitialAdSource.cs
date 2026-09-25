using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Stand-in interstitial source for the Editor, EditMode tests and every platform AdMob is not wired
    /// for: an ad is always "ready" and "shown" instantly, with nothing on screen. Deterministic on
    /// purpose, like <see cref="DeterministicRewardSource"/>, so the placement logic built on top of it
    /// can be played and tested without an SDK — but it still honours "remove ads", because that is part
    /// of the <see cref="IInterstitialAdSource"/> contract and the placement logic relies on it.
    /// </summary>
    public sealed class DeterministicInterstitialAdSource : IInterstitialAdSource
    {
        private readonly AdRemovalSystem _adRemovalSystem;

        public DeterministicInterstitialAdSource(AdRemovalSystem adRemovalSystem)
        {
            _adRemovalSystem = adRemovalSystem;
        }

        public UniTask PreloadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }

        public UniTask<bool> TryShowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(!_adRemovalSystem.AdsRemoved);
        }
    }
}
