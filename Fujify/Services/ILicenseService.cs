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
using System.Threading;
using System.Threading.Tasks;

namespace Fujify.Services
{
    public enum LicenseTier
    {
        Free,
        Pro
    }

    public enum LicenseSource
    {
        None,
        LicenseKey,
        StripeCheckout,
        MicrosoftStore,
        GooglePlay,
        AppleAppStore
    }

    public class LicenseStatus
    {
        public LicenseTier Tier { get; set; } = LicenseTier.Free;
        public LicenseSource Source { get; set; } = LicenseSource.None;
        public string? LicenseeEmail { get; set; }
        public string? LicenseKey { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public int MaxActivations { get; set; } = 3;
        public int UsedActivations { get; set; } = 0;
        public string? PlanType { get; set; }
        public bool IsAccountBound { get; set; }
        public string? AccountEmail { get; set; }
        public bool IsPro => Tier == LicenseTier.Pro;
    }

    public class LicenseActivationResult
    {
        public bool Success { get; }
        public string? ErrorMessage { get; }
        public LicenseStatus? Status { get; }

        public LicenseActivationResult(bool success, string? errorMessage = null, LicenseStatus? status = null)
        {
            Success = success;
            ErrorMessage = errorMessage;
            Status = status;
        }
    }

    public interface ILicenseService
    {
        LicenseStatus CurrentStatus { get; }
        bool IsPro { get; }
        int MaxFreeQueueSize { get; }
        int DelaySeconds { get; }
        string? AccountEmail { get; }
        bool IsAccountLoggedIn { get; }
        int UsedActivations { get; }
        int MaxActivations { get; }
        string SlotInfoText { get; }

        event EventHandler<LicenseStatus>? LicenseChanged;

        void Initialize();
        void SyncFromSettings();
        Task<LicenseActivationResult> ActivateLicenseKeyAsync(string licenseKey);
        Task<LicenseActivationResult> LoginWithAccountAsync(string email, string password);
        Task<LicenseActivationResult> LoginWithGoogleOAuthAsync(Action<string>? statusCallback = null, CancellationToken cancellationToken = default);
        Task<LicenseActivationResult> LoginWithGoogleIdTokenAsync(string googleIdToken);
        Task<LicenseActivationResult> RetrieveLicenseFromAccountAsync();
        Task<LicenseActivationResult> CompleteOAuthCallbackAsync(string code, string? codeVerifier = null, string? state = null);
        Task<bool> DeactivateLicenseAsync();
        Task<bool> LogoutAccountAsync();
        Task<LicenseActivationResult> VerifyCurrentLicenseAsync(bool forceNetwork = false);
        bool VerifyLicenseKey(string licenseKey, out string? email, out string? error);
    }
}
