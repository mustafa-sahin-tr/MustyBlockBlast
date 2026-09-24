using MustyBlockBlast.Presentation.Views;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Pins the objective demos' progress chip (issue #447): its parts at loop start, the counter
    /// crossfade and check-badge pop that <see cref="InfoDemoChoreography.AdvanceChip"/> queues, a chip
    /// that is not yet at its target keeping its badge hidden, and the right-zone tray slots beside it.
    /// </summary>
    public class InfoDemoProgressChipTests
    {
        private const float TOLERANCE = 0.0001f;
        private const string CAPTION_KEY = "test.chip.caption";

        private static InfoDemoElementState[] Sample(InfoDemoTimeline timeline, float time)
        {
            InfoDemoElementState[] states = new InfoDemoElementState[timeline.ElementCount];
            timeline.Evaluate(time, states);
            return states;
        }

        [Test]
        public void ProgressChip_AtLoopStart_ShowsChipCaptionAndStartCounter_BadgeHidden()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            InfoDemoProgressChip chip = InfoDemoChoreography.ProgressChip(builder, CAPTION_KEY, "2", 0, 1);
            InfoDemoTimeline timeline = builder.Build();
            InfoDemoElementState[] states = Sample(timeline, 0f);

            Assert.AreEqual(InfoDemoElementKind.Panel, timeline.GetElement(chip.PanelId).Kind);
            Assert.AreEqual(InfoDemoPaint.WHITE, states[chip.PanelId].Paint);
            Assert.AreEqual(1f, states[chip.PanelId].Alpha, TOLERANCE);
            Assert.AreEqual(InfoDemoLayout.ChipCentre, states[chip.PanelId].Position);

            InfoDemoElement caption = timeline.GetElement(chip.CaptionId);
            Assert.AreEqual(CAPTION_KEY, caption.LabelKey);
            Assert.AreEqual("2", caption.LabelArgument);
            Assert.AreEqual(1f, states[chip.CaptionId].Alpha, TOLERANCE);

            InfoDemoElement startCounter = timeline.GetElement(chip.CounterLabelId(0));
            InfoDemoElement endCounter = timeline.GetElement(chip.CounterLabelId(1));
            Assert.IsNull(startCounter.LabelKey, "a counter is literal text");
            Assert.AreEqual("0/1", startCounter.LabelArgument);
            Assert.AreEqual("1/1", endCounter.LabelArgument);
            Assert.AreEqual(1f, states[chip.CounterLabelId(0)].Alpha, TOLERANCE);
            Assert.AreEqual(0f, states[chip.CounterLabelId(1)].Alpha, TOLERANCE);

            Assert.AreEqual(0f, states[chip.BadgeId].Alpha, TOLERANCE);
            Assert.AreEqual(InfoDemoPaint.SUCCESS, states[chip.BadgeId].Paint);
            Assert.AreEqual(0f, states[chip.CheckId].Alpha, TOLERANCE);
            Assert.AreEqual(InfoDemoSprite.CheckMark, timeline.GetElement(chip.CheckId).Sprite);

            // The badge sits on the chip's top-right corner.
            Assert.Greater(states[chip.BadgeId].Position.x, InfoDemoLayout.ChipCentre.x);
            Assert.Less(states[chip.BadgeId].Position.y, InfoDemoLayout.ChipCentre.y);
        }

        [Test]
        public void AdvanceChip_ToTarget_CrossfadesTheCounter_AndPopsTheCheckBadge()
        {
            const float advanceTime = 1.75f;
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            InfoDemoProgressChip chip = InfoDemoChoreography.ProgressChip(builder, CAPTION_KEY, "2", 0, 1);
            float settled = InfoDemoChoreography.AdvanceChip(builder, chip, advanceTime);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(1, chip.Value);
            Assert.IsTrue(chip.IsComplete);
            Assert.Greater(settled, advanceTime);

            InfoDemoElementState[] before = Sample(timeline, advanceTime - 0.01f);
            Assert.AreEqual(1f, before[chip.CounterLabelId(0)].Alpha, TOLERANCE);
            Assert.AreEqual(0f, before[chip.CounterLabelId(1)].Alpha, TOLERANCE);
            Assert.AreEqual(0f, before[chip.BadgeId].Alpha, TOLERANCE);

            InfoDemoElementState[] midway = Sample(timeline, advanceTime + (InfoDemoChoreography.CHIP_COUNTER_CROSSFADE * 0.5f));
            Assert.That(midway[chip.CounterLabelId(0)].Alpha, Is.InRange(0.01f, 0.99f));
            Assert.That(midway[chip.CounterLabelId(1)].Alpha, Is.InRange(0.01f, 0.99f));

            InfoDemoElementState[] after = Sample(timeline, settled + 0.01f);
            Assert.AreEqual(0f, after[chip.CounterLabelId(0)].Alpha, TOLERANCE);
            Assert.AreEqual(1f, after[chip.CounterLabelId(1)].Alpha, TOLERANCE);
            Assert.AreEqual(1f, after[chip.CounterLabelId(1)].Scale, TOLERANCE);
            Assert.AreEqual(1f, after[chip.BadgeId].Alpha, TOLERANCE);
            Assert.AreEqual(1f, after[chip.BadgeId].Scale, TOLERANCE);
            Assert.AreEqual(1f, after[chip.CheckId].Alpha, TOLERANCE);
            Assert.AreEqual(1f, after[chip.CheckId].Scale, TOLERANCE);
        }

        [Test]
        public void AdvanceChip_ShortOfTarget_KeepsTheBadgeHidden()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            InfoDemoProgressChip chip = InfoDemoChoreography.ProgressChip(builder, CAPTION_KEY, null, 0, 3);
            InfoDemoChoreography.AdvanceChip(builder, chip, 1f);
            InfoDemoTimeline timeline = builder.Build();
            InfoDemoElementState[] states = Sample(timeline, 4f);

            Assert.AreEqual(1, chip.Value);
            Assert.IsFalse(chip.IsComplete);
            Assert.AreEqual("1/3", timeline.GetElement(chip.CounterLabelId(1)).LabelArgument);
            Assert.AreEqual(1f, states[chip.CounterLabelId(1)].Alpha, TOLERANCE);
            Assert.AreEqual(0f, states[chip.CounterLabelId(0)].Alpha, TOLERANCE);
            Assert.AreEqual(0f, states[chip.CounterLabelId(2)].Alpha, TOLERANCE);
            Assert.AreEqual(0f, states[chip.BadgeId].Alpha, TOLERANCE);
            Assert.AreEqual(0f, states[chip.CheckId].Alpha, TOLERANCE);
        }

        [Test]
        public void AdvanceChip_OnACompleteChip_QueuesNothing()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            InfoDemoProgressChip chip = InfoDemoChoreography.ProgressChip(builder, CAPTION_KEY, null, 0, 1);
            InfoDemoChoreography.AdvanceChip(builder, chip, 1f);
            int stepsAfterFirst = builder.Build().StepCount;

            float returned = InfoDemoChoreography.AdvanceChip(builder, chip, 2f);

            Assert.AreEqual(2f, returned, TOLERANCE);
            Assert.AreEqual(stepsAfterFirst, builder.Build().StepCount);
            Assert.AreEqual(1, chip.Value);
        }

        [Test]
        public void ChipLeftTraySlots_SitInTheStripsRightZone_AtTheMockupCentres()
        {
            float[] expectedMockX = { 124f, 176f, 226f };
            for (int slotIndex = 0; slotIndex < expectedMockX.Length; slotIndex++)
            {
                Vector2 mock = InfoDemoLayout.ToMockPoint(InfoDemoLayout.TraySlot(slotIndex, InfoDemoTrayLayout.ChipLeft));
                Assert.AreEqual(expectedMockX[slotIndex], mock.x, 0.001f);
                Assert.AreEqual(InfoDemoLayout.MOCK_STRIP_CENTRE_Y, mock.y, 0.001f);
            }

            // The plain layout is unchanged by the option.
            Assert.AreEqual(
                InfoDemoLayout.TraySlot(1), InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ThreeSlots));

            Vector2 chipMock = InfoDemoLayout.ToMockPoint(InfoDemoLayout.ChipCentre);
            Assert.AreEqual(InfoDemoLayout.MOCK_CHIP_CENTRE_X, chipMock.x, 0.001f);
            Assert.AreEqual(InfoDemoLayout.MOCK_STRIP_CENTRE_Y, chipMock.y, 0.001f);
        }
    }
}
