using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Novalist.Desktop.ViewModels;

public enum ToastSeverity { Info, Success, Warning, Error }

public partial class ToastNotification : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Message { get; }
    public ToastSeverity Severity { get; }

    public ToastNotification(string message, ToastSeverity severity = ToastSeverity.Info)
    {
        Message = message;
        Severity = severity;
    }
}
