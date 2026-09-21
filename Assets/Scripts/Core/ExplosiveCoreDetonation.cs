using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// One <see cref="SpecialCellKind.ExplosiveCore"/> detonation's own work: the cell it detonated
    /// from (<see cref="Origin"/>, already empty by the time <see cref="ExplosiveCoreEffect.Apply"/>
    /// runs) and every gap it filled to finish a row or column (<see cref="FinishedTargets"/>), in the
    /// order it filled them — Presentation's cue to fly the core's icon from <see cref="Origin"/> to
    /// each target in turn rather than resolving every finished line in one silent instant.
    /// <para>
    /// A shared corner — one gap that completes a row and a column at once — appears exactly once here:
    /// <see cref="ExplosiveCoreEffect.Apply"/> only records a target the first time a gap is actually
    /// filled, and the second qualifying line that shares it finds nothing left to do.
    /// </para>
    /// </summary>
    public readonly struct ExplosiveCoreDetonation
    {
        public ExplosiveCoreDetonation(GridPosition origin, IReadOnlyList<GridPosition> finishedTargets)
        {
            Origin = origin;
            FinishedTargets = finishedTargets;
        }

        public GridPosition Origin { get; }

        public IReadOnlyList<GridPosition> FinishedTargets { get; }
    }
}
