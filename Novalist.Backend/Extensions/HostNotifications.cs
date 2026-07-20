using System.Linq;
using Novalist.Sdk.Models;

namespace Novalist.Backend.Extensions;

/// <summary>Sinks the extension host raises toward the renderer (wired to
/// JSON-RPC ui/* notifications by the backend host).</summary>
public static class HostNotifications
{
    public static Action<string>? Error { get; set; }
}

/// <summary>Headless hotkey registry: extension-contributed gestures are
/// collected here and served to the renderer's hotkeys.ts.</summary>
public static class HotkeyRegistry
{
    private static readonly List<HotkeyDescriptor> Registered = [];

    public static IReadOnlyList<HotkeyDescriptor> All
    {
        get { lock (Registered) return Registered.ToList(); }
    }

    public static void Register(HotkeyDescriptor descriptor)
    {
        lock (Registered) Registered.Add(descriptor);
    }

    public static void RegisterRange(IEnumerable<HotkeyDescriptor> descriptors)
    {
        lock (Registered) Registered.AddRange(descriptors);
    }

    public static void Unregister(string actionId)
    {
        lock (Registered) Registered.RemoveAll(d => d.ActionId == actionId);
    }

    /// <summary>Finds a registered descriptor by action id, or null.</summary>
    public static HotkeyDescriptor? Find(string actionId)
    {
        lock (Registered) return Registered.FirstOrDefault(d => d.ActionId == actionId);
    }
}
