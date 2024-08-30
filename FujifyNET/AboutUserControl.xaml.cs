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
using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
namespace FujifyNET
{
    
    public partial class AboutUserControl : System.Windows.Controls.UserControl
    {
        public AboutUserControl(string title, FlowDocument flowDocument)
        {
            InitializeComponent();
            DataContext = new AboutUserControlViewModel(title, flowDocument);
            Loaded += AboutUserControl_Loaded;
            richTextBox.Document = flowDocument;
            AttachHyperlinkRequestNavigate(flowDocument);
        }

        private void AboutUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // This ensures that hyperlinks are wired up when the user control is loaded
            AttachHyperlinkRequestNavigate(richTextBox.Document as FlowDocument);
        }

        private void AttachHyperlinkRequestNavigate(FlowDocument document)
        {
            if (document == null)
                return;

            foreach (var block in document.Blocks)
            {
                if (block is Paragraph paragraph)
                {
                    foreach (var inline in paragraph.Inlines)
                    {
                        if (inline is Hyperlink hyperlink)
                        {
                            hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
                        }
                    }
                }
            }
        }

        // Event handler for hyperlink navigation
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // Start the link in the default browser
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // Handle any errors that occur when opening the link
                System.Windows.MessageBox.Show("Failed to open link: " + ex.Message);
            }
            e.Handled = true; // Mark the event as handled
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the AboutUserControl, which might be part of a dialog or window
            var window = Window.GetWindow(this);
            window?.Close();
        }
    }
}