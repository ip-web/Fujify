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
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Fujify.Common;
using Fujify.Models;

namespace Fujify.Services
{
    public class LicensePayload
    {
        public string? Product { get; set; } = "fujifier";
        public string? Tier { get; set; } = "pro";
        public string? Email { get; set; }
        public string? Issued { get; set; }
    }

    public class LicenseService : ILicenseService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString
        };

        private const string RsaPublicKeyXml = @"<RSAKeyValue><Modulus>xNCFZILhKeCCfY0Vk7/cFywefj5rUmDPXrYciIbQ3jC5GgVNpdUsag/noI1tncmttOiwiqXZFO6K1VbYCbXbWujHA8iOfdYLU3JITwZ+6fKw5jJAuE9pptqpx0s47SZzieki6XrcX7mQ5o6eo+RXIhlfdeTveXmX71ndzCA6EAQBjpvOVE/7IYEtzkEVxwg2+SBAHeLTmjCygwWdePFhr9CRuVCQ+6AEvyA0ZRiHjyorowt8WM2KVaNLEALZMh7hxXQBcuh8qffVTS89MG1+N8NOurFRtQAG3ZCHp5YrMh/cpTdIrgB0jhwqPv0ZiGhQ9ORZNECzjQ/TUhtveX9mBQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        private readonly ISettingsService _settingsService;
        private readonly DeviceIdentityService _deviceIdentity;
        private readonly HttpClient _httpClient;
        private readonly LicenseStatus _currentStatus = new();

        public LicenseStatus CurrentStatus => _currentStatus;
        public bool IsPro => _currentStatus.IsPro;
        public int MaxFreeQueueSize => 10;
        public int DelaySeconds => 15;

        public string? AccountEmail => _settingsService.Settings.AccountEmail;
        public bool IsAccountLoggedIn => !string.IsNullOrWhiteSpace(_settingsService.Settings.AccessToken);
        public int UsedActivations => _currentStatus.UsedActivations;
        public int MaxActivations => _currentStatus.MaxActivations;
        public string SlotInfoText => $"{UsedActivations} of {MaxActivations} device slots active";

        public static Action<string, string?>? OnOAuthCallbackReceived { get; set; }

        private string? _pendingCodeVerifier;
        private string? _pendingExpectedState;

        public event EventHandler<LicenseStatus>? LicenseChanged;

        public LicenseService(ISettingsService? settingsService = null, HttpClient? httpClient = null)
        {
            _settingsService = settingsService ?? new SettingsService();
            _deviceIdentity = new DeviceIdentityService(_settingsService);
            _httpClient = httpClient ?? new HttpClient();

            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _httpClient.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("Fujifier-App", "2.1.0"));
            }

            Initialize();
        }

        public void Initialize()
        {
            string? storedKey = _settingsService.Settings.LicenseKey;
            string? storedEmail = _settingsService.Settings.LicenseeEmail ?? _settingsService.Settings.AccountEmail;
            var offlineExpiry = _settingsService.Settings.OfflineTokenExpires;

            // 1. Check local / dev override key
            if (!string.IsNullOrWhiteSpace(storedKey))
            {
                if (IsDevOverrideKey(storedKey))
                {
                    SetProState(storedKey, "pro-developer@fujifier.net", LicenseSource.LicenseKey, 1, 3);
                    return;
                }

                if (VerifyLegacyRsaKey(storedKey, out var rsaEmail))
                {
                    SetProState(storedKey, rsaEmail ?? storedEmail, LicenseSource.LicenseKey, 1, 3);
                    return;
                }
            }

            // 2. Check offline entitlement token (valid for 7 days without internet)
            if (!string.IsNullOrWhiteSpace(_settingsService.Settings.OfflineToken) &&
                offlineExpiry.HasValue && offlineExpiry.Value > DateTime.UtcNow)
            {
                SetProState(
                    storedKey,
                    storedEmail,
                    IsAccountLoggedIn ? LicenseSource.StripeCheckout : LicenseSource.LicenseKey,
                    _settingsService.Settings.UsedActivations > 0 ? _settingsService.Settings.UsedActivations : 1,
                    _settingsService.Settings.MaxActivations > 0 ? _settingsService.Settings.MaxActivations : 3,
                    offlineExpiry);

                // Run background verification to refresh token if network is available
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(3000);
                        await VerifyCurrentLicenseAsync(forceNetwork: true);
                    }
                    catch { }
                });
                return;
            }

            // 3. If stored key exists but token expired, verify online
            if (!string.IsNullOrWhiteSpace(storedKey))
            {
                SetProState(storedKey, storedEmail, LicenseSource.LicenseKey, _settingsService.Settings.UsedActivations, _settingsService.Settings.MaxActivations);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await VerifyCurrentLicenseAsync(forceNetwork: true);
                    }
                    catch { }
                });
                return;
            }

            // Default to Free Tier
            SetFreeState();
        }

        public async Task<LicenseActivationResult> ActivateLicenseKeyAsync(string licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                return new LicenseActivationResult(false, "License key cannot be empty.");
            }

            string cleanKey = licenseKey.Trim();

            // 1. Local Developer / Offline test override
            if (IsDevOverrideKey(cleanKey))
            {
                SetProState(cleanKey, "pro-developer@fujifier.net", LicenseSource.LicenseKey, 1, 3);
                SaveLicenseSettings(cleanKey, "pro-developer@fujifier.net", 1, 3);
                LicenseChanged?.Invoke(this, _currentStatus);
                return new LicenseActivationResult(true, null, _currentStatus);
            }

            // 2. Legacy RSA offline cryptographic key
            if (VerifyLegacyRsaKey(cleanKey, out var rsaEmail))
            {
                SetProState(cleanKey, rsaEmail, LicenseSource.LicenseKey, 1, 3);
                SaveLicenseSettings(cleanKey, rsaEmail, 1, 3);
                LicenseChanged?.Invoke(this, _currentStatus);
                return new LicenseActivationResult(true, null, _currentStatus);
            }

            // 3. Centralized Website API Activation (Fujifier-website: /wp-json/fujifier/v1/license/activate)
            try
            {
                string deviceId = _deviceIdentity.GetOrCreateDeviceId();
                string deviceName = _deviceIdentity.GetDeviceName();
                string platform = _deviceIdentity.GetPlatform();

                var requestPayload = new LicenseActivationRequest
                {
                    LicenseKey = cleanKey,
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Platform = platform,
                    AppVersion = "2.1.0"
                };

                string url = $"{GetApiBaseUrl()}/license/activate";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

                if (!string.IsNullOrWhiteSpace(_settingsService.Settings.AccessToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settingsService.Settings.AccessToken);
                }

                using var response = await _httpClient.SendAsync(request);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string errorMsg = ParseApiErrorMessage(responseJson) ?? $"Activation failed (HTTP {(int)response.StatusCode}).";
                    return new LicenseActivationResult(false, errorMsg);
                }

                var res = JsonSerializer.Deserialize<LicenseActivationResponse>(responseJson, JsonOptions);
                if (res == null || !res.Success)
                {
                    string errorMsg = res?.Message ?? "Server rejected license activation.";
                    return new LicenseActivationResult(false, errorMsg);
                }

                DateTime? offlineExpiry = null;
                if (!string.IsNullOrWhiteSpace(res.OfflineExpires) && DateTime.TryParse(res.OfflineExpires, out var dt))
                {
                    offlineExpiry = dt;
                }

                int used = res.UsedActivations > 0 ? res.UsedActivations : 1;
                int max = res.MaxActivations > 0 ? res.MaxActivations : 3;

                SetProState(cleanKey, _settingsService.Settings.LicenseeEmail, LicenseSource.LicenseKey, used, max, offlineExpiry);
                SaveLicenseSettings(cleanKey, _settingsService.Settings.LicenseeEmail, used, max, res.OfflineToken, offlineExpiry);

                LicenseChanged?.Invoke(this, _currentStatus);
                return new LicenseActivationResult(true, null, _currentStatus);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LicenseService] Activation network error: {ex.Message}");
                return new LicenseActivationResult(false, $"Network error connecting to licensing server: {ex.Message}");
            }
        }

        public async Task<LicenseActivationResult> LoginWithAccountAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return new LicenseActivationResult(false, "Please provide both email and password.");
            }

            try
            {
                var payload = new AuthLoginRequest
                {
                    Email = email.Trim(),
                    Password = password
                };

                string url = $"{GetApiBaseUrl()}/auth/login";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string errorMsg = ParseApiErrorMessage(responseJson) ?? "Invalid credentials. Please verify your email and password.";
                    return new LicenseActivationResult(false, errorMsg);
                }

                var loginRes = JsonSerializer.Deserialize<AuthLoginResponse>(responseJson, JsonOptions);
                if (loginRes == null || !loginRes.Success)
                {
                    return new LicenseActivationResult(false, loginRes?.Message ?? "Login failed.");
                }

                // Store account tokens and user details
                _settingsService.Settings.AccessToken = loginRes.AccessToken;
                _settingsService.Settings.RefreshToken = loginRes.RefreshToken;
                _settingsService.Settings.AccountEmail = loginRes.User?.Email ?? email.Trim();
                _settingsService.Settings.AccountDisplayName = loginRes.User?.DisplayName;
                _settingsService.Settings.LicenseeEmail = loginRes.User?.Email ?? email.Trim();
                _settingsService.Save();

                // Look for an active license on this account
                UserLicenseDto? activeLicense = null;
                if (loginRes.Licenses != null && loginRes.Licenses.Count > 0)
                {
                    activeLicense = loginRes.Licenses.Find(l => string.Equals(l.Status, "active", StringComparison.OrdinalIgnoreCase))
                                 ?? loginRes.Licenses[0];
                }

                if (activeLicense != null && !string.IsNullOrWhiteSpace(activeLicense.LicenseKey))
                {
                    // Activate this device using the account's license
                    return await ActivateLicenseKeyAsync(activeLicense.LicenseKey);
                }

                LicenseChanged?.Invoke(this, _currentStatus);
                return new LicenseActivationResult(true, "Signed in successfully. No active Pro license found on this account.", _currentStatus);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LicenseService] Login network error: {ex.Message}");
                return new LicenseActivationResult(false, $"Connection error: {ex.Message}");
            }
        }

        public async Task<LicenseActivationResult> LoginWithGoogleOAuthAsync(Action<string>? statusCallback = null, CancellationToken cancellationToken = default)
        {
            byte[] verifierBytes = new byte[32];
            RandomNumberGenerator.Fill(verifierBytes);
            string codeVerifier = Base64UrlEncode(verifierBytes);

            using var sha256 = SHA256.Create();
            string codeChallenge = Base64UrlEncode(sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)));
            string expectedState = Guid.NewGuid().ToString("N");

            _pendingCodeVerifier = codeVerifier;
            _pendingExpectedState = expectedState;

            string redirectUri;
            HttpListener? listener = null;

            if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
            {
                redirectUri = "fujifier://oauth-callback";
            }
            else
            {
                try
                {
                    int port = GetAvailableLoopbackPort();
                    redirectUri = $"http://127.0.0.1:{port}/callback";
                    listener = new HttpListener();
                    listener.Prefixes.Add($"http://127.0.0.1:{port}/callback/");
                    listener.Start();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LicenseService] Could not start HttpListener: {ex.Message}");
                    listener = null;
                    redirectUri = "fujifier://oauth-callback";
                }
            }

            var tcs = new TaskCompletionSource<(string Code, string? State)>(TaskCreationOptions.RunContinuationsAsynchronously);

            OnOAuthCallbackReceived = (code, state) => tcs.TrySetResult((code, state));

            if (listener != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var ctx = await listener.GetContextAsync();
                        string? code = ctx.Request.QueryString["code"];
                        string? state = ctx.Request.QueryString["state"];

                        string responseHtml = @"<!DOCTYPE html><html><head><meta charset='utf-8'><title>Fujifier - Authorized</title><style>body{background:#09090B;color:#F4F4F6;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;}.card{background:#121216;border:1px solid rgba(255,255,255,0.08);border-radius:20px;padding:40px;max-width:420px;width:90%;text-align:center;box-shadow:0 20px 50px rgba(0,0,0,0.6);}.badge{display:inline-block;background:rgba(0,154,102,0.15);color:#00C781;border:1px solid rgba(0,154,102,0.3);border-radius:999px;padding:6px 16px;font-weight:600;font-size:13px;margin-bottom:16px;}h2{margin:0 0 10px;font-size:22px;}p{color:#8E8E93;font-size:14px;line-height:1.5;margin:0;}</style></head><body><div class='card'><div class='badge'>✔ Authorized</div><h2>Welcome to Fujifier</h2><p>You have signed in to Fujifier.<br>You can now close this tab and return to the application.</p></div></body></html>";
                        byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        ctx.Response.ContentLength64 = buffer.Length;
                        await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        ctx.Response.OutputStream.Close();

                        tcs.TrySetResult((code ?? string.Empty, state));
                    }
                    catch { }
                });
            }

            try
            {
                string authUrl = $"{GetWebsiteBaseUrl()}/auth/app-handoff?redirect_uri={Uri.EscapeDataString(redirectUri)}&code_challenge={codeChallenge}&state={expectedState}";
                statusCallback?.Invoke("Opening browser for authentication...");
                CrossPlatformLauncher.OpenUrl(authUrl);

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                using (linkedCts.Token.Register(() => tcs.TrySetCanceled()))
                {
                    var (code, state) = await tcs.Task;

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        return new LicenseActivationResult(false, "Authorization cancelled or no code received.");
                    }

                    if (!string.IsNullOrWhiteSpace(state) && !string.Equals(state, expectedState, StringComparison.Ordinal))
                    {
                        return new LicenseActivationResult(false, "Security state mismatch. Authorization rejected.");
                    }

                    statusCallback?.Invoke("Exchanging authorization code...");
                    return await CompleteOAuthCallbackAsync(code, codeVerifier, state);
                }
            }
            catch (OperationCanceledException)
            {
                return new LicenseActivationResult(false, "Google Sign-In timed out or was cancelled.");
            }
            finally
            {
                OnOAuthCallbackReceived = null;
                try
                {
                    listener?.Stop();
                    listener?.Close();
                }
                catch { }
            }
        }

        public async Task<LicenseActivationResult> CompleteOAuthCallbackAsync(string code, string? codeVerifier = null, string? state = null)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return new LicenseActivationResult(false, "Authorization code is required.");
            }

            string verifier = codeVerifier ?? _pendingCodeVerifier ?? string.Empty;

            try
            {
                var exchangePayload = new PkceExchangeRequest
                {
                    Code = code.Trim(),
                    CodeVerifier = verifier
                };

                string url = $"{GetApiBaseUrl()}/auth/exchange-token";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(JsonSerializer.Serialize(exchangePayload), Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string errorMsg = ParseApiErrorMessage(responseJson) ?? $"Token exchange failed (HTTP {(int)response.StatusCode}).";
                    return new LicenseActivationResult(false, errorMsg);
                }

                var authRes = JsonSerializer.Deserialize<AuthLoginResponse>(responseJson, JsonOptions);
                if (authRes == null || !authRes.Success)
                {
                    return new LicenseActivationResult(false, authRes?.Message ?? "Authorization exchange failed.");
                }

                // Store account tokens and user details
                _settingsService.Settings.AccessToken = authRes.AccessToken;
                _settingsService.Settings.RefreshToken = authRes.RefreshToken;
                _settingsService.Settings.AccountEmail = authRes.User?.Email;
                _settingsService.Settings.AccountDisplayName = authRes.User?.DisplayName;
                _settingsService.Settings.LicenseeEmail = authRes.User?.Email;
                _settingsService.Save();

                // Check for active license
                UserLicenseDto? activeLicense = null;
                if (authRes.Licenses != null && authRes.Licenses.Count > 0)
                {
                    activeLicense = authRes.Licenses.Find(l => string.Equals(l.Status, "active", StringComparison.OrdinalIgnoreCase))
                                 ?? authRes.Licenses[0];
                }

                if (activeLicense != null && !string.IsNullOrWhiteSpace(activeLicense.LicenseKey))
                {
                    return await ActivateLicenseKeyAsync(activeLicense.LicenseKey);
                }

                SetFreeState();
                LicenseChanged?.Invoke(this, _currentStatus);
                return new LicenseActivationResult(true, $"Signed in as {authRes.User?.Email}. No active Pro license found on fujifier.cc.", _currentStatus);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LicenseService] Token exchange error: {ex.Message}");
                return new LicenseActivationResult(false, $"Connection error during authorization: {ex.Message}");
            }
        }

        public async Task<LicenseActivationResult> LoginWithGoogleIdTokenAsync(string googleIdToken)
        {
            if (string.IsNullOrWhiteSpace(googleIdToken))
            {
                return new LicenseActivationResult(false, "Google ID token is required.");
            }

            try
            {
                var payload = new AuthLoginRequest
                {
                    GoogleIdToken = googleIdToken.Trim()
                };

                string url = $"{GetApiBaseUrl()}/auth/login";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string errorMsg = ParseApiErrorMessage(responseJson) ?? "Google authentication failed.";
                    return new LicenseActivationResult(false, errorMsg);
                }

                var loginRes = JsonSerializer.Deserialize<AuthLoginResponse>(responseJson, JsonOptions);
                if (loginRes == null || !loginRes.Success)
                {
                    return new LicenseActivationResult(false, loginRes?.Message ?? "Google login failed.");
                }

                _settingsService.Settings.AccessToken = loginRes.AccessToken;
                _settingsService.Settings.RefreshToken = loginRes.RefreshToken;
                _settingsService.Settings.AccountEmail = loginRes.User?.Email;
                _settingsService.Settings.AccountDisplayName = loginRes.User?.DisplayName;
                _settingsService.Settings.LicenseeEmail = loginRes.User?.Email;
                _settingsService.Save();

                UserLicenseDto? activeLicense = null;
                if (loginRes.Licenses != null && loginRes.Licenses.Count > 0)
                {
                    activeLicense = loginRes.Licenses.Find(l => string.Equals(l.Status, "active", StringComparison.OrdinalIgnoreCase))
                                 ?? loginRes.Licenses[0];
                }

                if (activeLicense != null && !string.IsNullOrWhiteSpace(activeLicense.LicenseKey))
                {
                    return await ActivateLicenseKeyAsync(activeLicense.LicenseKey);
                }

                SetFreeState();
                LicenseChanged?.Invoke(this, _currentStatus);
                return new LicenseActivationResult(true, $"Signed in with Google as {loginRes.User?.Email}. No active Pro license found on fujifier.cc.", _currentStatus);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LicenseService] Google ID token login error: {ex.Message}");
                return new LicenseActivationResult(false, $"Connection error: {ex.Message}");
            }
        }

        public async Task<LicenseActivationResult> RetrieveLicenseFromAccountAsync()
        {
            if (string.IsNullOrWhiteSpace(_settingsService.Settings.AccessToken))
            {
                return new LicenseActivationResult(false, "You are not signed in to a Fujifier account.");
            }

            try
            {
                string url = $"{GetApiBaseUrl()}/auth/me";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settingsService.Settings.AccessToken);

                using var response = await _httpClient.SendAsync(request);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    bool refreshed = await TryRefreshTokenAsync();
                    if (refreshed)
                    {
                        return await RetrieveLicenseFromAccountAsync();
                    }
                    return new LicenseActivationResult(false, "Session expired. Please sign in again.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    string errorMsg = ParseApiErrorMessage(responseJson) ?? $"Failed to retrieve account details (HTTP {(int)response.StatusCode}).";
                    return new LicenseActivationResult(false, errorMsg);
                }

                var meRes = JsonSerializer.Deserialize<AuthMeResponse>(responseJson, JsonOptions);
                if (meRes == null || !meRes.Success)
                {
                    return new LicenseActivationResult(false, meRes?.Message ?? "Could not read account details.");
                }

                if (meRes.User != null)
                {
                    _settingsService.Settings.AccountEmail = meRes.User.Email;
                    _settingsService.Settings.AccountDisplayName = meRes.User.DisplayName;
                    _settingsService.Settings.LicenseeEmail = meRes.User.Email;
                    _settingsService.Save();
                }

                UserLicenseDto? activeLicense = null;
                if (meRes.Licenses != null && meRes.Licenses.Count > 0)
                {
                    activeLicense = meRes.Licenses.Find(l => string.Equals(l.Status, "active", StringComparison.OrdinalIgnoreCase))
                                 ?? meRes.Licenses[0];
                }

                if (activeLicense != null && !string.IsNullOrWhiteSpace(activeLicense.LicenseKey))
                {
                    return await ActivateLicenseKeyAsync(activeLicense.LicenseKey);
                }

                SetFreeState();
                LicenseChanged?.Invoke(this, _currentStatus);
                return new LicenseActivationResult(true, $"Signed in as {_settingsService.Settings.AccountEmail}. No active Pro license found on fujifier.cc.", _currentStatus);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LicenseService] Retrieve license network error: {ex.Message}");
                return new LicenseActivationResult(false, $"Connection error: {ex.Message}");
            }
        }

        private async Task<bool> TryRefreshTokenAsync()
        {
            string? refreshToken = _settingsService.Settings.RefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken)) return false;

            try
            {
                string url = $"{GetApiBaseUrl()}/auth/refresh";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                var payload = new { refresh_token = refreshToken };
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return false;

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
                {
                    _settingsService.Settings.AccessToken = tokenProp.GetString();
                    _settingsService.Save();
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static int GetAvailableLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public async Task<bool> DeactivateLicenseAsync()
        {
            string? key = _currentStatus.LicenseKey;
            string deviceId = _deviceIdentity.GetOrCreateDeviceId();

            // Try to release slot on server
            if (!string.IsNullOrWhiteSpace(key) && !IsDevOverrideKey(key))
            {
                try
                {
                    var deactReq = new LicenseDeactivationRequest
                    {
                        LicenseKey = key,
                        DeviceId = deviceId
                    };

                    string url = $"{GetApiBaseUrl()}/license/deactivate";
                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Content = new StringContent(JsonSerializer.Serialize(deactReq), Encoding.UTF8, "application/json");

                    if (!string.IsNullOrWhiteSpace(_settingsService.Settings.AccessToken))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settingsService.Settings.AccessToken);
                    }

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _httpClient.SendAsync(request, cts.Token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LicenseService] Remote deactivation failed (continuing local deactivation): {ex.Message}");
                }
            }

            // Clear local license state
            SetFreeState();
            _settingsService.Settings.LicenseKey = null;
            _settingsService.Settings.OfflineToken = null;
            _settingsService.Settings.OfflineTokenExpires = null;
            _settingsService.Settings.UsedActivations = 0;
            _settingsService.Save();

            LicenseChanged?.Invoke(this, _currentStatus);
            return true;
        }

        public Task<bool> LogoutAccountAsync()
        {
            _settingsService.Settings.AccessToken = null;
            _settingsService.Settings.RefreshToken = null;
            _settingsService.Settings.AccountEmail = null;
            _settingsService.Settings.AccountDisplayName = null;
            _settingsService.Save();

            _currentStatus.IsAccountBound = false;
            _currentStatus.AccountEmail = null;
            LicenseChanged?.Invoke(this, _currentStatus);
            return Task.FromResult(true);
        }

        public async Task<LicenseActivationResult> VerifyCurrentLicenseAsync(bool forceNetwork = false)
        {
            string? key = _settingsService.Settings.LicenseKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                SetFreeState();
                return new LicenseActivationResult(false, "No license to verify.");
            }

            if (IsDevOverrideKey(key) || VerifyLegacyRsaKey(key, out _))
            {
                return new LicenseActivationResult(true, null, _currentStatus);
            }

            // Check if offline entitlement token is valid
            var offlineExpiry = _settingsService.Settings.OfflineTokenExpires;
            if (!forceNetwork && offlineExpiry.HasValue && offlineExpiry.Value > DateTime.UtcNow)
            {
                return new LicenseActivationResult(true, null, _currentStatus);
            }

            // Perform heartbeat check with server
            try
            {
                string deviceId = _deviceIdentity.GetOrCreateDeviceId();
                var req = new LicenseVerificationRequest
                {
                    LicenseKey = key,
                    DeviceId = deviceId
                };

                string url = $"{GetApiBaseUrl()}/license/verify";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var verRes = JsonSerializer.Deserialize<LicenseVerificationResponse>(responseJson, JsonOptions);
                    if (verRes != null && verRes.Valid)
                    {
                        if (!string.IsNullOrWhiteSpace(verRes.OfflineExpires) && DateTime.TryParse(verRes.OfflineExpires, out var newExpiry))
                        {
                            _settingsService.Settings.OfflineToken = verRes.OfflineToken ?? _settingsService.Settings.OfflineToken;
                            _settingsService.Settings.OfflineTokenExpires = newExpiry;
                            _settingsService.Save();
                            _currentStatus.ExpirationDate = newExpiry;
                        }
                        return new LicenseActivationResult(true, null, _currentStatus);
                    }
                }

                // If server explicitly returned 403 / 404 (e.g. license revoked or expired)
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                    response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    SetFreeState();
                    _settingsService.Settings.LicenseKey = null;
                    _settingsService.Settings.OfflineToken = null;
                    _settingsService.Save();
                    LicenseChanged?.Invoke(this, _currentStatus);
                    return new LicenseActivationResult(false, "License is no longer active on the server.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LicenseService] Verification network error: {ex.Message}");
                // If network is unreachable, fallback to offline grace period if not expired
                if (offlineExpiry.HasValue && offlineExpiry.Value > DateTime.UtcNow)
                {
                    return new LicenseActivationResult(true, null, _currentStatus);
                }
            }

            return new LicenseActivationResult(true, null, _currentStatus);
        }

        public bool VerifyLicenseKey(string licenseKey, out string? email, out string? error)
        {
            email = null;
            error = null;

            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                error = "License key is empty.";
                return false;
            }

            string key = licenseKey.Trim();

            if (IsDevOverrideKey(key))
            {
                email = "pro-developer@fujifier.net";
                return true;
            }

            if (VerifyLegacyRsaKey(key, out email))
            {
                return true;
            }

            // Website key format: FUJI-XXXX-XXXX-XXXX
            if (key.StartsWith("FUJI-", StringComparison.OrdinalIgnoreCase) && key.Length >= 14)
            {
                return true;
            }

            error = "Invalid license key format. Expected 'FUJI-XXXX-XXXX-XXXX'.";
            return false;
        }

        private static bool IsDevOverrideKey(string key)
        {
            return key.Equals("FUJIFIER-PRO-DEV-OVERRIDE", StringComparison.OrdinalIgnoreCase) ||
                   key.Equals("FUJIFY-PRO-DEV-OVERRIDE", StringComparison.OrdinalIgnoreCase) ||
                   key.Equals("FUJIFIER-LIFETIME-PRO-TEST", StringComparison.OrdinalIgnoreCase) ||
                   key.Equals("FUJIFY-LIFETIME-PRO-TEST", StringComparison.OrdinalIgnoreCase);
        }

        private static bool VerifyLegacyRsaKey(string key, out string? email)
        {
            email = null;
            try
            {
                string[] parts = key.Split('.');
                if (parts.Length != 3 || (!parts[0].Equals("FUJIFIER-PRO", StringComparison.OrdinalIgnoreCase) &&
                                          !parts[0].Equals("FUJIFY-PRO", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                string payloadBase64 = parts[1];
                string signatureBase64 = parts[2];

                byte[] dataBytes = Encoding.UTF8.GetBytes(payloadBase64);
                byte[] signatureBytes = Convert.FromBase64String(signatureBase64);

                using var rsa = RSA.Create();
                rsa.FromXmlString(RsaPublicKeyXml);

                if (!rsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    return false;
                }

                string payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(payloadBase64));
                var payload = JsonSerializer.Deserialize<LicensePayload>(payloadJson);
                if (payload == null) return false;

                email = payload.Email;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SetProState(string? key, string? email, LicenseSource source, int used = 1, int max = 3, DateTime? expiry = null)
        {
            _currentStatus.Tier = LicenseTier.Pro;
            _currentStatus.Source = source;
            _currentStatus.LicenseKey = key;
            _currentStatus.LicenseeEmail = email;
            _currentStatus.UsedActivations = used;
            _currentStatus.MaxActivations = max;
            _currentStatus.ExpirationDate = expiry;
            _currentStatus.IsAccountBound = IsAccountLoggedIn;
            _currentStatus.AccountEmail = AccountEmail;
        }

        private void SetFreeState()
        {
            _currentStatus.Tier = LicenseTier.Free;
            _currentStatus.Source = LicenseSource.None;
            _currentStatus.LicenseKey = null;
            _currentStatus.LicenseeEmail = null;
            _currentStatus.UsedActivations = 0;
            _currentStatus.MaxActivations = 3;
            _currentStatus.ExpirationDate = null;
            _currentStatus.IsAccountBound = IsAccountLoggedIn;
            _currentStatus.AccountEmail = AccountEmail;
        }

        private void SaveLicenseSettings(string key, string? email, int used, int max, string? offlineToken = null, DateTime? offlineExpiry = null)
        {
            _settingsService.Settings.LicenseKey = key;
            if (!string.IsNullOrWhiteSpace(email))
            {
                _settingsService.Settings.LicenseeEmail = email;
            }
            _settingsService.Settings.UsedActivations = used;
            _settingsService.Settings.MaxActivations = max;
            if (offlineToken != null)
            {
                _settingsService.Settings.OfflineToken = offlineToken;
            }
            if (offlineExpiry.HasValue)
            {
                _settingsService.Settings.OfflineTokenExpires = offlineExpiry.Value;
            }
            _settingsService.Settings.LosslessCompression = true;
            _settingsService.Settings.MaxConcurrentConversions = Math.Max(1, Environment.ProcessorCount / 2);
            _settingsService.Save();
        }

        public string GetWebsiteBaseUrl()
        {
            string api = GetApiBaseUrl();
            if (api.StartsWith("https://fujifier.cc", StringComparison.OrdinalIgnoreCase))
            {
                return "https://fujifier.cc";
            }
            try
            {
                var uri = new Uri(api);
                return $"{uri.Scheme}://{uri.Authority}";
            }
            catch
            {
                return "https://fujifier.cc";
            }
        }

        private string GetApiBaseUrl()
        {
            string url = _settingsService.Settings.LicenseApiBaseUrl;
            if (string.IsNullOrWhiteSpace(url) || url.Contains("fujifier.net"))
            {
                url = "https://fujifier.cc/wp-json/fujifier/v1";
            }
            return url.TrimEnd('/');
        }

        private static string? ParseApiErrorMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("code", out var codeProp))
                {
                    string code = codeProp.GetString() ?? "";
                    if (code == "activation_limit_reached")
                    {
                        return "Activation limit reached (3 of 3 devices used). Please deactivate a device in your Fujifier account dashboard to activate this device.";
                    }
                    if (code == "invalid_license_key")
                    {
                        return "License key not recognized. Please check your license key.";
                    }
                    if (code == "license_inactive" || code == "license_expired")
                    {
                        return "This license is inactive or expired. Please check your account on fujifier.cc.";
                    }
                }

                if (root.TryGetProperty("message", out var msgProp))
                {
                    return msgProp.GetString();
                }
            }
            catch { }

            return null;
        }

        public static string GenerateTestKey(string email, string rsaPrivateKeyXml)
        {
            var payload = new LicensePayload
            {
                Product = "fujifier",
                Tier = "pro",
                Email = email,
                Issued = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };

            string json = JsonSerializer.Serialize(payload);
            string payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            byte[] dataBytes = Encoding.UTF8.GetBytes(payloadB64);

            using var rsa = RSA.Create();
            rsa.FromXmlString(rsaPrivateKeyXml);
            byte[] signature = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            string sigB64 = Convert.ToBase64String(signature);

            return $"FUJIFIER-PRO.{payloadB64}.{sigB64}";
        }
    }
}
