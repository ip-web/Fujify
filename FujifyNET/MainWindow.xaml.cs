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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Application = System.Windows.Application;
using ListView = System.Windows.Controls.ListView;
using ListViewItem = System.Windows.Controls.ListViewItem;
using ProgressBar = System.Windows.Controls.ProgressBar;
using Binding = System.Windows.Data.Binding;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DataFormats = System.Windows.DataFormats;
using FujifyNET.Models;
using FujifyNET.Services;
using FujifyNET.ViewModels;

namespace FujifyNET
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly IDialogService _dialogService;
        private bool _isTextBlockHidden;
        private GridViewColumnHeader? _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public MainWindow()
        {
            InitializeComponent();

            _dialogService = new DialogService();
            _viewModel = new MainViewModel(dialogService: _dialogService);
            DataContext = _viewModel;

            Loaded += Window_Loaded;
            listView.SizeChanged += (s, e) => RecalculateFirstColumnWidth();

            _viewModel.Files.CollectionChanged += (s, e) =>
            {
                Dispatcher.InvokeAsync(RecalculateFirstColumnWidth);
                if (_viewModel.HasFiles)
                {
                    UpdateTextBlockVisibility();
                }
            };

            _viewModel.RequestRecalculateColumns += () => Dispatcher.InvokeAsync(RecalculateFirstColumnWidth);
            _viewModel.ProcessingCompleted += () => Dispatcher.InvokeAsync(AnimateProgressBarToCompletion);
        }

        #region Window Chrome & Aero Drop Shadow

        public static class DwmDropShadow
        {
            [DllImport("dwmapi.dll", PreserveSig = true)]
            private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

            [DllImport("dwmapi.dll")]
            private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMarInset);

            public static void DropShadowToWindow(Window window)
            {
                if (!DropShadow(window))
                {
                    window.SourceInitialized += Window_SourceInitialized;
                }
            }

            private static void Window_SourceInitialized(object? sender, EventArgs e)
            {
                if (sender is not Window window) return;
                DropShadow(window);
                window.SourceInitialized -= Window_SourceInitialized;
            }

            private static bool DropShadow(Window window)
            {
                try
                {
                    WindowInteropHelper helper = new(window);
                    int val = 2;
                    int ret1 = DwmSetWindowAttribute(helper.Handle, 2, ref val, 4);

                    if (ret1 == 0)
                    {
                        Margins m = new() { Bottom = 0, Left = 0, Right = 0, Top = 0 };
                        int ret2 = DwmExtendFrameIntoClientArea(helper.Handle, ref m);
                        return ret2 == 0;
                    }
                    return false;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= Window_Loaded;

            var fadeInStoryboard = (Storyboard)FindResource("FadeInStoryboardMain");
            Storyboard.SetTarget(fadeInStoryboard, this);
            Storyboard.SetTargetProperty(fadeInStoryboard, new PropertyPath("Opacity"));
            fadeInStoryboard.Begin();

            RecalculateFirstColumnWidth();
            DwmDropShadow.DropShadowToWindow(this);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            Properties.Settings.Default.Save();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            CloseWithAnimation();
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            CloseWithAnimation();
        }

        private void CloseWithAnimation()
        {
            var fadeOutStoryboard = (Storyboard)Resources["FadeOutStoryboard"];
            Storyboard.SetTarget(fadeOutStoryboard, this);
            Storyboard.SetTargetProperty(fadeOutStoryboard, new PropertyPath("Opacity"));

            fadeOutStoryboard.Completed += (s, args) =>
            {
                Close();
                Application.Current.Shutdown();
            };

            fadeOutStoryboard.Begin();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CustomTitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            TitleBarContextMenu.PlacementTarget = sender as UIElement;
            TitleBarContextMenu.IsOpen = true;
        }

        private void Move_Click(object sender, RoutedEventArgs e)
        {
            MouseLeftButtonDown += Window_MouseLeftButtonDown;
            CaptureMouse();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                MouseLeftButtonDown -= Window_MouseLeftButtonDown;
                ReleaseMouseCapture();
                DragMove();
            }
        }

        private const int WM_SYSCOMMAND = 0x112;
        private const int SC_SIZE = 0xF000;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void Resize_Click(object sender, RoutedEventArgs e)
        {
            WindowInteropHelper helper = new(this);
            SendMessage(helper.Handle, WM_SYSCOMMAND, (IntPtr)SC_SIZE, IntPtr.Zero);
        }

        #endregion

        #region Drag and Drop

        private void MainWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] data && data.All(path => Directory.Exists(path) || File.Exists(path)))
                {
                    e.Effects = DragDropEffects.Copy;
                    return;
                }
            }

            e.Effects = DragDropEffects.None;
        }

        private void MainWindow_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                UpdateTextBlockVisibility();
                _ = _viewModel.AddFilesAsync(paths);
            }
        }

        private void UpdateTextBlockVisibility()
        {
            var fadeOutStoryboard = (Storyboard)FindResource("FadeOutStoryboard2");

            if (DragDropTextBlock.Visibility == Visibility.Visible && !_isTextBlockHidden)
            {
                fadeOutStoryboard.Stop();
                _isTextBlockHidden = true;
                fadeOutStoryboard.Begin();
                fadeOutStoryboard.Completed += (s, e) =>
                {
                    DragDropTextBlock.Visibility = Visibility.Collapsed;
                };
            }
        }

        #endregion

        #region ListView Layout & Animations

        private void RecalculateFirstColumnWidth()
        {
            if (listView.View is not GridView gridView || gridView.Columns.Count < 4) return;

            double totalWidth = listView.ActualWidth;
            if (totalWidth <= 150) return;

            const double thumbWidth = 85;
            gridView.Columns[0].Width = thumbWidth;

            double remainingWidth = Math.Max(0, totalWidth - thumbWidth - 30);
            const double minColWidth = 100;

            gridView.Columns[1].Width = Math.Max(minColWidth, remainingWidth * 0.38);
            gridView.Columns[2].Width = Math.Max(minColWidth, remainingWidth * 0.38);
            gridView.Columns[3].Width = Math.Max(minColWidth, remainingWidth * 0.24);
        }

        private void OnListViewItemLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ListViewItem listViewItem)
            {
                if (listViewItem.DataContext is FileItem fileItem && !fileItem.HasAnimationPlayed)
                {
                    fileItem.HasAnimationPlayed = true;

                    var fadeInStoryboard = (Storyboard)FindResource("FadeInStoryboard");
                    var zoomInStoryboard = (Storyboard)FindResource("ZoomInStoryboard");

                    fadeInStoryboard.Begin(listViewItem);
                    zoomInStoryboard.Begin(listViewItem);
                }
            }
        }

        private void ColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is GridViewColumnHeader header)
            {
                Sort(header);
            }
        }

        private void Sort(GridViewColumnHeader header)
        {
            if (header.Column is not GridViewColumn column) return;

            var direction = ListSortDirection.Ascending;
            if (_lastHeaderClicked == header)
            {
                direction = _lastDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }

            _lastHeaderClicked = header;
            _lastDirection = direction;

            string? sortBy = null;
            if (column.DisplayMemberBinding is Binding wpfBinding)
            {
                sortBy = wpfBinding.Path.Path;
            }
            else if (column.DisplayMemberBinding is MultiBinding multiBinding)
            {
                sortBy = string.Join(", ", multiBinding.Bindings.Select(b => (b as Binding)?.Path.Path));
            }

            var view = CollectionViewSource.GetDefaultView(listView.ItemsSource);
            view.SortDescriptions.Clear();
            if (sortBy != null)
            {
                view.SortDescriptions.Add(new SortDescription(sortBy, direction));
                view.Refresh();
            }
        }

        private void ListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not ListView lv || lv.ContextMenu == null) return;

            bool hasSelectedItem = lv.SelectedItem != null;

            foreach (var item in lv.ContextMenu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    if (menuItem.Header.ToString() == "Add Pictures" || menuItem.Header.ToString() == "Add Folder")
                    {
                        menuItem.IsEnabled = true;
                    }
                    else
                    {
                        menuItem.IsEnabled = hasSelectedItem;
                    }
                }
            }
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (listView.SelectedItem is FileItem selectedItem)
            {
                _viewModel.OpenFileCommand.Execute(selectedItem);
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = listView.SelectedItems.Cast<FileItem>().ToList();
            foreach (var item in selectedItems)
            {
                _viewModel.OpenFileCommand.Execute(item);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = listView.SelectedItems.OfType<FileItem>().ToList();
            var openedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in selectedItems)
            {
                string? folderPath = Path.GetDirectoryName(item.FilePath);
                if (!string.IsNullOrEmpty(folderPath) && openedFolders.Add(folderPath))
                {
                    _viewModel.OpenFolderCommand.Execute(item);
                }
            }
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = listView.SelectedItems.Cast<FileItem>().ToList();
            if (selectedItems.Count == 0) return;

            foreach (var selectedFile in selectedItems)
            {
                if (listView.ItemContainerGenerator.ContainerFromItem(selectedFile) is ListViewItem listViewItem)
                {
                    var storyboard = ((Storyboard)FindResource("FadeOutStoryboard")).Clone();
                    Storyboard.SetTarget(storyboard, listViewItem);
                    Storyboard.SetTargetProperty(storyboard, new PropertyPath("Opacity"));

                    storyboard.Completed += (s, args) =>
                    {
                        _viewModel.Files.Remove(selectedFile);
                        if (_viewModel.Files.Count == 0)
                        {
                            listView.UpdateLayout();
                        }
                    };

                    storyboard.Begin();
                }
                else
                {
                    _viewModel.Files.Remove(selectedFile);
                }
            }

            listView.UpdateLayout();
        }

        private async void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.Files.Count > 0)
            {
                var listViewItems = new List<ListViewItem>();

                foreach (var item in _viewModel.Files.ToList())
                {
                    if (listView.ItemContainerGenerator.ContainerFromItem(item) is ListViewItem listViewItem)
                    {
                        listViewItems.Add(listViewItem);
                    }
                }

                var storyboard = (Storyboard)FindResource("FadeOutStoryboard");

                foreach (var listViewItem in listViewItems)
                {
                    Storyboard.SetTarget(storyboard, listViewItem);
                    Storyboard.SetTargetProperty(storyboard, new PropertyPath("Opacity"));
                    storyboard.Begin();
                }

                HideProgressBar();
                await Task.Delay(350);

                await _viewModel.ClearCommand.ExecuteAsync();
                listView.UpdateLayout();
            }
        }

        #endregion

        #region Progress Bar Animations

        private void AnimateProgressBarToCompletion()
        {
            DoubleAnimation progressBarAnimation = new()
            {
                From = progressBar.Value,
                To = 100,
                Duration = new Duration(TimeSpan.FromMilliseconds(350)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard storyboard = new();
            storyboard.Children.Add(progressBarAnimation);
            Storyboard.SetTarget(progressBarAnimation, progressBar);
            Storyboard.SetTargetProperty(progressBarAnimation, new PropertyPath(ProgressBar.ValueProperty));

            storyboard.Completed += (s, e) => HideProgressBar();
            storyboard.Begin();
        }

        private async void HideProgressBar()
        {
            await Task.Delay(500);
            var storyboard = (Storyboard)FindResource("CollapseProgressBarStoryboard");
            storyboard.Begin(progressBarContainer);
            await Task.Delay(300);

            progressBar.Visibility = Visibility.Collapsed;
            progressBar.Value = 0;
            _viewModel.IsProgressVisible = false;
        }

        #endregion

        #region Dialog Actions

        private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _dialogService.ShowPreferencesDialog(this);
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _dialogService.ShowAboutDialog();
        }

        #endregion
    }

    public static class CommandExtensions
    {
        public static async Task ExecuteAsync(this ICommand command, object? parameter = null)
        {
            if (command is AsyncRelayCommand asyncCommand)
            {
                if (asyncCommand.CanExecute(parameter))
                {
                    asyncCommand.Execute(parameter);
                    await Task.CompletedTask;
                }
            }
            else if (command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }
    }
}