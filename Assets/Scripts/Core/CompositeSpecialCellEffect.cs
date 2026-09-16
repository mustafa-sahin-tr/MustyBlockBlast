using System;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Presents several <see cref="ISpecialCellEffect"/> implementations to
    /// <see cref="CascadeClearResolver"/> as one.
    /// <para>
    /// The resolver takes a single effect on purpose — it sequences "clear, detect, apply, re-check"
    /// and has no business knowing how many kinds exist — so fanning one trigger out to every installed
    /// kind is this class's job rather than a second parameter on the loop. Each effect already ignores
    /// a trigger whose <see cref="SpecialCellTrigger.Kind"/> is not its own, so applying all of them to
    /// every trigger is exactly equivalent to dispatching on the kind, without this class having to
    /// carry a map of which kind belongs to which effect.
    /// </para>
    /// <para>
    /// Order is the order the effects were given in and is deliberately not something callers should
    /// depend on: two effects never react to the same kind, so at most one of them does anything to any
    /// given trigger.
    /// </para>
    /// </summary>
    public sealed class CompositeSpecialCellEffect : ISpecialCellEffect
    {
        private readonly ISpecialCellEffect[] _effects;

        public CompositeSpecialCellEffect(params ISpecialCellEffect[] effects)
        {
            if (effects == null)
            {
                throw new ArgumentNullException(nameof(effects));
            }

            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i] == null)
                {
                    throw new ArgumentException("An installed effect must not be null.", nameof(effects));
                }
            }

            _effects = effects;
        }

        public void Apply(Board board, SpecialCellTrigger trigger)
        {
            for (int i = 0; i < _effects.Length; i++)
            {
                _effects[i].Apply(board, trigger);
            }
        }
    }
}
