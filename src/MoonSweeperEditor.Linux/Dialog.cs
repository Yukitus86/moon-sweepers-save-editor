using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace MoonSweeperEditor;

/// <summary>Small dark message box (Avalonia has none built in).</summary>
static class Dialog
{
    public static Task Info(Window owner, string text, string title) => Show(owner, text, title, question: false);

    public static Task<bool> Ask(Window owner, string text, string title) => Show(owner, text, title, question: true);

    static Task<bool> Show(Window owner, string text, string title, bool question)
    {
        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (IBrush)owner.FindResource("Panel")!,
            Foreground = (IBrush)owner.FindResource("Text")!,
            FontSize = 13,
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        var primary = new Button { Content = Text.Of(question ? "Yes" : "Ok"), MinWidth = 90, IsDefault = true };
        primary.Classes.Add("accent");
        primary.Click += (_, _) => window.Close(true);
        if (question)
        {
            var no = new Button { Content = Text.Of("No"), MinWidth = 90, IsCancel = true };
            no.Click += (_, _) => window.Close(false);
            buttons.Children.Add(no);
        }
        buttons.Children.Add(primary);

        window.Content = new StackPanel
        {
            Margin = new Thickness(22, 20),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
                buttons,
            },
        };
        return window.ShowDialog<bool>(owner);
    }
}

/// <summary>Hotbar slot (tool id) ⇄ <see cref="Choice"/>; Choice is a record, so a new instance selects the matching item.</summary>
public sealed class ChoiceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => new Choice(value as string ?? "");
    // null happens while the item list is rebuilt; that must not clear the slot.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Choice c ? c.Id : Avalonia.Data.BindingOperations.DoNothing;
}
