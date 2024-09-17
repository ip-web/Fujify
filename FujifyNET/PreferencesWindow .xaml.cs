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
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace FujifyNET
{
    public partial class PreferencesWindow : Window
    {
        public PreferencesWindow()
        {
            InitializeComponent();
            LoadPreferences();
            Loaded += Window_Loaded;
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Start the fade-in and scale-up animations
            var fadeInStoryboard = (Storyboard)FindResource("FadeInStoryboard");
            var scaleUpStoryboard = (Storyboard)FindResource("ScaleUpStoryboard");

            fadeInStoryboard.Begin(this);
            scaleUpStoryboard.Begin(PrefGrid);
        }
        private void LoadPreferences()
        {
            DarkThemeToggle.IsChecked = App.IsDarkTheme;
            EmbedOriginalRawToggle.IsChecked = Properties.Settings.Default.EmbedOriginalRaw;
        }

        private void CloseWithAnimation(bool dialogResult)
        {
            var fadeOutStoryboard = (Storyboard)FindResource("FadeOutStoryboard");
            fadeOutStoryboard.Completed += (s, e) =>
            {
                DialogResult = dialogResult;
                Close();
            };
            fadeOutStoryboard.Begin(this);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            App.ApplyTheme(DarkThemeToggle.IsChecked ?? true);

            bool newEmbedSetting = EmbedOriginalRawToggle.IsChecked ?? false;
            Properties.Settings.Default.EmbedOriginalRaw = newEmbedSetting;
            Properties.Settings.Default.Save();

            Properties.Settings.Default.Reload();

            CloseWithAnimation(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseWithAnimation(false);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            CloseWithAnimation(false);
        }

        private void DarkThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            App.ApplyTheme(true);
        }

        private void DarkThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            App.ApplyTheme(false);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

    }
}