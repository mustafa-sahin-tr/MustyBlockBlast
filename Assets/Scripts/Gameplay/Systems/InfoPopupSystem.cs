using System;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Decides which info popup, if any, a game event earns, and persists which ones the player has
    /// already been shown so an automatic one is never shown twice. The one and only writer of
    /// <see cref="InfoPopupModel"/> and the one and only reader/writer of the "seen" flags (see
    /// <see cref="InfoPopupSeenKey"/>), mirroring how <see cref="PowerUpSystem"/> is the one owner of the
    /// power-up inventory it persists.
    /// <para>
    /// Listens to four triggers, each of which names a genuine, one-time-per-instance game event:
    /// <see cref="SpecialCellSpawnedMessage"/> (a special cell was just painted onto the board),
    /// <see cref="PowerUpGrantedMessage"/> (a power-up kind's first grant — every kind, not just the
    /// starter three, unlike the coach-mark system this replaces), <see cref="HoldFirstUseMessage"/>
    /// (published on every Hold use — "first" is decided here, by the seen-flag, not by the publisher),
    /// and <see cref="SpecialPieceSpawnedMessage"/> (a special piece was just injected into the dock).
    /// </para>
    /// <para>
    /// Unlike the old spotlight/coach-mark system this replaces, an info popup is never mandatory and
    /// never forces an action: <see cref="TryAutoOpen"/> only shows a popup automatically the first time
    /// its subject appears, and only when nothing else is already open — an optional popup never
    /// interrupts something already on screen. <see cref="Open"/> is the manual reopen path: a View calls
    /// it when the player deliberately asks to see the same content again, and it always opens regardless
    /// of seen state.
    /// </para>
    /// <para>
    /// <b>Migration.</b> A power-up already granted when this feature first ships must not suddenly show
    /// a popup for something the player has had all along, and must not be silently forgotten either — it
    /// is marked seen outright, once, via <see cref="RunAlreadyGrantedPowerUpMigration"/>, gated by its
    /// own one-shot flag so it can never re-run and re-mark a kind the player has since (legitimately) had
    /// reset.
    /// </para>
    /// </summary>
    public sealed class InfoPopupSystem : IDisposable
    {
        private static readonly PowerUpKind[] AllPowerUpKinds =
        {
            PowerUpKind.Bomb, PowerUpKind.RowClear, PowerUpKind.ColumnClear, PowerUpKind.Joker,
            PowerUpKind.ColorCleanser, PowerUpKind.Rotate, PowerUpKind.Reroll,
            PowerUpKind.DoubleMultiplier, PowerUpKind.GhostFit, PowerUpKind.CoinSower, PowerUpKind.Hold,
        };

        private const string HOLD_ID = "Hold";
        private const string SPECIAL_CELL_ID_PREFIX = "SpecialCell_";
        private const string POWERUP_ID_PREFIX = "PowerUp_";
        private const string SPECIAL_PIECE_ID_PREFIX = "SpecialPiece_";

        private readonly InfoPopupModel _model;

        private readonly IDisposable _specialCellSpawnedSubscription;
        private readonly IDisposable _powerUpGrantedSubscription;
        private readonly IDisposable _holdFirstUseSubscription;
        private readonly IDisposable _specialPieceSpawnedSubscription;

        [Inject]
        public InfoPopupSystem(
            InfoPopupModel model,
            PowerUpModel powerUpModel,
            ISubscriber<SpecialCellSpawnedMessage> specialCellSpawnedSubscriber,
            ISubscriber<PowerUpGrantedMessage> powerUpGrantedSubscriber,
            ISubscriber<HoldFirstUseMessage> holdFirstUseSubscriber,
            ISubscriber<SpecialPieceSpawnedMessage> specialPieceSpawnedSubscriber)
        {
            _model = model;

            RunAlreadyGrantedPowerUpMigration(powerUpModel);

            _specialCellSpawnedSubscription = specialCellSpawnedSubscriber.Subscribe(OnSpecialCellSpawned);
            _powerUpGrantedSubscription = powerUpGrantedSubscriber.Subscribe(OnPowerUpGranted);
            _holdFirstUseSubscription = holdFirstUseSubscriber.Subscribe(OnHoldFirstUse);
            _specialPieceSpawnedSubscription = specialPieceSpawnedSubscriber.Subscribe(OnSpecialPieceSpawned);
        }

        public void Dispose()
        {
            _specialCellSpawnedSubscription.Dispose();
            _powerUpGrantedSubscription.Dispose();
            _holdFirstUseSubscription.Dispose();
            _specialPieceSpawnedSubscription.Dispose();
        }

        /// <summary>
        /// Called by a View when the player deliberately asks to see this subject's content again — a
        /// tap or long-press on the relevant on-screen element. Always opens, regardless of seen state,
        /// and marks it seen as a side effect (idempotent if already seen), so a subject somehow never
        /// auto-shown still stops auto-showing after a manual look.
        /// </summary>
        public void Open(InfoPopupSubjectKind subjectKind, int kindValue)
        {
            BuildContent(subjectKind, kindValue, out string id, out string headerKey, out string bodyKey);
            MarkSeen(id);
            _model.OpenContent.Value = new InfoPopupContent(id, subjectKind, kindValue, headerKey, bodyKey);
        }

        public void Close() => _model.OpenContent.Value = null;

        private void OnSpecialCellSpawned(SpecialCellSpawnedMessage message)
        {
            if (message.Kind == SpecialCellKind.None)
            {
                return;
            }

            TryAutoOpen(InfoPopupSubjectKind.SpecialCell, (int)message.Kind);
        }

        /// <summary>
        /// Every <see cref="PowerUpKind"/> earns this popup on its first grant — no kind filter, unlike
        /// the old coach-mark system's starter-three-only gating. <see cref="PowerUpGrantedMessage"/>
        /// fires on every grant, not only the first, so "first" is decided here by the seen-flag,
        /// mirroring <see cref="OnHoldFirstUse"/>.
        /// </summary>
        private void OnPowerUpGranted(PowerUpGrantedMessage message)
        {
            TryAutoOpen(InfoPopupSubjectKind.PowerUp, (int)message.Kind);
        }

        private void OnHoldFirstUse(HoldFirstUseMessage message)
        {
            // Every use publishes this, not only the first — TryAutoOpen's own seen-check is what makes
            // this a no-op from the second acknowledged use onward.
            TryAutoOpen(InfoPopupSubjectKind.Hold, -1);
        }

        private void OnSpecialPieceSpawned(SpecialPieceSpawnedMessage message)
        {
            if (message.Kind == SpecialPieceKind.None)
            {
                return;
            }

            TryAutoOpen(InfoPopupSubjectKind.SpecialPiece, (int)message.Kind);
        }

        /// <summary>
        /// Opens <paramref name="subjectKind"/>/<paramref name="kindValue"/>'s popup only if unseen and
        /// nothing else is currently open — optional content never interrupts something already on
        /// screen. When something else is open, this is still marked seen so it does not try again
        /// later; the player can still reach it manually afterward through <see cref="Open"/>.
        /// </summary>
        private void TryAutoOpen(InfoPopupSubjectKind subjectKind, int kindValue)
        {
            BuildContent(subjectKind, kindValue, out string id, out string headerKey, out string bodyKey);
            if (IsSeen(id))
            {
                return;
            }

            if (_model.OpenContent.Value != null)
            {
                MarkSeen(id);
                return;
            }

            MarkSeen(id);
            _model.OpenContent.Value = new InfoPopupContent(id, subjectKind, kindValue, headerKey, bodyKey);
        }

        /// <summary>Builds the id and the header/body localization keys for
        /// <paramref name="subjectKind"/>/<paramref name="kindValue"/> — the one place both the trigger
        /// handlers and <see cref="Open"/> resolve identity, so they can never disagree about it.</summary>
        private static void BuildContent(
            InfoPopupSubjectKind subjectKind, int kindValue, out string id, out string headerKey, out string bodyKey)
        {
            switch (subjectKind)
            {
                case InfoPopupSubjectKind.SpecialCell:
                    id = SPECIAL_CELL_ID_PREFIX + (SpecialCellKind)kindValue;
                    KeysForSpecialCell((SpecialCellKind)kindValue, out headerKey, out bodyKey);
                    break;
                case InfoPopupSubjectKind.PowerUp:
                    id = POWERUP_ID_PREFIX + (PowerUpKind)kindValue;
                    KeysForPowerUp((PowerUpKind)kindValue, out headerKey, out bodyKey);
                    break;
                case InfoPopupSubjectKind.SpecialPiece:
                    id = SPECIAL_PIECE_ID_PREFIX + (SpecialPieceKind)kindValue;
                    KeysForSpecialPiece((SpecialPieceKind)kindValue, out headerKey, out bodyKey);
                    break;
                default:
                    id = HOLD_ID;
                    headerKey = LocalizationKeys.INFO_POPUP_HOLD_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_HOLD_BODY;
                    break;
            }
        }

        /// <summary>Per-kind header/body key pair for a <see cref="PowerUpKind"/> (every kind except
        /// <see cref="PowerUpKind.Hold"/>, which never reaches here — see <see cref="BuildContent"/>).</summary>
        private static void KeysForPowerUp(PowerUpKind kind, out string headerKey, out string bodyKey)
        {
            switch (kind)
            {
                case PowerUpKind.Bomb:
                    headerKey = LocalizationKeys.INFO_POPUP_POWERUP_BOMB_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_POWERUP_BOMB_BODY;
                    break;
                case PowerUpKind.RowClear:
                    headerKey = LocalizationKeys.INFO_POPUP_POWERUP_ROW_CLEAR_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_POWERUP_ROW_CLEAR_BODY;
                    break;
                case PowerUpKind.ColumnClear:
                    headerKey = LocalizationKeys.INFO_POPUP_POWERUP_COLUMN_CLEAR_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_POWERUP_COLUMN_CLEAR_BODY;
                    break;
                case PowerUpKind.Joker:
                    headerKey = LocalizationKeys.INFO_POPUP_POWERUP_JOKER_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_POWERUP_JOKER_BODY;
                    break;
                case PowerUpKind.ColorCleanser:
                    headerKey = LocalizationKeys.INFO_POPUP_POWERUP_COLOR_CLEANSER_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_POWERUP_COLOR_CLEANSER_BODY;
                    break;
                case PowerUpKind.Rotate:
                    headerKey = LocalizationKeys.INFO_POPUP_POWERUP_ROTATE_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_POWERUP_ROTATE_BODY;
                    break;
                case PowerUpKind.Reroll:
                    headerKey = LocalizationKeys.INFO_POPUP_POWERUP_REROLL_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_POWERUP_REROLL_BODY;
                    break;
                case PowerUpKind.DoubleMultiplier:
                    headerKey = LocalizationKeys.INFO_POPUP_POWERUP_DOUBLE_MULTIPLIER_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_POWERUP_DOUBLE_MULTIPLIER_BODY;
                    break;
                case PowerUpKind.GhostFit:
                    headerKey = LocalizationKeys.INFO_POPUP_POWERUP_GHOST_FIT_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_POWERUP_GHOST_FIT_BODY;
                    break;
                case PowerUpKind.CoinSower:
                    headerKey = LocalizationKeys.INFO_POPUP_POWERUP_COIN_SOWER_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_POWERUP_COIN_SOWER_BODY;
                    break;
                default:
                    headerKey = LocalizationKeys.INFO_POPUP_HOLD_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_HOLD_BODY;
                    break;
            }
        }

        /// <summary>Per-kind header/body key pair for a <see cref="SpecialCellKind"/>.</summary>
        private static void KeysForSpecialCell(SpecialCellKind kind, out string headerKey, out string bodyKey)
        {
            switch (kind)
            {
                case SpecialCellKind.ExplosiveCore:
                    headerKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_EXPLOSIVE_CORE_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_EXPLOSIVE_CORE_BODY;
                    break;
                case SpecialCellKind.Laser:
                    headerKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_LASER_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_LASER_BODY;
                    break;
                case SpecialCellKind.ScoreGem:
                    headerKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_SCORE_GEM_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_SCORE_GEM_BODY;
                    break;
                case SpecialCellKind.Vortex:
                    headerKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_VORTEX_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_VORTEX_BODY;
                    break;
                case SpecialCellKind.ChainLightning:
                    headerKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_CHAIN_LIGHTNING_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_CHAIN_LIGHTNING_BODY;
                    break;
                case SpecialCellKind.Coin:
                    headerKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_COIN_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_COIN_BODY;
                    break;
                case SpecialCellKind.Timer:
                    headerKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_TIMER_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_SPECIAL_CELL_TIMER_BODY;
                    break;
                default:
                    headerKey = LocalizationKeys.INFO_POPUP_HOLD_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_HOLD_BODY;
                    break;
            }
        }

        /// <summary>Per-kind header/body key pair for a <see cref="SpecialPieceKind"/>.</summary>
        private static void KeysForSpecialPiece(SpecialPieceKind kind, out string headerKey, out string bodyKey)
        {
            switch (kind)
            {
                case SpecialPieceKind.Golden:
                    headerKey = LocalizationKeys.INFO_POPUP_SPECIAL_PIECE_GOLDEN_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_SPECIAL_PIECE_GOLDEN_BODY;
                    break;
                case SpecialPieceKind.PiercingRocket:
                    headerKey = LocalizationKeys.INFO_POPUP_SPECIAL_PIECE_PIERCING_ROCKET_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_SPECIAL_PIECE_PIERCING_ROCKET_BODY;
                    break;
                case SpecialPieceKind.DemolitionHammer:
                    headerKey = LocalizationKeys.INFO_POPUP_SPECIAL_PIECE_DEMOLITION_HAMMER_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_SPECIAL_PIECE_DEMOLITION_HAMMER_BODY;
                    break;
                default:
                    headerKey = LocalizationKeys.INFO_POPUP_HOLD_HEADER;
                    bodyKey = LocalizationKeys.INFO_POPUP_HOLD_BODY;
                    break;
            }
        }

        /// <summary>
        /// Marks every <see cref="PowerUpKind"/> already granted (count &gt; 0) at the moment this
        /// feature first ships as seen, without ever opening a popup for them — the whole point being
        /// that a returning player is not shown a popup for something they already have. Gated by its
        /// own one-shot flag so it runs exactly once, on the first boot after this feature ships.
        /// </summary>
        private static void RunAlreadyGrantedPowerUpMigration(PowerUpModel powerUpModel)
        {
            if (PlayerPrefs.GetInt(InfoPopupSeenKey.ALREADY_GRANTED_POWERUPS_MIGRATION_DONE, 0) != 0)
            {
                return;
            }

            for (int kindIndex = 0; kindIndex < AllPowerUpKinds.Length; kindIndex++)
            {
                PowerUpKind kind = AllPowerUpKinds[kindIndex];
                if (CountFor(powerUpModel, kind) > 0)
                {
                    MarkSeen(POWERUP_ID_PREFIX + kind);
                }
            }

            PlayerPrefs.SetInt(InfoPopupSeenKey.ALREADY_GRANTED_POWERUPS_MIGRATION_DONE, 1);
            PlayerPrefs.Save();
        }

        private static int CountFor(PowerUpModel powerUpModel, PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.Bomb:
                    return powerUpModel.BombCount.Value;
                case PowerUpKind.RowClear:
                    return powerUpModel.RowClearCount.Value;
                case PowerUpKind.ColumnClear:
                    return powerUpModel.ColumnClearCount.Value;
                case PowerUpKind.Joker:
                    return powerUpModel.JokerCount.Value;
                case PowerUpKind.ColorCleanser:
                    return powerUpModel.ColorCleanserCount.Value;
                case PowerUpKind.Rotate:
                    return powerUpModel.RotateCount.Value;
                case PowerUpKind.Reroll:
                    return powerUpModel.RerollCount.Value;
                case PowerUpKind.DoubleMultiplier:
                    return powerUpModel.DoubleMultiplierCount.Value;
                case PowerUpKind.GhostFit:
                    return powerUpModel.GhostFitCount.Value;
                case PowerUpKind.CoinSower:
                    return powerUpModel.CoinSowerCount.Value;
                case PowerUpKind.Hold:
                    return powerUpModel.HoldCount.Value;
                default:
                    return 0;
            }
        }

        private static bool IsSeen(string id) => PlayerPrefs.GetInt(InfoPopupSeenKey.For(id), 0) != 0;

        private static void MarkSeen(string id)
        {
            PlayerPrefs.SetInt(InfoPopupSeenKey.For(id), 1);
            PlayerPrefs.Save();
        }
    }
}
