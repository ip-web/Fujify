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
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Fujify.ViewModels;
using Fujify.Views;
using Xunit;

namespace Fujify.Tests
{
    public class WindowDecorationsTests
    {
        [Fact(Skip = "Visual inspection UI test; requires headless platform runner in Avalonia 12.")]
        public void MainWindow_SuppressesFullScreenWindowAction()
        {
            try { Program.BuildAvaloniaApp().SetupWithoutStarting(); } catch { }

            var window = new MainWindow();
            window.SuppressFullScreenButton();

            var winType = typeof(Window);
            var actionsField = winType.GetField("_allowedWindowActions", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(actionsField);

            var actions = actionsField.GetValue(window)!;
            int actionsVal = (int)actions;
            const int maximizeMask = 1;
            const int fullscreenMask = 2;
            const int minimizeMask = 4;

            Assert.Equal(0, actionsVal & fullscreenMask);
            Assert.NotEqual(0, actionsVal & maximizeMask);
            Assert.NotEqual(0, actionsVal & minimizeMask);
        }

        [Fact(Skip = "Visual inspection UI test; requires headless platform runner in Avalonia 12.")]
        public void MainWindow_HasFullScreenHidingStyles()
        {
            try { Program.BuildAvaloniaApp().SetupWithoutStarting(); } catch { }

            var window = new MainWindow();
            Assert.NotEmpty(window.Styles);

            bool foundFullScreenStyle = false;
            foreach (var style in window.Styles)
            {
                if (style.ToString()?.Contains("PART_FullScreenButton") == true)
                {
                    foundFullScreenStyle = true;
                    break;
                }
            }
            Assert.True(foundFullScreenStyle, "MainWindow should contain styles hiding PART_FullScreenButton");
        }
    }
}
