using System;
using Android.App;
using Android.Gms.Ads;
using Android.Gms.Ads.AppOpen;
using Android.Gms.Ads.Initialization;
using Android.Gms.Ads.Interstitial;
using Android.Util;
using Android.Views;
using Android.Widget;
using Fujify.Services;

namespace Fujify.Android;

/// <summary>
/// Manages Google AdMob banner, app open (splash), and interstitial ads for Fujifier Android.
/// </summary>
public class AdManager
{
    // Production AdMob IDs provided by user
    public const string ProdAppId = "ca-app-pub-7034111600506292~2070313807";
    public const string ProdSplashAppOpenUnitId = "ca-app-pub-7034111600506292/8496121355";
    public const string ProdInterstitialUnitId = "ca-app-pub-7034111600506292/9017323545";
    public const string ProdBannerUnitId = "ca-app-pub-7034111600506292/6391160201";

    // Google's official Test Ad Unit IDs (used in DEBUG to protect the publisher account from invalid traffic penalties)
    public const string TestSplashAppOpenUnitId = "ca-app-pub-3940256099942544/9257390708";
    public const string TestInterstitialUnitId = "ca-app-pub-3940256099942544/1033173712";
    public const string TestBannerUnitId = "ca-app-pub-3940256099942544/6300978111";

#if DEBUG
    public static string SplashAppOpenUnitId => TestSplashAppOpenUnitId;
    public static string InterstitialUnitId => TestInterstitialUnitId;
    public static string BannerUnitId => TestBannerUnitId;
#else
    public static string SplashAppOpenUnitId => ProdSplashAppOpenUnitId;
    public static string InterstitialUnitId => ProdInterstitialUnitId;
    public static string BannerUnitId => ProdBannerUnitId;
#endif

    private static AdManager? _instance;
    public static AdManager Instance => _instance ??= new AdManager();

    private Activity? _activity;
    private AdView? _bannerAdView;
    private FrameLayout? _bannerContainer;
    private InterstitialAd? _interstitialAd;
    private AppOpenAd? _appOpenAd;
    private bool _isShowingAd;
    private bool _isInitialized;

    public void Initialize(Activity activity)
    {
        _activity = activity;

        try
        {
            MobileAds.Initialize(activity, new InitializationListener(() =>
            {
                _isInitialized = true;
                Log.Info("FujifierAds", "Google Mobile Ads initialized successfully.");

                activity.RunOnUiThread(() =>
                {
                    // Preload first interstitial ad
                    PreloadInterstitial();

                    // Preload and display Splash / App Open Ad on launch
                    ShowSplashAppOpenAd();

                    // Setup Banner Ad
                    SetupBannerAd();
                });
            }));
        }
        catch (Exception ex)
        {
            Log.Error("FujifierAds", $"Failed to initialize MobileAds: {ex}");
        }

        // Wire AdService callbacks from shared code
        AdService.PlatformShowSplashCallback = () => activity.RunOnUiThread(ShowSplashAppOpenAd);
        AdService.PlatformShowInterstitialCallback = () => activity.RunOnUiThread(ShowInterstitialAd);
        AdService.PlatformSetBannerVisibleCallback = (visible) => activity.RunOnUiThread(() => SetBannerVisible(visible));
    }

    #region Splash Screen (App Open Ad)

