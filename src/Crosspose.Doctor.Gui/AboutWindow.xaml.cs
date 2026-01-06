using System.Windows;

namespace Crosspose.Doctor.Gui;

public partial class AboutWindow : Window
{
    public AboutWindow(string text)
    {
        InitializeComponent();
        ContentText.Text = text;
    }
}
