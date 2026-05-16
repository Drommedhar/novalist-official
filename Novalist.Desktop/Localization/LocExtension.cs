using System;
using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Novalist.Desktop.Localization;

/// <summary>
/// Markup extension for localized strings.
/// Usage: <code>{loc:Loc ribbon.home}</code>
/// Creates a binding that automatically refreshes when the UI language changes.
/// Uses a WeakReference to the target so the markup never roots the visual tree.
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
            UpdateValue(ao, ap);

            // Hold the target via a WeakReference so the LanguageChanged
            // subscription never roots the visual tree. When the target is
            // collected, the handler unsubscribes itself the next time it fires.
            var weakTarget = new WeakReference<AvaloniaObject>(ao);
            var weakProp = ap;
            Action? handler = null;
            handler = () =>
            {
                if (weakTarget.TryGetTarget(out var liveTarget))
                {
                    UpdateValue(liveTarget, weakProp);
                }
                else if (handler != null)
                {
                    Loc.Instance.LanguageChanged -= handler;
                }
            };
            Loc.Instance.LanguageChanged += handler;
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
