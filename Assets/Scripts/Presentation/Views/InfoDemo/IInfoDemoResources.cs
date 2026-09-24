using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// What an <see cref="InfoDemoStage"/> needs from the View hosting it: the game's own authored
    /// sprites (so a demo never re-authors an icon), translated label text, and the real board's cell
    /// proportions (so demo blocks bevel exactly like board blocks). Deliberately nothing else — no
    /// board, run or gameplay state is reachable through it (issue #445 AC5).
    /// </summary>
    internal interface IInfoDemoResources
    {
        /// <summary>Resolves an <see cref="InfoDemoElementKind.Icon"/>'s sprite and resting tint.
        /// False when the View has no sprite for it; the icon is then hidden.</summary>
        bool TryGetSprite(InfoDemoSprite sprite, int parameter, out Sprite resolved, out Color tint);

        string Translate(string localizationKey);

        /// <summary>The real board's cell size, face inset and bevel thickness, in reference pixels.</summary>
        void GetBoardCellMetrics(out float cellSize, out float inset, out float bevelThickness);
    }
}
