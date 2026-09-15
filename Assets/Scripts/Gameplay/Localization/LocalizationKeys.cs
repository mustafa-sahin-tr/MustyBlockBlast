namespace MustyBlockBlast.Gameplay.Localization
{
    /// <summary>
    /// Every String Table entry key the game asks for, in one place. Views reference these constants
    /// instead of spelling keys inline, so a renamed entry is a compile error rather than a label that
    /// silently starts rendering its own key at runtime.
    /// <para>
    /// Keys must stay in sync with the <c>GameStrings</c> String Table Collection.
    /// </para>
    /// </summary>
    public static class LocalizationKeys
    {
        /// <summary>Name of the String Table Collection these keys live in.</summary>
        public const string TABLE_NAME = "GameStrings";

        // --- Shared formats ---

        /// <summary>A duration in whole seconds. <c>{0}</c> is the number. The single source of the
        /// seconds unit: the countdown, the best-score suffix and the duration picker all use it, so
        /// they can never disagree on spelling or spacing.</summary>
        public const string FORMAT_SECONDS = "format.seconds";

        // --- Power-ups ---

        /// <summary>Double Multiplier's HUD banner while its window is open. <c>{0}</c> is the
        /// already-formatted remaining duration (via <see cref="FORMAT_SECONDS"/>) — this template only
        /// supplies the "2x"/"x2"/"kat" idiom, which differs by language unlike a bare symbol.</summary>
        public const string POWERUP_DOUBLE_MULTIPLIER_ACTIVE = "powerup.double_multiplier_active";

        /// <summary>Ghost Fit's answer when its search found no legal placement for any dock piece.
        /// Shown instead of a suggestion, and nothing is charged for it.</summary>
        public const string POWERUP_GHOST_FIT_NO_PLACEMENTS = "powerup.ghost_fit_no_placements";

        // --- Splash ---

        public const string SPLASH_SKIP_HINT = "splash.skip_hint";
        public const string SPLASH_JINGLE_NOTE = "splash.jingle_note";

        // --- Score HUD ---

        public const string SCORE_BEST = "score.best";

        /// <summary>Best label in timed mode. <c>{0}</c> is the already-formatted round length.</summary>
        public const string SCORE_BEST_TIMED = "score.best_timed";

        // --- Game over ---

        public const string GAME_OVER_TITLE_NO_MOVES = "gameover.title.no_moves";
        public const string GAME_OVER_TITLE_TIME_UP = "gameover.title.time_up";

        /// <summary>Title shown when a Path-mode run ended because its level was cleared — the one
        /// end-of-run card that is a congratulation rather than a commiseration.</summary>
        public const string GAME_OVER_TITLE_LEVEL_COMPLETE = "gameover.title.level_complete";
        public const string GAME_OVER_HINT = "gameover.hint";
        public const string GAME_OVER_CHANGE_MODE = "gameover.change_mode";

        /// <summary><c>{0}</c> is the formatted round length, <c>{1}</c> the best score.</summary>
        public const string GAME_OVER_BEST_FOR_DURATION = "gameover.best_for_duration";

        /// <summary><c>{0}</c> is the final score, <c>{1}</c> the best score.</summary>
        public const string GAME_OVER_SCORE_AND_BEST = "gameover.score_and_best";

        /// <summary>Path mode's score line. <c>{0}</c> is this level's own score, <c>{1}</c> the
        /// running total for the whole walk of the path — both, because a level resets the first and
        /// only ever adds to the second.</summary>
        public const string GAME_OVER_SCORE_AND_PATH_TOTAL = "gameover.score_and_path_total";

        // --- Game modes ---

        public const string MODE_ENDLESS = "mode.endless";
        public const string MODE_TIMED = "mode.timed";
        public const string MODE_PATH = "mode.path";

        // --- Level path overlay ---

        /// <summary>The walk's running total on the level path card. <c>{0}</c> is the total. Shown
        /// only in Path mode, where a total across levels is a thing that exists.</summary>
        public const string LEVEL_PATH_TOTAL = "level_path.total";

        /// <summary>Hint under the node grid in Path mode, telling the player the nodes are tappable —
        /// they are inert status lights in every other mode, so the affordance has to be stated.</summary>
        public const string LEVEL_PATH_TAP_HINT = "level_path.tap_hint";

        // --- Settings panel ---

        public const string SETTINGS_TITLE = "settings.title";
        public const string SETTINGS_ROW_MODE = "settings.row.mode";
        public const string SETTINGS_ROW_THEME = "settings.row.theme";
        public const string SETTINGS_ROW_SOUND = "settings.row.sound";
        public const string SETTINGS_ROW_DURATION = "settings.row.duration";
        public const string SETTINGS_ROW_LANGUAGE = "settings.row.language";
        public const string SETTINGS_THEME_SCREEN_TITLE = "settings.theme_screen.title";
        public const string SETTINGS_LANGUAGE_SCREEN_TITLE = "settings.language_screen.title";
        public const string SETTINGS_MODE_SCREEN_TITLE = "settings.mode_screen.title";
        public const string SETTINGS_DURATION_SCREEN_TITLE = "settings.duration_screen.title";
        public const string SETTINGS_CONFIRM_TITLE = "settings.confirm.title";
        public const string SETTINGS_CONFIRM_BODY = "settings.confirm.body";
        public const string SETTINGS_CONFIRM_YES = "settings.confirm.yes";
        public const string SETTINGS_CONFIRM_NO = "settings.confirm.no";

        // --- Objectives ---
        // One description template per ObjectiveType. None of them spells the target value: the HUD
        // renders that separately as "2/3", so a template that repeated it could drift from the model.

        /// <summary><c>{0}</c> is the number of lines that must fall in one placement.</summary>
        public const string OBJECTIVE_SIMULTANEOUS_LINE_CLEAR = "objective.simultaneous_line_clear";

        /// <summary><c>{0}</c> is the localized piece-family name.</summary>
        public const string OBJECTIVE_PIECE_FAMILY_COUNT = "objective.piece_family_count";

        public const string OBJECTIVE_SCORE_IN_RUN = "objective.score_in_run";
        public const string OBJECTIVE_BOARD_WIPE_COUNT = "objective.board_wipe_count";
        public const string OBJECTIVE_STREAK_THRESHOLD = "objective.streak_threshold";
        public const string OBJECTIVE_BOMB_INDUCED_LINE_CLEAR = "objective.bomb_induced_line_clear";
        public const string OBJECTIVE_ROW_AND_COLUMN_CROSS_CLEAR = "objective.row_and_column_cross_clear";
        public const string OBJECTIVE_CLUTCH_RECOVERY_CLEAR = "objective.clutch_recovery_clear";
        public const string OBJECTIVE_AT_LEAST_LINE_CLEAR = "objective.at_least_line_clear";
        public const string OBJECTIVE_PIECE_ID_COUNT = "objective.piece_id_count";
        public const string OBJECTIVE_PIECE_ID_LINE_CLEAR = "objective.piece_id_line_clear";
        public const string OBJECTIVE_FOUR_CORNERS_CLEARED = "objective.four_corners_cleared";
        public const string OBJECTIVE_CENTER_CORE_EVACUATED = "objective.center_core_evacuated";
        public const string OBJECTIVE_NO_ISOLATED_HOLES_STREAK = "objective.no_isolated_holes_streak";

        /// <summary><c>{0}</c> is the rolling window width in whole seconds. The line-clear target
        /// itself is left out, same as every other template here — the HUD renders it as "4/6".</summary>
        public const string OBJECTIVE_LINE_CLEAR_BURST = "objective.line_clear_burst";

        /// <summary><c>{0}</c> is the deadline in whole seconds, counted from run start. The score
        /// target itself is left out, same as every other template here — the HUD renders it as the
        /// "1000/2500" progress.</summary>
        public const string OBJECTIVE_EARLY_SCORE_RUSH = "objective.early_score_rush";

        public const string OBJECTIVE_REROLL_SAVE = "objective.reroll_save";

        // Translated display names for individual catalog pieces, keyed by piece id in
        // ObjectiveDescriptionFormatter.PieceIdNameKey. Not exhaustive — only pieces an authored
        // objective actually references need one; an unmapped id falls back to its raw catalog id.
        // Add a new piece's name here (plus a row in GameStrings) as new objectives need it.
        public const string OBJECTIVE_PIECE_NAME_SQUARE_3X3 = "objective.piece_name.square_3x3";
        public const string OBJECTIVE_PIECE_NAME_LINE_5 = "objective.piece_name.line_5";

        // Family names are their own entries rather than being baked into the template above, so a
        // new family is one row per language instead of one template per language.
        public const string OBJECTIVE_FAMILY_SINGLE = "objective.family.single";
        public const string OBJECTIVE_FAMILY_LINE = "objective.family.line";
        public const string OBJECTIVE_FAMILY_SQUARE = "objective.family.square";
        public const string OBJECTIVE_FAMILY_CORNER = "objective.family.corner";
        public const string OBJECTIVE_FAMILY_T_SHAPE = "objective.family.t_shape";
        public const string OBJECTIVE_FAMILY_S_SHAPE = "objective.family.s_shape";
        public const string OBJECTIVE_FAMILY_Z_SHAPE = "objective.family.z_shape";

        // --- Themes ---

        public const string THEME_YAZ = "theme.yaz";
        public const string THEME_KIS = "theme.kis";
        public const string THEME_ILKBAHAR = "theme.ilkbahar";
        public const string THEME_SONBAHAR = "theme.sonbahar";
    }
}
