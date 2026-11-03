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
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Fujify.Views
{
    public partial class MainWindow : Window
    {
        private const uint WM_ACTIVATE = 0x0006;
        private const uint WM_NCACTIVATE = 0x0086;
        private const uint WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;

        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;
        private const int SC_MAXIMIZE = 0xF030;
        private const int SC_RESTORE = 0xF120;

        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_SYSMENU = 0x00080000;

        private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
        private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;
        private const int DWMWCP_ROUND = 2;

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

        [DllImport("user32.dll", EntryPoint = "SendMessage")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
        private static extern int DwmSetWindowAttributeUint(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

        private static int GetWindowStyle(IntPtr hWnd)
        {
            return IntPtr.Size == 8 ? (int)GetWindowLongPtr64(hWnd, GWL_STYLE) : GetWindowLong32(hWnd, GWL_STYLE);
        }

        private static void SetWindowStyle(IntPtr hWnd, int style)
        {
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(hWnd, GWL_STYLE, (IntPtr)style);
            else
                SetWindowLong32(hWnd, GWL_STYLE, style);
        }



        public MainWindow()
        {
            InitializeComponent();
            SuppressFullScreenButton();
            Loaded += (s, e) => SuppressFullScreenButton();
            if (!App.SupportsMica)
            {
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                Background = null;
            }

            if (OperatingSystem.IsWindows())
            {
                Win32Properties.AddWndProcHookCallback(this, WndProcHook);
                ActualThemeVariantChanged += (s, e) =>
                {
                    var handle = TryGetPlatformHandle()?.Handle;
                    if (handle.HasValue && handle.Value != IntPtr.Zero)
                    {
                        EnsureDwmTransitionsEnabled(handle.Value);
                    }
                };
            }
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            SuppressFullScreenButton();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            SuppressFullScreenButton();
            if (OperatingSystem.IsWindows())
            {
                var handle = TryGetPlatformHandle()?.Handle;
                if (handle.HasValue && handle.Value != IntPtr.Zero)
                {
                    EnsureDwmTransitionsEnabled(handle.Value);
                }
            }
        }

        public void SuppressFullScreenButton()
        {
            try
            {
                var winType = typeof(Window);
                var actionsField = winType.GetField("_allowedWindowActions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (actionsField != null)
                {
                    var current = actionsField.GetValue(this);
                    if (current != null)
                    {
                        int currentVal = (int)current;
                        const int fullscreenMask = 2; // PlatformAllowedWindowActions.Fullscreen
                        if ((currentVal & fullscreenMask) != 0)
                        {
                            int updatedVal = currentVal & ~fullscreenMask;
                            var updatedEnum = Enum.ToObject(actionsField.FieldType, updatedVal);
                            actionsField.SetValue(this, updatedEnum);

                            var onActionsChanged = winType.GetMethod("OnAllowedWindowActionsChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            onActionsChanged?.Invoke(this, null);
                        }
                    }
                }
            }
            catch
            {
                // Safe fallback
            }

            try
            {
                foreach (var visual in this.GetVisualDescendants())
                {
                    if (visual is Button btn)
                    {
                        if (btn.Name == "PART_FullScreenButton" ||
                            Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(btn) == Avalonia.Input.WindowDecorationsElementRole.FullScreenButton)
                        {
                            btn.IsVisible = false;
                            btn.Width = 0;
                            btn.MaxWidth = 0;
                            btn.Opacity = 0;
                        }
                    }
                }
            }
            catch
            {
                // Safe fallback
            }
        }

        public void EnsureDwmTransitionsEnabled(IntPtr hWnd)
        {
            if (!OperatingSystem.IsWindows() || hWnd == IntPtr.Zero) return;

            try
            {
                int disabled = 0;
                DwmSetWindowAttribute(hWnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disabled, sizeof(int));

                int cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                int darkMode = (ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light) ? 0 : 1;
                DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                uint captionColor = DWMWA_COLOR_NONE;
                DwmSetWindowAttributeUint(hWnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(uint));

                uint borderColor = DWMWA_COLOR_NONE;
                DwmSetWindowAttributeUint(hWnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));

                int currentStyle = GetWindowStyle(hWnd);
                int desired = WS_MAXIMIZEBOX | WS_MINIMIZEBOX | WS_THICKFRAME | WS_CAPTION | WS_SYSMENU;
                if ((currentStyle & desired) != desired)
                {
                    SetWindowStyle(hWnd, currentStyle | desired);
                }
            }
            catch
            {
                // Ignored on non-supporting configurations
            }
        }

        public void ToggleMaximizeAnimated()
        {
            if (OperatingSystem.IsWindows())
            {
                var handle = TryGetPlatformHandle()?.Handle;
                if (handle.HasValue && handle.Value != IntPtr.Zero)
                {
                    EnsureDwmTransitionsEnabled(handle.Value);
                    if (WindowState == WindowState.Maximized)
                    {
                        SendMessage(handle.Value, WM_SYSCOMMAND, (IntPtr)SC_RESTORE, IntPtr.Zero);
                        return;
                    }
                    else
                    {
                        SendMessage(handle.Value, WM_SYSCOMMAND, (IntPtr)SC_MAXIMIZE, IntPtr.Zero);
                        return;
                    }
                }
            }

            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        public void MinimizeAnimated()
        {
            if (OperatingSystem.IsWindows())
            {
                var handle = TryGetPlatformHandle()?.Handle;
                if (handle.HasValue && handle.Value != IntPtr.Zero)
                {
                    EnsureDwmTransitionsEnabled(handle.Value);
                    SendMessage(handle.Value, WM_SYSCOMMAND, (IntPtr)SC_MINIMIZE, IntPtr.Zero);
                    return;
                }
            }

            WindowState = WindowState.Minimized;
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
            if (msg == WM_NCACTIVATE || msg == WM_ACTIVATE)
            {
                EnsureDwmTransitionsEnabled(hWnd);
            }

            return IntPtr.Zero;
        }
    }
}
