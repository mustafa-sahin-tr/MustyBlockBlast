namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published exactly once, on the placement that pushed an objective to its target. Further
    /// qualifying placements are swallowed by the objective itself, so consumers never de-duplicate.
    /// </summary>
    public readonly struct ObjectiveCompletedMessage
    {
        public ObjectiveCompletedMessage(string objectiveId)
        {
            ObjectiveId = objectiveId;
        }

        public string ObjectiveId { get; }
    }
}
