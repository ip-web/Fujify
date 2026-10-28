using System;
using System.Linq;
using Cats.Google.MobileAds;
using Foundation;
using Fujify.Services;
using UIKit;

namespace Fujify.iOS;

/// <summary>
/// Manages Google AdMob banner, splash (app open), and interstitial ads for Fujifier iOS.
/// </summary>
public class AdManager
{
    // Production AdMob IDs provided by user
    public const string ProdAppId = "ca-app-pub-7034111600506292~2070313807";
    public const string ProdSplashAppOpenUnitId = "ca-app-pub-7034111600506292/8496121355";
    public const string ProdInterstitialUnitId = "ca-app-pub-7034111600506292/9017323545";
    public const string ProdBannerUnitId = "ca-app-pub-7034111600506292/6391160201";

    // Google's official Test Ad Unit IDs for iOS (used in DEBUG)
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
    private bool _isInitialized;

    public void Initialize(UIWindow? window)
    {
        _window = window;

        try
        {
            MobileAds.SharedInstance.Start(status =>
            {
                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] MobileAds initialized.");

                UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                {
                    PreloadInterstitial();
                    ShowSplashAppOpenAd();
                    SetupBannerAd();
                });
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Failed to initialize MobileAds: {ex}");
        }

        AdService.PlatformShowSplashCallback = () => UIApplication.SharedApplication.BeginInvokeOnMainThread(ShowSplashAppOpenAd);
        AdService.PlatformShowInterstitialCallback = () => UIApplication.SharedApplication.BeginInvokeOnMainThread(ShowInterstitialAd);
        AdService.PlatformSetBannerVisibleCallback = (visible) => UIApplication.SharedApplication.BeginInvokeOnMainThread(() => SetBannerVisible(visible));
    }

    public void ShowSplashAppOpenAd()
    {
        if (AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] Loading Splash App Open Ad...");
            var req = Request.GetDefaultRequest();

            AppOpenAd.Load(SplashAppOpenUnitId, req, (ad, error) =>
            {
                if (error != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Splash App Open Ad load error: {error.LocalizedDescription}");
                    return;
                }

                _appOpenAd = ad;
                var topVc = GetTopViewController();
                if (topVc != null && _appOpenAd != null && _appOpenAd.CanPresent(topVc, out NSError? presentError))
                {
                    System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] Presenting Splash App Open Ad...");
                    _appOpenAd.PresentFromRootViewController(topVc);
                    _appOpenAd = null;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Error showing Splash App Open Ad: {ex}");
        }
    }

    public void PreloadInterstitial()
    {
        if (_interstitialAd != null) return;
        if (AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] Preloading Interstitial Ad...");
            var req = Request.GetDefaultRequest();

            InterstitialAd.Load(InterstitialUnitId, req, (ad, error) =>
            {
                if (error != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Fujify iOS Ads] Interstitial preload error: {error.LocalizedDescription}");
                    _interstitialAd = null;
                    return;
                }

                _interstitialAd = ad;
                System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] Interstitial preloaded successfully.");
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

    public void SetupBannerAd()
    {
        if (_bannerView != null) return;
        if (AdService.Instance?.ShouldShowAds == false) return;

        try
        {
            var topVc = GetTopViewController();
            if (topVc == null || topVc.View == null) return;

            _bannerView = new BannerView(AdSizeCons.Banner)
            {
                AdUnitId = BannerUnitId,
                RootViewController = topVc,
                TranslatesAutoresizingMaskIntoConstraints = false
            };

            topVc.View.AddSubview(_bannerView);

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                _bannerView.BottomAnchor.ConstraintEqualTo(topVc.View.SafeAreaLayoutGuide.BottomAnchor),
                _bannerView.CenterXAnchor.ConstraintEqualTo(topVc.View.CenterXAnchor)
            });

            _bannerView.LoadRequest(Request.GetDefaultRequest());
            System.Diagnostics.Debug.WriteLine("[Fujify iOS Ads] Banner Ad created and added to screen.");
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
        }
        else if (visible)
        {
            SetupBannerAd();
        }
    }

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

