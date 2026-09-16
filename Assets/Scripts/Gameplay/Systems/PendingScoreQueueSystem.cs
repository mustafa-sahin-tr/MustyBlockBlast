using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="PendingScoreModel"/>: banks a score the game could not submit, keeps it on disk
    /// across launches, and drains it as soon as the backend is reachable again.
    /// <para>
    /// The queue exists because a run's score is earned the moment the run ends, but a leaderboard can
    /// only accept it when the network can. Without a queue, a player finishing a run on a plane loses
    /// the score outright — and losing earned progress is a far worse failure than filing it late.
    /// </para>
    /// <para>
    /// Constructed eagerly as a dependency of <see cref="LeaderboardSystem"/>, which the LifetimeScope
    /// resolves before boot completes. That matters beyond being ready for the first game over: the
    /// constructor is also what restores and starts draining the previous session's backlog.
    /// </para>
    /// </summary>
    public sealed class PendingScoreQueueSystem : IDisposable
    {
        /// <summary>Single PlayerPrefs key holding the whole obfuscated save blob — see
        /// <see cref="PendingScoreSaveData"/>.</summary>
        private const string SAVE_KEY = "Leaderboard.PendingScores";

        /// <summary>
        /// Gap between drain attempts. Unity exposes no "reachability changed" event, so "flush when the
        /// connection comes back" is necessarily a poll; this interval is long enough that the polling
        /// itself costs nothing, and short enough that a player who regains signal mid-session sees
        /// their backlog land during that session rather than at the next launch.
        /// </summary>
        private const float RETRY_INTERVAL_SECONDS = 20f;

        /// <summary>
        /// Key the stored blob is XORed against. Casual obfuscation only — it keeps a curious player
        /// from editing scores out of a plainly readable PlayerPrefs entry, and is not, and is not meant
        /// to be, real encryption against anyone determined.
        /// </summary>
        private static readonly byte[] ObfuscationKey = Encoding.UTF8.GetBytes("mbb.pending.v1");

        private readonly PendingScoreModel _pendingScoreModel;
        private readonly ILeaderboardsService _leaderboardsService;
        private readonly IAuthService _authService;
        private readonly IConnectivityService _connectivityService;

        /// <summary>
        /// Cancels the retry loop and any submission still in flight when the scope goes away, so an
        /// await never resumes into a disposed container.
        /// </summary>
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>Reused across writes so persisting the queue does not allocate a save object per
        /// game over.</summary>
        private readonly PendingScoreSaveData _saveData;

        /// <summary>Reused snapshot buffer for <see cref="FlushAsync"/> — the model is mutated while it
        /// is being walked, so the walk cannot run off the live list.</summary>
        private readonly List<PendingScoreEntry> _flushBuffer = new List<PendingScoreEntry>();

        private bool _isRetryLoopRunning;

        public PendingScoreQueueSystem(
            PendingScoreModel pendingScoreModel,
            ILeaderboardsService leaderboardsService,
            IAuthService authService,
            IConnectivityService connectivityService)
        {
            _pendingScoreModel = pendingScoreModel;
            _leaderboardsService = leaderboardsService;
            _authService = authService;
            _connectivityService = connectivityService;

            _saveData = Load();
            _pendingScoreModel.ReplaceAll(ToEntries(_saveData));

            // A backlog restored from a previous session is the whole point of persisting one: start
            // draining it now rather than waiting for another game over to notice it is there.
            if (_pendingScoreModel.Entries.Count > 0)
            {
                StartRetryLoop();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        /// <summary>
        /// Banks <paramref name="score"/> for later submission and writes it to disk before returning.
        /// <para>
        /// The write is synchronous and immediate on purpose: the app can be killed the instant after a
        /// game over — the player swiping it away from the end-of-run card is an ordinary thing to do —
        /// and a score only living in memory until some later flush would not survive that. One extra
        /// PlayerPrefs write per offline game over is a trivial price for never losing one.
        /// </para>
        /// </summary>
        internal void Enqueue(GameMode mode, int score)
        {
            _pendingScoreModel.Add(new PendingScoreEntry(mode, score));
            Save();
            StartRetryLoop();
        }

        /// <summary>
        /// Attempts every queued score once, removing only those the backend confirms.
        /// <para>
        /// Being offline or signed out is not a failure and not logged: both are expected states — the
        /// queue exists precisely because of the first, and sign-in can still be in flight on a slow
        /// network — and attempting anyway would burn a round trip to learn what was already known.
        /// </para>
        /// <para>
        /// Internal rather than private so the drain rules — what survives a failure, what a missing
        /// board does — are testable without waiting out a retry interval.
        /// </para>
        /// </summary>
        internal async UniTask FlushAsync(CancellationToken cancellationToken)
        {
            if (_connectivityService.IsOffline || !_authService.IsSignedIn)
            {
                return;
            }

            // Snapshot first: a confirmed entry is removed from the model as it goes, so walking the
            // live list would skip its neighbour.
            _flushBuffer.Clear();
            IReadOnlyList<PendingScoreEntry> entries = _pendingScoreModel.Entries;
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                _flushBuffer.Add(entries[entryIndex]);
            }

            bool anyRemoved = false;

            for (int bufferIndex = 0; bufferIndex < _flushBuffer.Count; bufferIndex++)
            {
                PendingScoreEntry entry = _flushBuffer[bufferIndex];

                if (!LeaderboardBoardIds.TryGetBoards(entry.Mode, out (string AllTimeId, string WeeklyId) boards))
                {
                    // Same semantics as the live path: a mode with no boards has nothing to submit, so
                    // the entry is dropped rather than retried forever.
                    anyRemoved |= TryRemove(entry);
                    continue;
                }

                try
                {
                    // In parallel, exactly as LeaderboardSystem submits: the two boards are independent.
                    await UniTask.WhenAll(
                        _leaderboardsService.AddPlayerScoreAsync(boards.AllTimeId, entry.Score, cancellationToken),
                        _leaderboardsService.AddPlayerScoreAsync(boards.WeeklyId, entry.Score, cancellationToken));

                    anyRemoved |= TryRemove(entry);
                }
                catch (OperationCanceledException)
                {
                    // Ordinary teardown. Everything not yet confirmed stays queued by construction, so
                    // there is nothing to clean up — but the partial progress still has to be written,
                    // hence the persist below rather than a bare rethrow here.
                    if (anyRemoved)
                    {
                        Save();
                    }

                    throw;
                }
                catch (Exception exception)
                {
                    // One unreachable board must not strand the rest of the backlog: the entry stays
                    // queued for the next attempt while the remaining entries still get their try.
                    Debug.LogError(
                        $"Queued leaderboard submission failed for {entry.Mode} and stays queued: {exception.Message}");
                }
            }

            // One write for the whole drain rather than one per entry — the file only has to be correct
            // once the walk is over.
            if (anyRemoved)
            {
                Save();
            }
        }

        /// <summary>
        /// Removes the first entry matching <paramref name="entry"/>, which is the oldest queued copy of
        /// it. Matching by value rather than by index because the model can have been mutated since the
        /// snapshot was taken; two identical scores are interchangeable, so removing either is correct.
        /// </summary>
        private bool TryRemove(PendingScoreEntry entry)
        {
            IReadOnlyList<PendingScoreEntry> entries = _pendingScoreModel.Entries;
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                PendingScoreEntry candidate = entries[entryIndex];
                if (candidate.Mode != entry.Mode || candidate.Score != entry.Score)
                {
                    continue;
                }

                _pendingScoreModel.RemoveAt(entryIndex);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Starts the drain loop unless one is already running. The guard is what keeps a burst of
        /// offline game overs from stacking loops that would all wake on the same interval.
        /// </summary>
        private void StartRetryLoop()
        {
            if (_isRetryLoopRunning || _cts.IsCancellationRequested)
            {
                return;
            }

            _isRetryLoopRunning = true;
            RetryLoop(_cts.Token).Forget();
        }

        /// <summary>
        /// Retries until the queue is empty, then stops — an idle game with nothing banked does no work
        /// at all, and the next <see cref="Enqueue"/> restarts the loop.
        /// </summary>
        private async UniTaskVoid RetryLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (_pendingScoreModel.Entries.Count > 0)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(RETRY_INTERVAL_SECONDS),
                        ignoreTimeScale: true,
                        cancellationToken: cancellationToken);

                    await FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Ordinary teardown — the scope was disposed between attempts. Not an error.
            }
            finally
            {
                _isRetryLoopRunning = false;
            }
        }

        /// <summary>
        /// Projects the loaded blob into model entries, silently skipping any row whose mode name no
        /// longer parses. A renamed or removed mode is a content change, not a corrupt file: it costs
        /// that one unrankable score, and must not cost the rest of the backlog.
        /// </summary>
        private static List<PendingScoreEntry> ToEntries(PendingScoreSaveData saveData)
        {
            var entries = new List<PendingScoreEntry>(saveData.entries.Count);

            for (int entryIndex = 0; entryIndex < saveData.entries.Count; entryIndex++)
            {
                PendingScoreSaveEntry saved = saveData.entries[entryIndex];
                if (saved == null)
                {
                    continue;
                }

                if (!Enum.TryParse(saved.mode, out GameMode mode))
                {
                    continue;
                }

                entries.Add(new PendingScoreEntry(mode, saved.score));
            }

            return entries;
        }

        /// <summary>
        /// Reads the save blob, falling back to an empty queue on anything unreadable. A corrupt file
        /// costs the banked scores, which is strictly better than failing the boot over them — and every
        /// score in it was, by definition, one the backend never saw anyway.
        /// </summary>
        private static PendingScoreSaveData Load()
        {
            string stored = PlayerPrefs.GetString(SAVE_KEY, string.Empty);
            if (string.IsNullOrEmpty(stored))
            {
                return new PendingScoreSaveData();
            }

            PendingScoreSaveData data = null;
            try
            {
                string json = Deobfuscate(stored);
                if (!string.IsNullOrEmpty(json))
                {
                    data = JsonUtility.FromJson<PendingScoreSaveData>(json);
                }
            }
            catch (ArgumentException exception)
            {
                Debug.LogError($"Pending score queue was unreadable and has been reset: {exception.Message}");
            }

            if (data == null)
            {
                return new PendingScoreSaveData();
            }

            // JsonUtility writes null for a list that was never populated, so the list is the one field
            // that needs guarding before anything reads it.
            if (data.entries == null)
            {
                data.entries = new List<PendingScoreSaveEntry>();
            }

            return data;
        }

        private void Save()
        {
            _saveData.schemaVersion = PendingScoreSaveData.CURRENT_SCHEMA_VERSION;
            _saveData.entries.Clear();

            IReadOnlyList<PendingScoreEntry> entries = _pendingScoreModel.Entries;
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                PendingScoreEntry entry = entries[entryIndex];
                _saveData.entries.Add(new PendingScoreSaveEntry
                {
                    mode = entry.Mode.ToString(),
                    score = entry.Score,
                });
            }

            PlayerPrefs.SetString(SAVE_KEY, Obfuscate(JsonUtility.ToJson(_saveData)));

            // Flushed rather than left to Unity's own timing: the whole point of writing here is to
            // survive a kill that can land in the next moment.
            PlayerPrefs.Save();
        }

        /// <summary>XOR against a fixed key, then Base64 so the result survives PlayerPrefs' string
        /// storage. Deliberately trivial — see <see cref="ObfuscationKey"/>.</summary>
        private static string Obfuscate(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            ApplyXor(bytes);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>Inverse of <see cref="Obfuscate"/>. Returns an empty string for anything that is not
        /// valid Base64, which <see cref="Load"/> treats as an unreadable file.</summary>
        private static string Deobfuscate(string stored)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(stored);
            }
            catch (FormatException)
            {
                return string.Empty;
            }

            ApplyXor(bytes);
            return Encoding.UTF8.GetString(bytes);
        }

        private static void ApplyXor(byte[] bytes)
        {
            for (int byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
            {
                bytes[byteIndex] ^= ObfuscationKey[byteIndex % ObfuscationKey.Length];
            }
        }
    }
}
