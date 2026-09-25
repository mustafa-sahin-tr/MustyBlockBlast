using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// A precomputed, looping info-popup demo: a fixed set of <see cref="InfoDemoElement"/>s and a flat
    /// array of <see cref="InfoDemoStep"/>s sorted by start time. Built once by
    /// <see cref="InfoDemoTimelineBuilder"/>; immutable after that, so one instance can be cached and
    /// replayed forever.
    /// <para>
    /// Sampling is stateless: <see cref="Evaluate"/> resets every element to its initial state and
    /// replays every step that has started by the sampled time, so any time can be sampled in any order
    /// (a loop wrap is just sampling time 0 again) and a frame costs one pass over the arrays with zero
    /// allocations.
    /// </para>
    /// </summary>
    internal sealed class InfoDemoTimeline
    {
        private readonly InfoDemoElement[] _elements;
        private readonly InfoDemoStep[] _steps;

        internal InfoDemoTimeline(
            float duration, float fadeInDuration, float fadeOutDuration, InfoDemoElement[] elements, InfoDemoStep[] steps)
        {
            Duration = Mathf.Max(0.01f, duration);
            FadeInDuration = Mathf.Max(0f, fadeInDuration);
            FadeOutDuration = Mathf.Max(0f, fadeOutDuration);
            _elements = elements;
            _steps = steps;
        }

        /// <summary>Length of one loop, in seconds.</summary>
        internal float Duration { get; }

        /// <summary>Seconds the whole demo fades in over at the start of each loop.</summary>
        internal float FadeInDuration { get; }

        /// <summary>Seconds the whole demo fades out over at the end of each loop.</summary>
        internal float FadeOutDuration { get; }

        internal int ElementCount => _elements.Length;

        internal int StepCount => _steps.Length;

        internal InfoDemoElement GetElement(int elementId) => _elements[elementId];

        internal InfoDemoStep GetStep(int stepIndex) => _steps[stepIndex];

        /// <summary>
        /// Writes every element's state at loop time <paramref name="time"/> (wrapped into
        /// [0, <see cref="Duration"/>)) into <paramref name="states"/>, which must hold at least
        /// <see cref="ElementCount"/> entries. Allocation-free.
        /// </summary>
        internal void Evaluate(float time, InfoDemoElementState[] states)
        {
            float loopTime = WrapTime(time);

            for (int elementIndex = 0; elementIndex < _elements.Length; elementIndex++)
            {
                states[elementIndex] = _elements[elementIndex].Initial;
            }

            for (int stepIndex = 0; stepIndex < _steps.Length; stepIndex++)
            {
                InfoDemoStep step = _steps[stepIndex];

                // Sorted by start time, so the first step still in the future ends the pass.
                if (loopTime < step.StartTime)
                {
                    break;
                }

                float progress = step.Duration <= 0f ? 1f : (loopTime - step.StartTime) / step.Duration;
                float eased = InfoDemoEase.Evaluate(step.Easing, progress);
                Apply(ref states[step.ElementId], step, eased);
            }
        }

        /// <summary>The whole demo's opacity at <paramref name="time"/>: ramps up over
        /// <see cref="FadeInDuration"/>, holds at 1, ramps down over the last
        /// <see cref="FadeOutDuration"/> of the loop — so a restart never snaps.</summary>
        internal float LoopAlpha(float time)
        {
            float loopTime = WrapTime(time);
            float fadeIn = FadeInDuration > 0f ? loopTime / FadeInDuration : 1f;
            float fadeOut = FadeOutDuration > 0f ? (Duration - loopTime) / FadeOutDuration : 1f;
            return Mathf.Clamp01(Mathf.Min(fadeIn, fadeOut));
        }

        /// <summary><paramref name="time"/> wrapped into [0, <see cref="Duration"/>).</summary>
        internal float WrapTime(float time)
        {
            float wrapped = time % Duration;
            return wrapped < 0f ? wrapped + Duration : wrapped;
        }

        private static void Apply(ref InfoDemoElementState state, in InfoDemoStep step, float eased)
        {
            switch (step.Property)
            {
                case InfoDemoProperty.Alpha:
                    state.Alpha = Mathf.LerpUnclamped(step.From.x, step.To.x, eased);
                    break;
                case InfoDemoProperty.Scale:
                    state.Scale = Mathf.LerpUnclamped(step.From.x, step.To.x, eased);
                    break;
                case InfoDemoProperty.Stretch:
                    state.Stretch = new Vector2(
                        Mathf.LerpUnclamped(step.From.x, step.To.x, eased),
                        Mathf.LerpUnclamped(step.From.y, step.To.y, eased));
                    break;
                case InfoDemoProperty.Position:
                    state.Position = new Vector2(
                        Mathf.LerpUnclamped(step.From.x, step.To.x, eased),
                        Mathf.LerpUnclamped(step.From.y, step.To.y, eased));
                    break;
                case InfoDemoProperty.Rotation:
                    state.Rotation = Mathf.LerpUnclamped(step.From.x, step.To.x, eased);
                    break;
                case InfoDemoProperty.Flash:
                    state.Flash = Mathf.LerpUnclamped(step.From.x, step.To.x, eased);
                    break;
                case InfoDemoProperty.Paint:
                    state.Paint = Mathf.RoundToInt(step.To.x);
                    break;
            }
        }
    }
}
