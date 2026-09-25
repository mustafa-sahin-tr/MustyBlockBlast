namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>A power-up was awarded to the player. <see cref="NewInventoryCount"/> is the count
    /// after the grant, so consumers never have to read the model to show the new total.</summary>
    public readonly struct PowerUpGrantedMessage
    {
        public PowerUpGrantedMessage(PowerUpKind kind, int newInventoryCount)
            : this(kind, newInventoryCount, PowerUpGrantSource.Reward)
        {
        }

        public PowerUpGrantedMessage(PowerUpKind kind, int newInventoryCount, PowerUpGrantSource source)
        {
            Kind = kind;
            NewInventoryCount = newInventoryCount;
            Source = source;
        }

        public PowerUpKind Kind { get; }

        public int NewInventoryCount { get; }

        /// <summary>Why the power-up was granted — what the fly-in's caption says (issue #464).</summary>
        public PowerUpGrantSource Source { get; }
    }
}
