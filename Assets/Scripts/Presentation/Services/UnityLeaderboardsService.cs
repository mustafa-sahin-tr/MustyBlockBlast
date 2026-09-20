using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using Mtafasahin.MobileServices;

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// Unity Gaming Services implementation of <see cref="ILeaderboardsService"/>. The only type in the
    /// project that touches the UGS Leaderboards SDK, so swapping backends — or stubbing one out in a
    /// test — is one binding in the LifetimeScope, exactly like <see cref="UnityAuthService"/>.
    /// </summary>
    public sealed class UnityLeaderboardsService : ILeaderboardsService
    {
        /// <summary>
        /// Shape the entry metadata is parsed back through. A field per key rather than a dictionary
        /// because <see cref="JsonUtility"/> cannot deserialise a map — and because the read side is
        /// deliberately narrower than the write side: an unknown key is simply not read, so adding one
        /// to a submission can never break an older client that does not know about it.
        /// </summary>
        [Serializable]
        private sealed class EntryMetadata
        {
            // Lower camel case and a public field because JsonUtility matches on the serialised name.
            // Named for LeaderboardMetadataKeys.AVATAR_ID, which is what writes it.
            public string avatarId;
        }

        public async UniTask AddPlayerScoreAsync(
            string leaderboardId,
            int score,
            IReadOnlyDictionary<string, string> metadata,
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
                .AddPlayerScoreAsync(leaderboardId, score, BuildOptions(metadata))
                .AsUniTask()
                .AttachExternalCancellation(cancellationToken);
        }

        public async UniTask<IReadOnlyList<LeaderboardEntryData>> GetScoresAsync(
            string leaderboardId,
            int limit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var options = new Unity.Services.Leaderboards.GetScoresOptions
            {
                Limit = limit,

                // Without this the SDK returns entries with a null Metadata string, and every row would
                // draw the default avatar however carefully it was submitted.
                IncludeMetadata = true,
            };

            Unity.Services.Leaderboards.Models.LeaderboardScoresPage page =
                await Unity.Services.Leaderboards.LeaderboardsService.Instance
                    .GetScoresAsync(leaderboardId, options)
                    .AsUniTask()
                    .AttachExternalCancellation(cancellationToken);

            List<Unity.Services.Leaderboards.Models.LeaderboardEntry> results = page?.Results;
            if (results == null)
            {
                return Array.Empty<LeaderboardEntryData>();
            }

            var entries = new LeaderboardEntryData[results.Count];
            for (int entryIndex = 0; entryIndex < results.Count; entryIndex++)
            {
                Unity.Services.Leaderboards.Models.LeaderboardEntry entry = results[entryIndex];
                entries[entryIndex] = new LeaderboardEntryData(
                    entry.Rank,
                    entry.PlayerName,

                    // The backend scores in double; a run's score is whole and well inside int range, so
                    // rounding back is exact for anything this game can have submitted.
                    (int)Math.Round(entry.Score),
                    ParseAvatarId(entry.Metadata));
            }

            return entries;
        }

        /// <summary>
        /// Null options for a bare submission rather than an options object with a null payload: the two
        /// mean the same thing to the backend, and the SDK already treats a missing options object as
        /// "no metadata".
        /// </summary>
        private static Unity.Services.Leaderboards.AddPlayerScoreOptions BuildOptions(
            IReadOnlyDictionary<string, string> metadata)
        {
            if (metadata == null || metadata.Count == 0)
            {
                return null;
            }

            // The SDK serialises this object itself, so the flat string map goes over as a flat JSON
            // object — which is exactly the shape EntryMetadata reads back.
            return new Unity.Services.Leaderboards.AddPlayerScoreOptions { Metadata = metadata };
        }

        /// <summary>
        /// Digs the avatar out of an entry's metadata, defaulting to the first preset for anything it
        /// cannot read.
        /// <para>
        /// Defensive throughout and deliberately silent: the payload is data a *different* client wrote,
        /// possibly an older build that submitted no metadata at all, so absent and malformed are both
        /// ordinary. A logged error per row would say nothing actionable and would be one line per
        /// entry, every fetch.
        /// </para>
        /// </summary>
        private static int ParseAvatarId(string metadataJson)
        {
            if (string.IsNullOrEmpty(metadataJson))
            {
                return 0;
            }

            EntryMetadata metadata = null;
            try
            {
                metadata = JsonUtility.FromJson<EntryMetadata>(metadataJson);
            }
            catch (ArgumentException)
            {
                // Not JSON, or not an object. Nothing to read.
            }

            if (metadata == null || string.IsNullOrEmpty(metadata.avatarId))
            {
                return 0;
            }

            // Invariant culture so a device locale cannot change how another player's id reads back,
            // matching the culture the submitting side writes it with.
            if (!int.TryParse(
                    metadata.avatarId,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int avatarId))
            {
                return 0;
            }

            // Clamping rather than trusting: an id from another client can name an avatar this build
            // does not have, and the view indexes its palette with it.
            return avatarId < 0 || avatarId >= ProfileModel.AVATAR_COUNT ? 0 : avatarId;
        }
    }
}
