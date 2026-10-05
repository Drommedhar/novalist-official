using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

namespace Novalist.Backend.Dictation;

// Native keyboard injection opens the user's microphone UI; validated manually.
[ExcludeFromCodeCoverage]
internal static class WindowsVoiceTyping
{
    // Windows owns capture, language, online recognition and stopping. Invoke
    // the native panel exactly as the documented Windows+H shortcut does.
    public static void Open()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Input[] keys = [Key(0x5B), Key(0x48), Key(0x48, true), Key(0x5B, true)];
        if (SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>()) != keys.Length)
        {
            Input[] release = [Key(0x48, true), Key(0x5B, true)];
            SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
            throw new InvalidOperationException("Press Windows+H to open Windows voice typing.");
        }
    }

    private static Input Key(ushort code, bool up = false) => new()
    {
        Type = 1,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = code, Flags = up ? 2u : 0u } }
    };
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
