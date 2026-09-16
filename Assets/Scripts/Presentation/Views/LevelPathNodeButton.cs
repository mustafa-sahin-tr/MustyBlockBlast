using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// One tap target inside <see cref="LevelPathPanelView"/> — a level node, the close cross or the
    /// scrim behind the card — forwarding its click to a callback the panel sets when it builds the
    /// widget.
    /// <para>
    /// This is the one place in the project that takes taps from an EventSystem rather than from
    /// <see cref="BoardInputView"/>'s manual hit-testing, and it has to be: the level trail lives in a
    /// ScrollRect, so telling a tap from the first frame of a drag means honouring the drag threshold,
    /// the pointer-down/up pairing and the "the finger left the widget" cancel. uGUI already does all
    /// three and gets them right; hand-rolling them against a raw pointer action would be a quiet
    /// source of nodes that start a level when the player only meant to scroll.
    /// </para>
    /// <para>
    /// Only <c>IPointerClickHandler</c> is implemented, deliberately: drag events stay unhandled and so
    /// bubble to the enclosing ScrollRect, which is what lets a drag begun on a node still scroll the
    /// trail.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class LevelPathNodeButton : MonoBehaviour, IPointerClickHandler
    {
        private Action _clicked;

        /// <summary>Sets the one callback this target fires. Called once per widget at build time.</summary>
        internal void SetClicked(Action clicked) => _clicked = clicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_clicked == null)
            {
                return;
            }

            _clicked();
        }
    }
}
