/*
 * This file is part of Fujify.
 *
 * Copyright (C) 2024 Isidore Paulin contact@ipweb.dev
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;

namespace Fujify.Services
{
    public interface IAdService
    {
        bool ShouldShowAds { get; }
        bool IsAdSupportedOnCurrentPlatform { get; }
        void ShowSplashAd();
        void ShowInterstitialAd();
        void SetBannerVisible(bool visible);
    }

    public class AdService : IAdService
    {
        private readonly ILicenseService _licenseService;

        public static AdService? Instance { get; private set; }
        public static Action? PlatformShowSplashCallback { get; set; }
        public static Action? PlatformShowInterstitialCallback { get; set; }
        public static Action<bool>? PlatformSetBannerVisibleCallback { get; set; }
        public static event Action<bool>? BannerVisibilityChanged;

        public bool ShouldShowAds => !_licenseService.IsPro;
        public bool IsAdSupportedOnCurrentPlatform => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

        public AdService(ILicenseService? licenseService = null)
        {
            Instance = this;
            _licenseService = licenseService ?? LicenseService.Instance ?? new LicenseService();
            _licenseService.LicenseChanged += (s, status) =>
            {
                SetBannerVisible(ShouldShowAds);
            };
            LicenseService.AnyLicenseChanged += (s, status) =>
            {
                SetBannerVisible(ShouldShowAds);
            };
        }

        public void ShowSplashAd()
        {
            if (ShouldShowAds && IsAdSupportedOnCurrentPlatform)
            {
                PlatformShowSplashCallback?.Invoke();
            }
        }

        public void ShowInterstitialAd()
        {
            if (ShouldShowAds && IsAdSupportedOnCurrentPlatform)
            {
                PlatformShowInterstitialCallback?.Invoke();
            }
        }

        public void SetBannerVisible(bool visible)
        {
            if (IsAdSupportedOnCurrentPlatform)
            {
                bool show = visible && ShouldShowAds;
                PlatformSetBannerVisibleCallback?.Invoke(show);
                BannerVisibilityChanged?.Invoke(show);
            }
        }
    }
}
