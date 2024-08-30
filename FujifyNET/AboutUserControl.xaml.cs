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
    public class AboutUserControlViewModel
    {
        public string Title { get; set; }
        public FlowDocument Message { get; set; } // Change type to FlowDocument
        public ICommand OkCommand { get; }

        public AboutUserControlViewModel(string title, FlowDocument message)
        {
            Title = title;
            Message = message; // Ensure this is a FlowDocument
            OkCommand = new RelayCommand(OnOk);
        }

        private void OnOk()
        {
            DialogHost.CloseDialogCommand.Execute(true, null);
        }
    }
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