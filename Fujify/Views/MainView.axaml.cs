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

using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Fujify.ViewModels;

namespace Fujify.Views
{
    public partial class MainView : UserControl
    {
        private Grid? _prefPanel;
        private Grid? _aboutPanel;

        public MainView()
        {
            InitializeComponent();
            AddHandler(DragDrop.DragEnterEvent, View_DragEnter);
            AddHandler(DragDrop.DragLeaveEvent, View_DragLeave);
            AddHandler(DragDrop.DragOverEvent, View_DragOver);
            AddHandler(DragDrop.DropEvent, View_Drop);
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _prefPanel = this.FindControl<Grid>("PreferencesSlidePanel");
            _aboutPanel = this.FindControl<Grid>("AboutSlidePanel");

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                TopLevel.SetAutoSafeAreaPadding(this, false);
                TopLevel.SetAutoSafeAreaPadding(topLevel, false);

                if (topLevel.InsetsManager != null)
                {
                    var mgr = topLevel.InsetsManager;
                    mgr.DisplayEdgeToEdge = true;
                    ApplySafeArea(mgr.SafeAreaPadding);
                    mgr.SafeAreaChanged += (s, args) => ApplySafeArea(args.SafeAreaPadding);
                }
                else if (System.OperatingSystem.IsAndroid() || System.OperatingSystem.IsIOS())
                {
                    ApplySafeArea(new Avalonia.Thickness(0, 47, 0, 24));
                }
            }

            UpdateSlidePanels(animated: false);
        }

        private void ApplySafeArea(Avalonia.Thickness insets)
        {
            double topInset = System.Math.Max(0, insets.Top);
            double bottomInset = System.Math.Max(0, insets.Bottom);
            double leftInset = System.Math.Max(0, insets.Left);
            double rightInset = System.Math.Max(0, insets.Right);

            // On mobile platforms, if insets manager was unavailable and reported 0
            if ((System.OperatingSystem.IsAndroid() || System.OperatingSystem.IsIOS()) && topInset == 0 && bottomInset == 0)
            {
                topInset = 47;
                bottomInset = 24;
            }

            var header = this.FindControl<Border>("HeaderBar");
            if (header != null)
            {
                header.Padding = new Avalonia.Thickness(16 + leftInset, 4 + topInset, 16 + rightInset, 6);
            }

            var phoneBottom = this.FindControl<Border>("PhoneBottomBar");
            if (phoneBottom != null)
            {
                phoneBottom.Padding = new Avalonia.Thickness(16 + leftInset, 6, 16 + rightInset, 6 + bottomInset);
            }

            var tabletBottom = this.FindControl<Border>("TabletBottomBar");
            if (tabletBottom != null)
            {
                tabletBottom.Padding = new Avalonia.Thickness(16 + leftInset, 6, 16 + rightInset, 6 + bottomInset);
            }

            var prefHeader = this.FindControl<Border>("PreferencesHeaderBar");
            if (prefHeader != null)
            {
                prefHeader.Padding = new Avalonia.Thickness(16 + leftInset, 4 + topInset, 16 + rightInset, 6);
            }

            var aboutHeader = this.FindControl<Border>("AboutHeaderBar");
            if (aboutHeader != null)
            {
                aboutHeader.Padding = new Avalonia.Thickness(16 + leftInset, 4 + topInset, 16 + rightInset, 6);
            }

            var modal = this.FindControl<Border>("ModalDialogContainer");
            if (modal != null)
            {
                modal.Margin = new Avalonia.Thickness(16 + leftInset, 16 + topInset, 16 + rightInset, 16 + bottomInset);
            }
        }

        private void UpdateSlidePanels(bool animated = true)
        {
            if (DataContext is not MainViewModel vm) return;

            double width = Bounds.Width > 0 ? Bounds.Width : 600;

            if (_prefPanel != null)
            {
                bool isOpen = vm.IsPreferencesModal;
                _prefPanel.IsHitTestVisible = isOpen;
                _prefPanel.Opacity = isOpen ? 1 : 0;
                _prefPanel.RenderTransform = new TranslateTransform(isOpen ? 0 : width, 0);
            }

            if (_aboutPanel != null)
            {
                bool isOpen = vm.IsAboutModal;
                _aboutPanel.IsHitTestVisible = isOpen;
                _aboutPanel.Opacity = isOpen ? 1 : 0;
                _aboutPanel.RenderTransform = new TranslateTransform(isOpen ? 0 : width, 0);
            }
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            try
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.UpdateLayoutMode(e.NewSize.Width, e.NewSize.Height);
                }
                UpdateSlidePanels(animated: false);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainView] OnSizeChanged handled exception: {ex}");
            }
        }

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.IsPreferencesModal) ||
                        args.PropertyName == nameof(MainViewModel.IsAboutModal))
                    {
                        UpdateSlidePanels(animated: true);
                    }
                };

                if (Bounds.Width > 0)
                {
                    vm.UpdateLayoutMode(Bounds.Width, Bounds.Height);
                }
                UpdateSlidePanels(animated: false);
            }
        }

        private void View_DragEnter(object? sender, DragEventArgs e)
        {
            if (DataContext is MainViewModel vm && e.Data.Contains(DataFormats.Files))
            {
                vm.IsDragOver = true;
            }
        }

        private void View_DragLeave(object? sender, DragEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.IsDragOver = false;
            }
        }

        private void View_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private async void View_Drop(object? sender, DragEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.IsDragOver = false;
            }

            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles()?.Select(f => f.Path.LocalPath).ToList();
                if (files != null && files.Count > 0 && DataContext is MainViewModel vm2)
                {
                    await vm2.AddFilesAsync(files);
                }
            }
        }

        private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.CloseModal();
            }
        }

        private void DataGrid_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.SelectedFile != null)
            {
                vm.OpenFileCommand.Execute(vm.SelectedFile);
            }
        }

        private void GridItem_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.SelectedFile != null)
            {
                vm.OpenFileCommand.Execute(vm.SelectedFile);
            }
        }
    }
}
