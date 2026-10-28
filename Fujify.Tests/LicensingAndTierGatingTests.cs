using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fujify.Models;
using Fujify.Services;
using Fujify.ViewModels;
using Xunit;

namespace Fujify.Tests
{
    [Collection("Registry")]
    public class LicensingAndTierGatingTests
    {
        private const string RsaPrivateKeyXml = @"<RSAKeyValue><Modulus>xNCFZILhKeCCfY0Vk7/cFywefj5rUmDPXrYciIbQ3jC5GgVNpdUsag/noI1tncmttOiwiqXZFO6K1VbYCbXbWujHA8iOfdYLU3JITwZ+6fKw5jJAuE9pptqpx0s47SZzieki6XrcX7mQ5o6eo+RXIhlfdeTveXmX71ndzCA6EAQBjpvOVE/7IYEtzkEVxwg2+SBAHeLTmjCygwWdePFhr9CRuVCQ+6AEvyA0ZRiHjyorowt8WM2KVaNLEALZMh7hxXQBcuh8qffVTS89MG1+N8NOurFRtQAG3ZCHp5YrMh/cpTdIrgB0jhwqPv0ZiGhQ9ORZNECzjQ/TUhtveX9mBQ==</Modulus><Exponent>AQAB</Exponent><P>3blMhv4bMYJ0jBqLOIVA71LFLW8PYo3ncYikI+yS3HiqjECBbwwNeTGq/O0ccwWvIb2P6XLKQP5Lb35a1lspeGsRcjbB9HaXi5J4Yf/iaxEiwtS4msRN4DGJgf9JFJA72zF6COAJqjGQHkF+ZpJT7WF0jOwRL3zOxc+tYJB6H68=</P><Q>4z1wb20vwjCBMV0YXDEKIIzPxRcdsiLdfxoPCArEXilMDZoWscAmGg3vtwGVvk0L642lz57iFmHRPPhnr/hFW4n4lbQW4tWFsX/gYw4HNOrNZpiWYjuf3iUQIvXVprgcuNngzS1WlxxPOijvk0c8/5tMXXwj6F32Gie+oWVdbos=</Q><DP>K2je+cMAUfYnmVKNFVjJeNGstxN6YKJFwO2urTXhHvck9OagMLycWrEFuBU4L7DaM/HoHh2SSZ0/6alEYZlhiij0NndQC+u4CeR4Wz989ddaBekd53cthApO0LvJdBQ1E9og+WHboo2nmLTb6XsWoXAmIH0LdE+sMABUAobm0hM=</DP><DQ>ggTTYk3894U9RntXxtggTUOEVebGDPi591F3rEhM1vuWEmpJ9eZVZq72q6VygnIfEeX4K3VjMx9A0zvmR98sABZPb4idV6iIWGksgEC0iuiWIc/Iq+glrrtSIk66sivB81pmr77zPn7qLgLiIUivdCjtZsfy2693cruu7TXMYcU=</DQ><InverseQ>iR/+jo6wOgkznEJ4e6SLr6Rb3fBKPpXexZ7xTtWGn5T/gf/ir0qSYyEQuGPXMrR9ZppBCFBfV+b8KI7907vnYTihE0pOb8XLbsi+PqgxbP3opMy6NHqtIkWiiCfY1gQLgitXHYelxkBXonthlPXYlkAUFxSxtRseXoWRtbql6yk=</InverseQ><D>a48p10xPkXtI1SSNbjmkJmXOJGAodtTBf5k27sX+z+8Aud+8F6zwoOYgR/YCugDGFeA02xDYGkBjT0QuvN/OCVi9qquoc8zOU8RZEBcUtPYVBzjfddqweqd1sZzCd5hT/EKU+dRTK11WeynmONtJYzFKojuDRHfXOM98D1ptYOJaOqVmicpdzWQW+L8LEgLNo4Al34tL0nU5d0aXEaUVQtSkSeuyI7i4LmDfW9WBu3Vet9Hy7qSOHPB9LHOi1mCQrB2+aUVjsm8MUttyvColAbza3fenI23ihR/XtIDhgHmQi8WWP4JNgAW9BtVRE1RTJgD3RDvz0vRzGlRVHWZU4Q==</D></RSAKeyValue>";

