using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Which info-popup subjects have an animated demo, and the demo for each (the Vortex special cell,
    /// #446; the Explosive Core, Laser, Score Gem, Chain Lightning, Coin and Timer special cells, #450;
    /// the six board-targeted power-ups, #448; the tray / targetless power-ups and the Hold pocket,
    /// #449; the Golden, Piercing Rocket and Demolition Hammer special pieces, #451; objective cards via
    /// <see cref="FindObjective"/> — Simultaneous Line Clear, #447; At Least, Row and Column Cross, Bomb-induced,
    /// Piece Id and Rolling Window line clears, #452; Board Wipe, Four Corners, Center Core, No Isolated Holes,
    /// Colour, Diamonds, Ice, Reinforced and Timer cells, #453; Piece Family, Score In Run, Streak Threshold, Piece Id
    /// Count, Clutch Recovery, Early Score Rush and Reroll Save, #454 — every objective type now has one). A subject with none
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
        private readonly InfoDemoTimeline[] _specialCells = new InfoDemoTimeline[(int)SpecialCellKind.PuzzleLink + 1];

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

        /// <summary>At Least Line Clear demos (issue #452), one per required line count, indexed by that count.</summary>
        private readonly InfoDemoTimeline[] _atLeastLineClear = new InfoDemoTimeline[AtLeastLineClearInfoDemo.MAX_LINE_COUNT + 1];

        /// <summary>The Row and Column Cross Clear and Bomb-induced line clear demos (issue #452) — neither
        /// objective has a parameter.</summary>
        private InfoDemoTimeline _rowAndColumnCrossClear;
        private InfoDemoTimeline _bombInducedLineClear;

        /// <summary>Piece Id Line Clear demos (issue #452), one per required catalog piece id.</summary>
        private readonly Dictionary<string, InfoDemoTimeline> _pieceIdLineClear = new Dictionary<string, InfoDemoTimeline>();

        /// <summary>Rolling line-clear window demos (issue #452), one per window in whole seconds.</summary>
        private readonly Dictionary<int, InfoDemoTimeline> _rollingLineClearWindow = new Dictionary<int, InfoDemoTimeline>();

        /// <summary>The board-layout objective demos without a parameter (issue #453).</summary>
        private InfoDemoTimeline _boardWipe;
        private InfoDemoTimeline _fourCornersCleared;
        private InfoDemoTimeline _centerCoreEvacuated;
        private InfoDemoTimeline _iceCellsCleared;
        private InfoDemoTimeline _reinforcedCellsCleared;
        private InfoDemoTimeline _timerCellsMeltedInTime;

        /// <summary>No Isolated Holes streak demos (issue #453), one per target the chip counts to.</summary>
        private readonly Dictionary<int, InfoDemoTimeline> _noIsolatedHolesStreak = new Dictionary<int, InfoDemoTimeline>();

        /// <summary>Colour Cleared and Diamonds Cleared demos (issue #453), one per colour id, indexed by it.</summary>
        private readonly InfoDemoTimeline[] _colourCleared = new InfoDemoTimeline[Board.COLOUR_COUNT + 1];
        private readonly InfoDemoTimeline[] _diamondsCleared = new InfoDemoTimeline[Board.COLOUR_COUNT + 1];

        /// <summary>The counter / streak objective demos (issue #454), each cached per the parameters it draws:
        /// Piece Family per (family, target), Piece Id Count per (piece id, target), Score In Run per target,
        /// Early Score Rush per (window seconds, target), Streak Threshold per target (indexed by it), and
        /// Clutch Recovery and Reroll Save per target.</summary>
        private readonly Dictionary<(PieceFamily, int), InfoDemoTimeline> _pieceFamilyCount =
            new Dictionary<(PieceFamily, int), InfoDemoTimeline>();
        private readonly Dictionary<(string, int), InfoDemoTimeline> _pieceIdCount = new Dictionary<(string, int), InfoDemoTimeline>();
        private readonly Dictionary<int, InfoDemoTimeline> _scoreInRun = new Dictionary<int, InfoDemoTimeline>();
        private readonly Dictionary<(int, int), InfoDemoTimeline> _earlyScoreRush = new Dictionary<(int, int), InfoDemoTimeline>();
        private readonly InfoDemoTimeline[] _streakThreshold = new InfoDemoTimeline[StreakThresholdInfoDemo.MAX_TARGET + 1];
        private readonly Dictionary<int, InfoDemoTimeline> _clutchRecoveryClear = new Dictionary<int, InfoDemoTimeline>();
        private readonly Dictionary<int, InfoDemoTimeline> _rerollSave = new Dictionary<int, InfoDemoTimeline>();

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
                case SpecialCellKind.PowerStar:
                    return PowerStarInfoDemo.Build();
                case SpecialCellKind.PuzzleLink:
                    return PuzzleLinkInfoDemo.Build();
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
        /// objective has none (its card keeps its static glyph). A parameterised demo shows the objective's
        /// own parameter — the required line count (Simultaneous, #447; At Least, #452), the required
        /// piece (Piece Id Line Clear, #452), the window (Rolling Line Clear Window, #452), the colour (Colour and
        /// Diamonds Cleared, #453), the streak target (No Isolated Holes, #453), the family, piece, deadline and
        /// occupancy threshold and the target of the counter / streak objectives (#454) — and a value the demo
        /// cannot draw gets null rather than a demo showing a different one.</summary>
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

                case ObjectiveType.AtLeastLineClear:
                {
                    int lineCount = definition.RequiredLineCount;
                    if (!AtLeastLineClearInfoDemo.Supports(lineCount))
                    {
                        return null;
                    }

                    if (_atLeastLineClear[lineCount] == null)
                    {
                        _atLeastLineClear[lineCount] = AtLeastLineClearInfoDemo.Build(lineCount);
                    }

                    return _atLeastLineClear[lineCount];
                }

                case ObjectiveType.RowAndColumnCrossClear:
                    if (_rowAndColumnCrossClear == null)
                    {
                        _rowAndColumnCrossClear = RowAndColumnCrossClearInfoDemo.Build();
                    }

                    return _rowAndColumnCrossClear;

                case ObjectiveType.BombInducedLineClear:
                    if (_bombInducedLineClear == null)
                    {
                        _bombInducedLineClear = BombInducedLineClearInfoDemo.Build();
                    }

                    return _bombInducedLineClear;

                case ObjectiveType.PieceIdLineClear:
                {
                    string pieceId = definition.RequiredPieceId;
                    if (!PieceIdLineClearInfoDemo.Supports(pieceId))
                    {
                        return null;
                    }

                    if (!_pieceIdLineClear.TryGetValue(pieceId, out InfoDemoTimeline demo))
                    {
                        demo = PieceIdLineClearInfoDemo.Build(pieceId);
                        _pieceIdLineClear.Add(pieceId, demo);
                    }

                    return demo;
                }

                case ObjectiveType.RollingLineClearWindow:
                {
                    if (!RollingLineClearWindowInfoDemo.Supports(definition.WindowSeconds))
                    {
                        return null;
                    }

                    int seconds = RollingLineClearWindowInfoDemo.WholeSeconds(definition.WindowSeconds);
                    if (!_rollingLineClearWindow.TryGetValue(seconds, out InfoDemoTimeline demo))
                    {
                        demo = RollingLineClearWindowInfoDemo.Build(seconds);
                        _rollingLineClearWindow.Add(seconds, demo);
                    }

                    return demo;
                }

                case ObjectiveType.BoardWipeCount:
                    if (_boardWipe == null)
                    {
                        _boardWipe = BoardWipeInfoDemo.Build();
                    }

                    return _boardWipe;

                case ObjectiveType.FourCornersCleared:
                    if (_fourCornersCleared == null)
                    {
                        _fourCornersCleared = FourCornersClearedInfoDemo.Build();
                    }

                    return _fourCornersCleared;

                case ObjectiveType.CenterCoreEvacuated:
                    if (_centerCoreEvacuated == null)
                    {
                        _centerCoreEvacuated = CenterCoreEvacuatedInfoDemo.Build();
                    }

                    return _centerCoreEvacuated;

                case ObjectiveType.NoIsolatedHolesStreak:
                {
                    int target = definition.TargetValue;
                    if (!NoIsolatedHolesStreakInfoDemo.Supports(target))
                    {
                        return null;
                    }

                    if (!_noIsolatedHolesStreak.TryGetValue(target, out InfoDemoTimeline demo))
                    {
                        demo = NoIsolatedHolesStreakInfoDemo.Build(target);
                        _noIsolatedHolesStreak.Add(target, demo);
                    }

                    return demo;
                }

                case ObjectiveType.ColourCleared:
                {
                    int colourId = definition.RequiredColourId;
                    if (!ColourClearedInfoDemo.Supports(colourId))
                    {
                        return null;
                    }

                    if (_colourCleared[colourId] == null)
                    {
                        _colourCleared[colourId] = ColourClearedInfoDemo.Build(colourId);
                    }

                    return _colourCleared[colourId];
                }

                case ObjectiveType.DiamondsCleared:
                {
                    int colourId = definition.RequiredColourId;
                    if (!DiamondsClearedInfoDemo.Supports(colourId))
                    {
                        return null;
                    }

                    if (_diamondsCleared[colourId] == null)
                    {
                        _diamondsCleared[colourId] = DiamondsClearedInfoDemo.Build(colourId);
                    }

                    return _diamondsCleared[colourId];
                }

                case ObjectiveType.IceCellsCleared:
                    if (_iceCellsCleared == null)
                    {
                        _iceCellsCleared = IceCellsClearedInfoDemo.Build();
                    }

                    return _iceCellsCleared;

                case ObjectiveType.ReinforcedCellsCleared:
                    if (_reinforcedCellsCleared == null)
                    {
                        _reinforcedCellsCleared = ReinforcedCellsClearedInfoDemo.Build();
                    }

                    return _reinforcedCellsCleared;

                case ObjectiveType.TimerCellsMeltedInTime:
                    if (_timerCellsMeltedInTime == null)
                    {
                        _timerCellsMeltedInTime = TimerCellsMeltedInTimeInfoDemo.Build();
                    }

                    return _timerCellsMeltedInTime;

                case ObjectiveType.PieceFamilyCount:
                case ObjectiveType.PieceIdCount:
                case ObjectiveType.ScoreInRun:
                case ObjectiveType.EarlyScoreRush:
                case ObjectiveType.StreakThreshold:
                case ObjectiveType.ClutchRecoveryClear:
                case ObjectiveType.RerollSave:
                    return FindCounterObjective(definition);

                default:
                    return null;
            }
        }

        /// <summary>The counter / streak objective demos (issue #454), each for the objective's own target and
        /// parameter — the family, the piece, the deadline, the occupancy threshold — or null for a value the
        /// demo cannot honestly draw.</summary>
        private InfoDemoTimeline FindCounterObjective(ObjectiveDefinition definition)
        {
            int target = definition.TargetValue;
            InfoDemoTimeline demo;

            switch (definition.Type)
            {
                case ObjectiveType.PieceFamilyCount:
                {
                    PieceFamily family = definition.RequiredPieceFamily;
                    if (!PieceFamilyCountInfoDemo.Supports(family, target))
                    {
                        return null;
                    }

                    if (!_pieceFamilyCount.TryGetValue((family, target), out demo))
                    {
                        demo = PieceFamilyCountInfoDemo.Build(family, target);
                        _pieceFamilyCount.Add((family, target), demo);
                    }

                    return demo;
                }

                case ObjectiveType.PieceIdCount:
                {
                    string pieceId = definition.RequiredPieceId;
                    if (!PieceIdCountInfoDemo.Supports(pieceId, target))
                    {
                        return null;
                    }

                    if (!_pieceIdCount.TryGetValue((pieceId, target), out demo))
                    {
                        demo = PieceIdCountInfoDemo.Build(pieceId, target);
                        _pieceIdCount.Add((pieceId, target), demo);
                    }

                    return demo;
                }

                case ObjectiveType.ScoreInRun:
                    if (!ScoreInRunInfoDemo.Supports(target))
                    {
                        return null;
                    }

                    if (!_scoreInRun.TryGetValue(target, out demo))
                    {
                        demo = ScoreInRunInfoDemo.Build(target);
                        _scoreInRun.Add(target, demo);
                    }

                    return demo;

                case ObjectiveType.EarlyScoreRush:
                {
                    if (!EarlyScoreRushInfoDemo.Supports(definition.WindowSeconds, target))
                    {
                        return null;
                    }

                    int seconds = EarlyScoreRushInfoDemo.WholeSeconds(definition.WindowSeconds);
                    if (!_earlyScoreRush.TryGetValue((seconds, target), out demo))
                    {
                        demo = EarlyScoreRushInfoDemo.Build(seconds, target);
                        _earlyScoreRush.Add((seconds, target), demo);
                    }

                    return demo;
                }

                case ObjectiveType.StreakThreshold:
                    if (!StreakThresholdInfoDemo.Supports(target))
                    {
                        return null;
                    }

                    if (_streakThreshold[target] == null)
                    {
                        _streakThreshold[target] = StreakThresholdInfoDemo.Build(target);
                    }

                    return _streakThreshold[target];

                case ObjectiveType.ClutchRecoveryClear:
                    if (!ClutchRecoveryClearInfoDemo.Supports(definition.RequiredOccupancyThreshold, target))
                    {
                        return null;
                    }

                    if (!_clutchRecoveryClear.TryGetValue(target, out demo))
                    {
                        demo = ClutchRecoveryClearInfoDemo.Build(target);
                        _clutchRecoveryClear.Add(target, demo);
                    }

                    return demo;

                case ObjectiveType.RerollSave:
                    if (!RerollSaveInfoDemo.Supports(target))
                    {
                        return null;
                    }

                    if (!_rerollSave.TryGetValue(target, out demo))
                    {
                        demo = RerollSaveInfoDemo.Build(target);
                        _rerollSave.Add(target, demo);
                    }

                    return demo;

                default:
                    return null;
            }
        }
    }
}
