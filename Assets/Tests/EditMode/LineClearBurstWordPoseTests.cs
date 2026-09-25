using MustyBlockBlast.Presentation.Views;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the combo word's bounce-in (issue #367 AC7): it springs in small and tilted, overshoots,
    /// settles at rest, then drifts up and fades out — and a higher tier bounces harder.
    /// </summary>
    public class LineClearBurstWordPoseTests
    {
        private const float TOLERANCE = 0.001f;

        [Test]
        public void WordPose_AtTheStart_IsSmallTiltedAndInvisible()
        {
            LineClearBurstView.WordPose(0f, 1f, out float scale, out float tilt, out float rise, out float alpha);

            Assert.AreEqual(0.15f, scale, TOLERANCE);
            Assert.AreEqual(-14f, tilt, TOLERANCE);
            Assert.AreEqual(0f, rise, TOLERANCE);
            Assert.AreEqual(0f, alpha, TOLERANCE);
        }

        [Test]
        public void WordPose_AtThePeak_OvershootsFullyVisible()
        {
            LineClearBurstView.WordPose(0.16f, 1f, out float scale, out _, out _, out float alpha);

            Assert.AreEqual(1.55f, scale, TOLERANCE);
            Assert.AreEqual(1f, alpha, TOLERANCE);
        }

        [Test]
        public void WordPose_AfterTheBounce_RestsUprightAtScaleOne()
        {
            LineClearBurstView.WordPose(0.54f, 1.15f, out float scale, out float tilt, out float rise, out float alpha);

            Assert.AreEqual(1f, scale, TOLERANCE);
            Assert.AreEqual(0f, tilt, TOLERANCE);
            Assert.AreEqual(0f, rise, TOLERANCE);
            Assert.AreEqual(1f, alpha, TOLERANCE);
        }

        [Test]
        public void WordPose_AtTheEnd_HasRisenAndFadedOut()
        {
            LineClearBurstView.WordPose(1f, 1f, out _, out _, out float rise, out float alpha);

            Assert.AreEqual(1f, rise, TOLERANCE);
            Assert.AreEqual(0f, alpha, TOLERANCE);
        }

        [Test]
        public void WordPose_WithAHigherBounce_OvershootsFurther()
        {
            LineClearBurstView.WordPose(0.16f, 1f, out float niceScale, out _, out _, out _);
            LineClearBurstView.WordPose(0.16f, 1.15f, out float amazingScale, out _, out _, out _);

            Assert.Greater(amazingScale, niceScale);
        }
    }
}
