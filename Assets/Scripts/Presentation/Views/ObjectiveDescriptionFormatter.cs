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

                default:
                    return string.Empty;
            }
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
