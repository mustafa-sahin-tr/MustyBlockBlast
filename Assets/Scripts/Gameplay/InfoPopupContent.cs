namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// One info popup: what it is about, and the copy to show for it. Pure data — built and owned by
    /// <see cref="Systems.InfoPopupSystem"/>, read by <see cref="Models.InfoPopupModel"/> and rendered by
    /// <see cref="Presentation.Views.InfoPopupView"/> — so it carries no Unity types.
    /// </summary>
    public readonly struct InfoPopupContent
    {
        public InfoPopupContent(
            string id,
            InfoPopupSubjectKind subjectKind,
            int kindValue,
            string headerLocalizationKey,
            string bodyLocalizationKey)
        {
            Id = id;
            SubjectKind = subjectKind;
            KindValue = kindValue;
            HeaderLocalizationKey = headerLocalizationKey;
            BodyLocalizationKey = bodyLocalizationKey;
        }

        /// <summary>Stable identity for this popup, and the persistence key suffix for its "seen" flag
        /// (see <see cref="Systems.InfoPopupSeenKey"/>) — e.g. <c>"PowerUp_Bomb"</c>.</summary>
        public string Id { get; }

        /// <summary>Which family of on-screen element this popup explains.</summary>
        public InfoPopupSubjectKind SubjectKind { get; }

        /// <summary>The underlying <see cref="Core.SpecialCellKind"/>/<see cref="PowerUpKind"/>/
        /// <see cref="Core.SpecialPieceKind"/> cast to <see cref="int"/>, or <c>-1</c> for
        /// <see cref="InfoPopupSubjectKind.Hold"/>, which has no per-kind variant.</summary>
        public int KindValue { get; }

        /// <summary>String Table key (see <see cref="Localization.LocalizationKeys"/>) for the popup's
        /// header.</summary>
        public string HeaderLocalizationKey { get; }

        /// <summary>String Table key for the popup's body copy.</summary>
        public string BodyLocalizationKey { get; }
    }
}
