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
using Fujify.ViewModels;

namespace Fujify.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
            AddHandler(DragDrop.DragOverEvent, View_DragOver);
            AddHandler(DragDrop.DropEvent, View_Drop);
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            if (DataContext is MainViewModel vm)
            {
                vm.UpdateLayoutMode(e.NewSize.Width, e.NewSize.Height);
            }
        }

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is MainViewModel vm && Bounds.Width > 0)
            {
                vm.UpdateLayoutMode(Bounds.Width, Bounds.Height);
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
            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles()?.Select(f => f.Path.LocalPath).ToList();
                if (files != null && files.Count > 0 && DataContext is MainViewModel vm)
                {
                    await vm.AddFilesAsync(files);
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
    }
}
