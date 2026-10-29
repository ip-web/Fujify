using System;
using System.Collections.Generic;
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
/// Includes automatic fallback to Google Test Ad Units when production units have no fill (Code 3).
/// </summary>
public class AdManager
{
    // Production AdMob IDs provided by user
    public const string ProdAppId = "ca-app-pub-7034111600506292~2070313807";
    public const string ProdSplashAppOpenUnitId = "ca-app-pub-7034111600506292/8496121355";
    public const string ProdInterstitialUnitId = "ca-app-pub-7034111600506292/9017323545";
    public const string ProdBannerUnitId = "ca-app-pub-7034111600506292/6391160201";

    // Google's official Test Ad Unit IDs (guaranteed fill, safe for testing)
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
    private bool _pendingSplashShow;
    private bool _pendingInterstitialPreload;

    // Retain strong references so managed callback wrappers are not collected by GC
    private InitializationListener? _initListener;
    private AppOpenLoadCallback? _appOpenCallback;
    private InterstitialLoadCallback? _interstitialCallback;
    private BannerAdListener? _bannerListener;

    public void PreInitialize(Activity activity)
    {
        _activity = activity;
        // Wire callbacks early so initial launch calls from Avalonia do not get lost
        AdService.PlatformShowSplashCallback = () => activity.RunOnUiThread(ShowSplashAppOpenAd);
        AdService.PlatformShowInterstitialCallback = () => activity.RunOnUiThread(ShowInterstitialAd);
        AdService.PlatformSetBannerVisibleCallback = (visible) => activity.RunOnUiThread(() => SetBannerVisible(visible));
    }

    public void Initialize(Activity activity)
    {
        _activity = activity;
        PreInitialize(activity);

        try
        {
            // Configure test devices including the emulator
            try
            {
                var testDevices = new List<string> { AdRequest.DeviceIdEmulator };
                var requestConfig = new RequestConfiguration.Builder()
                    .SetTestDeviceIds(testDevices)
                    .Build();
                MobileAds.RequestConfiguration = requestConfig;
            }
            catch (Exception ex)
            {
                Log.Warn("FujifierAds", $"RequestConfiguration warning: {ex.Message}");
            }

            _initListener = new InitializationListener(() =>
            {
                _isInitialized = true;
                Log.Info("FujifierAds", "Google Mobile Ads initialized successfully.");

                activity.RunOnUiThread(() =>
                {
                    // Setup Banner Ad
                    SetupBannerAd();

                    // Preload first interstitial ad
                    PreloadInterstitial();

                    // Show splash if requested or at launch
                    if (_pendingSplashShow || AdService.Instance?.ShouldShowAds != false)
                    {
                        _pendingSplashShow = false;
                        ShowSplashAppOpenAd();
                    }

                    if (_pendingInterstitialPreload)
                    {
                        _pendingInterstitialPreload = false;
                        PreloadInterstitial();
                    }
                });
            });

            MobileAds.Initialize(activity, _initListener);
        }
        catch (Exception ex)
        {
            Log.Error("FujifierAds", $"Failed to initialize MobileAds: {ex}");
        }
    }

    #region Splash Screen (App Open Ad)

    public void ShowSplashAppOpenAd()
    {
        if (_activity == null || _activity.IsFinishing || _activity.IsDestroyed) return;
        if (AdService.Instance?.ShouldShowAds == false) return;

        if (!_isInitialized)
        {
            Log.Info("FujifierAds", "Splash requested before MobileAds finished initializing; queued.");
            _pendingSplashShow = true;
            return;
        }

        LoadAndShowSplashInternal(SplashAppOpenUnitId);
    }

