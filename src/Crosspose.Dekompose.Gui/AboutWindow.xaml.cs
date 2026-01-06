using System.Windows;

namespace Crosspose.Dekompose.Gui;

public partial class AboutWindow : Window
{
    public AboutWindow(string text)
    {
        InitializeComponent();
        ContentText.Text = text;
    }
}
