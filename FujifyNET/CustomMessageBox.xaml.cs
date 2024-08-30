using System;
using System.Collections.Generic;
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
    public partial class CustomMessageBox : System.Windows.Controls.UserControl
    {
        // Constructor to initialize the UserControl
        public CustomMessageBox(string title, string message)
        {
            InitializeComponent();
            // Set the DataContext to a new instance of CustomMessageBoxViewModel
            DataContext = new CustomMessageBoxViewModel(title, message);
        }
    }
}

