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
    /// (issue #448 — the same sprites the strip and shop draw), and text from
    /// <see cref="LocalizationSystem"/>. A plain
    /// read-only adapter each host builds from its own injected dependencies — it holds no state and
    /// exposes nothing of the board or run beyond what the interface names (issue #445 AC5).
    /// </summary>
    internal sealed class InfoDemoResources : IInfoDemoResources
    {
        private readonly BoardView _boardView;
        private readonly PowerUpInventoryView _powerUpInventoryView;
        private readonly LocalizationSystem _localizationSystem;

        /// <param name="powerUpInventoryView">Source of <see cref="InfoDemoSprite.PowerUpIcon"/>; null for
        /// a host that plays no power-up demo (an icon it cannot resolve is simply hidden).</param>
        internal InfoDemoResources(
            BoardView boardView, PowerUpInventoryView powerUpInventoryView, LocalizationSystem localizationSystem)
        {
            _boardView = boardView;
            _powerUpInventoryView = powerUpInventoryView;
            _localizationSystem = localizationSystem;
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
