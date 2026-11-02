using System;
using System.Linq;
using Cats.Google.MobileAds;
using Foundation;
using Fujify.Services;
using UIKit;

namespace Fujify.iOS;

/// <summary>
/// Manages Google AdMob banner, splash (app open), and interstitial ads for Fujifier iOS.
/// Includes automatic fallback to Google Test Ad Units when production units have no fill.
/// </summary>
public class AdManager
{
    // Production AdMob IDs provided by user
    public const string ProdAppId = "ca-app-pub-7034111600506292~2070313807";
    public const string ProdSplashAppOpenUnitId = "ca-app-pub-7034111600506292/8496121355";
    public const string ProdInterstitialUnitId = "ca-app-pub-7034111600506292/9017323545";
    public const string ProdBannerUnitId = "ca-app-pub-7034111600506292/6391160201";

    // Google's official Test Ad Unit IDs for iOS (guaranteed fill, safe for testing & fallback)
    public const string TestSplashAppOpenUnitId = "ca-app-pub-3940256099942544/5575463023";
    public const string TestInterstitialUnitId = "ca-app-pub-3940256099942544/4411468910";
    public const string TestBannerUnitId = "ca-app-pub-3940256099942544/2934735716";

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

    private UIWindow? _window;
    private BannerView? _bannerView;
    private InterstitialAd? _interstitialAd;
    private AppOpenAd? _appOpenAd;
    private bool _isShowingAd;
    private bool _isInitialized;
    private bool _pendingSplashShow;
    private bool _pendingInterstitialPreload;
    public bool IsInitialized => _isInitialized;

    public void Initialize(UIWindow? window)
    {
        _window = window;

        AdService.PlatformShowSplashCallback = () => UIApplication.SharedApplication.BeginInvokeOnMainThread(ShowSplashAppOpenAd);
        AdService.PlatformShowInterstitialCallback = () => UIApplication.SharedApplication.BeginInvokeOnMainThread(ShowInterstitialAd);
        AdService.PlatformSetBannerVisibleCallback = (visible) => UIApplication.SharedApplication.BeginInvokeOnMainThread(() => SetBannerVisible(visible));

        try
        {
            MobileAds.SharedInstance.Start(status =>
            {
                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] MobileAds initialized successfully.");

                UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                {
                    SetupBannerAd();
                    if (_pendingSplashShow)
                    {
                        _pendingSplashShow = false;
                        ShowSplashAppOpenAd();
                    }
                    else
                    {
                        ShowSplashAppOpenAd();
                    }

                    if (_pendingInterstitialPreload)
                    {
                        _pendingInterstitialPreload = false;
                    }
                    PreloadInterstitial();
                });
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Failed to initialize MobileAds: {ex}");
        }
    }

    #region Splash (App Open) Ad

    public void ShowSplashAppOpenAd()
    {
        if (AdService.Instance?.ShouldShowAds == false) return;

        if (!_isInitialized)
        {
            _pendingSplashShow = true;
            return;
        }

        LoadAndShowSplashInternal(SplashAppOpenUnitId);
    }

