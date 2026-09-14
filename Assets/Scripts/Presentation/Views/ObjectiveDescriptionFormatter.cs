using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Systems;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Turns an <see cref="ObjectiveDefinition"/> into the sentence the HUD shows. Lives in
    /// Presentation on purpose: Core describes what an objective measures, not how it is worded, so
    /// adding a language or rewording a goal never touches the rule engine.
    /// </summary>
    internal static class ObjectiveDescriptionFormatter
    {
        /// <summary>
        /// Describes <paramref name="definition"/> in the active language. The target value is left
        /// out — the HUD renders it as the "2/3" progress, so repeating it here could only disagree
        /// with the model.
        /// </summary>
        internal static string Describe(ObjectiveDefinition definition, LocalizationSystem localization)
        {
            if (definition == null || localization == null)
            {
                return string.Empty;
            }

            switch (definition.Type)
            {
                case ObjectiveType.SimultaneousLineClear:
                    return localization.Format(
                        LocalizationKeys.OBJECTIVE_SIMULTANEOUS_LINE_CLEAR,
                        definition.RequiredLineCount.ToString());

                case ObjectiveType.PieceFamilyCount:
                    return localization.Format(
                        LocalizationKeys.OBJECTIVE_PIECE_FAMILY_COUNT,
                        localization.Translate(FamilyKey(definition.RequiredPieceFamily)));

                case ObjectiveType.BoardWipeCount:
                    return localization.Translate(LocalizationKeys.OBJECTIVE_BOARD_WIPE_COUNT);

                case ObjectiveType.ScoreInRun:
                    return localization.Translate(LocalizationKeys.OBJECTIVE_SCORE_IN_RUN);

                case ObjectiveType.StreakThreshold:
                    return localization.Translate(LocalizationKeys.OBJECTIVE_STREAK_THRESHOLD);

                case ObjectiveType.BombInducedLineClear:
                    return localization.Translate(LocalizationKeys.OBJECTIVE_BOMB_INDUCED_LINE_CLEAR);

                case ObjectiveType.RowAndColumnCrossClear:
                    return localization.Translate(LocalizationKeys.OBJECTIVE_ROW_AND_COLUMN_CROSS_CLEAR);

                case ObjectiveType.ClutchRecoveryClear:
                    return localization.Translate(LocalizationKeys.OBJECTIVE_CLUTCH_RECOVERY_CLEAR);

                case ObjectiveType.AtLeastLineClear:
                    return localization.Format(
                        LocalizationKeys.OBJECTIVE_AT_LEAST_LINE_CLEAR,
                        definition.RequiredLineCount.ToString());

                case ObjectiveType.PieceIdCount:
                    return localization.Format(
                        LocalizationKeys.OBJECTIVE_PIECE_ID_COUNT, PieceIdDisplayName(definition.RequiredPieceId, localization));

                case ObjectiveType.PieceIdLineClear:
                    return localization.Format(
                        LocalizationKeys.OBJECTIVE_PIECE_ID_LINE_CLEAR, PieceIdDisplayName(definition.RequiredPieceId, localization));

                case ObjectiveType.FourCornersCleared:
                    return localization.Translate(LocalizationKeys.OBJECTIVE_FOUR_CORNERS_CLEARED);

                case ObjectiveType.CenterCoreEvacuated:
                    return localization.Translate(LocalizationKeys.OBJECTIVE_CENTER_CORE_EVACUATED);

                case ObjectiveType.NoIsolatedHolesStreak:
                    return localization.Translate(LocalizationKeys.OBJECTIVE_NO_ISOLATED_HOLES_STREAK);

                case ObjectiveType.RollingLineClearWindow:
                    return localization.Format(
                        LocalizationKeys.OBJECTIVE_LINE_CLEAR_BURST,
                        WholeSeconds(definition.WindowSeconds));

                case ObjectiveType.EarlyScoreRush:
                    return localization.Format(
                        LocalizationKeys.OBJECTIVE_EARLY_SCORE_RUSH,
                        WholeSeconds(definition.WindowSeconds));

                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Translated display name for a catalog piece id, when <see cref="PieceIdNameKey"/> has one
        /// authored; the raw id otherwise. Not every catalog piece needs a name — only the ones an
        /// authored objective actually references — so a piece nobody has named yet degrades to a
        /// functional (if unpolished) raw id rather than blank text.
        /// </summary>
        private static string PieceIdDisplayName(string pieceId, LocalizationSystem localization)
        {
            string key = PieceIdNameKey(pieceId);
            return key != null ? localization.Translate(key) : pieceId;
        }

        /// <summary>
        /// Localization key for one catalog piece's display name. Add a case here plus the
        /// corresponding GameStrings row to name a piece a new objective references — this is
        /// deliberately incremental, not a full 27-piece table built up front.
        /// </summary>
        private static string PieceIdNameKey(string pieceId)
        {
            switch (pieceId)
            {
                case "square_3x3":
                    return LocalizationKeys.OBJECTIVE_PIECE_NAME_SQUARE_3X3;
                case "line_h5":
                case "line_v5":
                    return LocalizationKeys.OBJECTIVE_PIECE_NAME_LINE_5;
                default:
                    return null;
            }
        }

        /// <summary>Renders a window/deadline duration as a whole-second count, matching how every
        /// other duration in the HUD (countdown, best-score suffix) is displayed via FORMAT_SECONDS.</summary>
        private static string WholeSeconds(float seconds)
        {
            return System.Math.Round(seconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FamilyKey(PieceFamily family)
        {
            switch (family)
            {
                case PieceFamily.Line:
                    return LocalizationKeys.OBJECTIVE_FAMILY_LINE;
                case PieceFamily.Square:
                    return LocalizationKeys.OBJECTIVE_FAMILY_SQUARE;
                case PieceFamily.Corner:
                    return LocalizationKeys.OBJECTIVE_FAMILY_CORNER;
                case PieceFamily.TShape:
                    return LocalizationKeys.OBJECTIVE_FAMILY_T_SHAPE;
                case PieceFamily.SShape:
                    return LocalizationKeys.OBJECTIVE_FAMILY_S_SHAPE;
                case PieceFamily.ZShape:
                    return LocalizationKeys.OBJECTIVE_FAMILY_Z_SHAPE;
                default:
                    return LocalizationKeys.OBJECTIVE_FAMILY_SINGLE;
            }
        }
    }
}
