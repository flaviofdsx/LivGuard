using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Flavirus.UI.Views;

public static class LivGuardToast
{
    public static void Show(Window owner, string title, string message, bool isError = false)
    {
        if (!owner.Dispatcher.CheckAccess())
        {
            owner.Dispatcher.BeginInvoke(new Action(() => Show(owner, title, message, isError)));
            return;
        }

        if (owner.FindName("ToastHost") is not Panel toastPanel)
            throw new InvalidOperationException("The window must define a Panel named ToastHost.");

        if (toastPanel.Children.Count >= 4)
            toastPanel.Children.RemoveAt(0);

        var accent = isError
            ? Color.FromRgb(255, 61, 61)
            : Color.FromRgb(0, 229, 255);
        var toast = new Border
        {
            Width = 360,
            Background = new SolidColorBrush(
                isError ? Color.FromRgb(60, 15, 15) : Color.FromRgb(15, 45, 35)),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(20, 15, 20, 15),
            Margin = new Thickness(0, 0, 0, 10),
            Opacity = 0
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(accent),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 220, 200)),
            FontSize = 12,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        toast.Child = stack;
        toastPanel.Children.Add(toast);

        toast.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            fadeOut.Completed += (_, _) => toastPanel.Children.Remove(toast);
            toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        };
        timer.Start();
    }
}
