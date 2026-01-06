using System.Windows;

namespace Crosspose.Gui;

public partial class AboutWindow : Window
{
    public AboutWindow(string text)
    {
        InitializeComponent();
        ContentText.Text = text;
    }
}