    public void ShowSplashAppOpenAd()
    {
        if (!_isInitialized || _activity == null || _activity.IsFinishing || _activity.IsDestroyed) return;
        if (AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            Log.Info("FujifierAds", "Loading Splash App Open Ad...");
            var adRequest = new AdRequest.Builder().Build();

            AppOpenAd.Load(
                _activity,
                SplashAppOpenUnitId,
                adRequest,
                new AppOpenLoadCallback(
                    onLoaded: (ad) =>
                    {
                        _appOpenAd = ad;
                        Log.Info("FujifierAds", "Splash App Open Ad loaded successfully. Showing...");
                        
                        _appOpenAd.FullScreenContentCallback = new AdFullScreenCallback(
                            onDismissed: () =>
                            {
                                _isShowingAd = false;
                                _appOpenAd = null;
                                Log.Info("FujifierAds", "Splash App Open Ad dismissed.");
                            },
                            onFailed: (err) =>
                            {
                                _isShowingAd = false;
                                _appOpenAd = null;
                                Log.Warn("FujifierAds", $"Splash App Open Ad failed to show: {err.Message}");
                            });

                        if (!_isShowingAd && _activity != null && !_activity.IsFinishing)
                        {
                            _isShowingAd = true;
                            _appOpenAd.Show(_activity);
                        }
                    },
                    onFailed: (err) =>
                    {
                        Log.Warn("FujifierAds", $"Failed to load Splash App Open Ad: {err.Message} (Code: {err.Code})");
                    }));
        }
        catch (Exception ex)
        {
            Log.Error("FujifierAds", $"Error in ShowSplashAppOpenAd: {ex}");
        }
    }

    #endregion

    #region Interstitial Ad

    public void PreloadInterstitial()
    {
        if (!_isInitialized || _activity == null || _activity.IsFinishing || _interstitialAd != null) return;
        if (AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            Log.Info("FujifierAds", "Preloading Interstitial Ad...");
            var adRequest = new AdRequest.Builder().Build();

            InterstitialAd.Load(
                _activity,
                InterstitialUnitId,
                adRequest,
                new InterstitialLoadCallback(
                    onLoaded: (ad) =>
                    {
                        _interstitialAd = ad;
                        Log.Info("FujifierAds", "Interstitial Ad preloaded successfully.");

                        _interstitialAd.FullScreenContentCallback = new AdFullScreenCallback(
                            onDismissed: () =>
                            {
                                _isShowingAd = false;
                                _interstitialAd = null;
                                Log.Info("FujifierAds", "Interstitial Ad dismissed. Preloading next...");
                                PreloadInterstitial();
                            },
                            onFailed: (err) =>
                            {
                                _isShowingAd = false;
                                _interstitialAd = null;
                                Log.Warn("FujifierAds", $"Interstitial Ad failed to show: {err.Message}");
                                PreloadInterstitial();
                            });
                    },
                    onFailed: (err) =>
                    {
                        _interstitialAd = null;
                        Log.Warn("FujifierAds", $"Failed to preload Interstitial Ad: {err.Message} (Code: {err.Code})");
                    }));
        }
        catch (Exception ex)
        {
            Log.Error("FujifierAds", $"Error in PreloadInterstitial: {ex}");
        }
    }

    public void ShowInterstitialAd()
    {
        if (_activity == null || _activity.IsFinishing || _activity.IsDestroyed) return;
        if (AdService.Instance?.ShouldShowAds == false) return;

        if (_interstitialAd != null && !_isShowingAd)
        {
            Log.Info("FujifierAds", "Showing Interstitial Ad...");
            _isShowingAd = true;
            _interstitialAd.Show(_activity);
        }
        else
        {
            Log.Info("FujifierAds", "Interstitial Ad not ready yet. Preloading...");
            PreloadInterstitial();
        }
    }

    #endregion

    #region Banner Ad

    public void SetupBannerAd()
    {
        if (!_isInitialized || _activity == null || _bannerContainer != null) return;
        if (AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            var decorView = _activity.Window?.DecorView as ViewGroup;
            if (decorView == null) return;

            // Container frame docked at the bottom of the screen
            _bannerContainer = new FrameLayout(_activity);
            var containerParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.Bottom | GravityFlags.CenterHorizontal
            };
            _bannerContainer.LayoutParameters = containerParams;

            // AdView
            _bannerAdView = new AdView(_activity)
            {
                AdUnitId = BannerUnitId,
                AdSize = AdSize.Banner
            };

            var adParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.CenterHorizontal
            };
            _bannerAdView.LayoutParameters = adParams;

