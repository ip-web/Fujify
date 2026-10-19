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
        private readonly bool _initialIsDarkTheme;

        public PreferencesWindow()
        {
            InitializeComponent();
            _initialIsDarkTheme = App.IsDarkTheme;
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
            SkipThumbnailGenerationToggle.IsChecked = Properties.Settings.Default.SkipThumbnailGeneration;

            int totalThreads = Environment.ProcessorCount;
            ConcurrencySlider.Maximum = totalThreads;

            int defaultConcurrency = Math.Max(1, totalThreads / 2);
            int savedConcurrency = Properties.Settings.Default.MaxConcurrentConversions;
            if (savedConcurrency <= 0 || savedConcurrency > totalThreads)
            {
                savedConcurrency = defaultConcurrency;
            }

            ConcurrencySlider.Value = savedConcurrency;
            UpdateConcurrencyUI(savedConcurrency);
        }

        private void ConcurrencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateConcurrencyUI((int)e.NewValue);
        }

        private void UpdateConcurrencyUI(int val)
        {
            if (ConcurrencyValueText == null || ConcurrencyHintText == null) return;

            int totalThreads = Environment.ProcessorCount;
            int defaultConcurrency = Math.Max(1, totalThreads / 2);

            ConcurrencyValueText.Text = val.ToString();

            if (val == defaultConcurrency)
            {
                ConcurrencyHintText.Text = $"Default: {val} (Recommended for P-core / E-core balance)";
            }
            else if (val == totalThreads)
            {
                ConcurrencyHintText.Text = $"Maximum: {val} (Full saturation - may cause disk I/O bottleneck)";
            }
            else if (val <= 8)
            {
                ConcurrencyHintText.Text = $"{val} threads (Low CPU load, runs primarily on P-cores)";
            }
            else
            {
                ConcurrencyHintText.Text = $"{val} threads (Utilizes P-cores and E-cores)";
            }
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
            Properties.Settings.Default.SkipThumbnailGeneration = SkipThumbnailGenerationToggle.IsChecked ?? false;
            Properties.Settings.Default.MaxConcurrentConversions = (int)ConcurrencySlider.Value;
            Properties.Settings.Default.Save();

            CloseWithAnimation(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            App.ApplyTheme(_initialIsDarkTheme);
            CloseWithAnimation(false);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            App.ApplyTheme(_initialIsDarkTheme);
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