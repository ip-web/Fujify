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
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fujify.Models
{
    public class FlexibleIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out int i)) return i;
                    if (reader.TryGetInt64(out long l)) return (int)l;
                    if (reader.TryGetDouble(out double d)) return (int)d;
                    return 0;

                case JsonTokenType.String:
                    string? str = reader.GetString();
                    if (string.IsNullOrWhiteSpace(str)) return 0;
                    if (int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out int val))
                        return val;
                    if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal))
                        return (int)dVal;
                    return 0;

                case JsonTokenType.Null:
                    return 0;

                case JsonTokenType.True:
                    return 1;

                case JsonTokenType.False:
                    return 0;

                default:
                    return 0;
            }
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    public class LicenseActivationRequest
    {
        [JsonPropertyName("license_key")]
        public string LicenseKey { get; set; } = string.Empty;

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("device_name")]
        public string DeviceName { get; set; } = string.Empty;

        [JsonPropertyName("platform")]
        public string Platform { get; set; } = "windows";

        [JsonPropertyName("app_version")]
        public string AppVersion { get; set; } = "2.1.0";
    }

    public class LicenseActivationResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("license_key")]
        public string? LicenseKey { get; set; }

        [JsonPropertyName("plan_type")]
        public string? PlanType { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("device_id")]
        public string? DeviceId { get; set; }

        [JsonPropertyName("device_name")]
        public string? DeviceName { get; set; }

        [JsonPropertyName("platform")]
        public string? Platform { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        [JsonPropertyName("max_activations")]
        public int MaxActivations { get; set; } = 3;

        [JsonConverter(typeof(FlexibleIntConverter))]
        [JsonPropertyName("used_activations")]
        public int UsedActivations { get; set; } = 1;

        [JsonPropertyName("offline_token")]
        public string? OfflineToken { get; set; }

        [JsonPropertyName("offline_expires")]
        public string? OfflineExpires { get; set; }

        [JsonPropertyName("features")]
        public List<string>? Features { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    public class LicenseVerificationRequest
    {
        [JsonPropertyName("license_key")]
        public string LicenseKey { get; set; } = string.Empty;

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;
    }

    public class LicenseVerificationResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("plan_type")]
        public string? PlanType { get; set; }

        [JsonPropertyName("offline_token")]
        public string? OfflineToken { get; set; }

        [JsonPropertyName("offline_expires")]
        public string? OfflineExpires { get; set; }

        [JsonPropertyName("server_time")]
        public string? ServerTime { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    public class LicenseDeactivationRequest
    {
        [JsonPropertyName("license_key")]
        public string LicenseKey { get; set; } = string.Empty;

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;
    }

    public class LicenseDeactivationResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }

    public class AuthLoginRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("google_id_token")]
        public string? GoogleIdToken { get; set; }
    }

    public class PkceExchangeRequest
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("code_verifier")]
        public string CodeVerifier { get; set; } = string.Empty;
    }

    public class AuthUserDto
    {
        [JsonConverter(typeof(FlexibleIntConverter))]
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }
    }

    public class UserLicenseDto
    {
        [JsonConverter(typeof(FlexibleIntConverter))]
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("license_key")]
        public string LicenseKey { get; set; } = string.Empty;

        [JsonPropertyName("plan_type")]
        public string? PlanType { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        [JsonPropertyName("max_activations")]
        public int MaxActivations { get; set; } = 3;

        [JsonConverter(typeof(FlexibleIntConverter))]
        [JsonPropertyName("used_activations")]
        public int UsedActivations { get; set; }

        [JsonPropertyName("expires_at")]
        public string? ExpiresAt { get; set; }
    }

    public class AuthLoginResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("user")]
        public AuthUserDto? User { get; set; }

        [JsonPropertyName("licenses")]
        public List<UserLicenseDto>? Licenses { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    public class AuthMeResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("user")]
        public AuthUserDto? User { get; set; }

        [JsonPropertyName("licenses")]
        public List<UserLicenseDto>? Licenses { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
