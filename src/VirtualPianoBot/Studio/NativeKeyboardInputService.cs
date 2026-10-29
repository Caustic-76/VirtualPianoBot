using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VirtualPianoBot.Studio;

/// <summary>
/// Windows keyboard adapter for VPB 6.
/// Uses the native INPUT layout expected by SendInput on both x86 and x64 Windows.
/// Input is opt-in: callers must explicitly Arm() it before any key events are sent.
/// </summary>
public sealed class NativeKeyboardInputService : IDisposable
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort VkShift = 0x10;
    private const ushort VkOem1 = 0xBA;

    private readonly HashSet<ushort> _pressed = [];
    private bool _armed;
    private bool _disposed;

    public bool IsArmed => _armed;
    public string LastError { get; private set; } = "";

    public void Arm()
    {
        if (_disposed) return;
        _armed = true;
        LastError = "";
    }

    public void Disarm()
    {
        ReleaseAll();
        _armed = false;
    }

    public bool HandleAction(PlaybackAction action)
    {
        if (_disposed || !_armed || !action.IsPlayable) return false;

        var vk = ToVirtualKey(action.Key);
        if (vk == 0)
        {
            LastError = $"Unsupported keyboard key '{action.Key}'.";
            return false;
        }

        if (action.IsNoteOn)
            KeyDown(vk, action.Shift);
        else
            KeyUpEvent(vk);

        return string.IsNullOrEmpty(LastError);
    }

    public void ReleaseAll()
    {
        if (_pressed.Count > 0)
        {
            var inputs = new List<NativeInput>(_pressed.Count + 1);
            foreach (var vk in _pressed.ToArray())
                inputs.Add(KeyInput(vk, KeyUp));


            SendInputs(inputs);
        }
        _pressed.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disarm();
    }

    private void KeyDown(ushort vk, bool shift)
    {
        // Match the v4.1.0 keyboard semantics: Shift is a modifier for the
        // character's key-down only. Holding Shift until the note-off corrupts
        // simultaneous unshifted notes in chords.
        if (shift) SendKey(VkShift, false);
        SendKey(vk, false);
        if (shift) SendKey(VkShift, true);
    }

    private void KeyUpEvent(ushort vk)
    {
        SendKey(vk, true);
    }

    private void SendKey(ushort vk, bool keyUp)
    {
        var flags = keyUp ? KeyUp : 0u;
        SendInputs([KeyInput(vk, flags)]);

        if (vk != VkShift)
        {
            if (keyUp) _pressed.Remove(vk);
            else _pressed.Add(vk);
        }
    }

    private void SendInputs(IReadOnlyList<NativeInput> inputs)
    {
        if (inputs.Count == 0) return;

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeInput>());
        if (sent != inputs.Count)
        {
            var error = Marshal.GetLastWin32Error();
            LastError = error == 0
                ? $"Windows accepted {sent} of {inputs.Count} keyboard events."
                : $"SendInput failed: {new Win32Exception(error).Message} (0x{error:X8}).";
        }
        else
        {
            LastError = "";
        }
    }

    private static NativeInput KeyInput(ushort vk, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new NativeInputData
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = vk,
                ScanCode = 0,
                Flags = flags,
                Time = 0,
                ExtraInfo = IntPtr.Zero
            }
        }
    };

    private static ushort ToVirtualKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        var value = key.Trim();
        if (value.Length != 1) return 0;

        var c = value[0];
        if (c is >= 'a' and <= 'z') return (ushort)char.ToUpperInvariant(c);
        if (c is >= 'A' and <= 'Z') return c;
        if (c is >= '0' and <= '9') return c;
        if (c == ';') return VkOem1;
        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, NativeInput[] inputs, int sizeOfInput);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputData Data;
    }

    // INPUT is DWORD + a 32-byte union on 64-bit Windows, for a total of 40 bytes.
    // The previous implementation's union was only the 24-byte KEYBDINPUT payload,
    // causing SendInput to receive the wrong cbSize and reject the events.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct NativeInputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
