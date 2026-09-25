using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The skin-specific break a Classic skin's cell plays when a line clears it (issue #333): jelly
    /// bursts into droplets of its own colour, a fruit is sliced into two halves that fall apart with juice
    /// of its own colour, wood cracks into spinning splinters, stone crumbles into rubble, a crystal
    /// shatters into shards. Fire-and-forget, parented to the board's cell layer next to the
    /// cell and never tied to the cell's own fade, exactly like BoardView's shatter shards. Creates a few
    /// short-lived images per cleared cell — an event, never per frame.
    /// </summary>
    internal static class SkinClearFx
    {
        private const float DURATION = 0.6f;
        private const int JELLY_DROPLETS = 7;
        private const int JUICE_DROPLETS = 5;
        private const int SPLINTERS = 6;
        private const int RUBBLE = 6;
        private const int SHARDS = 7;

        private static readonly Color WoodColour = new Color(0.62f, 0.38f, 0.18f, 1f);

        internal static async UniTaskVoid PlayAsync(
            RectTransform parent,
            Vector2 centre,
            float cellSize,
            ClassicSkinClearEffect effect,
            Sprite sprite,
            Color tint,
            Color particleColour,
            CancellationToken token)
        {
            if (parent == null || effect == ClassicSkinClearEffect.None)
            {
                return;
            }

            int count;
            switch (effect)
            {
                case ClassicSkinClearEffect.JellySplat:
                    count = JELLY_DROPLETS;
                    break;
                case ClassicSkinClearEffect.FruitSlice:
                    count = 2 + JUICE_DROPLETS;
                    break;
                case ClassicSkinClearEffect.StoneCrumble:
                    count = RUBBLE;
                    break;
                case ClassicSkinClearEffect.CrystalShatter:
                    count = SHARDS;
                    break;
                default:
                    count = SPLINTERS;
                    break;
            }

            var rects = new RectTransform[count];
            var images = new Image[count];
            var directions = new Vector2[count];
            var spins = new float[count];

            for (int index = 0; index < count; index++)
            {
                BuildParticle(
                    parent, centre, cellSize, effect, sprite, tint, particleColour, index, out rects[index], out images[index]);
                float angle = ((index + 0.5f) / count) * Mathf.PI * 2f;
                directions[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f + 0.4f);
                spins[index] = (index % 2 == 0 ? 1f : -1f) * SpinFor(effect);
            }

            if (effect == ClassicSkinClearEffect.FruitSlice)
            {
                // The two halves part left and right; the juice sprays from the cut.
                directions[0] = new Vector2(-0.45f, 0.35f);
                directions[1] = new Vector2(0.45f, 0.35f);
                spins[0] = -30f;
                spins[1] = 30f;
            }

            try
            {
                float elapsed = 0f;
                while (elapsed < DURATION)
                {
                    float t = elapsed / DURATION;
                    float travel = cellSize * (1f - ((1f - t) * (1f - t)));
                    float fall = cellSize * 0.9f * t * t;
                    float alpha = t < 0.5f ? 1f : 1f - ((t - 0.5f) / 0.5f);

                    for (int index = 0; index < count; index++)
                    {
                        float reach = IsHalf(effect, index) ? 0.45f : 0.75f;
                        rects[index].anchoredPosition = centre + (directions[index] * travel * reach) + new Vector2(0f, -fall);
                        rects[index].localRotation = Quaternion.Euler(0f, 0f, spins[index] * t);
                        Color colour = images[index].color;
                        colour.a = alpha;
                        images[index].color = colour;
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            catch (OperationCanceledException)
            {
                // The board went away mid-break; the particles go with it below.
            }
            finally
            {
                for (int index = 0; index < count; index++)
                {
                    if (rects[index] != null)
                    {
                        UnityEngine.Object.Destroy(rects[index].gameObject);
                    }
                }
            }
        }

        /// <summary>Degrees per clear a particle turns: splinters and shards whirl, rubble tumbles, drops
        /// barely turn at all.</summary>
        private static float SpinFor(ClassicSkinClearEffect effect)
        {
            switch (effect)
            {
                case ClassicSkinClearEffect.WoodSplinters:
                    return 540f;
                case ClassicSkinClearEffect.CrystalShatter:
                    return 420f;
                case ClassicSkinClearEffect.StoneCrumble:
                    return 180f;
                default:
                    return 60f;
            }
        }

        private static bool IsHalf(ClassicSkinClearEffect effect, int index)
            => effect == ClassicSkinClearEffect.FruitSlice && index < 2;

        private static void BuildParticle(
            RectTransform parent,
            Vector2 centre,
            float cellSize,
            ClassicSkinClearEffect effect,
            Sprite sprite,
            Color tint,
            Color particleColour,
            int index,
            out RectTransform rect,
            out Image image)
        {
            var particle = new GameObject("SkinClearParticle", typeof(RectTransform), typeof(Image));
            rect = (RectTransform)particle.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = centre;
            image = particle.GetComponent<Image>();
            image.raycastTarget = false;

            if (IsHalf(effect, index))
            {
                // One half of the fruit: the skin art itself, filled from its own side only.
                rect.sizeDelta = new Vector2(cellSize, cellSize);
                image.sprite = sprite;
                image.preserveAspect = true;
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = index == 0 ? (int)Image.OriginHorizontal.Left : (int)Image.OriginHorizontal.Right;
                image.fillAmount = 0.5f;
                image.color = Color.white;
                return;
            }

            if (effect == ClassicSkinClearEffect.StoneCrumble)
            {
                // Chunky, uneven rubble in the stone's own greys.
                float chunk = cellSize * (0.2f - ((index % 3) * 0.04f));
                rect.sizeDelta = new Vector2(chunk, chunk * 0.85f);
                image.sprite = UiSpriteFactory.RoundedSquare;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 8f;
                image.color = Color.Lerp(particleColour, Color.black, (index % 3) * 0.15f);
                return;
            }

            if (effect == ClassicSkinClearEffect.CrystalShatter)
            {
                // Sharp facets in the crystal's own (block) colour, a little lighter so they glint.
                float shard = cellSize * (0.24f - ((index % 3) * 0.05f));
                rect.sizeDelta = new Vector2(shard, shard);
                image.sprite = UiSpriteFactory.TriangleFacet;
                image.type = Image.Type.Simple;
                image.color = Color.Lerp(tint, Color.white, 0.35f + ((index % 2) * 0.2f));
                return;
            }

            if (effect == ClassicSkinClearEffect.WoodSplinters)
            {
                rect.sizeDelta = new Vector2(cellSize * 0.36f, cellSize * 0.09f);
                image.sprite = UiSpriteFactory.RoundedSquare;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 6f;
                image.color = Color.Lerp(WoodColour, Color.black, (index % 3) * 0.12f);
                return;
            }

            float size = cellSize * (effect == ClassicSkinClearEffect.JellySplat ? 0.2f : 0.13f) * (1f - ((index % 3) * 0.15f));
            rect.sizeDelta = new Vector2(size, size);
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            image.color = effect == ClassicSkinClearEffect.JellySplat ? Color.Lerp(tint, Color.white, 0.15f) : particleColour;
        }
    }
}
