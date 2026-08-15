using System;
using System.Collections;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// Rewarded-ad entry point used to unlock ad-gated skins. This is currently a mock
    /// implementation (a short "watching" delay that always grants the reward) so the whole
    /// unlock flow is testable today without an ad network account. To wire up a real
    /// network (Unity Ads, AdMob, ...), replace the body of ShowRewardedAd with that SDK's
    /// "show rewarded ad" call and invoke onRewardGranted only from its actual reward
    /// callback - every caller in this codebase already goes through this one method.
    /// </summary>
    public static class AdService
    {
        const float MockAdDuration = 2.5f;

        public static void ShowRewardedAd(MonoBehaviour runner, Action onRewardGranted)
        {
            runner.StartCoroutine(MockAdFlow(onRewardGranted));
        }

        static IEnumerator MockAdFlow(Action onRewardGranted)
        {
            // WaitForSecondsRealtime so this still progresses if triggered while
            // Time.timeScale is 0 (e.g. opening the skin shop from the Game Over screen).
            yield return new WaitForSecondsRealtime(MockAdDuration);
            onRewardGranted?.Invoke();
        }
    }
}
