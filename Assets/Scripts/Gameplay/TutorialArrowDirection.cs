namespace MustyBlockBlast.Gameplay
{
    /// <summary>Which side of the spotlighted target a <see cref="TutorialStep"/>'s arrow points from,
    /// or <see cref="None"/> for a step whose target needs no pointer at all.</summary>
    public enum TutorialArrowDirection
    {
        None,
        Up,
        Down,
        Left,
        Right,
    }
}
