using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using MoonSweeperEditor.Core;

namespace MoonSweeperEditor;

/// <summary>XAML shorthand for a live-updating translated text: Text="{l:T SaveChanges}".</summary>
public sealed class TExtension(string key)
{
    public string Key { get; set; } = key;

    public IBinding ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(Loc.Language)) { Source = Loc.Instance, Mode = BindingMode.OneWay, Converter = new TextConverter(Key) };

    sealed class TextConverter(string key) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Text.Of(key);
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
    }
}

public static class Text
{
    /// <summary>Translated text without the leading emoji, which many Linux setups have no font for.</summary>
    public static string Of(string key, params object[] args) => Clean(Loc.T(key, args));

    public static string Clean(string text)
    {
        int i = 0;
        while (i < text.Length && (char.IsSurrogate(text[i]) || char.IsWhiteSpace(text[i]) || text[i] == '️'
                                   || CharUnicodeInfo.GetUnicodeCategory(text[i]) == UnicodeCategory.OtherSymbol))
            i++;
        var sb = new System.Text.StringBuilder(text.Length - i);
        for (; i < text.Length; i++)
        {
            if (char.IsSurrogate(text[i]) || text[i] == '️') continue;
            sb.Append(text[i]);
        }
        return sb.ToString();
    }
}
