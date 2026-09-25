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

            public string Name => _name;

            public int ScoreThreshold => _scoreThreshold;

            public Sprite Sprite => _sprite;

            public bool TintByBlockColour => _tintByBlockColour;

            public ClassicSkinClearEffect ClearEffect => _clearEffect;
        }

        [SerializeField] private List<Stage> _stages = new List<Stage>();

        /// <summary>The stages in order. Never null.</summary>
        public IReadOnlyList<Stage> Stages => _stages ?? (IReadOnlyList<Stage>)Array.Empty<Stage>();

        /// <summary>The stage a run at <paramref name="score"/> is on: the last one whose threshold it has
        /// reached, or 0 when none has one.</summary>
        public int StageIndexFor(int score)
        {
            IReadOnlyList<Stage> stages = Stages;
            int stageIndex = 0;
            for (int index = 0; index < stages.Count; index++)
            {
                if (stages[index] != null && score >= stages[index].ScoreThreshold)
                {
                    stageIndex = index;
                }
            }

            return stageIndex;
        }

        /// <summary>The stage at <paramref name="stageIndex"/>, or null when there is none.</summary>
        public Stage StageAt(int stageIndex)
        {
            IReadOnlyList<Stage> stages = Stages;
            return stageIndex >= 0 && stageIndex < stages.Count ? stages[stageIndex] : null;
        }
    }
}
