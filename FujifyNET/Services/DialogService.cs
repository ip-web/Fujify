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
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Application = System.Windows.Application;
using MaterialDesignThemes.Wpf;
using FujifyNET.Models;

namespace FujifyNET.Services
{
    public class DialogService : IDialogService
    {
        private bool _isDialogOpen;

        public async Task ShowMessageAsync(string title, string message)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                await Application.Current.Dispatcher.InvokeAsync(async () => await ShowMessageAsync(title, message)).Task.Unwrap();
                return;
            }

            if (_isDialogOpen) return;

            try
            {
                _isDialogOpen = true;
                var view = new CustomMessageBox(title, message);
                await DialogHost.Show(view, "RootDialog");
            }
            finally
            {
                _isDialogOpen = false;
            }
        }

        public async Task<FileConflictResult> PromptFileConflictAsync(string destinationFilePath)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                return await Application.Current.Dispatcher.InvokeAsync(async () => await PromptFileConflictAsync(destinationFilePath)).Task.Unwrap();
            }

            var view = new FileConflictDialog(destinationFilePath);
            var result = await DialogHost.Show(view, "RootDialog");
            if (result is FileConflictResult conflictResult)
            {
                return conflictResult;
            }
            return new FileConflictResult(FileConflictAction.Skip, false);
        }

        public string[]? ShowOpenFileDialog(string filter, bool multiselect = true)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = filter,
                Multiselect = multiselect
            };

            return openFileDialog.ShowDialog() == true ? openFileDialog.FileNames : null;
        }

        public string? ShowFolderBrowserDialog(string description)
        {
            using var folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = description,
                ShowNewFolderButton = true
            };

            return folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                ? folderBrowserDialog.SelectedPath
                : null;
        }

        public bool? ShowPreferencesDialog(Window? owner)
        {
            var preferencesWindow = new PreferencesWindow
            {
                Owner = owner
            };

            bool? result = preferencesWindow.ShowDialog();
            if (result == true)
            {
                App.ApplyTheme(Properties.Settings.Default.IsDarkTheme);
            }

            return result;
        }

        public void ShowAboutDialog()
        {
            var flowDocument = CreateAboutFlowDocument();
            var aboutControl = new AboutUserControl("About FUJIFY", flowDocument);
            DialogHost.Show(aboutControl, "RootDialog");
        }

        private static FlowDocument CreateAboutFlowDocument()
        {
            var flowDocument = new FlowDocument();
            var paragraph = new Paragraph();

            var brushConverter = new BrushConverter();
            var defaultLinkColor = (Brush?)brushConverter.ConvertFrom("#d5d5d5") ?? Brushes.Gray;
            var hoverLinkColor = (Brush?)brushConverter.ConvertFrom("#006400") ?? Brushes.Green;

            AddInline(paragraph, "FUJIFY ", null);
            AddNewLine(paragraph);
            string appVersion = typeof(DialogService).Assembly
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.1";
            if (appVersion.Contains('+')) appVersion = appVersion.Split('+')[0];
            AddInline(paragraph, $"Version {appVersion}", null);
            AddNewLine(paragraph);
            AddNewLine(paragraph);
            AddInline(paragraph, "Developed by: Isidore Paulin", null);
            AddNewLine(paragraph);

            AddHyperlink(paragraph, "Website", new Uri("https://ipweb.dev"), defaultLinkColor, hoverLinkColor);
            AddNewLine(paragraph);
            AddHyperlink(paragraph, "Instagram", new Uri("https://www.instagram.com/isi.do.re/"), defaultLinkColor, hoverLinkColor);
            AddNewLine(paragraph);
            AddNewLine(paragraph);

            AddInline(paragraph, "Credits: ", null);
            AddNewLine(paragraph);

            AddHyperlink(paragraph, "ExifTool", new Uri("https://exiftool.org/"), defaultLinkColor, hoverLinkColor);
            AddInline(paragraph, " by Phil Harvey ", null);
            AddNewLine(paragraph);

            AddHyperlink(paragraph, "DNGLab", new Uri("https://github.com/dnglab/dnglab"), defaultLinkColor, hoverLinkColor);
            AddNewLine(paragraph);

            AddHyperlink(paragraph, "LibRaw Library", new Uri("https://www.libraw.org/"), defaultLinkColor, hoverLinkColor);
            AddInline(paragraph, " by LibRaw LLC ", null);
            AddNewLine(paragraph);
            AddNewLine(paragraph);

            AddInline(paragraph, "Buy me a coffee: ", null);
            AddNewLine(paragraph);

            AddHyperlink(paragraph, "PayPal", new Uri("https://www.paypal.com/donate/?hosted_button_id=7UJ9B3LBLTN4J"), defaultLinkColor, hoverLinkColor);
            AddNewLine(paragraph);
            AddInline(paragraph, "USDT (BSC): 0xac487782e8a66d21d1fd099dda1872ee23376f6e", null);

            AddNewLine(paragraph);
            AddNewLine(paragraph);
            AddInline(paragraph, "Licensed under GNU General Public License v3.0", null);
            AddNewLine(paragraph);
            AddNewLine(paragraph);
            AddInline(paragraph, "Copyright(C) 2024 Isidore Paulin", null);

            flowDocument.Blocks.Add(paragraph);
            return flowDocument;
        }

        private static void AddInline(Paragraph paragraph, string text, Brush? color)
        {
            var run = new Run(text);
            if (color != null)
            {
                run.Foreground = color;
            }
            paragraph.Inlines.Add(run);
        }

        private static void AddNewLine(Paragraph paragraph)
        {
            paragraph.Inlines.Add(new LineBreak());
        }

        private static void AddHyperlink(Paragraph paragraph, string text, Uri navigateUri, Brush defaultColor, Brush hoverColor)
        {
            var hyperlink = new Hyperlink(new Run(text))
            {
                NavigateUri = navigateUri,
                Foreground = defaultColor,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            hyperlink.MouseEnter += (s, e) => hyperlink.Foreground = hoverColor;
            hyperlink.MouseLeave += (s, e) => hyperlink.Foreground = defaultColor;

            hyperlink.RequestNavigate += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                    e.Handled = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open hyperlink: {ex.Message}");
                }
            };

            paragraph.Inlines.Add(hyperlink);
        }
    }
}
