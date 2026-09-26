using System;
using System.Collections.Generic;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The Classic-mode skin sequence (issue #333): an ordered list of stages, each entered once the run's
    /// score reaches its threshold. Stage 0 is the plain colour blocks every run starts on. Purely cosmetic
    /// — a skin never changes a rule. Only Classic (Timed) runs walk the sequence; Path and Şölen keep the
    /// colour blocks.
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Classic Skin Config", fileName = "ClassicSkinConfig")]
    public sealed class ClassicSkinConfig : ScriptableObject
    {
        [Serializable]
        public sealed class Stage
        {
            [Tooltip("Name for designers only.")]
            [SerializeField] private string _name = "Colours";

            [Tooltip("Run score at which the board converts to this stage's skin. Stages must be in "
                + "ascending order; the first (the colour blocks) should be 0.")]
            [SerializeField] private int _scoreThreshold;

            [Tooltip("The art every ordinary block is drawn with at this stage. Empty = the plain colour blocks.")]
            [SerializeField] private Sprite _sprite;

            [Tooltip("Tint the art in each block's own colour (a neutral jelly becomes a red, blue… jelly). "
                + "Off for art that carries its own colours (fruit, wood).")]
            [SerializeField] private bool _tintByBlockColour;

            [Tooltip("How this skin's cells break when a line clears them.")]
            [SerializeField] private ClassicSkinClearEffect _clearEffect = ClassicSkinClearEffect.None;

            [Tooltip("The sound a line clear makes while the board wears this skin — jelly squish, fruit "
                + "slice, wood crack. Empty = the ordinary line-clear sound.")]
            [SerializeField] private AudioClip _clearSound;

            [Tooltip("Colour of the break's particles that are not the skin's own tint: a fruit's juice, a "
                + "stone's rubble. Jelly and crystal use the block's colour instead.")]
            [SerializeField] private Color _particleColour = new Color(0.86f, 0.12f, 0.22f, 1f);

            [Tooltip("Colour of the thin border every block wears in this skin (issue #518). A skin tinted "
                + "by block colour multiplies this by the block's fill, so white borders each block in its "
                + "own colour.")]
            [SerializeField] private Color _borderColour = Color.white;

            public string Name => _name;

            public int ScoreThreshold => _scoreThreshold;

            public Sprite Sprite => _sprite;

            public bool TintByBlockColour => _tintByBlockColour;

            public ClassicSkinClearEffect ClearEffect => _clearEffect;

            public AudioClip ClearSound => _clearSound;

            public Color ParticleColour => _particleColour;

            public Color BorderColour => _borderColour;
        }

        [SerializeField] private List<Stage> _stages = new List<Stage>();

        [Tooltip("Past the last stage the sequence starts over from stage 1 (skipping the colour blocks), "
            + "one more skin every this many points, for as long as the run lasts. 0 = stop at the last stage.")]
        [SerializeField] private int _loopStepPoints = 250;

        /// <summary>The stages in order. Never null.</summary>
        public IReadOnlyList<Stage> Stages => _stages ?? (IReadOnlyList<Stage>)Array.Empty<Stage>();

        /// <summary>
        /// Where a run at <paramref name="score"/> is in the sequence. Within the authored list it is the
        /// last stage whose threshold the score has reached; past the last one it keeps counting, one step
        /// per <see cref="_loopStepPoints"/>, so the skins cycle for as long as the run lasts. It only ever
        /// grows with the score. Map it to a stage with <see cref="StageAt"/>.
        /// </summary>
        public int StageIndexFor(int score)
        {
            IReadOnlyList<Stage> stages = Stages;
            int position = 0;
            for (int index = 0; index < stages.Count; index++)
            {
                if (stages[index] != null && score >= stages[index].ScoreThreshold)
                {
                    position = index;
                }
            }

            int last = stages.Count - 1;
            if (last < 2 || position < last || _loopStepPoints <= 0 || stages[last] == null)
            {
                return position;
            }

            return last + ((score - stages[last].ScoreThreshold) / _loopStepPoints);
        }

        /// <summary>The stage at sequence position <paramref name="position"/> (see
        /// <see cref="StageIndexFor"/>): the authored stage itself within the list, and past its end the
        /// list again from stage 1 — the colour blocks never come back mid-run. Null when there is none.</summary>
        public Stage StageAt(int position)
        {
            IReadOnlyList<Stage> stages = Stages;
            if (position < 0 || stages.Count == 0)
            {
                return null;
            }

            if (position < stages.Count)
            {
                return stages[position];
            }

            int loopLength = stages.Count - 1;
            return loopLength > 0 ? stages[1 + ((position - 1) % loopLength)] : null;
        }
    }
}