    private void LoadAndShowSplashInternal(string adUnitId)
    {
        if (_isShowingAd || AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Loading Splash App Open Ad (Unit: {adUnitId})...");
            var req = Request.GetDefaultRequest();

            AppOpenAd.Load(adUnitId, req, (ad, error) =>
            {
                if (error != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Splash App Open Ad load error ({adUnitId}): {error.LocalizedDescription}");
                    if (adUnitId != TestSplashAppOpenUnitId)
                    {
                        System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] Falling back to Google Test Splash App Open Unit...");
                        UIApplication.SharedApplication.BeginInvokeOnMainThread(() => LoadAndShowSplashInternal(TestSplashAppOpenUnitId));
                    }
                    return;
                }

                _appOpenAd = ad;
                var topVc = GetTopViewController();
                if (topVc != null && _appOpenAd != null && _appOpenAd.CanPresent(topVc, out NSError? presentError))
                {
                    System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] Presenting Splash App Open Ad...");
                    _isShowingAd = true;
                    _appOpenAd.PresentFromRootViewController(topVc);
                    _appOpenAd = null;
                    _isShowingAd = false;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Error showing Splash App Open Ad: {ex}");
            _isShowingAd = false;
        }
    }

    #endregion

    #region Interstitial Ad

    public void PreloadInterstitial()
    {
        if (_interstitialAd != null) return;
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
        if (_interstitialAd != null) return;
        if (AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Preloading Interstitial Ad (Unit: {adUnitId})...");
            var req = Request.GetDefaultRequest();

            InterstitialAd.Load(adUnitId, req, (ad, error) =>
            {
                if (error != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Interstitial preload error ({adUnitId}): {error.LocalizedDescription}");
                    _interstitialAd = null;
                    if (adUnitId != TestInterstitialUnitId)
                    {
                        System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] Falling back to Google Test Interstitial Unit...");
                        UIApplication.SharedApplication.BeginInvokeOnMainThread(() => PreloadInterstitialInternal(TestInterstitialUnitId));
                    }
                    return;
                }

                _interstitialAd = ad;
                System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Interstitial preloaded successfully (Unit: {adUnitId}).");
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Error preloading Interstitial Ad: {ex}");
        }
    }

    public void ShowInterstitialAd()
    {
        if (AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            if (_interstitialAd != null)
            {
                var topVc = GetTopViewController();
                if (topVc != null && _interstitialAd.CanPresent(topVc, out NSError? presentError))
                {
                    System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] Presenting Interstitial Ad...");
                    _interstitialAd.Present(topVc);
                    _interstitialAd = null;
                    PreloadInterstitial();
                    return;
                }
            }

            PreloadInterstitial();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Error showing Interstitial Ad: {ex}");
        }
    }

    #endregion

    #region Banner Ad

    public void SetupBannerAd()
    {
        LoadBannerInternal(BannerUnitId);
    }

    private void LoadBannerInternal(string adUnitId)
    {
        if (AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            var topVc = GetTopViewController();
            if (topVc == null || topVc.View == null) return;

            if (_bannerView != null)
            {
                _bannerView.RemoveFromSuperview();
                _bannerView.Dispose();
                _bannerView = null;
            }

            _bannerView = new BannerView(AdSizeCons.Banner)
            {
                AdUnitId = adUnitId,
                RootViewController = topVc,
                TranslatesAutoresizingMaskIntoConstraints = false
            };

            _bannerView.AdReceived += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Banner Ad loaded successfully! (Unit: {adUnitId})");
                if (_bannerView != null)
                {
                    _bannerView.Hidden = !(AdService.Instance?.ShouldShowAds ?? true);
                    topVc.View?.BringSubviewToFront(_bannerView);
                }
            };

            _bannerView.ReceiveAdFailed += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Banner Ad failed to load ({adUnitId}): {e.Error?.LocalizedDescription}");
                if (adUnitId != TestBannerUnitId)
                {
                    System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] Falling back to Google Test Banner Unit...");
                    UIApplication.SharedApplication.BeginInvokeOnMainThread(() => LoadBannerInternal(TestBannerUnitId));
                }
            };

            topVc.View.AddSubview(_bannerView);
            topVc.View.BringSubviewToFront(_bannerView);

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                _bannerView.BottomAnchor.ConstraintEqualTo(topVc.View.SafeAreaLayoutGuide.BottomAnchor),
                _bannerView.CenterXAnchor.ConstraintEqualTo(topVc.View.CenterXAnchor),
                _bannerView.WidthAnchor.ConstraintEqualTo(320),
                _bannerView.HeightAnchor.ConstraintEqualTo(50)
            });

            _bannerView.LoadRequest(Request.GetDefaultRequest());
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Banner Ad created and requested (Unit: {adUnitId}).");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Failed to setup Banner Ad: {ex}");
        }
    }

    public void SetBannerVisible(bool visible)
    {
        if (_bannerView != null)
        {
            _bannerView.Hidden = !visible;
            if (visible)
            {
                var topVc = GetTopViewController();
                topVc?.View?.BringSubviewToFront(_bannerView);
            }
        }
        else if (visible)
        {
            SetupBannerAd();
        }
    }

    #endregion

    private UIViewController? GetTopViewController()
    {
        var window = _window ?? UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);

#pragma warning disable CA1422
        window ??= UIApplication.SharedApplication.Windows.FirstOrDefault();
#pragma warning restore CA1422

        var root = window?.RootViewController;
        while (root?.PresentedViewController != null)
        {
            root = root.PresentedViewController;
        }
        return root;
    }
}
