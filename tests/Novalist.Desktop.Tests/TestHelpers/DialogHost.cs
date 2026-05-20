using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Novalist.Desktop.Tests.TestHelpers;

/// <summary>
/// Helpers for exercising dialog / view code-behind under the headless Avalonia
/// fixture: hosts a control in a window so OnAttachedToVisualTree fires, pumps
/// the dispatcher, and invokes the private Click handlers / protected key
/// handlers that XAML wires up (no real input device under headless).
/// </summary>
internal static class DialogHost
{
    private static Window? _host;

    /// <summary>
    /// Attach <paramref name="content"/> to a single reused headless window and run posted jobs
    /// so attach-time logic (OnAttachedToVisualTree focus posts) executes. One persistent window
    /// is reused for the whole run because repeatedly creating/closing windows churns the headless
    /// FocusManager and leaks NREs into sibling tests. Focus exceptions from the headless keyboard
    /// device are swallowed — they are infra noise, not code under test.
    /// </summary>
    public static void Show(Control content)
    {
        if (_host is null)
        {
            _host = new Window();
            _host.Show();
        }
        try
        {
            _host.Content = content;
            Dispatcher.UIThread.RunJobs();
            _host.Content = null;
            Dispatcher.UIThread.RunJobs();
        }
        catch (System.NullReferenceException) { /* headless FocusManager noise */ }
    }

    /// <summary>Invoke a private/protected Click handler by name with a synthetic RoutedEventArgs.</summary>
    public static void Click(object target, string handler)
        => Invoke(target, handler, null, new RoutedEventArgs());

    /// <summary>Raise a KeyDown on the control, driving the protected OnKeyDown override.</summary>
    public static void PressKey(Control target, Key key)
        => target.RaiseEvent(new KeyEventArgs { Key = key, RoutedEvent = InputElement.KeyDownEvent });

    /// <summary>Pump queued dispatcher jobs (e.g. TextChanged-driven renders), swallowing headless focus noise.</summary>
    public static void RunJobs()
    {
        try { Dispatcher.UIThread.RunJobs(); }
        catch (System.NullReferenceException) { /* headless FocusManager noise */ }
    }

    /// <summary>Read a XAML-named control off a code-behind partial via its generated backing field.</summary>
    public static T? GetVisualNamed<T>(this object target, string name) where T : class
    {
        var fi = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return fi?.GetValue(target) as T;
    }

    /// <summary>
    /// Fabricate an event-args instance whose constructor is internal to the input
    /// pipeline (PointerPressedEventArgs, TappedEventArgs, …) so handlers that only
    /// read/write <c>e.Handled</c> can be driven by reflection. The object is zero-
    /// initialised — only safe when the handler does not read other members of <c>e</c>.
    /// </summary>
    public static T UninitializedArgs<T>() where T : class
        => (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));

    public static void Invoke(object target, string method, params object?[] args)
    {
        // Match by name + arg count: a handler like OnPointerEntered(object, PointerEventArgs)
        // collides with the base InputElement.OnPointerEntered(PointerEventArgs), so a name-only
        // GetMethod throws AmbiguousMatchException.
        var candidates = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name == method && m.GetParameters().Length == args.Length)
            .ToList();
        var mi = candidates.Count == 1
            ? candidates[0]
            : target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
              ?? throw new MissingMethodException(target.GetType().Name, method);
        mi.Invoke(target, args);
    }
}
