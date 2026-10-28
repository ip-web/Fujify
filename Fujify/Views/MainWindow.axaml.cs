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
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Fujify.Views
{
    public partial class MainWindow : Window
    {
        private const uint WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        private Control? _captionButtonsPanel;

        public MainWindow()
        {
            InitializeComponent();
            if (!App.SupportsMica)
            {
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                Background = null;
            }

            if (OperatingSystem.IsWindows())
            {
                Win32Properties.AddWndProcHookCallback(this, WndProcHook);
            }
        }

        public static bool IsPointerOverCaptionButtons(int clientX, int clientY, int clientWidth, double scaling, double buttonsWidthDip = 138.0, double buttonsHeightDip = 34.0)
        {
            double scale = scaling > 0 ? scaling : 1.0;
            double buttonsWidthPx = buttonsWidthDip * scale;
            double buttonsHeightPx = buttonsHeightDip * scale;

            return clientX >= (clientWidth - buttonsWidthPx) &&
                   clientX <= clientWidth + 30 &&
                   clientY >= -30 &&
                   clientY <= buttonsHeightPx;
        }

        private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                int screenX = unchecked((short)(long)lParam);
                int screenY = unchecked((short)((long)lParam >> 16));

                POINT pt = new POINT { X = screenX, Y = screenY };
                if (ScreenToClient(hWnd, ref pt) && GetClientRect(hWnd, out RECT clientRect))
                {
                    _captionButtonsPanel ??= this.FindControl<Control>("CaptionButtonsPanel");

                    double scaling = RenderScaling > 0 ? RenderScaling : 1.0;
                    double buttonsWidthDip = (_captionButtonsPanel != null && _captionButtonsPanel.Bounds.Width > 0)
                        ? _captionButtonsPanel.Bounds.Width
                        : 138.0;
                    double buttonsHeightDip = (_captionButtonsPanel != null && _captionButtonsPanel.Bounds.Height > 0)
                        ? _captionButtonsPanel.Bounds.Height
                        : 34.0;

                    // If pointer is within the caption buttons region in the top-right corner,
                    // force HTCLIENT so standard XAML buttons receive 100% of pointer hover, press, and click events.
                    if (IsPointerOverCaptionButtons(pt.X, pt.Y, clientRect.Right, scaling, buttonsWidthDip, buttonsHeightDip))
                    {
                        handled = true;
                        return (IntPtr)HTCLIENT;
                    }
                }
            }

            return IntPtr.Zero;
        }
    }
}
