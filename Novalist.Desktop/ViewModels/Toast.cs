using System;

namespace Novalist.Desktop.ViewModels;

/// <summary>
/// Static dispatcher for toast notifications. MainWindowViewModel sets <see cref="Show"/>
/// on construction; any code (VMs, services) can call <c>Toast.Show?.Invoke(...)</c>.
/// </summary>
public static class Toast
{
    public static Action<string, ToastSeverity>? Show;
}
