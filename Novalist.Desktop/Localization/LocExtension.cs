using System;
using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Novalist.Desktop.Localization;

/// <summary>
/// Markup extension for localized strings.
/// Usage: <code>{loc:Loc ribbon.home}</code>
/// Creates a binding that automatically refreshes when the UI language changes.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; }
    public string? StringFormat { get; set; }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        if (target?.TargetObject is AvaloniaObject ao && target.TargetProperty is AvaloniaProperty ap)
        {
            // Set the initial value
            UpdateValue(ao, ap);

            // Subscribe to language changes
            Loc.Instance.LanguageChanged += () => UpdateValue(ao, ap);
        }

        return FormatValue(Loc.T(Key));
    }

    private void UpdateValue(AvaloniaObject ao, AvaloniaProperty ap)
    {
        ao.SetValue(ap, FormatValue(Loc.T(Key)));
    }

    private object FormatValue(string value)
    {
        if (!string.IsNullOrEmpty(StringFormat))
        {
            try { return string.Format(StringFormat, value); }
            catch (FormatException) { }
        }
        return value;
    }
}
