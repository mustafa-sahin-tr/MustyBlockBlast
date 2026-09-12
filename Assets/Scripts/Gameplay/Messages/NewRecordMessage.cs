namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published once per run, the moment <see cref="Models.ScoreModel.Score"/> first exceeds the
    /// high score the run started with. Endless-mode only; consumed by views for celebration effects.
    /// </summary>
    public readonly struct NewRecordMessage
    {
    }
}