    private void LoadAndShowSplashInternal(string adUnitId)
    {
        if (_activity == null || _activity.IsFinishing || _activity.IsDestroyed) return;
        if (AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            Log.Info("FujifierAds", $"Loading Splash App Open Ad (Unit: {adUnitId})...");
            var adRequest = new AdRequest.Builder().Build();

            _appOpenCallback = new AppOpenLoadCallback(
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
                    Log.Warn("FujifierAds", $"Failed to load Splash App Open Ad ({adUnitId}): {err.Message} (Code: {err.Code})");

                    // If production unit failed with NO_FILL (Code 3) or other error, fallback to Google Test Unit
                    if (adUnitId != TestSplashAppOpenUnitId)
                    {
                        Log.Info("FujifierAds", "Falling back to Google Test Splash App Open Unit...");
                        LoadAndShowSplashInternal(TestSplashAppOpenUnitId);
                    }
                });

            AppOpenAd.Load(_activity, adUnitId, adRequest, _appOpenCallback);
        }
        catch (Exception ex)
        {
            Log.Error("FujifierAds", $"Error in LoadAndShowSplashInternal: {ex}");
        }
    }

    #endregion

    #region Interstitial Ad

    public void PreloadInterstitial()
    {
        if (_activity == null || _activity.IsFinishing || _interstitialAd != null) return;
        if (AdService.Instance?.ShouldShowAds == false) return;

        if (!_isInitialized)
        {
            _pendingInterstitialPreload = true;
            return;
        }

        PreloadInterstitialInternal(InterstitialUnitId);
    }

    private void PreloadInterstitialInternal(string adUnitId)
    {
        if (_activity == null || _activity.IsFinishing || _interstitialAd != null) return;
        if (AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            Log.Info("FujifierAds", $"Preloading Interstitial Ad (Unit: {adUnitId})...");
            var adRequest = new AdRequest.Builder().Build();

            _interstitialCallback = new InterstitialLoadCallback(
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
                    Log.Warn("FujifierAds", $"Failed to preload Interstitial Ad ({adUnitId}): {err.Message} (Code: {err.Code})");

                    // Fallback to Test Interstitial if production had no fill
                    if (adUnitId != TestInterstitialUnitId)
                    {
                        Log.Info("FujifierAds", "Falling back to Google Test Interstitial Unit...");
                        PreloadInterstitialInternal(TestInterstitialUnitId);
                    }
                });

            InterstitialAd.Load(_activity, adUnitId, adRequest, _interstitialCallback);
        }
        catch (Exception ex)
        {
            Log.Error("FujifierAds", $"Error in PreloadInterstitialInternal: {ex}");
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
            // Container frame docked at the bottom of the screen
            _bannerContainer = new FrameLayout(_activity);
            var containerParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.Bottom | GravityFlags.CenterHorizontal
            };
            _bannerContainer.LayoutParameters = containerParams;
            _bannerContainer.Elevation = 100f;
            _bannerContainer.TranslationZ = 100f;

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

            _bannerListener = new BannerAdListener(
                onLoaded: () =>
                {
                    Log.Info("FujifierAds", "Banner Ad loaded successfully!");
                    if (_bannerContainer != null)
                    {
                        _bannerContainer.Visibility = ViewStates.Visible;
                        _bannerContainer.BringToFront();
                    }
                },
                onFailed: (err) =>
                {
                    Log.Warn("FujifierAds", $"Banner Ad failed to load ({_bannerAdView?.AdUnitId}): {err.Message} (Code: {err.Code})");

                    // Fallback to Google Test Banner Unit if production has no fill
                    if (_bannerAdView != null && _bannerAdView.AdUnitId != TestBannerUnitId)
                    {
                        Log.Info("FujifierAds", "Falling back to Google Test Banner Unit...");
                        _activity?.RunOnUiThread(() =>
                        {
                            try
                            {
                                _bannerAdView.AdUnitId = TestBannerUnitId;
                                _bannerAdView.LoadAd(new AdRequest.Builder().Build());
                            }
                            catch (Exception ex)
                            {
                                Log.Error("FujifierAds", $"Error loading test banner fallback: {ex}");
                            }
                        });
                    }
                });

            _bannerAdView.AdListener = _bannerListener;
            _bannerContainer.AddView(_bannerAdView);

            // Add container to the Activity's content view hierarchy on top of Avalonia
            _activity.AddContentView(_bannerContainer, containerParams);
            _bannerContainer.BringToFront();

            // Load ad request
            var adRequest = new AdRequest.Builder().Build();
            _bannerAdView.LoadAd(adRequest);

            Log.Info("FujifierAds", $"Banner Ad created and added to screen (Unit: {BannerUnitId}).");
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
            if (visible)
            {
                _bannerContainer.BringToFront();
            }
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
        _initListener = null;
        _appOpenCallback = null;
        _interstitialCallback = null;
        _bannerListener = null;
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

    private class BannerAdListener : AdListener
    {
        private readonly Action _onLoaded;
        private readonly Action<LoadAdError> _onFailed;

        public BannerAdListener(Action onLoaded, Action<LoadAdError> onFailed)
        {
            _onLoaded = onLoaded;
            _onFailed = onFailed;
        }

        public override void OnAdLoaded()
        {
            base.OnAdLoaded();
            _onLoaded();
        }

        public override void OnAdFailedToLoad(LoadAdError loadAdError)
        {
            base.OnAdFailedToLoad(loadAdError);
            _onFailed(loadAdError);
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
