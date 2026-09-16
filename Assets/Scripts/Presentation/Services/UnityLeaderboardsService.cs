using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Systems;

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// Unity Gaming Services implementation of <see cref="ILeaderboardsService"/>. The only type in the
    /// project that touches the UGS Leaderboards SDK, so swapping backends — or stubbing one out in a
    /// test — is one binding in the LifetimeScope, exactly like <see cref="UnityAuthService"/>.
    /// </summary>
    public sealed class UnityLeaderboardsService : ILeaderboardsService
    {
        public async UniTask AddPlayerScoreAsync(
            string leaderboardId,
            int score,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Fully qualified rather than a using directive: the SDK has its own ILeaderboardsService,
            // and importing the namespace would make our seam's name ambiguous in this file.
            //
            // AttachExternalCancellation rather than a cancellable overload because the SDK call itself
            // takes no token: cancelling abandons the await (so nothing touches a disposed scope), while
            // the underlying request is left to finish on its own — which is the right trade here, since
            // a score that lands after teardown is still a score the player earned.
            await Unity.Services.Leaderboards.LeaderboardsService.Instance
                .AddPlayerScoreAsync(leaderboardId, score)
                .AsUniTask()
                .AttachExternalCancellation(cancellationToken);
        }
    }
}
