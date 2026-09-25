namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// A rewarded ad was just put on screen and closed again (issue #502) — whether or not it paid out,
    /// since what matters to the listener is that the player sat through an ad. Published by the ad
    /// source that showed it; <c>InterstitialFrequencySystem</c> listens so that a player who has just
    /// watched a rewarded ad is never handed an interstitial straight after it.
    /// <para>
    /// Not published by the Editor's deterministic reward stubs: they put nothing on screen.
    /// </para>
    /// </summary>
    public readonly struct RewardedAdShownMessage
    {
    }
}
