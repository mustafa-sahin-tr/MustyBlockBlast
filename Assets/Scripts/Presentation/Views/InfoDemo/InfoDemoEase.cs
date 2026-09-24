using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>The easing curves of <see cref="InfoDemoEasing"/>, as allocation-free pure functions
    /// of a normalised time. The same hand-rolled idiom <c>PowerUpGrantAnimationView.EaseOutCubic</c>
    /// uses — no tween package.</summary>
    internal static class InfoDemoEase
    {
        /// <summary>How far <see cref="InfoDemoEasing.EaseOutBack"/> overshoots — the standard
        /// "back" constant, a gentle ~10% overshoot.</summary>
        private const float BACK_OVERSHOOT = 1.70158f;

        /// <summary>Maps <paramref name="t"/> (clamped to 0..1) through <paramref name="easing"/>.
        /// Every curve returns 0 at 0; every curve but <see cref="InfoDemoEasing.Pulse"/> returns 1 at
        /// 1 (a pulse returns to 0).</summary>
        internal static float Evaluate(InfoDemoEasing easing, float t)
        {
            float clamped = Mathf.Clamp01(t);

            switch (easing)
            {
                case InfoDemoEasing.EaseInCubic:
                    return clamped * clamped * clamped;
                case InfoDemoEasing.EaseOutCubic:
                {
                    float inverse = 1f - clamped;
                    return 1f - (inverse * inverse * inverse);
                }
                case InfoDemoEasing.EaseInOutCubic:
                {
                    if (clamped < 0.5f)
                    {
                        return 4f * clamped * clamped * clamped;
                    }

                    float shifted = (-2f * clamped) + 2f;
                    return 1f - ((shifted * shifted * shifted) * 0.5f);
                }
                case InfoDemoEasing.EaseOutBack:
                {
                    float shifted = clamped - 1f;
                    float c3 = BACK_OVERSHOOT + 1f;
                    return 1f + (c3 * shifted * shifted * shifted) + (BACK_OVERSHOOT * shifted * shifted);
                }
                case InfoDemoEasing.Pulse:
                    return Mathf.Sin(clamped * Mathf.PI);
                default:
                    return clamped;
            }
        }
    }
}
