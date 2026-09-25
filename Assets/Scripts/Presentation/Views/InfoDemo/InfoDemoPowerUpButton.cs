using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Authoring handle for a power-up demo's strip button (issue #448), returned by
    /// <see cref="InfoDemoPowerUpChoreography.PowerUpButton"/>: the element ids of the button's parts and
    /// its centre. Used only while a timeline is being built and never touched at play time.
    /// </summary>
    internal sealed class InfoDemoPowerUpButton
    {
        internal InfoDemoPowerUpButton(
            Vector2 centre,
            int shadowId,
            int armedGlowId,
            int armedRingId,
            int plateId,
            int iconId,
            int countLabelId,
            int spentCountLabelId)
        {
            Centre = centre;
            ShadowId = shadowId;
            ArmedGlowId = armedGlowId;
            ArmedRingId = armedRingId;
            PlateId = plateId;
            IconId = iconId;
            CountLabelId = countLabelId;
            SpentCountLabelId = spentCountLabelId;
        }

        /// <summary>The button's centre in board units — where a tap on it lands.</summary>
        internal Vector2 Centre { get; }

        internal int ShadowId { get; }

        /// <summary>The soft yellow glow behind the plate while armed.</summary>
        internal int ArmedGlowId { get; }

        /// <summary>The yellow ring around the plate while armed.</summary>
        internal int ArmedRingId { get; }

        internal int PlateId { get; }

        /// <summary>The power-up's own icon on the plate.</summary>
        internal int IconId { get; }

        /// <summary>The count badge's number before the charge is spent.</summary>
        internal int CountLabelId { get; }

        /// <summary>The count badge's number after one charge is spent (hidden until then).</summary>
        internal int SpentCountLabelId { get; }
    }
}
