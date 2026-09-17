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

        /// <summary>A duration in whole minutes. <c>{0}</c> is the number. Used wherever a timed round
        /// length is named — the duration picker, the best-score suffix and the game-over card — since
        /// round lengths are minutes rather than seconds.</summary>
        public const string FORMAT_MINUTES = "format.minutes";

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
        /// end-of-run card that is a congratulation rather than a commiseration. <c>{0}</c> is the
        /// completed level's number.</summary>
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

        // --- Run result (issue #220) ---

        /// <summary>Title of the result summary card shown on game over, before the game-over card.</summary>
        public const string RUN_RESULT_TITLE = "result.title";

        /// <summary><c>{0}</c> is the score of the run that just ended.</summary>
        public const string RUN_RESULT_RUN_SCORE = "result.run_score";

        /// <summary><c>{0}</c> is the lifetime total of every point ever scored, this run included.</summary>
        public const string RUN_RESULT_LIFETIME_TOTAL = "result.lifetime_total";

        /// <summary>Dismiss hint at the foot of the result card.</summary>
        public const string RUN_RESULT_TAP_HINT = "result.tap_hint";

        /// <summary>Heading over the badges unlocked this run on the result card, which doubles as the
        /// tap-to-claim instruction. Shown only when there is at least one (issue #221).</summary>
        public const string RUN_RESULT_BADGES_TITLE = "result.badges_title";

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
        public const string SETTINGS_ROW_REMOVE_ADS = "settings.row.remove_ads";

        /// <summary>The Remove Ads row's pill before the purchase. A call to action, not a price: what
        /// the product costs in money lives in the store consoles, so this card cannot state it.</summary>
        public const string SETTINGS_REMOVE_ADS_BUY = "settings.remove_ads.buy";

        /// <summary>The Remove Ads row's pill once the product is owned. A state, not an offer — a
        /// non-consumable cannot be bought twice, so the row stops being a button.</summary>
        public const string SETTINGS_REMOVE_ADS_OWNED = "settings.remove_ads.owned";
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

        // --- Power-up catalog (shop rows) ---
        //
        // One name and one one-line description per kind, so a new kind is two rows per language
        // rather than a string buried in a View. Keyed off the enum by PowerUpShopView.

        public const string POWERUP_NAME_BOMB = "powerup.name.bomb";
        public const string POWERUP_NAME_ROW_CLEAR = "powerup.name.row_clear";
        public const string POWERUP_NAME_COLUMN_CLEAR = "powerup.name.column_clear";
        public const string POWERUP_NAME_JOKER = "powerup.name.joker";
        public const string POWERUP_NAME_COLOR_CLEANSER = "powerup.name.color_cleanser";
        public const string POWERUP_NAME_ROTATE = "powerup.name.rotate";
        public const string POWERUP_NAME_REROLL = "powerup.name.reroll";
        public const string POWERUP_NAME_DOUBLE_MULTIPLIER = "powerup.name.double_multiplier";
        public const string POWERUP_NAME_GHOST_FIT = "powerup.name.ghost_fit";

        public const string POWERUP_DESC_BOMB = "powerup.desc.bomb";
        public const string POWERUP_DESC_ROW_CLEAR = "powerup.desc.row_clear";
        public const string POWERUP_DESC_COLUMN_CLEAR = "powerup.desc.column_clear";
        public const string POWERUP_DESC_JOKER = "powerup.desc.joker";
        public const string POWERUP_DESC_COLOR_CLEANSER = "powerup.desc.color_cleanser";
        public const string POWERUP_DESC_ROTATE = "powerup.desc.rotate";
        public const string POWERUP_DESC_REROLL = "powerup.desc.reroll";
        public const string POWERUP_DESC_DOUBLE_MULTIPLIER = "powerup.desc.double_multiplier";
        public const string POWERUP_DESC_GHOST_FIT = "powerup.desc.ghost_fit";

        // --- Badges ---
        //
        // Tile names, one per authored badge id. The catalog row carries the key (BadgeConfig.
        // DisplayNameKey) rather than the text, so a new badge is one content row plus one row per
        // language here.

        public const string BADGE_NAME_FIRST_STEPS = "badge.name.first_steps";
        public const string BADGE_NAME_LINE_CUTTER = "badge.name.line_cutter";
        public const string BADGE_NAME_BOARD_SWEEPER = "badge.name.board_sweeper";
        public const string BADGE_NAME_HIGH_SCORER = "badge.name.high_scorer";
        public const string BADGE_NAME_CENTURY_CLUB = "badge.name.century_club";
        public const string BADGE_NAME_PERFECTIONIST = "badge.name.perfectionist";
        public const string BADGE_NAME_VETERAN = "badge.name.veteran";
        public const string BADGE_NAME_POWER_PLAYER = "badge.name.power_player";
        public const string BADGE_NAME_MARATHON = "badge.name.marathon";
        public const string BADGE_NAME_ELITE_SCORER = "badge.name.elite_scorer";

        // --- Hub ---

        /// <summary>Header titles of the hub's tabs. Settings reuses <see cref="SETTINGS_TITLE"/>.</summary>
        public const string HUB_TAB_POWER_UP_SHOP = "hub.tab.power_up_shop";
        public const string HUB_TAB_LEADERBOARD = "hub.tab.leaderboard";
        public const string HUB_TAB_PROFILE = "hub.tab.profile";
        public const string HUB_TAB_BADGES = "hub.tab.badges";

        // --- Hold slot ---

        /// <summary>Short label under the empty Hold slot, telling the player what dragging a tray
        /// piece there does.</summary>
        public const string HOLD_SLOT_EMPTY_HINT = "hold_slot.empty_hint";

        // --- Themes ---

        public const string THEME_YAZ = "theme.yaz";
        public const string THEME_KIS = "theme.kis";
        public const string THEME_ILKBAHAR = "theme.ilkbahar";
        public const string THEME_SONBAHAR = "theme.sonbahar";
    }
}
