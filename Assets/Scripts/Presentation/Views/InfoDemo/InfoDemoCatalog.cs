using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Which info-popup subjects have an animated demo, and the demo for each (the Vortex special cell,
    /// #446; the Explosive Core, Laser, Score Gem, Chain Lightning, Coin and Timer special cells, #450;
    /// the six board-targeted power-ups, #448; the tray / targetless power-ups and the Hold pocket,
    /// #449; the Golden, Piercing Rocket and Demolition Hammer special pieces, #451; objective cards via
    /// <see cref="FindObjective"/>, #447). A subject with none
    /// returns null and its card keeps today's static hero icon (issue #445 AC9). Each demo is built
    /// once, the first time it is asked for, and cached — a timeline is immutable, so replaying it
    /// every time the card opens costs nothing.
    /// <para>
    /// Adding a demo (issues #447–#454) is: a new script class with a static <c>Build()</c>, one case
    /// in <see cref="Find"/> (info-popup subjects) or <see cref="FindObjective"/> (objective cards),
    /// and one cache slot. Objectives are looked up by their own definition rather than through
    /// <see cref="InfoPopupSubjectKind"/>, so an objective type can never collide with a special
    /// cell/power-up kind value, and a demo can match the objective's own parameters.
    /// </para>
    /// </summary>
    internal sealed class InfoDemoCatalog
    {
        /// <summary>Special-cell demos (issues #446, #450), indexed by <see cref="SpecialCellKind"/> value;
        /// a kind with no demo stays null.</summary>
        private readonly InfoDemoTimeline[] _specialCells = new InfoDemoTimeline[(int)SpecialCellKind.Locked + 1];

        /// <summary>Special dock piece demos (issue #451), indexed by <see cref="SpecialPieceKind"/> value;
        /// <see cref="SpecialPieceKind.None"/> stays null.</summary>
        private readonly InfoDemoTimeline[] _specialPieces = new InfoDemoTimeline[(int)SpecialPieceKind.DemolitionHammer + 1];

        /// <summary>The Hold demo (issue #449), shared by both subjects that open the Hold card.</summary>
        private InfoDemoTimeline _hold;

        /// <summary>Power-up demos (issue #448), indexed by <see cref="PowerUpKind"/> value; a kind with
        /// no demo stays null.</summary>
        private readonly InfoDemoTimeline[] _powerUps = new InfoDemoTimeline[(int)PowerUpKind.PaintCross + 1];

        /// <summary>Simultaneous Line Clear demos, one per required line count, indexed by that count.</summary>
        private readonly InfoDemoTimeline[] _simultaneousLineClear =
            new InfoDemoTimeline[SimultaneousLineClearInfoDemo.MAX_LINE_COUNT + 1];

        /// <summary>The demo for (<paramref name="subjectKind"/>, <paramref name="kindValue"/>), or null
        /// when that subject has none.</summary>
        internal InfoDemoTimeline Find(InfoPopupSubjectKind subjectKind, int kindValue)
        {
            if (subjectKind == InfoPopupSubjectKind.SpecialCell)
            {
                return FindSpecialCell(kindValue);
            }

            if (subjectKind == InfoPopupSubjectKind.PowerUp)
            {
                return FindPowerUp(kindValue);
            }

            if (subjectKind == InfoPopupSubjectKind.SpecialPiece)
            {
                return FindSpecialPiece(kindValue);
            }

            // The Hold card opens as its own subject (first park, a tap on the pocket) and also as the
            // Hold power-up (its first granted charge) — InfoPopupSystem maps both to the same card.
            if (subjectKind == InfoPopupSubjectKind.Hold)
            {
                return FindHold();
            }

            return null;
        }

        /// <summary>The special-cell demos — Vortex (issue #446) and Explosive Core, Laser, Score Gem, Chain
        /// Lightning, Coin and Timer (issue #450). Diamond and Locked have none yet and keep their icon.</summary>
        private InfoDemoTimeline FindSpecialCell(int kindValue)
        {
            if (kindValue < 0 || kindValue >= _specialCells.Length)
            {
                return null;
            }

            if (_specialCells[kindValue] == null)
            {
                _specialCells[kindValue] = BuildSpecialCell((SpecialCellKind)kindValue);
            }

            return _specialCells[kindValue];
        }

        private static InfoDemoTimeline BuildSpecialCell(SpecialCellKind kind)
        {
            switch (kind)
            {
                case SpecialCellKind.ExplosiveCore:
                    return ExplosiveCoreInfoDemo.Build();
                case SpecialCellKind.Laser:
                    return LaserInfoDemo.Build();
                case SpecialCellKind.ScoreGem:
                    return ScoreGemInfoDemo.Build();
                case SpecialCellKind.Vortex:
                    return VortexInfoDemo.Build();
                case SpecialCellKind.ChainLightning:
                    return ChainLightningInfoDemo.Build();
                case SpecialCellKind.Coin:
                    return CoinCellInfoDemo.Build();
                case SpecialCellKind.Timer:
                    return TimerInfoDemo.Build();
                default:
                    return null;
            }
        }

        /// <summary>The special dock piece demos (issue #451) — Golden, Piercing Rocket and Demolition
        /// Hammer. <see cref="SpecialPieceKind.None"/> is not a special piece and has none.</summary>
        private InfoDemoTimeline FindSpecialPiece(int kindValue)
        {
            if (kindValue < 0 || kindValue >= _specialPieces.Length)
            {
                return null;
            }

            if (_specialPieces[kindValue] == null)
            {
                _specialPieces[kindValue] = BuildSpecialPiece((SpecialPieceKind)kindValue);
            }

            return _specialPieces[kindValue];
        }

        private static InfoDemoTimeline BuildSpecialPiece(SpecialPieceKind kind)
        {
            switch (kind)
            {
                case SpecialPieceKind.Golden:
                    return GoldenPieceInfoDemo.Build();
                case SpecialPieceKind.PiercingRocket:
                    return PiercingRocketInfoDemo.Build();
                case SpecialPieceKind.DemolitionHammer:
                    return DemolitionHammerInfoDemo.Build();
                default:
                    return null;
            }
        }

        private InfoDemoTimeline FindHold()
        {
            if (_hold == null)
            {
                _hold = HoldInfoDemo.Build();
            }

            return _hold;
        }

        /// <summary>The power-up demos — the board-targeted Bomb, Row/Column Clear, Joker, Color Cleanser
        /// and Paint Cross (issue #448) and the tray / targetless Rotate, Reroll, Double Multiplier, Ghost
        /// Fit, Coin Sower and Hold (issue #449). Every power-up kind has one.</summary>
        private InfoDemoTimeline FindPowerUp(int kindValue)
        {
            if (kindValue < 0 || kindValue >= _powerUps.Length)
            {
                return null;
            }

            if (kindValue == (int)PowerUpKind.Hold)
            {
                return FindHold();
            }

            if (_powerUps[kindValue] == null)
            {
                _powerUps[kindValue] = BuildPowerUp((PowerUpKind)kindValue);
            }

            return _powerUps[kindValue];
        }

        private static InfoDemoTimeline BuildPowerUp(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.Bomb:
                    return BombInfoDemo.Build();
                case PowerUpKind.RowClear:
                    return LineClearPowerUpInfoDemo.Build(true);
                case PowerUpKind.ColumnClear:
                    return LineClearPowerUpInfoDemo.Build(false);
                case PowerUpKind.Joker:
                    return JokerInfoDemo.Build();
                case PowerUpKind.ColorCleanser:
                    return ColorCleanserInfoDemo.Build();
                case PowerUpKind.PaintCross:
                    return PaintCrossInfoDemo.Build();
                case PowerUpKind.Rotate:
                    return RotateInfoDemo.Build();
                case PowerUpKind.Reroll:
                    return RerollInfoDemo.Build();
                case PowerUpKind.DoubleMultiplier:
                    return DoubleMultiplierInfoDemo.Build();
                case PowerUpKind.GhostFit:
                    return GhostFitInfoDemo.Build();
                case PowerUpKind.CoinSower:
                    return CoinSowerInfoDemo.Build();
                default:
                    return null;
            }
        }

        /// <summary>The demo for the objective card of <paramref name="definition"/>, or null when that
        /// objective has none (its card keeps its static glyph). A Simultaneous Line Clear demo shows the
        /// objective's own required line count; a count the demo cannot draw gets null.</summary>
        internal InfoDemoTimeline FindObjective(ObjectiveDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            switch (definition.Type)
            {
                case ObjectiveType.SimultaneousLineClear:
                {
                    int lineCount = definition.RequiredLineCount;
                    if (!SimultaneousLineClearInfoDemo.Supports(lineCount))
                    {
                        return null;
                    }

                    if (_simultaneousLineClear[lineCount] == null)
                    {
                        _simultaneousLineClear[lineCount] = SimultaneousLineClearInfoDemo.Build(lineCount);
                    }

                    return _simultaneousLineClear[lineCount];
                }

                default:
                    return null;
            }
        }
    }
}
