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

using System.Threading;
using System.Threading.Tasks;

namespace Fujify.Services
{
    public enum RafSensorType
    {
        Bayer,
        XTrans,
        Invalid
    }

    public record RafContainerInspection(
        bool IsValid,
        RafSensorType SensorType,
        string? CameraModel,
        int Width,
        int Height,
        string? ErrorMessage);

    public record RafConversionResult(bool Success, string? OutputRafPath, string? ErrorMessage);

    public interface IRafConverterService
    {
        bool IsAvailable { get; }
        string? GetRaw2RafPath();
        RafConversionResult ConvertToRaf(string inputRawPath, string outputRafPath, string containerArg, string? exiftoolPath = null);
        Task<RafConversionResult> ConvertToRafAsync(string inputRawPath, string outputRafPath, string containerArg, string? exiftoolPath = null, CancellationToken cancellationToken = default);
        bool IsBayerSensor(string rafFilePath);
        RafContainerInspection InspectContainer(string rafFilePath);
        Task<RafConversionResult> PatchModelAsync(string inputRafPath, string outputRafPath, string targetModel, CancellationToken cancellationToken = default);
        bool HasValidContainer(string? containerArg);
    }
}

