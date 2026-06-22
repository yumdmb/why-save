using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Serilog;
using WhySave.Native;

namespace WhySave.App.Services;

public sealed class GlobalHotKeyService : IDisposable
{
    public const int HotKeyId = 0x5748; // 'WH'

    private readonly ILogger _logger;
    private HwndSource? _hwndSource;
    private HotKeyDescriptor _currentDescriptor;
    private bool _isRegistered;

    public GlobalHotKeyService(ILogger logger)
    {
        _logger = logger;
        _currentDescriptor = HotKeyDescriptor.Default.Clone();
    }

    public event EventHandler? HotKeyPressed;

    public HotKeyDescriptor CurrentDescriptor => _currentDescriptor.Clone();

    public bool IsRegistered => _isRegistered;

    public HotKeyRegistrationResult Register(HwndSource hwndSource, HotKeyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(hwndSource);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (_hwndSource is not null && _hwndSource.Handle != hwndSource.Handle)
            UnregisterCurrent();

        _hwndSource = hwndSource;

        if (!descriptor.IsValid)
        {
            _logger.Warning("Hotkey descriptor is not valid: {Descriptor}", descriptor);
            return HotKeyRegistrationResult.Invalid;
        }

        if (_isRegistered && SameDescriptor(_currentDescriptor, descriptor))
        {
            _logger.Information("Hotkey already registered with same descriptor: {Descriptor}", descriptor);
            return HotKeyRegistrationResult.AlreadyRegistered;
        }

        UnregisterCurrent();

        var virtualKey = KeyInterop.VirtualKeyFromKey(descriptor.Key);
        var modifiers = descriptor.ToNativeModifiers();

        _logger.Information("Registering global hotkey {Descriptor} (vk={Vk}, mods={Mods})", descriptor, virtualKey, modifiers);

        var registered = HotKeyRegistrar.RegisterHotKey(
            hwndSource.Handle,
            HotKeyId,
            modifiers,
            (uint)virtualKey);

        if (!registered)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.Warning("Failed to register global hotkey {Descriptor}; Win32 error {Error}", descriptor, error);
            _isRegistered = false;
            return error == HotKeyRegistrar.ERROR_HOTKEY_ALREADY_REGISTERED
                ? HotKeyRegistrationResult.AlreadyInUse
                : HotKeyRegistrationResult.Failed;
        }

        _currentDescriptor = descriptor.Clone();
        _isRegistered = true;
        hwndSource.AddHook(HwndHook);
        _logger.Information("Global hotkey registered: {Descriptor}", descriptor);
        return HotKeyRegistrationResult.Success;
    }

    public HotKeyRegistrationResult ReRegister(HotKeyDescriptor descriptor)
    {
        if (_hwndSource is null)
        {
            _logger.Warning("Cannot re-register hotkey: no HWND source is attached");
            return HotKeyRegistrationResult.NoWindow;
        }

        return Register(_hwndSource, descriptor);
    }

    public void UnregisterCurrent()
    {
        if (_hwndSource is null || !_isRegistered)
            return;

        HotKeyRegistrar.UnregisterHotKey(_hwndSource.Handle, HotKeyId);
        _hwndSource.RemoveHook(HwndHook);
        _isRegistered = false;
        _logger.Information("Global hotkey unregistered");
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WindowMessage.WM_HOTKEY && wParam.ToInt32() == HotKeyId)
        {
            _logger.Information("Global hotkey pressed");
            HotKeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static bool SameDescriptor(HotKeyDescriptor a, HotKeyDescriptor b) =>
        a.Modifiers == b.Modifiers && a.Key == b.Key;

    public void Dispose()
    {
        UnregisterCurrent();
    }
}

public enum HotKeyRegistrationResult
{
    Success,
    AlreadyRegistered,
    AlreadyInUse,
    Invalid,
    NoWindow,
    Failed,
}

public static class HotKeyParser
{
    public static bool TryParse(string text, out HotKeyDescriptor descriptor)
    {
        descriptor = new HotKeyDescriptor();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        var modifiers = HotKeyModifier.None;
        Key? key = null;

        foreach (var part in parts)
        {
            var normalized = part;
            if (normalized.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotKeyModifier.Control;
            }
            else if (normalized.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotKeyModifier.Alt;
            }
            else if (normalized.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotKeyModifier.Shift;
            }
            else if (normalized.Equals("Win", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Windows", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Cmd", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotKeyModifier.Win;
            }
            else
            {
                if (key is not null)
                    return false;

                if (!TryParseKey(part, out var parsedKey))
                    return false;

                key = parsedKey;
            }
        }

        if (key is null)
            return false;

        descriptor.Modifiers = modifiers;
        descriptor.Key = key.Value;
        return true;
    }

    private static bool TryParseKey(string text, out Key key)
    {
        key = Key.None;

        if (text.Length == 1)
        {
            var ch = text[0];
            if (ch >= '0' && ch <= '9')
            {
                key = (Key)((int)Key.D0 + (ch - '0'));
                return true;
            }

            if (ch >= 'A' && ch <= 'Z')
            {
                key = (Key)((int)Key.A + (ch - 'A'));
                return true;
            }

            if (ch >= 'a' && ch <= 'z')
            {
                key = (Key)((int)Key.A + (ch - 'a'));
                return true;
            }
        }

        return Enum.TryParse<Key>(text, true, out key) && key != Key.None;
    }
}
