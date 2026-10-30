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

using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Fujify.Models;

namespace Fujify.Services
{
    public interface IDialogService
    {
        Task ShowMessageAsync(Window? owner, string title, string message, bool isSuccess = true);
        Task<bool> ShowConfirmationAsync(Window? owner, string title, string message, string? confirmText = null, string? cancelText = null);
        Task<FileConflictResolution> PromptFileConflictAsync(Window? owner, string destinationFilePath);
        Task<IReadOnlyList<string>> ShowOpenFileDialogAsync(Window? owner, string title, bool allowMultiple = true);
        Task<string?> ShowFolderBrowserDialogAsync(Window? owner, string title);
        Task ShowPreferencesDialogAsync(Window? owner);
        Task ShowAboutDialogAsync(Window? owner);
    }
}
