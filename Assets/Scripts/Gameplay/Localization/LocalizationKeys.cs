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

        /// <summary>Classic mode's "Sınırsız" duration picker entry (issue #355) — the sentinel length
        /// (<see cref="Gameplay.Settings.TimedModeConfig.ENDLESS_DURATION_SECONDS"/>) that plays forever
        /// with no countdown. Its own key rather than a 0-minute <see cref="FORMAT_MINUTES"/>: "0 dk"
        /// would misname a duration that never ends.</summary>
        public const string DURATION_ENDLESS = "duration.endless";

        // --- Power-ups ---

        /// <summary>Double Multiplier's HUD banner while its window is open. <c>{0}</c> is the
        /// already-formatted remaining duration (via <see cref="FORMAT_SECONDS"/>) — this template only
        /// supplies the "2x"/"x2"/"kat" idiom, which differs by language unlike a bare symbol.</summary>
        public const string POWERUP_DOUBLE_MULTIPLIER_ACTIVE = "powerup.double_multiplier_active";

        /// <summary>Ghost Fit's answer when its search found no legal placement for any dock piece.
        /// Shown instead of a suggestion, and nothing is charged for it.</summary>
        public const string POWERUP_GHOST_FIT_NO_PLACEMENTS = "powerup.ghost_fit_no_placements";

        // --- Info popups ---
        // One header+body pair per specific enum value (issue #283 content pass) — every PowerUpKind
        // except Hold, every SpecialCellKind and every SpecialPieceKind gets its own copy. Hold has
        // only one subject, so its "class" key already is its "specific" key and is kept as-is. Shown
        // automatically the first time a kind first appears, and reopenable on demand afterward — see
        // InfoPopupSystem.

        public const string INFO_POPUP_HOLD_HEADER = "info_popup.hold.header";
        public const string INFO_POPUP_HOLD_BODY = "info_popup.hold.body";

        public const string INFO_POPUP_POWERUP_BOMB_HEADER = "info_popup.powerup.bomb.header";
        public const string INFO_POPUP_POWERUP_BOMB_BODY = "info_popup.powerup.bomb.body";
        public const string INFO_POPUP_POWERUP_ROW_CLEAR_HEADER = "info_popup.powerup.row_clear.header";
        public const string INFO_POPUP_POWERUP_ROW_CLEAR_BODY = "info_popup.powerup.row_clear.body";
        public const string INFO_POPUP_POWERUP_COLUMN_CLEAR_HEADER = "info_popup.powerup.column_clear.header";
        public const string INFO_POPUP_POWERUP_COLUMN_CLEAR_BODY = "info_popup.powerup.column_clear.body";
        public const string INFO_POPUP_POWERUP_JOKER_HEADER = "info_popup.powerup.joker.header";
        public const string INFO_POPUP_POWERUP_JOKER_BODY = "info_popup.powerup.joker.body";
        public const string INFO_POPUP_POWERUP_COLOR_CLEANSER_HEADER = "info_popup.powerup.color_cleanser.header";
        public const string INFO_POPUP_POWERUP_COLOR_CLEANSER_BODY = "info_popup.powerup.color_cleanser.body";
        public const string INFO_POPUP_POWERUP_PAINT_CROSS_HEADER = "info_popup.powerup.paint_cross.header";
        public const string INFO_POPUP_POWERUP_PAINT_CROSS_BODY = "info_popup.powerup.paint_cross.body";
        public const string INFO_POPUP_POWERUP_ROTATE_HEADER = "info_popup.powerup.rotate.header";
        public const string INFO_POPUP_POWERUP_ROTATE_BODY = "info_popup.powerup.rotate.body";
        public const string INFO_POPUP_POWERUP_REROLL_HEADER = "info_popup.powerup.reroll.header";
        public const string INFO_POPUP_POWERUP_REROLL_BODY = "info_popup.powerup.reroll.body";
        public const string INFO_POPUP_POWERUP_DOUBLE_MULTIPLIER_HEADER = "info_popup.powerup.double_multiplier.header";
        public const string INFO_POPUP_POWERUP_DOUBLE_MULTIPLIER_BODY = "info_popup.powerup.double_multiplier.body";
        public const string INFO_POPUP_POWERUP_GHOST_FIT_HEADER = "info_popup.powerup.ghost_fit.header";
        public const string INFO_POPUP_POWERUP_GHOST_FIT_BODY = "info_popup.powerup.ghost_fit.body";
        public const string INFO_POPUP_POWERUP_COIN_SOWER_HEADER = "info_popup.powerup.coin_sower.header";
        public const string INFO_POPUP_POWERUP_COIN_SOWER_BODY = "info_popup.powerup.coin_sower.body";

        public const string INFO_POPUP_SPECIAL_CELL_EXPLOSIVE_CORE_HEADER = "info_popup.special_cell.explosive_core.header";
        public const string INFO_POPUP_SPECIAL_CELL_EXPLOSIVE_CORE_BODY = "info_popup.special_cell.explosive_core.body";
        public const string INFO_POPUP_SPECIAL_CELL_LASER_HEADER = "info_popup.special_cell.laser.header";
        public const string INFO_POPUP_SPECIAL_CELL_LASER_BODY = "info_popup.special_cell.laser.body";
        public const string INFO_POPUP_SPECIAL_CELL_VORTEX_HEADER = "info_popup.special_cell.vortex.header";
        public const string INFO_POPUP_SPECIAL_CELL_VORTEX_BODY = "info_popup.special_cell.vortex.body";

        /// <summary>The floating label the Vortex info demo shows as its islands fill (issue #446).</summary>
        public const string INFO_POPUP_DEMO_VORTEX_FILLED = "info_popup.demo.vortex.filled";

        /// <summary>An objective demo's progress-chip caption for an exact line count, "{0}" the count
        /// (issue #447, e.g. "EXACTLY 2").</summary>
        public const string INFO_POPUP_DEMO_CHIP_EXACTLY = "info_popup.demo.chip.exactly";

        /// <summary>The floating label a demo shows as several lines clear at once, "{0}" the line
        /// count (issue #447, e.g. "2 lines!").</summary>
        public const string INFO_POPUP_DEMO_LINES_CLEARED = "info_popup.demo.lines_cleared";

        /// <summary>The floating label the Row/Column Clear demos show as a not-full line clears
        /// (issue #448, "Even if not full!").</summary>
        public const string INFO_POPUP_DEMO_EVEN_IF_NOT_FULL = "info_popup.demo.even_if_not_full";

        /// <summary>The floating label the Joker demo shows as its fill completes a row (issue #448).</summary>
        public const string INFO_POPUP_DEMO_ROW_COMPLETE = "info_popup.demo.row_complete";

        /// <summary>The floating label the Color Cleanser demo shows as one colour clears — worded without
        /// naming the colour, so it survives a theme change (issue #448).</summary>
        public const string INFO_POPUP_DEMO_SAME_COLOUR = "info_popup.demo.same_colour";

        /// <summary>The floating label the Paint Cross demo shows after the repaint (issue #448).</summary>
        public const string INFO_POPUP_DEMO_NOTHING_CLEARED = "info_popup.demo.nothing_cleared";

        /// <summary>The floating label the Rotate demo shows as the turned piece clears two lines (issue #449).</summary>
        public const string INFO_POPUP_DEMO_NOW_FITS = "info_popup.demo.now_fits";

        /// <summary>The pill the Reroll demo shows over a dock where no piece fits (issue #449).</summary>
        public const string INFO_POPUP_DEMO_NOTHING_FITS = "info_popup.demo.nothing_fits";

        /// <summary>The floating label the Ghost Fit demo shows over its silhouette (issue #449).</summary>
        public const string INFO_POPUP_DEMO_BEST_MOVE = "info_popup.demo.best_move";

        /// <summary>The floating label the Coin Sower demo shows as a cleared coin cell pays out (issue #449).</summary>
        public const string INFO_POPUP_DEMO_PLUS_COIN = "info_popup.demo.plus_coin";

        /// <summary>The floating label the Hold demo shows as a piece is parked in the pocket (issue #449).</summary>
        public const string INFO_POPUP_DEMO_HELD = "info_popup.demo.held";

        /// <summary>The floating label the Hold demo shows as a park swaps the pocket's piece out (issue #449).</summary>
        public const string INFO_POPUP_DEMO_SWAPPED = "info_popup.demo.swapped";

        /// <summary>The floating label the Explosive Core demo shows as its bonus wipe clears a line (issue #450).</summary>
        public const string INFO_POPUP_DEMO_BONUS_LINE = "info_popup.demo.bonus_line";

        /// <summary>The floating label the Laser demo shows as it wipes the line at right angles (issue #450).</summary>
        public const string INFO_POPUP_DEMO_PERPENDICULAR_LINE = "info_popup.demo.perpendicular_line";

        /// <summary>The floating label the Chain Lightning demo shows after its strike; a format string whose
        /// <c>{0}</c> is the number of blocks struck (issue #450).</summary>
        public const string INFO_POPUP_DEMO_RANDOM_BLOCKS = "info_popup.demo.random_blocks";

        /// <summary>The floating label the Timer demo shows as the timer cell clears before it runs out (issue #450).</summary>
        public const string INFO_POPUP_DEMO_JUST_IN_TIME = "info_popup.demo.just_in_time";

        public const string INFO_POPUP_SPECIAL_CELL_CHAIN_LIGHTNING_HEADER = "info_popup.special_cell.chain_lightning.header";
        public const string INFO_POPUP_SPECIAL_CELL_CHAIN_LIGHTNING_BODY = "info_popup.special_cell.chain_lightning.body";
        public const string INFO_POPUP_SPECIAL_CELL_SCORE_GEM_HEADER = "info_popup.special_cell.score_gem.header";
        public const string INFO_POPUP_SPECIAL_CELL_SCORE_GEM_BODY = "info_popup.special_cell.score_gem.body";
        public const string INFO_POPUP_SPECIAL_CELL_COIN_HEADER = "info_popup.special_cell.coin.header";
        public const string INFO_POPUP_SPECIAL_CELL_COIN_BODY = "info_popup.special_cell.coin.body";
        public const string INFO_POPUP_SPECIAL_CELL_TIMER_HEADER = "info_popup.special_cell.timer.header";
        public const string INFO_POPUP_SPECIAL_CELL_TIMER_BODY = "info_popup.special_cell.timer.body";

        public const string INFO_POPUP_SPECIAL_PIECE_GOLDEN_HEADER = "info_popup.special_piece.golden.header";
        public const string INFO_POPUP_SPECIAL_PIECE_GOLDEN_BODY = "info_popup.special_piece.golden.body";
        public const string INFO_POPUP_SPECIAL_PIECE_PIERCING_ROCKET_HEADER = "info_popup.special_piece.piercing_rocket.header";
        public const string INFO_POPUP_SPECIAL_PIECE_PIERCING_ROCKET_BODY = "info_popup.special_piece.piercing_rocket.body";
        public const string INFO_POPUP_SPECIAL_PIECE_DEMOLITION_HAMMER_HEADER = "info_popup.special_piece.demolition_hammer.header";
        public const string INFO_POPUP_SPECIAL_PIECE_DEMOLITION_HAMMER_BODY = "info_popup.special_piece.demolition_hammer.body";

        // --- Splash ---

        public const string SPLASH_SKIP_HINT = "splash.skip_hint";
        public const string SPLASH_JINGLE_NOTE = "splash.jingle_note";

        // --- Score HUD ---

        public const string SCORE_BEST = "score.best";

        /// <summary>Best label in timed mode. <c>{0}</c> is the already-formatted round length.</summary>
        public const string SCORE_BEST_TIMED = "score.best_timed";

        // --- Storefront HUD (issue #265) ---
        // The small uppercase captions on the score card, the goal row and the level pill. Kept
        // short: they sit beside a number in the display face and must not out-measure it.

        /// <summary>Caption over the run score on the score card.</summary>
        public const string HUD_SCORE_LABEL = "hud.score_label";

        /// <summary>The combo streak pill. <c>{0}</c> is the already-formatted score multiplier the
        /// streak has earned (e.g. "2,5"), so the template only supplies the "x" and the word.</summary>
        public const string HUD_STREAK = "hud.streak";

        /// <summary>Caption at the head of the goal row in endless and timed modes.</summary>
        public const string HUD_GOAL_LABEL = "hud.goal_label";

        /// <summary>The abbreviated "level" caption on the level-path pill, beside the frontier number.</summary>
        public const string HUD_LEVEL_SHORT = "hud.level_short";

        /// <summary>The full "Level n" caption on the level-path pill in Path mode. <c>{0}</c> is the
        /// active level's number.</summary>
        public const string HUD_LEVEL_NUMBER = "hud.level_number";

        /// <summary>Caption inside the empty Hold pocket at the end of the tray card.</summary>
        public const string HUD_POCKET_LABEL = "hud.pocket_label";

        // --- Game over / run result (issues #220, #221, #266) ---

        public const string GAME_OVER_TITLE_NO_MOVES = "gameover.title.no_moves";
        public const string GAME_OVER_TITLE_TIME_UP = "gameover.title.time_up";

        /// <summary>Title shown when a Path-mode run ended because its level was cleared — the one
        /// end-of-run card that is a congratulation rather than a commiseration. <c>{0}</c> is the
        /// completed level's number.</summary>
        public const string GAME_OVER_TITLE_LEVEL_COMPLETE = "gameover.title.level_complete";

        /// <summary>Title shown when a Path-mode run ended because a timer cell's countdown reached 0
        /// (issue #307 AC4/AC6b) — distinct from <see cref="GAME_OVER_TITLE_NO_MOVES"/> so the player can
        /// tell the two failures apart.</summary>
        public const string GAME_OVER_TITLE_OBJECTIVE_MISSED = "gameover.title.objective_missed";

        /// <summary>Reason line under the title when no tray piece fits (Endless and Timed).</summary>
        public const string RUN_RESULT_REASON_NO_MOVES = "result.reason.no_moves";

        /// <summary>Reason line when the timed clock ran out. <c>{0}</c> is the formatted round length.</summary>
        public const string RUN_RESULT_REASON_TIME_UP = "result.reason.time_up";

        /// <summary>Reason line under a Path level's "complete" title.</summary>
        public const string RUN_RESULT_REASON_LEVEL_COMPLETE = "result.reason.level_complete";

        /// <summary>Reason line when a Path level ran out of moves before its objective. <c>{0}</c> is
        /// the level's number.</summary>
        public const string RUN_RESULT_REASON_LEVEL_FAILED = "result.reason.level_failed";

        /// <summary>Reason line when a Path level ended because a timer cell expired. <c>{0}</c> is the
        /// level's number.</summary>
        public const string RUN_RESULT_REASON_OBJECTIVE_MISSED = "result.reason.objective_missed";

        /// <summary>Caption over the lifetime total of every point ever scored, this run included.</summary>
        public const string RUN_RESULT_TOTAL_LABEL = "result.total_label";

        /// <summary>Caption over the just-ended Path level's own score.</summary>
        public const string RUN_RESULT_LEVEL_SCORE_LABEL = "result.level_score_label";

        /// <summary>Caption over the running total of the whole walk of the path.</summary>
        public const string RUN_RESULT_PATH_TOTAL_LABEL = "result.path_total_label";

        /// <summary>Caption over the coin balance on a Path level-complete card, beside the "+N" the
        /// level itself paid out.</summary>
        public const string RUN_RESULT_COIN_LABEL = "result.coin_label";

        /// <summary>Tag on the record plate when this run set a new record.</summary>
        public const string RUN_RESULT_NEW_RECORD = "result.new_record";

        /// <summary>Heading over the badges unlocked this run. Shown only when there is at least one.</summary>
        public const string RUN_RESULT_BADGES_HEADING = "result.badges_heading";

        /// <summary>The tap-to-claim instruction beside the badges heading.</summary>
        public const string RUN_RESULT_CLAIM_HINT = "result.claim_hint";

        public const string RUN_RESULT_PLAY_AGAIN = "result.play_again";
        public const string RUN_RESULT_CHANGE_MODE = "result.change_mode";

        /// <summary>The Path-mode advance button. <c>{0}</c> is the next level's number.</summary>
        public const string RUN_RESULT_NEXT_LEVEL = "result.next_level";

        /// <summary>The restart button when a Path level was failed rather than cleared.</summary>
        public const string RUN_RESULT_TRY_AGAIN = "result.try_again";

        /// <summary>The rewarded-ad rescue button on a no-moves ending (issue #371): a fresh dock, same run.</summary>
        public const string RUN_RESULT_WATCH_AD = "result.watch_ad";

        // --- Game modes ---

        public const string MODE_ENDLESS = "mode.endless";
        public const string MODE_TIMED = "mode.timed";
        public const string MODE_PATH = "mode.path";

        /// <summary>One line under each mode's name in the settings card's mode picker (issue #260).</summary>
        public const string MODE_ENDLESS_DESCRIPTION = "mode.endless.description";
        public const string MODE_TIMED_DESCRIPTION = "mode.timed.description";
        public const string MODE_PATH_DESCRIPTION = "mode.path.description";

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

        /// <summary>The note under the round-length row's value, saying which mode unlocks it (issue #260).</summary>
        public const string SETTINGS_ROW_DURATION_TIMED_ONLY = "settings.row.duration.timed_only";

        /// <summary>The sound row's value beside its toggle (issue #260).</summary>
        public const string SETTINGS_SOUND_ON = "settings.sound.on";
        public const string SETTINGS_SOUND_OFF = "settings.sound.off";

        /// <summary>Small caption over the Remove Ads button, naming what the purchase is (issue #260).</summary>
        public const string SETTINGS_REMOVE_ADS_CAPTION = "settings.remove_ads.caption";

        /// <summary>The Remove Ads button's label before the purchase. A call to action, not a price:
        /// what the product costs in money lives in the store consoles, so this card cannot state it.</summary>
        public const string SETTINGS_REMOVE_ADS_BUY = "settings.remove_ads.buy";

        /// <summary>The strip that replaces the Remove Ads button once the product is owned. A state,
        /// not an offer — a non-consumable cannot be bought twice, so the button stops existing.</summary>
        public const string SETTINGS_REMOVE_ADS_OWNED = "settings.remove_ads.owned";

        /// <summary>The game's own confirmation before the store is asked to sell ad removal (issue
        /// #423): its title, its one line of body, the caption under the store's price, and its two
        /// buttons. The price itself is the store's localized string, never a table entry.</summary>
        public const string SETTINGS_REMOVE_ADS_CONFIRM_TITLE = "settings.remove_ads.confirm_title";
        public const string SETTINGS_REMOVE_ADS_CONFIRM_BODY = "settings.remove_ads.confirm_body";
        public const string SETTINGS_REMOVE_ADS_CONFIRM_PRICE_CAPTION = "settings.remove_ads.confirm_price_caption";
        public const string SETTINGS_REMOVE_ADS_CONFIRM_BUY = "settings.remove_ads.confirm_buy";
        public const string SETTINGS_REMOVE_ADS_CONFIRM_CANCEL = "settings.remove_ads.confirm_cancel";
        public const string SETTINGS_THEME_SCREEN_TITLE = "settings.theme_screen.title";

        /// <summary>The hint on the right of the theme picker's header row (issue #260).</summary>
        public const string SETTINGS_THEME_SCREEN_HINT = "settings.theme_screen.hint";
        public const string SETTINGS_LANGUAGE_SCREEN_TITLE = "settings.language_screen.title";
        public const string SETTINGS_MODE_SCREEN_TITLE = "settings.mode_screen.title";

        /// <summary>The tag on the mode plate that is being played (issue #260).</summary>
        public const string SETTINGS_MODE_PLAYING = "settings.mode.playing";
        public const string SETTINGS_DURATION_SCREEN_TITLE = "settings.duration_screen.title";

        /// <summary>The restart confirmation's title. <c>{0}</c> is the mode being switched to.</summary>
        public const string SETTINGS_CONFIRM_SWITCH_TITLE = "settings.confirm.switch_title";
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

        /// <summary>Was missing entirely until 2026-09-19 — <c>ObjectiveDescriptionFormatter.Describe</c>
        /// fell through to an empty string for this type with no template to format.</summary>
        public const string OBJECTIVE_REINFORCED_CELLS_CLEARED = "objective.reinforced_cells_cleared";

        /// <summary>IceCellsCleared's description (issue #433) — "melt every ice cell". No template
        /// argument: the target is always "all of them", which the HUD renders as the progress
        /// fraction.</summary>
        public const string OBJECTIVE_ICE_CELLS_CLEARED = "objective.ice_cells_cleared";

        // --- Objective titles ---
        // One short, bold name per ObjectiveType, shown as the header of the objective info card
        // (ObjectiveInfoPopupView) — distinct from the OBJECTIVE_* description templates above, which
        // spell out what the objective asks for in a full sentence. Mirrors how POWERUP_NAME_* sits
        // alongside POWERUP_DESC_*.

        public const string OBJECTIVE_NAME_SIMULTANEOUS_LINE_CLEAR = "objective.name.simultaneous_line_clear";
        public const string OBJECTIVE_NAME_PIECE_FAMILY_COUNT = "objective.name.piece_family_count";
        public const string OBJECTIVE_NAME_SCORE_IN_RUN = "objective.name.score_in_run";
        public const string OBJECTIVE_NAME_BOARD_WIPE_COUNT = "objective.name.board_wipe_count";
        public const string OBJECTIVE_NAME_STREAK_THRESHOLD = "objective.name.streak_threshold";
        public const string OBJECTIVE_NAME_BOMB_INDUCED_LINE_CLEAR = "objective.name.bomb_induced_line_clear";
        public const string OBJECTIVE_NAME_ROW_AND_COLUMN_CROSS_CLEAR = "objective.name.row_and_column_cross_clear";
        public const string OBJECTIVE_NAME_CLUTCH_RECOVERY_CLEAR = "objective.name.clutch_recovery_clear";
        public const string OBJECTIVE_NAME_AT_LEAST_LINE_CLEAR = "objective.name.at_least_line_clear";
        public const string OBJECTIVE_NAME_PIECE_ID_COUNT = "objective.name.piece_id_count";
        public const string OBJECTIVE_NAME_PIECE_ID_LINE_CLEAR = "objective.name.piece_id_line_clear";
        public const string OBJECTIVE_NAME_FOUR_CORNERS_CLEARED = "objective.name.four_corners_cleared";
        public const string OBJECTIVE_NAME_CENTER_CORE_EVACUATED = "objective.name.center_core_evacuated";
        public const string OBJECTIVE_NAME_NO_ISOLATED_HOLES_STREAK = "objective.name.no_isolated_holes_streak";
        public const string OBJECTIVE_NAME_ROLLING_LINE_CLEAR_WINDOW = "objective.name.rolling_line_clear_window";
        public const string OBJECTIVE_NAME_EARLY_SCORE_RUSH = "objective.name.early_score_rush";
        public const string OBJECTIVE_NAME_REROLL_SAVE = "objective.name.reroll_save";
        public const string OBJECTIVE_NAME_REINFORCED_CELLS_CLEARED = "objective.name.reinforced_cells_cleared";
        public const string OBJECTIVE_NAME_COLOUR_CLEARED = "objective.name.colour_cleared";

        /// <summary>ColourCleared's description. <c>{0}</c> is an inline colour swatch (a rich-text
        /// coloured glyph drawn from the active theme), never a colour name — see
        /// <c>ObjectiveDescriptionFormatter</c>.</summary>
        public const string OBJECTIVE_COLOUR_CLEARED = "objective.colour_cleared";

        /// <summary>DiamondsCleared's title (issue #395).</summary>
        public const string OBJECTIVE_NAME_DIAMONDS_CLEARED = "objective.name.diamonds_cleared";

        /// <summary>IceCellsCleared's title (issue #433).</summary>
        public const string OBJECTIVE_NAME_ICE_CELLS_CLEARED = "objective.name.ice_cells_cleared";

        /// <summary>DiamondsCleared's description. <c>{0}</c> is the gem colour's inline swatch, the
        /// same rich-text glyph <see cref="OBJECTIVE_COLOUR_CLEARED"/> uses, since the objective is
        /// scoped by the diamond's colour the way ColourCleared is by the block's.</summary>
        public const string OBJECTIVE_DIAMONDS_CLEARED = "objective.diamonds_cleared";

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
        public const string POWERUP_NAME_PAINT_CROSS = "powerup.name.paint_cross";

        public const string POWERUP_DESC_BOMB = "powerup.desc.bomb";
        public const string POWERUP_DESC_ROW_CLEAR = "powerup.desc.row_clear";
        public const string POWERUP_DESC_COLUMN_CLEAR = "powerup.desc.column_clear";
        public const string POWERUP_DESC_JOKER = "powerup.desc.joker";
        public const string POWERUP_DESC_COLOR_CLEANSER = "powerup.desc.color_cleanser";
        public const string POWERUP_DESC_ROTATE = "powerup.desc.rotate";
        public const string POWERUP_DESC_REROLL = "powerup.desc.reroll";
        public const string POWERUP_DESC_DOUBLE_MULTIPLIER = "powerup.desc.double_multiplier";
        public const string POWERUP_DESC_GHOST_FIT = "powerup.desc.ghost_fit";
        public const string POWERUP_DESC_PAINT_CROSS = "powerup.desc.paint_cross";

        // --- Paint Cross colour picker (issue #295) ---
        //
        // The bottom sheet a Paint Cross tap opens: its title and its confirm button. Keyed here rather
        // than in the View, like every other string.

        public const string PAINT_CROSS_PICKER_TITLE = "paint_cross.picker.title";
        public const string PAINT_CROSS_PICKER_CONFIRM = "paint_cross.picker.confirm";

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

        // --- Power-up shop chrome (issue #255) ---
        //
        // The stall's title, sub-tabs, section labels, panel copy and toast messages, distinct from
        // the POWERUP_NAME_*/POWERUP_DESC_* item catalog above. Was plain English const string on
        // PowerUpShopView until now — see that class's PaintChrome/BuildCoinsTab/toast call sites.

        public const string SHOP_TITLE = "shop.title";
        public const string SHOP_TAB_POWER_UPS = "shop.tab.power_ups";
        public const string SHOP_TAB_COINS = "shop.tab.coins";
        public const string SHOP_TAB_DEALS = "shop.tab.deals";
        public const string SHOP_EARN_BUTTON = "shop.earn_button";
        public const string SHOP_DEALS_PLACEHOLDER = "shop.deals_placeholder";
        public const string SHOP_SECTION_FREE = "shop.section.free";
        public const string SHOP_SECTION_BUNDLES = "shop.section.bundles";
        public const string SHOP_CONVERT_TITLE = "shop.convert.title";
        public const string SHOP_TOTAL_SCORE_LABEL = "shop.convert.total_score_label";
        public const string SHOP_CONVERTIBLE_LABEL = "shop.convert.convertible_label";
        public const string SHOP_CONVERT_BUTTON = "shop.convert.button";

        /// <summary>The convert panel's rate caption. <c>{0}</c> is the sample score
        /// (<c>RATE_SAMPLE_SCORE</c>), <c>{1}</c> the coins it quotes to, e.g. "100 pts = 10 coins".</summary>
        public const string SHOP_RATE_FORMAT = "shop.convert.rate_format";

        public const string SHOP_AD_TITLE = "shop.ad.title";

        /// <summary>
        /// The watch-an-ad row's caption while at least one grant remains today (issue #257). <c>{0}</c>
        /// is <see cref="MustyBlockBlast.Gameplay.Systems.CurrencySystem.RemainingAdGrantsToday"/>, e.g.
        /// "2 left today".
        /// </summary>
        public const string SHOP_AD_CAPTION_FORMAT = "shop.ad.caption_format";

        /// <summary>The watch-an-ad row's caption once the daily coin-ad cap is spent (issue #257): "Come
        /// back tomorrow". Shown in place of <see cref="SHOP_AD_CAPTION_FORMAT"/>, alongside the same
        /// unaffordable-grey the button takes.</summary>
        public const string SHOP_AD_CAPTION_EXHAUSTED = "shop.ad.caption_exhausted";

        public const string SHOP_BUNDLE_BUTTON = "shop.bundle.button";
        public const string SHOP_NO_BUNDLES = "shop.bundle.none";
        public const string SHOP_TOAST_CONVERTED = "shop.toast.converted";
        public const string SHOP_TOAST_NOTHING_TO_CONVERT = "shop.toast.nothing_to_convert";

        /// <summary>Shown for both an ad grant and a bundle purchase — the two faucets read the same
        /// "Coins added!" toast, so one key serves both call sites.</summary>
        public const string SHOP_TOAST_COINS_ADDED = "shop.toast.coins_added";

        public const string SHOP_TOAST_AD_REFUSED = "shop.toast.ad_refused";
        public const string SHOP_TOAST_BUNDLE_REFUSED = "shop.toast.bundle_refused";
        public const string SHOP_TOAST_PURCHASED = "shop.toast.purchased";
        public const string SHOP_TOAST_INSUFFICIENT_COINS = "shop.toast.insufficient_coins";
        public const string SHOP_TOAST_LOCKED = "shop.toast.locked";

        // --- Themes ---

        public const string THEME_YAZ = "theme.yaz";
        public const string THEME_KIS = "theme.kis";
        public const string THEME_ILKBAHAR = "theme.ilkbahar";
        public const string THEME_SONBAHAR = "theme.sonbahar";
    }
}