        private class MockSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new();
            public void Save() { }
            public void Load() { }
        }

        [Fact]
        public void LicenseService_DefaultState_ShouldBeFreeTier()
        {
            var settings = new MockSettingsService();
            var service = new LicenseService(settings);

            Assert.False(service.IsPro);
            Assert.Equal(LicenseTier.Free, service.CurrentStatus.Tier);
            Assert.Equal(10, service.MaxFreeQueueSize);
            Assert.Equal(15, service.DelaySeconds);
        }

        [Fact]
        public async Task LicenseService_ActivateDevOverrideKey_ShouldUnlockPro()
        {
            var settings = new MockSettingsService();
            var service = new LicenseService(settings);

            var result = await service.ActivateLicenseKeyAsync("FUJIFIER-PRO-DEV-OVERRIDE");

            Assert.True(result.Success);
            Assert.True(service.IsPro);
            Assert.Equal(LicenseTier.Pro, service.CurrentStatus.Tier);
            Assert.Equal("pro-developer@fujifier.net", service.CurrentStatus.LicenseeEmail);
        }

        [Fact]
        public async Task LicenseService_CryptographicKeyVerification_Lifecycle()
        {
            var settings = new MockSettingsService();
            var service = new LicenseService(settings);

            // 1. Generate valid signed key using private key
            string email = "photographer@example.com";
            string validKey = LicenseService.GenerateTestKey(email, RsaPrivateKeyXml);

            // 2. Activate key
            var result = await service.ActivateLicenseKeyAsync(validKey);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(service.IsPro);
            Assert.Equal(email, service.CurrentStatus.LicenseeEmail);

            // 3. Deactivate key
            await service.DeactivateLicenseAsync();
            Assert.False(service.IsPro);
            Assert.Equal(LicenseTier.Free, service.CurrentStatus.Tier);
        }

        [Fact]
        public async Task LicenseService_ForgedKey_ShouldBeRejected()
        {
            var settings = new MockSettingsService();
            var service = new LicenseService(settings);

            var result = await service.ActivateLicenseKeyAsync("FUJIFIER-PRO.invalidpayload.invalidsignature");
            Assert.False(result.Success);
            Assert.False(service.IsPro);
        }

        [Fact]
        public void PreferencesViewModel_FreeTier_ContextMenuCannotBeEnabled()
        {
            var settings = new MockSettingsService();
            var licenseService = new LicenseService(settings);
            var vm = new PreferencesViewModel(settings, licenseService);

            Assert.False(vm.IsPro);

            // Attempt to enable context menu as a free user
            vm.IsWindowsContextMenuEnabled = true;

            // Should remain disabled
            Assert.False(vm.IsWindowsContextMenuEnabled);
            Assert.True(vm.HasLicenseMessage);
        }

        [Fact]
        public async Task PreferencesViewModel_ProTier_ContextMenuCanBeEnabled()
        {
            var settings = new MockSettingsService();
            var licenseService = new LicenseService(settings);
            await licenseService.ActivateLicenseKeyAsync("FUJIFIER-PRO-DEV-OVERRIDE");

            var vm = new PreferencesViewModel(settings, licenseService);

            Assert.True(vm.IsPro);

            // Enable context menu as Pro user
            vm.IsWindowsContextMenuEnabled = true;
            Assert.True(vm.IsWindowsContextMenuEnabled);
        }

        [Fact]
        public void PreferencesViewModel_FreeTier_LosslessCompression_IsDisabledAndCannotBeEnabled()
        {
            var settings = new MockSettingsService();
            var licenseService = new LicenseService(settings);
            var vm = new PreferencesViewModel(settings, licenseService);

            Assert.False(vm.IsPro);
            Assert.False(vm.LosslessCompression);

            // Attempt to enable lossless compression on free tier
            vm.LosslessCompression = true;
            Assert.False(vm.LosslessCompression);
            Assert.True(vm.HasLicenseMessage);
            Assert.Contains("exclusive to Fujifier Pro", vm.LicenseMessageText);
        }

