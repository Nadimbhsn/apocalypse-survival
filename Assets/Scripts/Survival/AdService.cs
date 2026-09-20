using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Advertisements;

namespace Platformer.Survival
{
    /// <summary>
    /// Unity Ads integration (package com.unity.ads). Two formats:
    ///  - Interstitial: shown after a death / game over, at most every DeathsPerInterstitial
    ///    deaths and never more often than MinSecondsBetweenInterstitials.
    ///  - Rewarded: watched voluntarily to unlock ad-gated characters; the reward is only
    ///    granted when the ad completes.
    ///
    /// SETUP (once): create the project on https://cloud.unity.com, enable Monetization >
    /// Ads, then paste the Android and iOS Game IDs below. Placement ids are Unity's
    /// defaults. Keep TestMode = true until the store release (test ads, no revenue, no
    /// policy risk). Until Game IDs are set, interstitials are skipped and rewarded
    /// unlocks fall back to a short mock delay so the whole flow stays testable.
    /// </summary>
    public class AdService : MonoBehaviour, IUnityAdsInitializationListener, IUnityAdsLoadListener, IUnityAdsShowListener
    {
        // ---- configuration --------------------------------------------------------------
        const string AndroidGameId = "YOUR_ANDROID_GAME_ID";
        const string IosGameId = "YOUR_IOS_GAME_ID";
        const bool TestMode = true;
        const string InterstitialAndroid = "Interstitial_Android";
        const string InterstitialIos = "Interstitial_iOS";
        const string RewardedAndroid = "Rewarded_Android";
        const string RewardedIos = "Rewarded_iOS";

        public static int DeathsPerInterstitial = 2;
        public static float MinSecondsBetweenInterstitials = 90f;
        const float MockAdDuration = 2.5f;

        static AdService instance;
        static int deathCounter;
        static float lastInterstitialRealtime = -9999f;

        bool initialized, interstitialReady, rewardedReady;
        Action pendingReward, pendingRewardFailed;

        static bool IsIos => Application.platform == RuntimePlatform.IPhonePlayer;
        static string GameId => IsIos ? IosGameId : AndroidGameId;
        static string InterstitialId => IsIos ? InterstitialIos : InterstitialAndroid;
        static string RewardedId => IsIos ? RewardedIos : RewardedAndroid;
        static bool Configured => !GameId.StartsWith("YOUR_");

        public static AdService Ensure()
        {
            if (instance == null)
            {
                var go = new GameObject("AdService");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<AdService>();
            }
            return instance;
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;

            if (!Configured)
            {
                Debug.Log("[Ads] Game IDs not set in AdService.cs - interstitials disabled, rewarded unlocks use a mock delay.");
                return;
            }
            if (Advertisement.isSupported && !Advertisement.isInitialized)
                Advertisement.Initialize(GameId, TestMode, this);
        }

        // ---- public API -----------------------------------------------------------------

        /// <summary>
        /// Call after a death or game over. Counts deaths and shows an interstitial when
        /// the pacing rules allow and an ad is loaded; otherwise does nothing.
        /// </summary>
        public static void OnPlayerDeath()
        {
            deathCounter++;
            var ads = Ensure();
            if (!ads.initialized || !ads.interstitialReady) return;
            if (deathCounter % DeathsPerInterstitial != 0) return;
            if (Time.realtimeSinceStartup - lastInterstitialRealtime < MinSecondsBetweenInterstitials) return;

            lastInterstitialRealtime = Time.realtimeSinceStartup;
            ads.interstitialReady = false;
            Advertisement.Show(InterstitialId, ads);
        }

        /// <summary>
        /// Shows a rewarded ad. onRewardGranted fires only if it is watched to the end;
        /// onFailed fires if it was skipped or no ad could be shown.
        /// </summary>
        public static void ShowRewardedAd(Action onRewardGranted, Action onFailed = null)
        {
            var ads = Ensure();
            if (ads.initialized && ads.rewardedReady)
            {
                ads.pendingReward = onRewardGranted;
                ads.pendingRewardFailed = onFailed;
                ads.rewardedReady = false;
                Advertisement.Show(RewardedId, ads);
                return;
            }

            // No real ad available: in development grant after a mock delay so the unlock
            // flow is testable; in a configured release build report the failure instead.
            if (!Configured || Application.isEditor)
                ads.StartCoroutine(ads.MockAdFlow(onRewardGranted));
            else
                onFailed?.Invoke();
        }

        IEnumerator MockAdFlow(Action onRewardGranted)
        {
            yield return new WaitForSecondsRealtime(MockAdDuration);
            onRewardGranted?.Invoke();
        }

        // ---- Unity Ads callbacks --------------------------------------------------------

        public void OnInitializationComplete()
        {
            initialized = true;
            Advertisement.Load(InterstitialId, this);
            Advertisement.Load(RewardedId, this);
        }

        public void OnInitializationFailed(UnityAdsInitializationError error, string message)
        {
            Debug.LogWarning($"[Ads] Initialization failed: {error} - {message}");
        }

        public void OnUnityAdsAdLoaded(string placementId)
        {
            if (placementId == InterstitialId) interstitialReady = true;
            else if (placementId == RewardedId) rewardedReady = true;
        }

        public void OnUnityAdsFailedToLoad(string placementId, UnityAdsLoadError error, string message)
        {
            Debug.LogWarning($"[Ads] Load failed for {placementId}: {error} - {message}");
            StartCoroutine(RetryLoad(placementId, 20f));
        }

        IEnumerator RetryLoad(string placementId, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (initialized) Advertisement.Load(placementId, this);
        }

        public void OnUnityAdsShowFailure(string placementId, UnityAdsShowError error, string message)
        {
            Debug.LogWarning($"[Ads] Show failed for {placementId}: {error} - {message}");
            FinishShow(placementId, completed: false);
        }

        public void OnUnityAdsShowStart(string placementId) { }
        public void OnUnityAdsShowClick(string placementId) { }

        public void OnUnityAdsShowComplete(string placementId, UnityAdsShowCompletionState showCompletionState)
        {
            FinishShow(placementId, showCompletionState == UnityAdsShowCompletionState.COMPLETED);
        }

        void FinishShow(string placementId, bool completed)
        {
            if (placementId == RewardedId)
            {
                var granted = pendingReward;
                var failed = pendingRewardFailed;
                pendingReward = null;
                pendingRewardFailed = null;
                if (completed) granted?.Invoke();
                else failed?.Invoke();
            }
            if (initialized) Advertisement.Load(placementId, this); // preload the next one
        }
    }
}
