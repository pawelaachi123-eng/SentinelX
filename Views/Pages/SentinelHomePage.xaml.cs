using System.Windows.Controls;
using SentinelX.Views.Behaviors;

namespace SentinelX.Views.Pages;

public partial class SentinelHomePage : UserControl
{
    public SentinelHomePage()
    {
        InitializeComponent();
        ResponseToastBehavior.Attach(this, ResponseToast, ToastText, DismissToastButton);
    }
}