        [Fact]
        public void PreferencesViewModel_FreeTier_ConcurrentConversions_IsLockedToOne()
        {
            var settings = new MockSettingsService();
            settings.Settings.MaxConcurrentConversions = 8;
            var licenseService = new LicenseService(settings);
            var vm = new PreferencesViewModel(settings, licenseService);

            Assert.False(vm.IsPro);
            Assert.Equal(1, vm.MaxConcurrentConversions);
            Assert.Contains("Free tier: sequential conversion", vm.ConcurrencyHint);

            // Attempt to set concurrent conversions > 1
            vm.MaxConcurrentConversions = 4;
            Assert.Equal(1, vm.MaxConcurrentConversions);
            Assert.True(vm.HasLicenseMessage);
            Assert.Contains("exclusive to Fujifier Pro", vm.LicenseMessageText);
        }

        [Fact]
        public async Task PreferencesViewModel_ProTier_LosslessAndConcurrency_CanBeConfigured()
        {
            var settings = new MockSettingsService();
            var licenseService = new LicenseService(settings);
            await licenseService.ActivateLicenseKeyAsync("FUJIFIER-PRO-DEV-OVERRIDE");

            var vm = new PreferencesViewModel(settings, licenseService);

            Assert.True(vm.IsPro);

            vm.LosslessCompression = true;
            Assert.True(vm.LosslessCompression);

            vm.MaxConcurrentConversions = 4;
            Assert.Equal(4, vm.MaxConcurrentConversions);
        }

        [Fact]
        public async Task LicenseActivation_ShouldAutomaticallySetLosslessDngAndHalfThreads()
        {
            var settings = new MockSettingsService();
            settings.Settings.LosslessCompression = false;
            settings.Settings.MaxConcurrentConversions = 1;

            var licenseService = new LicenseService(settings);
            var vm = new PreferencesViewModel(settings, licenseService);

            // Initially Free tier: Lossless DNG disabled, concurrency locked to 1
            Assert.False(licenseService.IsPro);
            Assert.False(vm.LosslessCompression);
            Assert.Equal(1, vm.MaxConcurrentConversions);

            int expectedThreads = Math.Max(1, Environment.ProcessorCount / 2);

            // Activate Pro license
            var result = await licenseService.ActivateLicenseKeyAsync("FUJIFIER-PRO-DEV-OVERRIDE");
            Assert.True(result.Success);
            Assert.True(licenseService.IsPro);

            // Verify settings were automatically configured
            Assert.True(settings.Settings.LosslessCompression);
            Assert.Equal(expectedThreads, settings.Settings.MaxConcurrentConversions);

            // Verify PreferencesViewModel properties updated dynamically
            Assert.True(vm.LosslessCompression);
            Assert.Equal(expectedThreads, vm.MaxConcurrentConversions);
        }

        [Fact]
        public void MainViewModel_FreeTier_LosslessCompression_IsDisabled()
        {
            var settings = new MockSettingsService();
            settings.Settings.LosslessCompression = true;
            var licenseService = new LicenseService(settings);
            var vm = new MainViewModel(settingsService: settings, licenseService: licenseService);

            Assert.False(vm.IsPro);
            Assert.False(vm.LosslessCompression);

            // Attempt to toggle
            vm.LosslessCompression = true;
            Assert.False(vm.LosslessCompression);
        }

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

