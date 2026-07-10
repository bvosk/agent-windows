using System.ComponentModel;
using System.Runtime.InteropServices;
using AgentWindows.Core.Input;

namespace AgentWindows.Automation.Input;

internal static partial class NativeInput
{
    private const uint _inputMouse = 0;
    private const uint _inputKeyboard = 1;
    private const uint _mouseLeftDown = 0x0002;
    private const uint _mouseLeftUp = 0x0004;
    private const uint _mouseRightDown = 0x0008;
    private const uint _mouseRightUp = 0x0010;
    private const uint _mouseMiddleDown = 0x0020;
    private const uint _mouseMiddleUp = 0x0040;
    private const uint _mouseWheel = 0x0800;
    private const uint _mouseHorizontalWheel = 0x1000;
    private const int _wheelDelta = 120;
    private const uint _keyUp = 0x0002;
    private const uint _unicode = 0x0004;

    public static void Click(System.Drawing.Point point, MouseButtonKind button, bool doubleClick)
    {
        if (!SetCursorPos(point.X, point.Y))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetCursorPos failed.");
        }

        var (down, up) = button switch
        {
            MouseButtonKind.Left => (_mouseLeftDown, _mouseLeftUp),
            MouseButtonKind.Right => (_mouseRightDown, _mouseRightUp),
            MouseButtonKind.Middle => (_mouseMiddleDown, _mouseMiddleUp),
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };
        var count = doubleClick ? 4 : 2;
        var inputs = new Input[count];
        inputs[0] = MouseEvent(down);
        inputs[1] = MouseEvent(up);
        if (doubleClick)
        {
            inputs[2] = MouseEvent(down);
            inputs[3] = MouseEvent(up);
        }

        Send(inputs);
    }

    public static void Press(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (!KeyMapper.TryMapKey(gesture.Key, out var key))
        {
            throw new ArgumentException($"Unknown key '{gesture.Key}'.", nameof(gesture));
        }

        var modifiers = gesture.Modifiers.Select(KeyMapper.ToVirtualKey).ToArray();
        var inputs = new Input[(modifiers.Length * 2) + 2];
        var index = 0;
        foreach (var modifier in modifiers)
        {
            inputs[index++] = KeyEvent((ushort)modifier, keyUp: false);
        }

        inputs[index++] = KeyEvent((ushort)key, keyUp: false);
        inputs[index++] = KeyEvent((ushort)key, keyUp: true);
        for (var i = modifiers.Length - 1; i >= 0; i--)
        {
            inputs[index++] = KeyEvent((ushort)modifiers[i], keyUp: true);
        }

        Send(inputs);
    }

    public static void Scroll(System.Drawing.Point point, ScrollDirection direction, double amount)
    {
        if (!SetCursorPos(point.X, point.Y))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetCursorPos failed.");
        }

        var magnitude = Math.Max(1, (int)Math.Round(Math.Abs(amount) * _wheelDelta));
        var (flags, delta) = direction switch
        {
            ScrollDirection.Up => (_mouseWheel, magnitude),
            ScrollDirection.Down => (_mouseWheel, -magnitude),
            ScrollDirection.Left => (_mouseHorizontalWheel, -magnitude),
            ScrollDirection.Right => (_mouseHorizontalWheel, magnitude),
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
        Send([MouseEvent(flags, unchecked((uint)delta))]);
    }

    public static void TypeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var inputs = new Input[text.Length * 2];
        var index = 0;
        foreach (var character in text)
        {
            inputs[index++] = UnicodeEvent(character, keyUp: false);
            inputs[index++] = UnicodeEvent(character, keyUp: true);
        }

        Send(inputs);
    }

    private static Input MouseEvent(uint flags, uint mouseData = 0) =>
        new()
        {
            Type = _inputMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput { MouseData = mouseData, Flags = flags },
            },
        };

    private static Input KeyEvent(ushort key, bool keyUp) =>
        new()
        {
            Type = _inputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput { VirtualKey = key, Flags = keyUp ? _keyUp : 0 },
            },
        };

    private static Input UnicodeEvent(char character, bool keyUp) =>
        new()
        {
            Type = _inputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = character,
                    Flags = keyUp ? _unicode | _keyUp : _unicode,
                },
            },
        };

    private static unsafe void Send(Input[] inputs)
    {
        if (inputs.Length == 0)
        {
            return;
        }

        fixed (Input* pointer = inputs)
        {
            var sent = SendInput((uint)inputs.Length, pointer, sizeof(Input));
            if (sent != inputs.Length)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    $"SendInput accepted {sent} of {inputs.Length} input events."
                );
            }
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint SendInput(uint count, Input* inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}
