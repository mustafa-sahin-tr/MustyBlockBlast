using MustyBlockBlast.Presentation.Views;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Pins the info-popup demo timeline's sampling (issue #446): initial state, start/mid/end values
    /// of a step, easing, hold-after-end, later-step override, instant paint switches, loop wrap, and
    /// the loop fade envelope.
    /// </summary>
    public class InfoDemoTimelineTests
    {
        private const float TOLERANCE = 0.0001f;

        private static InfoDemoElementState[] Sample(InfoDemoTimeline timeline, float time)
        {
            InfoDemoElementState[] states = new InfoDemoElementState[timeline.ElementCount];
            timeline.Evaluate(time, states);
            return states;
        }

        [Test]
        public void Builder_AlwaysCreatesSixtyFourEmptyBoardBlocksFirst()
        {
            InfoDemoTimeline timeline = new InfoDemoTimelineBuilder(2f).Build();

            Assert.AreEqual(InfoDemoLayout.BOARD_CELL_COUNT, timeline.ElementCount);
            for (int elementIndex = 0; elementIndex < timeline.ElementCount; elementIndex++)
            {
                Assert.AreEqual(InfoDemoElementKind.BoardBlock, timeline.GetElement(elementIndex).Kind);
                Assert.AreEqual(InfoDemoPaint.NONE, timeline.GetElement(elementIndex).Initial.Paint);
            }

            InfoDemoElementState[] states = Sample(timeline, 0f);
            Assert.AreEqual(InfoDemoLayout.Cell(3, 5), states[InfoDemoLayout.BoardBlockId(3, 5)].Position);
        }

        [Test]
        public void SetBoardRow_PaintsCellsFromThePattern()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(2f);
            builder.SetBoardRow(7, "pg.ub..v");
            InfoDemoElementState[] states = Sample(builder.Build(), 0f);

            Assert.AreEqual(InfoDemoPaint.BLOCK_1, states[InfoDemoLayout.BoardBlockId(7, 0)].Paint);
            Assert.AreEqual(InfoDemoPaint.BLOCK_2, states[InfoDemoLayout.BoardBlockId(7, 1)].Paint);
            Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(7, 2)].Paint);
            Assert.AreEqual(InfoDemoPaint.BLOCK_3, states[InfoDemoLayout.BoardBlockId(7, 3)].Paint);
            Assert.AreEqual(InfoDemoPaint.BLOCK_5, states[InfoDemoLayout.BoardBlockId(7, 4)].Paint);
            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, states[InfoDemoLayout.BoardBlockId(7, 7)].Paint);
        }

        [Test]
        public void LinearStep_SamplesInitialBeforeStart_FromAtStart_MidpointMidway_ToAtAndAfterEnd()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(4f);
            int glow = builder.AddGlow(Vector2.zero, 1f, InfoDemoPaint.WHITE, 0.2f);
            builder.Fade(glow, 1f, 2f, 0f, 1f);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(0.2f, Sample(timeline, 0.5f)[glow].Alpha, TOLERANCE, "before start: initial");
            Assert.AreEqual(0f, Sample(timeline, 1f)[glow].Alpha, TOLERANCE, "at start: from");
            Assert.AreEqual(0.5f, Sample(timeline, 2f)[glow].Alpha, TOLERANCE, "midway: halfway");
            Assert.AreEqual(1f, Sample(timeline, 3f)[glow].Alpha, TOLERANCE, "at end: to");
            Assert.AreEqual(1f, Sample(timeline, 3.9f)[glow].Alpha, TOLERANCE, "after end: holds to");
        }

        [Test]
        public void EasedStep_IsSampledThroughItsCurve()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(4f);
            int glow = builder.AddGlow(Vector2.zero, 1f, InfoDemoPaint.WHITE);
            builder.Scale(glow, 0f, 2f, 0f, 1f, InfoDemoEasing.EaseOutCubic);
            InfoDemoTimeline timeline = builder.Build();

            // EaseOutCubic(0.5) = 1 - 0.5^3 = 0.875.
            Assert.AreEqual(0.875f, Sample(timeline, 1f)[glow].Scale, TOLERANCE);
        }

        [Test]
        public void Easings_StartAtZero_AndEndAtOne_ExceptPulseWhichReturnsToZero()
        {
            InfoDemoEasing[] endAtOne =
            {
                InfoDemoEasing.Linear, InfoDemoEasing.EaseInCubic, InfoDemoEasing.EaseOutCubic,
                InfoDemoEasing.EaseInOutCubic, InfoDemoEasing.EaseOutBack,
            };

            for (int easingIndex = 0; easingIndex < endAtOne.Length; easingIndex++)
            {
                Assert.AreEqual(0f, InfoDemoEase.Evaluate(endAtOne[easingIndex], 0f), TOLERANCE, endAtOne[easingIndex].ToString());
                Assert.AreEqual(1f, InfoDemoEase.Evaluate(endAtOne[easingIndex], 1f), TOLERANCE, endAtOne[easingIndex].ToString());
            }

            Assert.AreEqual(0.5f, InfoDemoEase.Evaluate(InfoDemoEasing.EaseInOutCubic, 0.5f), TOLERANCE);
            Assert.AreEqual(0.125f, InfoDemoEase.Evaluate(InfoDemoEasing.EaseInCubic, 0.5f), TOLERANCE);
            Assert.Greater(InfoDemoEase.Evaluate(InfoDemoEasing.EaseOutBack, 0.8f), 1f, "EaseOutBack overshoots");
            Assert.AreEqual(1f, InfoDemoEase.Evaluate(InfoDemoEasing.Pulse, 0.5f), TOLERANCE);
            Assert.AreEqual(0f, InfoDemoEase.Evaluate(InfoDemoEasing.Pulse, 1f), TOLERANCE);
        }

        [Test]
        public void PositionStep_InterpolatesBothAxes()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(2f);
            int piece = builder.AddPiece(new[] { Vector2Int.zero }, InfoDemoPaint.BLOCK_1, new Vector2(1f, 9f), 0.5f);
            builder.Move(piece, 0f, 1f, new Vector2(1f, 9f), new Vector2(5f, 7f));
            InfoDemoTimeline timeline = builder.Build();

            Vector2 midway = Sample(timeline, 0.5f)[piece].Position;
            Assert.AreEqual(3f, midway.x, TOLERANCE);
            Assert.AreEqual(8f, midway.y, TOLERANCE);
            Assert.AreEqual(0.5f, Sample(timeline, 0.5f)[piece].Scale, TOLERANCE, "untouched property keeps its initial value");
        }

        [Test]
        public void LaterStep_OnTheSameProperty_OverridesOnceStarted_RegardlessOfQueueOrder()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(4f);
            int glow = builder.AddGlow(Vector2.zero, 1f, InfoDemoPaint.WHITE);

            // Queued out of order on purpose: Build sorts by start time.
            builder.Fade(glow, 2f, 1f, 1f, 0f);
            builder.Fade(glow, 0f, 1f, 0f, 1f);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(1f, Sample(timeline, 1.5f)[glow].Alpha, TOLERANCE, "first step held");
            Assert.AreEqual(0.5f, Sample(timeline, 2.5f)[glow].Alpha, TOLERANCE, "second step took over");
            Assert.LessOrEqual(timeline.GetStep(0).StartTime, timeline.GetStep(1).StartTime);
        }

        [Test]
        public void PaintStep_SwitchesInstantlyAtItsStartTime()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(4f);
            int blockId = InfoDemoLayout.BoardBlockId(2, 2);
            builder.Paint(blockId, 1f, InfoDemoPaint.VORTEX_BLOCK);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(InfoDemoPaint.NONE, Sample(timeline, 0.999f)[blockId].Paint);
            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, Sample(timeline, 1f)[blockId].Paint);
        }

        [Test]
        public void Sampling_WrapsAtTheLoopDuration_BackToTheInitialState()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(4f);
            int glow = builder.AddGlow(Vector2.zero, 1f, InfoDemoPaint.WHITE, 0.3f);
            builder.Fade(glow, 1f, 2f, 0f, 1f);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(0.3f, Sample(timeline, 4.5f)[glow].Alpha, TOLERANCE, "4.5s is 0.5s into the next loop");
            Assert.AreEqual(0.5f, Sample(timeline, 6f)[glow].Alpha, TOLERANCE, "6s is 2s into the next loop");
            Assert.AreEqual(0.5f, timeline.WrapTime(4.5f), TOLERANCE);
            Assert.AreEqual(3.5f, timeline.WrapTime(-0.5f), TOLERANCE);
        }

        [Test]
        public void Sampling_IsStateless_SoOutOfOrderSamplesAgree()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(4f);
            int glow = builder.AddGlow(Vector2.zero, 1f, InfoDemoPaint.WHITE);
            builder.Fade(glow, 0f, 2f, 0f, 1f);
            InfoDemoTimeline timeline = builder.Build();

            InfoDemoElementState[] states = new InfoDemoElementState[timeline.ElementCount];
            timeline.Evaluate(3f, states);
            timeline.Evaluate(1f, states);
            Assert.AreEqual(0.5f, states[glow].Alpha, TOLERANCE);
        }

        [Test]
        public void LoopAlpha_FadesInAtTheStart_HoldsOne_AndFadesOutAtTheEnd()
        {
            InfoDemoTimeline timeline = new InfoDemoTimelineBuilder(5f, 0.25f, 0.5f).Build();

            Assert.AreEqual(0f, timeline.LoopAlpha(0f), TOLERANCE);
            Assert.AreEqual(0.5f, timeline.LoopAlpha(0.125f), TOLERANCE);
            Assert.AreEqual(1f, timeline.LoopAlpha(2.5f), TOLERANCE);
            Assert.AreEqual(0.5f, timeline.LoopAlpha(4.75f), TOLERANCE);
            Assert.AreEqual(1f, timeline.LoopAlpha(5.25f + 0.25f), TOLERANCE, "wraps into the next loop");
        }
    }
}
