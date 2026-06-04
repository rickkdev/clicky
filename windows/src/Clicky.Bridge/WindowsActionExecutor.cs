using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Clicky.Bridge;

public sealed class WindowsActionExecutor : IWindowsActionExecutor
{
    public Task<Dictionary<string, object?>> ClickAsync(ActionProposal proposal, CancellationToken cancellationToken)
        => SendMouseClickAsync(proposal, clickCount: 1, cancellationToken);

    public Task<Dictionary<string, object?>> DoubleClickAsync(ActionProposal proposal, CancellationToken cancellationToken)
        => SendMouseClickAsync(proposal, clickCount: 2, cancellationToken);

    public Task<Dictionary<string, object?>> TypeTextAsync(ActionProposal proposal, CancellationToken cancellationToken)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        var text = proposal.InputPreview ?? string.Empty;
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException("typeText requires inputPreview text");

        foreach (var ch in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendUnicodeChar(ch);
        }

        return Task.FromResult(new Dictionary<string, object?>
        {
            ["method"] = "typeText",
            ["charactersTyped"] = text.Length,
            ["redactedText"] = true,
        });
    }

    public Task<Dictionary<string, object?>> HotkeyAsync(ActionProposal proposal, CancellationToken cancellationToken)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        var keys = proposal.Hotkey?.Where(k => !string.IsNullOrWhiteSpace(k)).Select(MapVirtualKey).ToList() ?? [];
        if (keys.Count == 0)
            throw new InvalidOperationException("hotkey requires at least one key");

        foreach (var key in keys)
            SendVirtualKey(key, keyUp: false);
        for (var i = keys.Count - 1; i >= 0; i--)
            SendVirtualKey(keys[i], keyUp: true);

        return Task.FromResult(new Dictionary<string, object?>
        {
            ["method"] = "hotkey",
            ["keyCount"] = keys.Count,
        });
    }

    public Task<Dictionary<string, object?>> OpenApplicationAsync(ActionProposal proposal, CancellationToken cancellationToken)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        var application = proposal.Application ?? proposal.TargetLabel;
        if (string.IsNullOrWhiteSpace(application))
            throw new InvalidOperationException("openApplication requires application");

        using var process = Process.Start(new ProcessStartInfo(application) { UseShellExecute = true });
        return Task.FromResult(new Dictionary<string, object?>
        {
            ["method"] = "openApplication",
            ["started"] = process is not null,
            ["processId"] = process?.Id,
        });
    }

    public Task<Dictionary<string, object?>> FocusWindowAsync(ActionProposal proposal, CancellationToken cancellationToken)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        var title = proposal.NativeSelector?.Kind == "windowTitle" ? proposal.NativeSelector.Value : proposal.TargetLabel;
        var hwnd = FindWindowByTitle(title);
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("window not found");
        var focused = SetForegroundWindow(hwnd);
        return Task.FromResult(new Dictionary<string, object?>
        {
            ["method"] = "focusWindow",
            ["focused"] = focused,
        });
    }

    private static Task<Dictionary<string, object?>> SendMouseClickAsync(ActionProposal proposal, int clickCount, CancellationToken cancellationToken)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        var coordinates = proposal.Coordinates ?? throw new InvalidOperationException($"{proposal.ActionType} requires coordinates");
        SetCursorPos(coordinates.X, coordinates.Y);
        for (var i = 0; i < clickCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendMouseButton(up: false);
            SendMouseButton(up: true);
        }

        return Task.FromResult(new Dictionary<string, object?>
        {
            ["method"] = proposal.ActionType,
            ["clickCount"] = clickCount,
            ["coordinateSpace"] = "physical_pixels",
            ["screen"] = coordinates.Screen,
        });
    }

    private static IntPtr FindWindowByTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return IntPtr.Zero;

        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            var length = GetWindowTextLength(hwnd);
            if (length == 0)
                return true;
            var builder = new StringBuilder(length + 1);
            GetWindowText(hwnd, builder, builder.Capacity);
            if (builder.ToString().Contains(title, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static ushort MapVirtualKey(string key)
    {
        return key.Trim().ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => 0x11,
            "SHIFT" => 0x10,
            "ALT" => 0x12,
            "WIN" or "WINDOWS" => 0x5B,
            "ENTER" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "SPACE" => 0x20,
            "BACKSPACE" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            { Length: 1 } s => (ushort)char.ToUpperInvariant(s[0]),
            _ => throw new InvalidOperationException($"unsupported hotkey key: {key}"),
        };
    }

    private static void SendUnicodeChar(char ch)
    {
        SendInputChecked(new[]
        {
            Input.KeyboardUnicode(ch, keyUp: false),
            Input.KeyboardUnicode(ch, keyUp: true),
        });
    }

    private static void SendVirtualKey(ushort key, bool keyUp)
        => SendInputChecked(new[] { Input.KeyboardVirtualKey(key, keyUp) });

    private static void SendMouseButton(bool up)
        => SendInputChecked(new[] { Input.MouseButton(up) });

    private static void SendInputChecked(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
            throw new InvalidOperationException("SendInput failed");
    }

    private static void EnsureWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Windows action execution is only available on Windows.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;

        public static Input MouseButton(bool keyUp) => new()
        {
            Type = 0,
            U = new InputUnion { Mouse = new MouseInput { DwFlags = keyUp ? 0x0004u : 0x0002u } },
        };

        public static Input KeyboardUnicode(char ch, bool keyUp) => new()
        {
            Type = 1,
            U = new InputUnion { Keyboard = new KeyboardInput { WScan = ch, DwFlags = 0x0004u | (keyUp ? 0x0002u : 0u) } },
        };

        public static Input KeyboardVirtualKey(ushort key, bool keyUp) => new()
        {
            Type = 1,
            U = new InputUnion { Keyboard = new KeyboardInput { WVk = key, DwFlags = keyUp ? 0x0002u : 0u } },
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }
}