            _bannerContainer.AddView(_bannerAdView);
            decorView.AddView(_bannerContainer);

            // Load ad request
            var adRequest = new AdRequest.Builder().Build();
            _bannerAdView.LoadAd(adRequest);

            Log.Info("FujifierAds", "Banner Ad created and added to screen.");
        }
        catch (Exception ex)
        {
            Log.Error("FujifierAds", $"Failed to setup Banner Ad: {ex}");
        }
    }

    public void SetBannerVisible(bool visible)
    {
        if (_bannerContainer != null)
        {
            _bannerContainer.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
        }

        if (_bannerAdView != null)
        {
            if (visible)
            {
                _bannerAdView.Resume();
            }
            else
            {
                _bannerAdView.Pause();
            }
        }
    }

    public void Resume()
    {
        _bannerAdView?.Resume();
    }

    public void Pause()
    {
        _bannerAdView?.Pause();
    }

    public void Destroy()
    {
        _bannerAdView?.Destroy();
        _bannerAdView = null;
        _bannerContainer = null;
        _interstitialAd = null;
        _appOpenAd = null;
    }

    #endregion

    #region Helper Callbacks

    private class InitializationListener : Java.Lang.Object, IOnInitializationCompleteListener
    {
        private readonly Action _onComplete;
        public InitializationListener(Action onComplete) => _onComplete = onComplete;

        public void OnInitializationComplete(IInitializationStatus status)
        {
            _onComplete();
        }
    }

    private class AppOpenLoadCallback : AppOpenAd.AppOpenAdLoadCallback
    {
        private readonly Action<AppOpenAd> _onLoaded;
        private readonly Action<LoadAdError> _onFailed;

        public AppOpenLoadCallback(Action<AppOpenAd> onLoaded, Action<LoadAdError> onFailed)
        {
            _onLoaded = onLoaded;
            _onFailed = onFailed;
        }

        public override void OnAdLoaded(AppOpenAd appOpenAd)
        {
            base.OnAdLoaded(appOpenAd);
            _onLoaded(appOpenAd);
        }

        public override void OnAdFailedToLoad(LoadAdError loadAdError)
        {
            base.OnAdFailedToLoad(loadAdError);
            _onFailed(loadAdError);
        }
    }

    private class InterstitialLoadCallback : InterstitialAdLoadCallback
    {
        private readonly Action<InterstitialAd> _onLoaded;
        private readonly Action<LoadAdError> _onFailed;

        public InterstitialLoadCallback(Action<InterstitialAd> onLoaded, Action<LoadAdError> onFailed)
        {
            _onLoaded = onLoaded;
            _onFailed = onFailed;
        }

        public override void OnAdLoaded(InterstitialAd interstitialAd)
        {
            base.OnAdLoaded(interstitialAd);
            _onLoaded(interstitialAd);
        }

        public override void OnAdFailedToLoad(LoadAdError loadAdError)
        {
            base.OnAdFailedToLoad(loadAdError);
            _onFailed(loadAdError);
        }
    }

    private class AdFullScreenCallback : FullScreenContentCallback
    {
        private readonly Action? _onDismissed;
        private readonly Action<AdError>? _onFailed;

        public AdFullScreenCallback(Action? onDismissed = null, Action<AdError>? onFailed = null)
        {
            _onDismissed = onDismissed;
            _onFailed = onFailed;
        }

        public override void OnAdDismissedFullScreenContent()
        {
            base.OnAdDismissedFullScreenContent();
            _onDismissed?.Invoke();
        }

        public override void OnAdFailedToShowFullScreenContent(AdError adError)
        {
            base.OnAdFailedToShowFullScreenContent(adError);
            _onFailed?.Invoke(adError);
        }
    }

    #endregion
}
