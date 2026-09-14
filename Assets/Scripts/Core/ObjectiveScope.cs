namespace MustyBlockBlast.Core
{
    /// <summary>How an objective's progress behaves across runs.</summary>
    public enum ObjectiveScope
    {
        /// <summary>Progress is discarded when a new run starts — the objective must be met within one run.</summary>
        PerRun = 0,

        /// <summary>Progress accumulates forever and survives game over and new runs.</summary>
        Cumulative = 1,
    }
}
