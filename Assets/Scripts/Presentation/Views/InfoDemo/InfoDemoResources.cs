using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The one <see cref="IInfoDemoResources"/> implementation, shared by every card that hosts an
    /// <see cref="InfoDemoStage"/> (<see cref="InfoPopupView"/>, <see cref="ObjectiveInfoPopupView"/>,
    /// issue #447): sprites and cell metrics borrowed from <see cref="BoardView"/>, which owns the
    /// board's authored art and proportions, power-up icons from <see cref="PowerUpInventoryView"/>
    /// (issue #448 — the same sprites the strip and shop draw), the streak pill's flame from
    /// <see cref="StreakPillView"/> (issue #451), and text from
    /// <see cref="LocalizationSystem"/>. A plain
    /// read-only adapter each host builds from its own injected dependencies — it holds no state and
    /// exposes nothing of the board or run beyond what the interface names (issue #445 AC5).
    /// </summary>
    internal sealed class InfoDemoResources : IInfoDemoResources
    {
        private readonly BoardView _boardView;
        private readonly PowerUpInventoryView _powerUpInventoryView;
        private readonly LocalizationSystem _localizationSystem;
        private readonly HoldSlotView _holdSlotView;
        private readonly CoinTotalHudView _coinTotalHudView;
        private readonly StreakPillView _streakPillView;

        /// <param name="powerUpInventoryView">Source of <see cref="InfoDemoSprite.PowerUpIcon"/>; null for
        /// a host that plays no power-up demo (an icon it cannot resolve is simply hidden).</param>
        /// <param name="holdSlotView">Source of <see cref="InfoDemoSprite.HoldPocket"/> (issue #449); null
        /// for a host that plays no Hold demo.</param>
        /// <param name="coinTotalHudView">Source of <see cref="InfoDemoSprite.Coin"/> (issue #449); null
        /// for a host that plays no coin demo.</param>
        /// <param name="streakPillView">Source of <see cref="InfoDemoSprite.StreakFlame"/> (issue #451); null
        /// for a host that plays no Golden piece demo.</param>
        internal InfoDemoResources(
            BoardView boardView,
            PowerUpInventoryView powerUpInventoryView,
            LocalizationSystem localizationSystem,
            HoldSlotView holdSlotView = null,
            CoinTotalHudView coinTotalHudView = null,
            StreakPillView streakPillView = null)
        {
            _boardView = boardView;
            _powerUpInventoryView = powerUpInventoryView;
            _localizationSystem = localizationSystem;
            _holdSlotView = holdSlotView;
            _coinTotalHudView = coinTotalHudView;
            _streakPillView = streakPillView;
        }

        public bool TryGetSprite(InfoDemoSprite sprite, int parameter, out Sprite resolved, out Color tint)
        {
            switch (sprite)
            {
                case InfoDemoSprite.SpecialCellIcon:
                    SpecialCellKind cellKind = (SpecialCellKind)parameter;
                    resolved = _boardView.IconSprite(cellKind);
                    tint = BoardView.IconTint(cellKind);
                    return resolved != null;
                case InfoDemoSprite.DiamondIcon:
                    // White: the element's paint (the gem's colour id) supplies DiamondVisuals.Tint.
                    resolved = _boardView.IconSprite(SpecialCellKind.Diamond);
                    tint = Color.white;
                    return resolved != null;
                case InfoDemoSprite.CheckMark:
                    resolved = UiSpriteFactory.CheckMark;
                    tint = Color.white;
                    return true;
                case InfoDemoSprite.PowerUpIcon:
                    resolved = _powerUpInventoryView != null ? _powerUpInventoryView.IconFor((PowerUpKind)parameter) : null;
                    tint = Color.white;
                    return resolved != null;
                case InfoDemoSprite.Disc:
                    resolved = UiSpriteFactory.Circle;
                    tint = Color.white;
                    return true;
                case InfoDemoSprite.SoftDisc:
                    resolved = UiSpriteFactory.RadialGlow;
                    tint = Color.white;
                    return true;
                case InfoDemoSprite.Coin:
                    if (_coinTotalHudView == null)
                    {
                        resolved = null;
                        tint = Color.clear;
                        return false;
                    }

                    _coinTotalHudView.GetCoinFace(out resolved, out tint);
                    return resolved != null;
                case InfoDemoSprite.StreakFlame:
                    resolved = _streakPillView != null ? _streakPillView.FlameSprite : null;
                    tint = Color.white;
                    return resolved != null;
                case InfoDemoSprite.HoldPocket:
                    resolved = _holdSlotView != null ? _holdSlotView.PocketSprite : null;
                    tint = Color.white;
                    return resolved != null;
                default:
                    resolved = null;
                    tint = Color.clear;
                    return false;
            }
        }

        public string Translate(string localizationKey) => _localizationSystem.Translate(localizationKey);

        public string Format(string localizationKey, string argument) => _localizationSystem.Format(localizationKey, argument);

        public void GetBoardCellMetrics(out float cellSize, out float inset, out float bevelThickness)
        {
            cellSize = _boardView.CellSize;
            inset = _boardView.CellInset;
            bevelThickness = _boardView.CellBevelThickness;
        }
    }
}
