namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published whenever a tracked objective's value actually moved — never for a placement that did
    /// not qualify. Carries the target alongside the value so a future HUD can render "3/10" without
    /// reaching back into the model.
    /// </summary>
    public readonly struct ObjectiveProgressChangedMessage
    {
        public ObjectiveProgressChangedMessage(string objectiveId, int currentValue, int targetValue)
        {
            ObjectiveId = objectiveId;
            CurrentValue = currentValue;
            TargetValue = targetValue;
        }

        public string ObjectiveId { get; }

        public int CurrentValue { get; }

        public int TargetValue { get; }
    }
}
