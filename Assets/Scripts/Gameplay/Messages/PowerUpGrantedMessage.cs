namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>A power-up was awarded to the player. <see cref="NewInventoryCount"/> is the count
    /// after the grant, so consumers never have to read the model to show the new total.</summary>
    public readonly struct PowerUpGrantedMessage
    {
        public PowerUpGrantedMessage(PowerUpKind kind, int newInventoryCount)
        {
            Kind = kind;
            NewInventoryCount = newInventoryCount;
        }

        public PowerUpKind Kind { get; }

        public int NewInventoryCount { get; }
    }
}
