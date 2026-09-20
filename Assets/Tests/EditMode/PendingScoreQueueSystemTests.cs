using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Mtafasahin.MobileServices;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the offline score queue's two load-bearing promises: a score earned with no connection is
    /// banked rather than lost, and an entry is only ever removed once the backend has actually taken
    /// it — a failed drain must leave it behind for the next attempt.
    /// </summary>
    public class PendingScoreQueueSystemTests
    {
        private const string SAVE_KEY = "Leaderboard.PendingScores";

        [SetUp]
        public void SetUp() => PlayerPrefs.DeleteKey(SAVE_KEY);

        [TearDown]
        public void TearDown() => PlayerPrefs.DeleteKey(SAVE_KEY);

        [Test]
        public void Enqueue_BanksTheScoreAndPersistsItImmediately()
        {
            var connectivity = new FakeConnectivityService { IsOffline = true };
            var leaderboards = new FakeLeaderboardsService();
            var model = new PendingScoreModel();

            using (var system = new PendingScoreQueueSystem(
                model, leaderboards, new FakeAuthService(), connectivity, new ProfileModel()))
            {
                system.Enqueue(GameMode.Endless, 1234);
            }

            Assert.AreEqual(1, model.Entries.Count);
            Assert.AreEqual(GameMode.Endless, model.Entries[0].Mode);
            Assert.AreEqual(1234, model.Entries[0].Score);
            Assert.IsEmpty(leaderboards.Submissions, "Nothing may be submitted while offline.");

            // The write has to have already happened when Enqueue returned — an app kill right after a
            // game over is exactly the case the queue exists for.
            Assert.IsNotEmpty(PlayerPrefs.GetString(SAVE_KEY, string.Empty));
        }

        [Test]
        public void Restored_FromDisk_SurvivesAnAppKill()
        {
            using (var first = new PendingScoreQueueSystem(
                new PendingScoreModel(),
                new FakeLeaderboardsService(),
                new FakeAuthService(),
                new FakeConnectivityService { IsOffline = true },
                new ProfileModel()))
            {
                first.Enqueue(GameMode.Timed, 777);
            }

            var restoredModel = new PendingScoreModel();
            using (new PendingScoreQueueSystem(
                restoredModel,
                new FakeLeaderboardsService(),
                new FakeAuthService(),
                new FakeConnectivityService { IsOffline = true },
                new ProfileModel()))
            {
                Assert.AreEqual(1, restoredModel.Entries.Count);
                Assert.AreEqual(GameMode.Timed, restoredModel.Entries[0].Mode);
                Assert.AreEqual(777, restoredModel.Entries[0].Score);
            }
        }

        [Test]
        public void FlushAsync_WhenOnline_SubmitsBothBoardsAndClearsTheEntry()
        {
            var connectivity = new FakeConnectivityService { IsOffline = true };
            var leaderboards = new FakeLeaderboardsService();
            var model = new PendingScoreModel();

            using (var system = new PendingScoreQueueSystem(
                model, leaderboards, new FakeAuthService(), connectivity, new ProfileModel()))
            {
                system.Enqueue(GameMode.Endless, 42);
                connectivity.IsOffline = false;

                system.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            Assert.AreEqual(2, leaderboards.Submissions.Count);
            Assert.AreEqual("endless_all_time", leaderboards.Submissions[0]);
            Assert.AreEqual("endless_weekly", leaderboards.Submissions[1]);
            Assert.IsEmpty(model.Entries);

            // A queued score must land looking like one that went straight out, avatar included —
            // otherwise a run finished on a plane ranks as a faceless row.
            Assert.IsNotNull(leaderboards.LastMetadata);
            Assert.IsTrue(leaderboards.LastMetadata.ContainsKey(LeaderboardMetadataKeys.AVATAR_ID));
        }

        [Test]
        public void FlushAsync_WhenSubmissionFails_LeavesTheEntryQueued()
        {
            var connectivity = new FakeConnectivityService { IsOffline = true };
            var leaderboards = new FakeLeaderboardsService { ShouldFail = true };
            var model = new PendingScoreModel();

            using (var system = new PendingScoreQueueSystem(
                model, leaderboards, new FakeAuthService(), connectivity, new ProfileModel()))
            {
                system.Enqueue(GameMode.Endless, 99);
                connectivity.IsOffline = false;

                // The system logs one error per failed entry; this test asserts the queue, not the log.
                LogAssert.Expect(LogType.Error, new Regex("stays queued"));
                system.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            Assert.AreEqual(1, model.Entries.Count, "A failed submission must not drop the score.");
            Assert.AreEqual(99, model.Entries[0].Score);
        }

        [Test]
        public void FlushAsync_WhileOffline_DoesNothing()
        {
            var connectivity = new FakeConnectivityService { IsOffline = true };
            var leaderboards = new FakeLeaderboardsService();
            var model = new PendingScoreModel();

            using (var system = new PendingScoreQueueSystem(
                model, leaderboards, new FakeAuthService(), connectivity, new ProfileModel()))
            {
                system.Enqueue(GameMode.Endless, 5);
                system.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            Assert.IsEmpty(leaderboards.Submissions);
            Assert.AreEqual(1, model.Entries.Count);
        }

        private sealed class FakeConnectivityService : IConnectivityService
        {
            public bool IsOffline { get; set; }
        }

        private sealed class FakeAuthService : IAuthService
        {
            public bool IsSignedIn => true;

            public string PlayerId => "test-player";

            public UniTask SignInAnonymouslyAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;

            public UniTask LinkWithAppleAsync(string identityToken, CancellationToken cancellationToken) =>
                UniTask.CompletedTask;

            public UniTask LinkWithGooglePlayGamesAsync(string authCode, CancellationToken cancellationToken) =>
                UniTask.CompletedTask;

            public UniTask UpdatePlayerNameAsync(string name, CancellationToken cancellationToken) =>
                UniTask.CompletedTask;

            public AccountLinkStatus GetLinkStatus() => AccountLinkStatus.Anonymous;
        }

        private sealed class FakeLeaderboardsService : ILeaderboardsService
        {
            public List<string> Submissions { get; } = new List<string>();

            /// <summary>Metadata of the most recent submission, so a test can assert a queued score is
            /// filed with the same avatar a live one would be.</summary>
            public IReadOnlyDictionary<string, string> LastMetadata { get; private set; }

            public bool ShouldFail { get; set; }

            public UniTask AddPlayerScoreAsync(
                string leaderboardId,
                int score,
                IReadOnlyDictionary<string, string> metadata,
                CancellationToken cancellationToken)
            {
                if (ShouldFail)
                {
                    throw new InvalidOperationException("backend unavailable");
                }

                Submissions.Add(leaderboardId);
                LastMetadata = metadata;
                return UniTask.CompletedTask;
            }

            public UniTask<IReadOnlyList<LeaderboardEntryData>> GetScoresAsync(
                string leaderboardId,
                int limit,
                CancellationToken cancellationToken)
            {
                // The queue never reads a board — it only files what it banked.
                return UniTask.FromResult<IReadOnlyList<LeaderboardEntryData>>(
                    Array.Empty<LeaderboardEntryData>());
            }
        }
    }
}
