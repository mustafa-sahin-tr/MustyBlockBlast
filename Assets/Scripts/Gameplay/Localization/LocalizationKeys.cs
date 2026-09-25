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

        /// <summary>The floating label the Piercing Rocket demo shows as it wipes both of its lines (issue #451).</summary>
        public const string INFO_POPUP_DEMO_ROW_AND_COLUMN = "info_popup.demo.row_and_column";

        /// <summary>The pill the Demolition Hammer demo shows as the hammer is handed over (issue #451).</summary>
        public const string INFO_POPUP_DEMO_LAST_RESORT = "info_popup.demo.last_resort";

        /// <summary>An objective demo's progress-chip caption for a minimum line count, "{0}" the count
        /// (issue #452, the At Least Line Clear objective, e.g. "AT LEAST 3").</summary>
        public const string INFO_POPUP_DEMO_CHIP_AT_LEAST = "info_popup.demo.chip.at_least";

        /// <summary>The Row and Column Cross Clear demo's progress-chip caption (issue #452, "CROSS").</summary>
        public const string INFO_POPUP_DEMO_CHIP_CROSS = "info_popup.demo.chip.cross";

        /// <summary>The Bomb-induced line clear demo's progress-chip caption (issue #452, "EMPTY LINE").</summary>
        public const string INFO_POPUP_DEMO_CHIP_EMPTY_LINE = "info_popup.demo.chip.empty_line";

        /// <summary>The rolling line-clear window demo's progress-chip caption, "{0}" the window's whole
        /// seconds (issue #452, e.g. "15 SEC").</summary>
        public const string INFO_POPUP_DEMO_CHIP_WINDOW = "info_popup.demo.chip.window";

        /// <summary>The floating label the Bomb-induced line clear demo shows as the blast leaves a row
        /// completely empty (issue #452).</summary>
        public const string INFO_POPUP_DEMO_ROW_EMPTIED = "info_popup.demo.row_emptied";

        /// <summary>The Board Wipe objective demo's progress-chip caption (issue #453, "EMPTY BOARD").</summary>
        public const string INFO_POPUP_DEMO_CHIP_BOARD_WIPE = "info_popup.demo.chip.board_wipe";

        /// <summary>The Four Corners objective demo's progress-chip caption (issue #453, "CORNER").</summary>
        public const string INFO_POPUP_DEMO_CHIP_CORNER = "info_popup.demo.chip.corner";

        /// <summary>The Center Core objective demo's progress-chip caption (issue #453, "CENTER").</summary>
        public const string INFO_POPUP_DEMO_CHIP_CENTER = "info_popup.demo.chip.center";

        /// <summary>The No Isolated Holes streak demo's progress-chip caption (issue #453, "NO HOLES").</summary>
        public const string INFO_POPUP_DEMO_CHIP_NO_HOLES = "info_popup.demo.chip.no_holes";

        /// <summary>The Ice Cells objective demo's progress-chip caption (issue #453, "ICE").</summary>
        public const string INFO_POPUP_DEMO_CHIP_ICE = "info_popup.demo.chip.ice";

        /// <summary>The Reinforced Cells objective demo's progress-chip caption (issue #453, "ARMOUR").</summary>
        public const string INFO_POPUP_DEMO_CHIP_ARMOUR = "info_popup.demo.chip.armour";

        /// <summary>The floating label the Board Wipe demo shows as the board is left empty (issue #453).</summary>
        public const string INFO_POPUP_DEMO_BOARD_CLEARED = "info_popup.demo.board_cleared";

        /// <summary>The floating label the Four Corners demo shows as a clear takes two corners (issue #453).</summary>
        public const string INFO_POPUP_DEMO_CORNER_CLEARED = "info_popup.demo.corner_cleared";

        /// <summary>The floating label the Center Core demo shows as the centre 4x4 empties (issue #453).</summary>
        public const string INFO_POPUP_DEMO_CENTER_EMPTY = "info_popup.demo.center_empty";

        /// <summary>The floating label the No Isolated Holes streak demo shows after its third clean
        /// placement (issue #453).</summary>
        public const string INFO_POPUP_DEMO_NO_HOLES = "info_popup.demo.no_holes";

        /// <summary>The floating label the Colour Cleared demo shows once the last block of its colour goes
        /// (issue #453). Generic — never a colour name, which a theme switch would make wrong; the label is
        /// drawn in the colour itself.</summary>
        public const string INFO_POPUP_DEMO_COLOUR_GONE = "info_popup.demo.colour_gone";

        /// <summary>The floating label the Diamonds Cleared demo shows as its gems reach the chip (issue #453).</summary>
        public const string INFO_POPUP_DEMO_DIAMONDS_COLLECTED = "info_popup.demo.diamonds_collected";

        /// <summary>The floating label the Ice Cells demo shows as the socket's last level melts (issue #453).</summary>
        public const string INFO_POPUP_DEMO_ICE_MELTED = "info_popup.demo.ice_melted";

        /// <summary>The floating label the Reinforced Cells demo shows as the armoured cell breaks (issue #453).</summary>
        public const string INFO_POPUP_DEMO_ARMOUR_BROKEN = "info_popup.demo.armour_broken";

        /// <summary>The Clutch Recovery and Reroll Save objective demos' progress-chip caption (issue #454, "SAVE").</summary>
        public const string INFO_POPUP_DEMO_CHIP_SAVE = "info_popup.demo.chip.save";

        /// <summary>The floating label the Piece Family demo shows once two different members of the family have
        /// counted (issue #454). Family-generic — the chip's glyph names the family.</summary>
        public const string INFO_POPUP_DEMO_FAMILY_COUNTS = "info_popup.demo.family_counts";

        /// <summary>The floating label the Piece Id Count demo shows once the named piece has counted twice (issue #454).</summary>
        public const string INFO_POPUP_DEMO_THIS_PIECE = "info_popup.demo.this_piece";

        /// <summary>The floating label the Score In Run and Early Score Rush demos show as the score crosses the
        /// target (issue #454).</summary>
        public const string INFO_POPUP_DEMO_TARGET_SCORE = "info_popup.demo.target_score";

        /// <summary>The floating label the Streak Threshold demo shows at its target streak (issue #454); its
        /// <c>{0}</c> is the streak, written with the HUD streak pill's own "x".</summary>
        public const string INFO_POPUP_DEMO_COMBO = "info_popup.demo.combo";

        /// <summary>The pill label the Clutch Recovery demo shows once the near-full board clears (issue #454).</summary>
        public const string INFO_POPUP_DEMO_LAST_MOMENT = "info_popup.demo.last_moment";

        /// <summary>The floating label the Reroll Save demo shows once the dead dock is rerolled (issue #454).</summary>
        public const string INFO_POPUP_DEMO_SAVED = "info_popup.demo.saved";

        public const string INFO_POPUP_SPECIAL_CELL_CHAIN_LIGHTNING_HEADER = "info_popup.special_cell.chain_lightning.header";
        public const string INFO_POPUP_SPECIAL_CELL_CHAIN_LIGHTNING_BODY = "info_popup.special_cell.chain_lightning.body";
        public const string INFO_POPUP_SPECIAL_CELL_SCORE_GEM_HEADER = "info_popup.special_cell.score_gem.header";
        public const string INFO_POPUP_SPECIAL_CELL_SCORE_GEM_BODY = "info_popup.special_cell.score_gem.body";
        public const string INFO_POPUP_SPECIAL_CELL_COIN_HEADER = "info_popup.special_cell.coin.header";
        public const string INFO_POPUP_SPECIAL_CELL_COIN_BODY = "info_popup.special_cell.coin.body";

        /// <summary>Locked cell info card (issue #481): what opens it, and the gold it reveals.</summary>
        public const string INFO_POPUP_SPECIAL_CELL_LOCKED_HEADER = "info_popup.special_cell.locked.header";
        public const string INFO_POPUP_SPECIAL_CELL_LOCKED_BODY = "info_popup.special_cell.locked.body";

        /// <summary>Power star info card (issue #482): charged by line clears, bursts at 3.</summary>
        public const string INFO_POPUP_SPECIAL_CELL_POWER_STAR_HEADER = "info_popup.special_cell.power_star.header";
        public const string INFO_POPUP_SPECIAL_CELL_POWER_STAR_BODY = "info_popup.special_cell.power_star.body";

        /// <summary>Puzzle link info card (issue #483): the group goes only when every piece is hit at once.</summary>
        public const string INFO_POPUP_SPECIAL_CELL_PUZZLE_LINK_HEADER = "info_popup.special_cell.puzzle_link.header";
        public const string INFO_POPUP_SPECIAL_CELL_PUZZLE_LINK_BODY = "info_popup.special_cell.puzzle_link.body";
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

        /// <summary>Caption on a Path level-complete card's reward box, beside the power-up(s) the
        /// level's first clear paid (issue #462).</summary>
        public const string RUN_RESULT_LEVEL_REWARD_LABEL = "result.level_reward_label";


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

        /// <summary>The short advance label on the next-level button beside the level's icon (issue
        /// #464). <c>{0}</c> is the next level's number.</summary>
        public const string RUN_RESULT_NEXT_SHORT = "result.next_short";

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

        /// <summary>Title over an earned power-up while the grant fly-in holds it at the centre of the
        /// screen (issue #464). Not shown for a purchase.</summary>
        public const string GRANT_WON = "grant.won";

        /// <summary>Title over a first-try streak bonus while the grant fly-in holds it (issue #464), so
        /// the bonus does not read as a second level reward.</summary>
        public const string GRANT_STREAK_BONUS = "grant.streak_bonus";

        // --- Level-start card (issue #464) ---

        /// <summary>Zone name: Meadow.</summary>
        public const string LEVEL_START_ZONE_MEADOW = "level_start.zone.meadow";

        /// <summary>Zone name: Winter.</summary>
        public const string LEVEL_START_ZONE_WINTER = "level_start.zone.winter";

        /// <summary>Zone name: City.</summary>
        public const string LEVEL_START_ZONE_CITY = "level_start.zone.city";

        /// <summary>Zone name: Neighborhood.</summary>
        public const string LEVEL_START_ZONE_NEIGHBORHOOD = "level_start.zone.neighborhood";

        /// <summary>The pill under the level name. <c>{0}</c> is the zone name, <c>{1}</c> the level number.</summary>
        public const string LEVEL_START_PILL = "level_start.pill";

        /// <summary>Chip beside the pill on every 5th level.</summary>
        public const string LEVEL_START_MILESTONE = "level_start.milestone";

        /// <summary>Caption over the rewards a first clear pays.</summary>
        public const string LEVEL_START_CLEAR_TO_WIN = "level_start.clear_to_win";

        /// <summary>Caption over the rewards on a locked level's preview.</summary>
        public const string LEVEL_START_LOCKED_CAPTION = "level_start.locked_caption";

        /// <summary>Caption over a replayed level's rewards.</summary>
        public const string LEVEL_START_COLLECTED_CAPTION = "level_start.collected_caption";

        /// <summary>Streak row while the clear would not complete a rule. <c>{0}</c> is "x/n".</summary>
        public const string LEVEL_START_STREAK_PROGRESS = "level_start.streak_progress";

        /// <summary>Streak row when a first-try clear of this level completes a rule. <c>{0}</c> is "n/n".</summary>
        public const string LEVEL_START_STREAK_LANDS = "level_start.streak_lands";

        /// <summary>Streak row on a replay or a level already failed this attempt.</summary>
        public const string LEVEL_START_STREAK_REPLAY = "level_start.streak_replay";

        /// <summary>Caption under the streak row's pending bonus. <c>{0}</c> is the count, <c>{1}</c> "n/n".</summary>
        public const string LEVEL_START_STREAK_AT = "level_start.streak_at";

        /// <summary>Caption under the bonus a first-try clear of this level pays. <c>{0}</c> is the count.</summary>
        public const string LEVEL_START_STREAK_BONUS = "level_start.streak_bonus";

        /// <summary>Caption over the inventory grid.</summary>
        public const string LEVEL_START_YOUR_POWER_UPS = "level_start.your_power_ups";


        /// <summary>Footer of a locked preview. <c>{0}</c> is the frontier level.</summary>
        public const string LEVEL_START_UNLOCK_HINT = "level_start.unlock_hint";

        /// <summary>Start button.</summary>
        public const string LEVEL_START_START = "level_start.start";

        /// <summary>Start button while coin cells are picked. <c>{0}</c> is the quantity.</summary>
        public const string LEVEL_START_START_SOW = "level_start.start_sow";

        /// <summary>Start button on a replay.</summary>
        public const string LEVEL_START_PLAY_AGAIN = "level_start.play_again";

        /// <summary>Watch ad button.</summary>
        public const string LEVEL_START_WATCH_AD = "level_start.watch_ad";

        /// <summary>Coin Sower row title.</summary>
        public const string LEVEL_START_SOW_ROW = "level_start.sow_row";

        /// <summary>Coin Sower row subtitle. <c>{0}</c> is the banked charge count.</summary>
        public const string LEVEL_START_CHARGES = "level_start.charges";

        /// <summary>Shown when the sow spend is refused.</summary>
        public const string LEVEL_START_SOW_FAILED = "level_start.sow_failed";

        /// <summary>Shown when the start is refused.</summary>
        public const string LEVEL_START_CANNOT_START = "level_start.cannot_start";

        /// <summary>The level-start card's lives row, bold line (issue #478). <c>{0}</c> is the current
        /// count, <c>{1}</c> the refill cap: "17 / 20 lives".</summary>
        public const string LEVEL_START_LIVES = "level_start.lives";

        /// <summary>The level-start card's lives row, second line: what the level costs.</summary>
        public const string LEVEL_START_LIVES_HINT = "level_start.lives_hint";

        // --- Out-of-lives sheet and the fail card's life row (issue #478) ---

        /// <summary>Sheet title at zero lives.</summary>
        public const string LIVES_SHEET_TITLE_OUT = "lives.sheet.title_out";

        /// <summary>Sheet title when opened from the HUD with lives left.</summary>
        public const string LIVES_SHEET_TITLE = "lives.sheet.title";

        /// <summary>Caption over the refill countdown. <c>{0}</c> is the refill amount.</summary>
        public const string LIVES_SHEET_NEXT_REFILL = "lives.sheet.next_refill";

        /// <summary>The rule, under the countdown. <c>{0}</c> is the refill amount, <c>{1}</c> the cap.</summary>
        public const string LIVES_SHEET_BODY = "lives.sheet.body";

        /// <summary>The ad button's caption; the "+N" and heart ride beside it in a chip.</summary>
        public const string LIVES_SHEET_WATCH_AD = "lives.sheet.watch_ad";

        /// <summary>The ad button's caption while lives are at or above the cap and it is disabled.</summary>
        public const string LIVES_SHEET_FULL = "lives.sheet.full";

        /// <summary>Shown under the button when the ad was declined, dismissed or did not fill.</summary>
        public const string LIVES_SHEET_AD_REFUSED = "lives.sheet.ad_refused";

        /// <summary>The coin lives pack button's caption (issue #479); the "+N ♥" pill rides left of it and
        /// the coin price chip right of it.</summary>
        public const string LIVES_SHEET_PACK = "lives.sheet.pack";

        /// <summary>Shown under the buttons when a lives pack tap is refused for a short coin balance.</summary>
        public const string LIVES_SHEET_PACK_REFUSED = "lives.sheet.pack_refused";

        /// <summary>The sheet's footer: the modes that never use lives.</summary>
        public const string LIVES_SHEET_FOOTER = "lives.sheet.footer";

        /// <summary>The fail card's life row, bold line: "You lost a life".</summary>
        public const string RUN_RESULT_LIFE_LOST = "result.life_lost";

        /// <summary>The fail card's life row, detail. <c>{0}</c> is the count before, <c>{1}</c> after:
        /// "17 → 16 lives".</summary>
        public const string RUN_RESULT_LIVES_CHANGE = "result.lives_change";

        /// <summary>Appended to <see cref="RUN_RESULT_LIVES_CHANGE"/> below the cap. <c>{0}</c> is the
        /// refill amount, <c>{1}</c> the "mm:ss" left: "+5 in 12:34".</summary>
        public const string RUN_RESULT_LIVES_REFILL_IN = "result.lives_refill_in";

        // Each level's name (issue #464), one key per level like the BADGE_NAME_* keys: the rows of
        // LevelIdentityCatalog carry these, and every key must be declared here for the table tests.
        public const string LEVEL_NAME_1 = "level.name.1";
        public const string LEVEL_NAME_2 = "level.name.2";
        public const string LEVEL_NAME_3 = "level.name.3";
        public const string LEVEL_NAME_4 = "level.name.4";
        public const string LEVEL_NAME_5 = "level.name.5";
        public const string LEVEL_NAME_6 = "level.name.6";
        public const string LEVEL_NAME_7 = "level.name.7";
        public const string LEVEL_NAME_8 = "level.name.8";
        public const string LEVEL_NAME_9 = "level.name.9";
        public const string LEVEL_NAME_10 = "level.name.10";
        public const string LEVEL_NAME_11 = "level.name.11";
        public const string LEVEL_NAME_12 = "level.name.12";
        public const string LEVEL_NAME_13 = "level.name.13";
        public const string LEVEL_NAME_14 = "level.name.14";
        public const string LEVEL_NAME_15 = "level.name.15";
        public const string LEVEL_NAME_16 = "level.name.16";
        public const string LEVEL_NAME_17 = "level.name.17";
        public const string LEVEL_NAME_18 = "level.name.18";
        public const string LEVEL_NAME_19 = "level.name.19";
        public const string LEVEL_NAME_20 = "level.name.20";
        public const string LEVEL_NAME_21 = "level.name.21";
        public const string LEVEL_NAME_22 = "level.name.22";
        public const string LEVEL_NAME_23 = "level.name.23";
        public const string LEVEL_NAME_24 = "level.name.24";
        public const string LEVEL_NAME_25 = "level.name.25";
        public const string LEVEL_NAME_26 = "level.name.26";
        public const string LEVEL_NAME_27 = "level.name.27";
        public const string LEVEL_NAME_28 = "level.name.28";
        public const string LEVEL_NAME_29 = "level.name.29";
        public const string LEVEL_NAME_30 = "level.name.30";
        public const string LEVEL_NAME_31 = "level.name.31";
        public const string LEVEL_NAME_32 = "level.name.32";
        public const string LEVEL_NAME_33 = "level.name.33";
        public const string LEVEL_NAME_34 = "level.name.34";
        public const string LEVEL_NAME_35 = "level.name.35";
        public const string LEVEL_NAME_36 = "level.name.36";
        public const string LEVEL_NAME_37 = "level.name.37";
        public const string LEVEL_NAME_38 = "level.name.38";
        public const string LEVEL_NAME_39 = "level.name.39";
        public const string LEVEL_NAME_40 = "level.name.40";
        public const string LEVEL_NAME_41 = "level.name.41";
        public const string LEVEL_NAME_42 = "level.name.42";
        public const string LEVEL_NAME_43 = "level.name.43";
        public const string LEVEL_NAME_44 = "level.name.44";
        public const string LEVEL_NAME_45 = "level.name.45";
        public const string LEVEL_NAME_46 = "level.name.46";
        public const string LEVEL_NAME_47 = "level.name.47";
        public const string LEVEL_NAME_48 = "level.name.48";
        public const string LEVEL_NAME_49 = "level.name.49";
        public const string LEVEL_NAME_50 = "level.name.50";
        public const string LEVEL_NAME_51 = "level.name.51";
        public const string LEVEL_NAME_52 = "level.name.52";
        public const string LEVEL_NAME_53 = "level.name.53";
        public const string LEVEL_NAME_54 = "level.name.54";
        public const string LEVEL_NAME_55 = "level.name.55";
        public const string LEVEL_NAME_56 = "level.name.56";
        public const string LEVEL_NAME_57 = "level.name.57";
        public const string LEVEL_NAME_58 = "level.name.58";
        public const string LEVEL_NAME_59 = "level.name.59";
        public const string LEVEL_NAME_60 = "level.name.60";
        public const string LEVEL_NAME_61 = "level.name.61";
        public const string LEVEL_NAME_62 = "level.name.62";
        public const string LEVEL_NAME_63 = "level.name.63";
        public const string LEVEL_NAME_64 = "level.name.64";
        public const string LEVEL_NAME_65 = "level.name.65";
        public const string LEVEL_NAME_66 = "level.name.66";
        public const string LEVEL_NAME_67 = "level.name.67";
        public const string LEVEL_NAME_68 = "level.name.68";
        public const string LEVEL_NAME_69 = "level.name.69";
        public const string LEVEL_NAME_70 = "level.name.70";
        public const string LEVEL_NAME_71 = "level.name.71";
        public const string LEVEL_NAME_72 = "level.name.72";
        public const string LEVEL_NAME_73 = "level.name.73";
        public const string LEVEL_NAME_74 = "level.name.74";
        public const string LEVEL_NAME_75 = "level.name.75";
        public const string LEVEL_NAME_76 = "level.name.76";
        public const string LEVEL_NAME_77 = "level.name.77";
        public const string LEVEL_NAME_78 = "level.name.78";
        public const string LEVEL_NAME_79 = "level.name.79";
        public const string LEVEL_NAME_80 = "level.name.80";
        public const string LEVEL_NAME_81 = "level.name.81";
        public const string LEVEL_NAME_82 = "level.name.82";
        public const string LEVEL_NAME_83 = "level.name.83";
        public const string LEVEL_NAME_84 = "level.name.84";
        public const string LEVEL_NAME_85 = "level.name.85";
        public const string LEVEL_NAME_86 = "level.name.86";
        public const string LEVEL_NAME_87 = "level.name.87";
        public const string LEVEL_NAME_88 = "level.name.88";
        public const string LEVEL_NAME_89 = "level.name.89";
        public const string LEVEL_NAME_90 = "level.name.90";
        public const string LEVEL_NAME_91 = "level.name.91";
        public const string LEVEL_NAME_92 = "level.name.92";
        public const string LEVEL_NAME_93 = "level.name.93";
        public const string LEVEL_NAME_94 = "level.name.94";
        public const string LEVEL_NAME_95 = "level.name.95";
        public const string LEVEL_NAME_96 = "level.name.96";
        public const string LEVEL_NAME_97 = "level.name.97";
        public const string LEVEL_NAME_98 = "level.name.98";
        public const string LEVEL_NAME_99 = "level.name.99";
        public const string LEVEL_NAME_100 = "level.name.100";

        // --- Level path overlay ---

        /// <summary>The walk's running total on the level path card. <c>{0}</c> is the total. Shown
        /// only in Path mode, where a total across levels is a thing that exists.</summary>
        public const string LEVEL_PATH_TOTAL = "level_path.total";

        /// <summary>Hint under the node grid in Path mode, telling the player the nodes are tappable —
        /// they are inert status lights in every other mode, so the affordance has to be stated.</summary>
        public const string LEVEL_PATH_TAP_HINT = "level_path.tap_hint";

        /// <summary>Path-mode line on the level path card showing progress towards the next
        /// rule-based bonus (issue #464). <c>{0}</c> is "progress/threshold" (e.g. "2/3"), <c>{1}</c> is
        /// how many power-ups that bonus pays.</summary>
        public const string LEVEL_PATH_STREAK_HINT = "level_path.streak_hint";

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

        /// <summary>TimerCellsMeltedInTime's title (issue #453 — the type had none, so its card and the
        /// Level Path panel fell back to another objective's name).</summary>
        public const string OBJECTIVE_NAME_TIMER_CELLS_MELTED_IN_TIME = "objective.name.timer_cells_melted_in_time";

        /// <summary>TimerCellsMeltedInTime's description (issue #453).</summary>
        public const string OBJECTIVE_TIMER_CELLS_MELTED_IN_TIME = "objective.timer_cells_melted_in_time";

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
