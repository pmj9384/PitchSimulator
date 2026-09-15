using System;
using UnityCommunity.UnitySingleton;
using UnityEngine;
#if UNITY_ANDROID
using GoogleMobileAds.Api;
#endif

public class AdsManager : PersistentMonoSingleton<AdsManager>
{
#if UNITY_ANDROID
    // 게임마다 AdMob 콘솔에서 만든 광고 단위 ID로 바꾼다 (앱 ID는 Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset)
    private const string RewardedAdUnitId = "REPLACE_WITH_REWARDED_UNIT_ID";
    private const string BannerAdUnitId = "REPLACE_WITH_BANNER_UNIT_ID";

    private RewardedAd rewardedAd;
    private BannerView bannerView;
    private bool bannerRequested;     // 초기화 전에 ShowBanner가 불리면 초기화 끝난 뒤 로드한다
    private bool isInitialized = false;

    public override void InitializeSingleton()
    {
        base.InitializeSingleton();
        MobileAds.RaiseAdEventsOnUnityMainThread = true;
        MobileAds.Initialize(status =>
        {
            isInitialized = true;
            Debug.Log("[AdsManager] AdMob 초기화 완료");
            LoadRewardedAd();
            if (bannerRequested) LoadBanner();
        });
    }

    // ── 배너 (WarTableSimulator 2026-09-14에서 역이식). 어디서 띄울지는 게임이 정한다 (예: 로비 Open에서 Show, 인게임 진입 때 Hide)
    public void ShowBanner()
    {
        bannerRequested = true;
        if (!isInitialized) return;   // 초기화 콜백에서 이어서 로드
        LoadBanner();
    }

    public void HideBanner()
    {
        bannerRequested = false;
        if (bannerView == null) return;
        bannerView.Destroy();
        bannerView = null;
    }

    private void LoadBanner()
    {
        if (bannerView != null) return;   // 이미 떠 있음

        bannerView = new BannerView(BannerAdUnitId, AdSize.Banner, AdPosition.Bottom);
        bannerView.OnBannerAdLoaded += OnBannerLoaded;
        bannerView.OnBannerAdLoadFailed += OnBannerLoadFailed;
        bannerView.LoadAd(new AdRequest());
    }

    private void OnBannerLoaded()
    {
        Debug.Log("[AdsManager] 배너 로드 완료");
    }

    private void OnBannerLoadFailed(LoadAdError error)
    {
        Debug.LogWarning($"[AdsManager] 배너 로드 실패: {error}");   // 새 광고 단위는 생성 후 최대 1시간 No fill이 정상
    }

    public void LoadRewardedAd()
    {
        if (rewardedAd != null)
        {
            rewardedAd.Destroy();
            rewardedAd = null;
        }

        RewardedAd.Load(RewardedAdUnitId, new AdRequest(), (RewardedAd ad, LoadAdError error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogWarning($"[AdsManager] 리워드 광고 로드 실패: {error}");
                return;
            }
            rewardedAd = ad;
            Debug.Log("[AdsManager] 리워드 광고 로드 완료");
        });
    }

    public bool IsRewardedAdReady => rewardedAd != null;

    public void ShowRewardedAd(Action onRewarded, Action onFailed = null)
    {
        if (!IsRewardedAdReady)
        {
            Debug.LogWarning("[AdsManager] 광고가 준비되지 않았습니다.");
            onFailed?.Invoke();
            return;
        }

        bool rewarded = false;
        var ad = rewardedAd;

        void OnClosed()
        {
            ad.OnAdFullScreenContentClosed -= OnClosed;
            ad.OnAdFullScreenContentFailed -= OnFailed;
            if (rewarded) onRewarded?.Invoke();
            LoadRewardedAd();
        }

        void OnFailed(AdError error)
        {
            ad.OnAdFullScreenContentClosed -= OnClosed;
            ad.OnAdFullScreenContentFailed -= OnFailed;
            Debug.LogWarning($"[AdsManager] 광고 표시 실패: {error}");
            onFailed?.Invoke();
            LoadRewardedAd();
        }

        ad.OnAdFullScreenContentClosed += OnClosed;
        ad.OnAdFullScreenContentFailed += OnFailed;

        ad.Show(reward =>
        {
            Debug.Log("[AdsManager] 리워드 지급 완료");
            rewarded = true;
        });
    }
#else
    public bool IsRewardedAdReady => false;

    public void ShowRewardedAd(Action onRewarded, Action onFailed = null)
    {
        Debug.Log("[AdsManager] 광고 미지원 플랫폼");
        onFailed?.Invoke();
    }
#endif
}