            public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_handler(request));
            }
        }

        [Fact]
        public void DeviceIdentityService_GeneratesAndPersistsDeviceId()
        {
            var settings = new MockSettingsService();
            var deviceService = new DeviceIdentityService(settings);

            string id1 = deviceService.GetOrCreateDeviceId();
            Assert.False(string.IsNullOrWhiteSpace(id1));
            Assert.Equal(id1, settings.Settings.DeviceId);

            // Calling again should return the exact same persisted ID
            string id2 = deviceService.GetOrCreateDeviceId();
            Assert.Equal(id1, id2);

            string name = deviceService.GetDeviceName();
            Assert.False(string.IsNullOrWhiteSpace(name));

            string platform = deviceService.GetPlatform();
            Assert.False(string.IsNullOrWhiteSpace(platform));
        }

        [Fact]
        public void LicenseService_WebsiteKeyFormat_Recognized()
        {
            var settings = new MockSettingsService();
            var service = new LicenseService(settings);

            bool isValid = service.VerifyLicenseKey("FUJI-A1B2-C3D4-E5F6", out _, out var error);
            Assert.True(isValid);
            Assert.Null(error);

            bool isInvalid = service.VerifyLicenseKey("INVALID-KEY", out _, out var err2);
            Assert.False(isInvalid);
            Assert.NotNull(err2);
        }

        [Fact]
        public void LicenseService_OfflineEntitlementToken_KeepsProActiveOffline()
        {
            var settings = new MockSettingsService();
            settings.Settings.LicenseKey = "FUJI-A1B2-C3D4-E5F6";
            settings.Settings.OfflineToken = "header.payload.signature";
            settings.Settings.OfflineTokenExpires = DateTime.UtcNow.AddDays(5);
            settings.Settings.UsedActivations = 1;
            settings.Settings.MaxActivations = 3;

            var service = new LicenseService(settings);

            Assert.True(service.IsPro);
            Assert.Equal("1 of 3 device slots active", service.SlotInfoText);
        }

        [Fact]
        public async Task LicenseService_MockHttp_ActivationSuccess_ActivatesAndSetsSlots()
        {
            var settings = new MockSettingsService();

            var handler = new MockHttpMessageHandler(req =>
            {
                if (req.RequestUri != null && req.RequestUri.ToString().Contains("/license/activate"))
                {
                    var responseObj = new LicenseActivationResponse
                    {
                        Success = true,
                        LicenseKey = "FUJI-TEST-KEY-0001",
                        PlanType = "pro_lifetime",
                        Status = "active",
                        UsedActivations = 2,
                        MaxActivations = 3,
                        OfflineToken = "signed.offline.jwt",
                        OfflineExpires = DateTime.UtcNow.AddDays(7).ToString("o")
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(responseObj))
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var httpClient = new HttpClient(handler);
            var service = new LicenseService(settings, httpClient);

            var result = await service.ActivateLicenseKeyAsync("FUJI-TEST-KEY-0001");

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(service.IsPro);
            Assert.Equal(2, service.UsedActivations);
            Assert.Equal(3, service.MaxActivations);
            Assert.Equal("2 of 3 device slots active", service.SlotInfoText);
        }

        [Fact]
        public async Task LicenseService_MockHttp_ActivationLimitReached_ReturnsFriendlyError()
        {
            var settings = new MockSettingsService();

            var handler = new MockHttpMessageHandler(req =>
            {
                string errorJson = @"{
                    ""code"": ""activation_limit_reached"",
                    ""message"": ""Activation limit reached (3/3 devices).""
                }";
                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent(errorJson)
                };
            });

            var httpClient = new HttpClient(handler);
            var service = new LicenseService(settings, httpClient);

            var result = await service.ActivateLicenseKeyAsync("FUJI-TEST-KEY-0002");

            Assert.False(result.Success);
            Assert.False(service.IsPro);
            Assert.Contains("Activation limit reached", result.ErrorMessage);
        }

        [Fact]
        public async Task LicenseService_MockHttp_AccountLoginSuccess_ActivatesUserLicense()
        {
            var settings = new MockSettingsService();

            var handler = new MockHttpMessageHandler(req =>
            {
                string uri = req.RequestUri?.ToString() ?? "";
                if (uri.Contains("/auth/login"))
                {
                    var loginResponse = new AuthLoginResponse
                    {
                        Success = true,
                        AccessToken = "jwt.access.token",
                        RefreshToken = "jwt.refresh.token",
                        User = new AuthUserDto
                        {
                            Id = 1,
                            Email = "photographer@ipweb.dev",
                            DisplayName = "Isidore"
                        },
                        Licenses = new List<UserLicenseDto>
                        {
                            new UserLicenseDto
                            {
                                Id = 10,
                                LicenseKey = "FUJI-USER-KEY-1234",
                                Status = "active",
                                MaxActivations = 3,
                                UsedActivations = 1
                            }
                        }
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(loginResponse))
                    };
                }

                if (uri.Contains("/license/activate"))
                {
                    var actResponse = new LicenseActivationResponse
                    {
                        Success = true,
                        LicenseKey = "FUJI-USER-KEY-1234",
                        Status = "active",
                        UsedActivations = 2,
                        MaxActivations = 3
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(actResponse))
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var httpClient = new HttpClient(handler);
            var service = new LicenseService(settings, httpClient);

            var result = await service.LoginWithAccountAsync("photographer@ipweb.dev", "SecurePassword123!");

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(service.IsPro);
            Assert.True(service.IsAccountLoggedIn);
            Assert.Equal("photographer@ipweb.dev", service.AccountEmail);
            Assert.Equal(2, service.UsedActivations);
        }

        [Fact]
        public async Task RetrieveLicenseFromAccountAsync_WhenLoggedInWithActiveLicense_ActivatesPro()
        {
            var settings = new MockSettingsService();
            settings.Settings.AccessToken = "valid_jwt_access_token";
            settings.Settings.AccountEmail = "pro-user@fujifier.cc";

            var handler = new MockHttpMessageHandler((req) =>
            {
                if (req.RequestUri!.AbsolutePath.EndsWith("/auth/me"))
                {
                    Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
                    Assert.Equal("valid_jwt_access_token", req.Headers.Authorization?.Parameter);

                    var meResponse = new AuthMeResponse
                    {
                        Success = true,
                        User = new AuthUserDto
                        {
                            Id = 42,
                            Email = "pro-user@fujifier.cc",
                            DisplayName = "Pro Photographer"
                        },
                        Licenses = new List<UserLicenseDto>
                        {
                            new()
                            {
                                Id = 101,
                                LicenseKey = "FUJI-RETRIEVE-KEY-1234",
                                Status = "active",
                                PlanType = "pro_lifetime",
                                MaxActivations = 3,
                                UsedActivations = 1
                            }
                        }
                    };

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(meResponse))
                    };
                }

                if (req.RequestUri!.AbsolutePath.EndsWith("/license/activate"))
                {
                    var actResponse = new LicenseActivationResponse
                    {
                        Success = true,
                        LicenseKey = "FUJI-RETRIEVE-KEY-1234",
                        Status = "active",
                        UsedActivations = 2,
                        MaxActivations = 3
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(actResponse))
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var httpClient = new HttpClient(handler);
            var service = new LicenseService(settings, httpClient);

            var result = await service.RetrieveLicenseFromAccountAsync();

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(service.IsPro);
            Assert.Equal("pro-user@fujifier.cc", service.AccountEmail);
            Assert.Equal("Pro Photographer", settings.Settings.AccountDisplayName);
            Assert.Equal("FUJI-RETRIEVE-KEY-1234", service.CurrentStatus.LicenseKey);
        }

        [Fact]
        public async Task LoginWithGoogleIdTokenAsync_WhenTokenIsValid_AuthenticatesAndActivatesPro()
        {
            var settings = new MockSettingsService();

            var handler = new MockHttpMessageHandler((req) =>
            {
                if (req.RequestUri!.AbsolutePath.EndsWith("/auth/login"))
                {
                    var loginResponse = new AuthLoginResponse
                    {
                        Success = true,
                        AccessToken = "google_jwt_access_token",
                        RefreshToken = "google_jwt_refresh_token",
                        User = new AuthUserDto
                        {
                            Id = 88,
                            Email = "google-photographer@gmail.com",
                            DisplayName = "Google Artist"
                        },
                        Licenses = new List<UserLicenseDto>
                        {
                            new()
                            {
                                Id = 202,
                                LicenseKey = "FUJI-GOOGLE-KEY-5678",
                                Status = "active",
                                PlanType = "pro_lifetime"
                            }
                        }
                    };

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(loginResponse))
                    };
                }

                if (req.RequestUri!.AbsolutePath.EndsWith("/license/activate"))
                {
                    var actResponse = new LicenseActivationResponse
                    {
                        Success = true,
                        LicenseKey = "FUJI-GOOGLE-KEY-5678",
                        Status = "active",
                        UsedActivations = 1,
                        MaxActivations = 3
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(actResponse))
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var httpClient = new HttpClient(handler);
            var service = new LicenseService(settings, httpClient);

            var result = await service.LoginWithGoogleIdTokenAsync("mock_google_id_token_xyz");

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(service.IsPro);
            Assert.True(service.IsAccountLoggedIn);
            Assert.Equal("google-photographer@gmail.com", service.AccountEmail);
            Assert.Equal("Google Artist", settings.Settings.AccountDisplayName);
            Assert.Equal("FUJI-GOOGLE-KEY-5678", service.CurrentStatus.LicenseKey);
        }

        [Fact]
        public async Task CompleteOAuthCallbackAsync_WhenCodeExchanged_AuthenticatesAndActivatesPro()
        {
            var settings = new MockSettingsService();

            var handler = new MockHttpMessageHandler((req) =>
            {
                if (req.RequestUri!.AbsolutePath.EndsWith("/auth/exchange-token"))
                {
                    var exchangeResponse = new AuthLoginResponse
                    {
                        Success = true,
                        AccessToken = "oauth_exchange_access_token",
                        RefreshToken = "oauth_exchange_refresh_token",
                        User = new AuthUserDto
                        {
                            Id = 99,
                            Email = "oauth-user@fujifier.cc",
                            DisplayName = "OAuth User"
                        },
                        Licenses = new List<UserLicenseDto>
                        {
                            new()
                            {
                                Id = 303,
                                LicenseKey = "FUJI-OAUTH-KEY-9999",
                                Status = "active"
                            }
                        }
                    };

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(exchangeResponse))
                    };
                }

                if (req.RequestUri!.AbsolutePath.EndsWith("/license/activate"))
                {
                    var actResponse = new LicenseActivationResponse
                    {
                        Success = true,
                        LicenseKey = "FUJI-OAUTH-KEY-9999",
                        Status = "active",
                        UsedActivations = 1,
                        MaxActivations = 3
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(actResponse))
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var httpClient = new HttpClient(handler);
            var service = new LicenseService(settings, httpClient);

            var result = await service.CompleteOAuthCallbackAsync("fuji_auth_test_code_123", "test_code_verifier_abc");

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(service.IsPro);
            Assert.True(service.IsAccountLoggedIn);
            Assert.Equal("oauth-user@fujifier.cc", service.AccountEmail);
            Assert.Equal("FUJI-OAUTH-KEY-9999", service.CurrentStatus.LicenseKey);
        }

        [Fact]
        public async Task LoginWithGoogleOAuthAsync_WhenPlatformOAuthLauncherConfigured_UsesLauncherAndActivatesPro()
        {
            var settings = new MockSettingsService();

            var handler = new MockHttpMessageHandler((req) =>
            {
                if (req.RequestUri!.AbsolutePath.EndsWith("/auth/exchange-token"))
                {
                    var exchangeResponse = new AuthLoginResponse
                    {
                        Success = true,
                        AccessToken = "ios_platform_access_token",
                        RefreshToken = "ios_platform_refresh_token",
                        User = new AuthUserDto
                        {
                            Id = 101,
                            Email = "ios-user@fujifier.cc",
                            DisplayName = "iOS User"
                        },
                        Licenses = new List<UserLicenseDto>
                        {
                            new()
                            {
                                Id = 404,
                                LicenseKey = "FUJI-IOS-KEY-7777",
                                Status = "active"
                            }
                        }
                    };

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(exchangeResponse))
                    };
                }

                if (req.RequestUri!.AbsolutePath.EndsWith("/license/activate"))
                {
                    var actResponse = new LicenseActivationResponse
                    {
                        Success = true,
                        LicenseKey = "FUJI-IOS-KEY-7777",
                        Status = "active",
                        UsedActivations = 1,
                        MaxActivations = 3
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(actResponse))
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var httpClient = new HttpClient(handler);
            var service = new LicenseService(settings, httpClient);

            string? capturedAuthUrl = null;
            string? capturedScheme = null;

            LicenseService.PlatformOAuthLauncher = (authUrl, scheme, ct) =>
            {
                capturedAuthUrl = authUrl;
                capturedScheme = scheme;

                // Extract state from the authUrl so the mock returns matching state
                var uri = new Uri(authUrl);
                var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
                string? state = queryParams["state"];

                return Task.FromResult<(string Code, string? State)?>((Code: "ios_test_code_888", State: state));
            };

            try
            {
                var result = await service.LoginWithGoogleOAuthAsync();

                Assert.True(result.Success, result.ErrorMessage);
                Assert.True(service.IsPro);
                Assert.True(service.IsAccountLoggedIn);
                Assert.Equal("ios-user@fujifier.cc", service.AccountEmail);
                Assert.Equal("FUJI-IOS-KEY-7777", service.CurrentStatus.LicenseKey);

                Assert.NotNull(capturedAuthUrl);
                Assert.Equal("fujifier", capturedScheme);
                Assert.Contains("redirect_uri=fujifier", capturedAuthUrl);
                Assert.Contains("platform=", capturedAuthUrl);
            }
            finally
            {
                LicenseService.PlatformOAuthLauncher = null;
            }
        }

        [Fact]
        public void LicenseService_DefaultsToFujifierCc_AndMigratesOldDomain()
        {
            var settings = new MockSettingsService();
            settings.Settings.LicenseApiBaseUrl = "https://fujifier.net/wp-json/fujifier/v1";

            var service = new LicenseService(settings);

            Assert.Equal("https://fujifier.cc", service.GetWebsiteBaseUrl());
        }

        [Fact]
        public async Task CompleteOAuthCallbackAsync_WhenResponseContainsStringIdsAndNumbers_DeserializesAndActivatesPro()
        {
            var settings = new MockSettingsService();

            var handler = new MockHttpMessageHandler((req) =>
            {
                if (req.RequestUri!.AbsolutePath.EndsWith("/auth/exchange-token"))
                {
                    // Simulated raw JSON response with string IDs and numeric values from WordPress/MySQL
                    string rawJson = @"{
                        ""success"": true,
                        ""access_token"": ""mock_token_123"",
                        ""refresh_token"": ""mock_refresh_123"",
                        ""token_type"": ""Bearer"",
                        ""expires_in"": ""3600"",
                        ""user"": {
                            ""id"": ""42"",
                            ""email"": ""photographer@fujifier.cc"",
                            ""display_name"": ""Studio Pro""
                        },
                        ""licenses"": [
                            {
                                ""id"": ""101"",
                                ""license_key"": ""FUJI-STR-ID-TEST-0001"",
                                ""plan_type"": ""pro_lifetime"",
                                ""status"": ""active"",
                                ""max_activations"": ""3"",
                                ""used_activations"": ""1"",
                                ""expires_at"": null
                            }
                        ]
                    }";

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(rawJson)
                    };
                }

                if (req.RequestUri!.AbsolutePath.EndsWith("/license/activate"))
                {
                    string actJson = @"{
                        ""success"": true,
                        ""license_key"": ""FUJI-STR-ID-TEST-0001"",
                        ""status"": ""active"",
                        ""max_activations"": ""3"",
                        ""used_activations"": ""1""
                    }";
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(actJson)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var httpClient = new HttpClient(handler);
            var service = new LicenseService(settings, httpClient);

            var result = await service.CompleteOAuthCallbackAsync("fuji_auth_code_str_test", "code_verifier_str_test");

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(service.IsPro);
            Assert.True(service.IsAccountLoggedIn);
            Assert.Equal("photographer@fujifier.cc", service.AccountEmail);
            Assert.Equal("Studio Pro", settings.Settings.AccountDisplayName);
            Assert.Equal("FUJI-STR-ID-TEST-0001", service.CurrentStatus.LicenseKey);
        }
    }
}
