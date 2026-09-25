using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns the player's Path-mode lives (issue #477): a failed Path run costs one, and every
    /// wall-clock hour boundary (xx:00) pays <see cref="LivesConfig.RefillAmount"/> back, up to
    /// <see cref="LivesConfig.RegenCap"/>. The only writer of <see cref="LivesModel"/>.
    /// <para>
    /// <b>What costs a life.</b> A Path run that ends with any reason other than
    /// <see cref="GameOverReason.LevelCompleted"/> — frontier or replay alike, unlike
    /// <see cref="RewardRuleSystem"/>, which only cares about the frontier. Only a
    /// <see cref="GameOverMessage"/> counts, so leaving a run mid-way (home, mode switch, app kill)
    /// costs nothing. The count never goes below zero. Endless and Timed never read or write lives.
    /// </para>
    /// <para>
    /// <b>What gives it back.</b> A failure the no-moves rescue then takes back
    /// (<see cref="RunRescuedMessage"/>) is refunded, the way <see cref="RewardRuleSystem"/> rolls its
    /// streak back — the player must not pay with both the ad and a life. A rescued run that dead-ends
    /// again is charged again, so it nets one life, as any failure does.
    /// </para>
    /// <para>
    /// <b>The refill.</b> Computed from the clock rather than counted by a timer: the save holds the hour
    /// the refill was last checked in, and every hour stamp crossed since is one boundary. So hours the
    /// app spent closed or suspended count exactly like hours it was open (3 lives at 10:40, reopened at
    /// 14:10: four boundaries, capped at 20). The refill only tops up to the cap — a count already at or
    /// above it (a coin pack, issue #479) is left alone, never lowered. A clock moved backwards just
    /// re-anchors the stamp to the current hour and grants nothing.
    /// </para>
    /// <para>
    /// A one-second loop re-checks the refill and keeps <see cref="LivesModel.SecondsUntilRefill"/>
    /// current for the HUD countdown. It is the countdown's clock, not the refill's: the refill itself
    /// is recomputed from the stamp on every check, so a suspended app catches up on its first tick
    /// after resume, and every deduction re-checks first so a boundary is never missed between ticks.
    /// </para>
    /// </summary>
    public sealed class LivesSystem : IStartable, IDisposable
    {
        /// <summary>Single PlayerPrefs key holding the whole JSON save blob — see <see cref="LivesSaveData"/>.</summary>
        private const string SAVE_KEY = "Lives.State";

        /// <summary>How often the loop re-checks the refill and repaints the countdown.</summary>
        private const float TICK_INTERVAL_SECONDS = 1f;

        private readonly LivesModel _model;
        private readonly LivesConfig _config;
        private readonly GameModeModel _gameModeModel;
        private readonly IDisposable _subscriptions;
        private readonly LivesSaveData _saveData;

        /// <summary>
        /// Where "now" comes from. Local time on purpose: the refill lands on the device's own xx:00,
        /// the moment the player sees on their clock. Behind a delegate for the reason
        /// <see cref="CurrencySystem"/>'s day marker is — an hour-driven rule is untestable against a
        /// clock nobody can move. The public constructor wires the real clock; only a test reaches the
        /// seeded overload.
        /// </summary>
        private readonly Func<DateTime> _localNowProvider;

        /// <summary>Cancels the countdown loop when the scope goes away, so it never resumes into a
        /// disposed container.</summary>
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// Whether the life most recently charged can still be refunded by a rescue — true from a
        /// rescue-available failure that actually cost a life until the rescue lands or the next run
        /// starts. A failure at zero lives charged nothing, so it leaves nothing to refund.
        /// </summary>
        private bool _hasPendingRefund;

        /// <summary>The count before the pending charge. A refund never lifts lives past
        /// <c>max(cap, this)</c>, so a refill that landed in between cannot turn the refund into a
        /// bonus life over the cap.</summary>
        private int _livesBeforeCharge;

        /// <summary>
        /// DI entry point. Explicitly marked because VContainer, absent an <see cref="InjectAttribute"/>,
        /// resolves the constructor with the most parameters — the seeded-clock one below, which it
        /// cannot satisfy (nothing registers a bare <see cref="Func{DateTime}"/>).
        /// </summary>
        [Inject]
        public LivesSystem(
            LivesModel model,
            LivesConfig config,
            GameModeModel gameModeModel,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunRescuedMessage> runRescuedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
            : this(
                model,
                config,
                gameModeModel,
                gameOverSubscriber,
                runRescuedSubscriber,
                runStartedSubscriber,
                LocalNow)
        {
        }

        internal LivesSystem(
            LivesModel model,
            LivesConfig config,
            GameModeModel gameModeModel,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunRescuedMessage> runRescuedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            Func<DateTime> localNowProvider)
        {
            _model = model;
            _config = config;
            _gameModeModel = gameModeModel;
            _localNowProvider = localNowProvider ?? LocalNow;

            _saveData = Load(_config, CurrentHourStamp());
            _model.CurrentLives.Value = _saveData.lives;

            // Catches up on every boundary crossed while the app was closed, then persists — which is
            // also what writes a first launch's starting lives, so the next launch reads them back.
            RefreshRefill();
            Save();

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            gameOverSubscriber.Subscribe(OnGameOver).AddTo(bag);
            runRescuedSubscriber.Subscribe(OnRunRescued).AddTo(bag);
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            _subscriptions = bag.Build();
        }

        /// <summary>Starts the countdown loop. Not started from the constructor, so a test that builds
        /// the system directly never has a loop running under it.</summary>
        public void Start() => TickLoop(_cts.Token).Forget();

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _subscriptions.Dispose();
        }

        private bool IsPathMode => _gameModeModel.CurrentMode.Value == GameMode.Path;

        /// <summary>
        /// Pays every hour boundary crossed since the last check, clamped at the cap, and re-derives the
        /// countdown. Saves only when something changed. Called by the loop every second, and first
        /// thing before every deduction; internal so a test can drive it against a seeded clock.
        /// </summary>
        internal void RefreshRefill()
        {
            long currentHourStamp = CurrentHourStamp();
            long crossedBoundaries = currentHourStamp - _saveData.lastRefillHourStamp;

            if (crossedBoundaries != 0)
            {
                // Backwards (crossedBoundaries < 0): the clock was moved back. Re-anchor and grant
                // nothing — the hours "crossed" going backwards were never lived.
                if (crossedBoundaries > 0)
                {
                    GrantRefill(crossedBoundaries);
                }

                _saveData.lastRefillHourStamp = currentHourStamp;
                Save();
            }

            UpdateCountdown();
        }

        /// <summary>A new run closes the rescue window on the last failure, so the refund it held is
        /// gone for good.</summary>
        private void OnRunStarted(RunStartedMessage message) => _hasPendingRefund = false;

        /// <summary>
        /// A Path run failed: one life, saved at once — waiting for the next run would let an app kill
        /// on the game-over screen dodge the charge. Held as refundable while the ending can still be
        /// rescued.
        /// </summary>
        private void OnGameOver(GameOverMessage message)
        {
            if (!IsPathMode || message.Reason == GameOverReason.LevelCompleted)
            {
                return;
            }

            // A boundary crossed since the last tick is paid before the charge, so the charge lands on
            // the count the player is actually entitled to.
            RefreshRefill();

            int lives = _saveData.lives;
            if (lives <= 0)
            {
                _hasPendingRefund = false;
                return;
            }

            _livesBeforeCharge = lives;
            _hasPendingRefund = message.IsRescueAvailable;
            SetLives(lives - 1);
            Save();
        }

        /// <summary>The failure just charged was taken back by the rescue ad: the run goes on, so its
        /// life comes back.</summary>
        private void OnRunRescued(RunRescuedMessage message)
        {
            if (!_hasPendingRefund)
            {
                return;
            }

            _hasPendingRefund = false;
            int ceiling = Math.Max(_config.RegenCap, _livesBeforeCharge);
            SetLives(Math.Min(_saveData.lives + 1, ceiling));
            Save();
        }

        /// <summary>
        /// Adds <see cref="LivesConfig.RefillAmount"/> per boundary, clamped at the cap. A count already
        /// at or above the cap is left exactly where it is — the clamp is to the cap, never down to it.
        /// </summary>
        private void GrantRefill(long crossedBoundaries)
        {
            int cap = _config.RegenCap;
            int lives = _saveData.lives;
            if (lives >= cap)
            {
                return;
            }

            // In long: a clock thrown years forward is a legal (if silly) input and must not overflow.
            long refilled = lives + (crossedBoundaries * _config.RefillAmount);
            SetLives((int)Math.Min(refilled, cap));
        }

        /// <summary>
        /// Whole seconds to the next xx:00 while below the cap, rounded up so the countdown reads 1:00
        /// until the boundary rather than 0:59 a second early; 0 at or above the cap, where nothing is
        /// coming.
        /// </summary>
        private void UpdateCountdown()
        {
            if (_saveData.lives >= _config.RegenCap)
            {
                _model.SecondsUntilRefill.Value = 0;
                return;
            }

            long nowTicks = _localNowProvider().Ticks;
            long nextBoundaryTicks = ((nowTicks / TimeSpan.TicksPerHour) + 1) * TimeSpan.TicksPerHour;
            long remainingTicks = nextBoundaryTicks - nowTicks;
            _model.SecondsUntilRefill.Value =
                (int)((remainingTicks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond);
        }

        private void SetLives(int lives)
        {
            int clamped = Math.Max(0, lives);
            _saveData.lives = clamped;
            _model.CurrentLives.Value = clamped;
            UpdateCountdown();
        }

        private long CurrentHourStamp() => _localNowProvider().Ticks / TimeSpan.TicksPerHour;

        /// <summary>
        /// Re-checks the refill once a second until the scope goes away. Unscaled time, so a paused run
        /// (timeScale 0) does not freeze the countdown — the hour it counts toward is the wall clock's.
        /// </summary>
        private async UniTaskVoid TickLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    RefreshRefill();
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(TICK_INTERVAL_SECONDS),
                        ignoreTimeScale: true,
                        cancellationToken: cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Ordinary teardown — the scope was disposed between ticks. Not an error.
            }
        }

        /// <summary>The real device clock, in local time. Behind a named method so the public
        /// constructor's default is one readable thing.</summary>
        private static DateTime LocalNow() => DateTime.Now;

        /// <summary>
        /// Reads the save blob, falling back to a fresh one on anything missing or unreadable. "Fresh"
        /// means the configured starting lives anchored to the current hour, which is exactly what an
        /// existing install upgrading into this feature should get too.
        /// </summary>
        private static LivesSaveData Load(LivesConfig config, long currentHourStamp)
        {
            string json = PlayerPrefs.GetString(SAVE_KEY, string.Empty);
            LivesSaveData data = null;

            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    data = JsonUtility.FromJson<LivesSaveData>(json);
                }
                catch (ArgumentException exception)
                {
                    Debug.LogError($"Lives save data was unreadable and has been reset: {exception.Message}");
                }
            }

            if (data == null)
            {
                return new LivesSaveData
                {
                    lives = config.StartingLives,
                    lastRefillHourStamp = currentHourStamp,
                };
            }

            data.lives = Math.Max(0, data.lives);
            return data;
        }

        private void Save()
        {
            _saveData.schemaVersion = LivesSaveData.CURRENT_SCHEMA_VERSION;
            PlayerPrefs.SetString(SAVE_KEY, JsonUtility.ToJson(_saveData));
        }
    }
}
