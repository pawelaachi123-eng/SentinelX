using System.Windows;
using System.Windows.Controls;
namespace SentinelX.Core;
public static class PasswordBinding
{
    public static readonly DependencyProperty ValueProperty=DependencyProperty.RegisterAttached("Value",typeof(string),typeof(PasswordBinding),new FrameworkPropertyMetadata("",FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,Changed));
    public static string GetValue(DependencyObject d)=>(string)d.GetValue(ValueProperty);
    public static void SetValue(DependencyObject d,string value)=>d.SetValue(ValueProperty,value);
    private static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        if(d is not PasswordBox box)return;
        box.PasswordChanged-=PasswordChanged;
        if(box.Password!=(string)e.NewValue)box.Password=(string)e.NewValue;
        box.PasswordChanged+=PasswordChanged;
    }
    private static void PasswordChanged(object sender,RoutedEventArgs e)=>SetValue((PasswordBox)sender,((PasswordBox)sender).Password);
}
