using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Clicky.Hotkey;

namespace Clicky.Companion;

/// <summary>
/// The observable surface the WPF companion panel binds to. Mirrors the
/// @Published surface on Mac's CompanionManager:
///   - voiceState
///   - hasMicrophonePermission
///   - hasScreenRecordingPermission
///   - the configured push-to-talk shortcut
///
/// This is a placeholder that later stories (US-004, US-005, US-010) will
/// drive. For US-003 it exists so the panel has something to bind against.
/// </summary>
public sealed class CompanionViewModel : INotifyPropertyChanged
{
    private VoiceState _voiceState = VoiceState.Idle;
    private bool _hasMicrophonePermission;
    private bool _hasScreenCapturePermission;
    private PushToTalkShortcut _pushToTalkShortcut = PushToTalkShortcut.ControlAlt;
    private bool _isShortcutPressed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Current voice pipeline state (Idle / Listening / Processing / Responding).</summary>
    public VoiceState VoiceState
    {
        get => _voiceState;
        set
        {
            if (_voiceState == value) return;
            _voiceState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VoiceStateDisplay));
        }
    }

    public string VoiceStateDisplay => _voiceState switch
    {
        VoiceState.Idle => "Idle",
        VoiceState.Listening => "Listening",
        VoiceState.Processing => "Processing",
        VoiceState.Responding => "Responding",
        _ => "Idle",
    };

    public bool HasMicrophonePermission
    {
        get => _hasMicrophonePermission;
        set
        {
            if (_hasMicrophonePermission == value) return;
            _hasMicrophonePermission = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MicrophonePermissionDisplay));
            OnPropertyChanged(nameof(AllPermissionsGranted));
        }
    }

    public string MicrophonePermissionDisplay => _hasMicrophonePermission ? "Granted" : "Not granted";

    public bool HasScreenCapturePermission
    {
        get => _hasScreenCapturePermission;
        set
        {
            if (_hasScreenCapturePermission == value) return;
            _hasScreenCapturePermission = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScreenCapturePermissionDisplay));
            OnPropertyChanged(nameof(AllPermissionsGranted));
        }
    }

    public string ScreenCapturePermissionDisplay => _hasScreenCapturePermission ? "Granted" : "Not granted";

    public bool AllPermissionsGranted => _hasMicrophonePermission && _hasScreenCapturePermission;

    public PushToTalkShortcut PushToTalkShortcut
    {
        get => _pushToTalkShortcut;
        set
        {
            if (_pushToTalkShortcut == value) return;
            _pushToTalkShortcut = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PushToTalkShortcutDisplay));
        }
    }

    public string PushToTalkShortcutDisplay => _pushToTalkShortcut.DisplayName();

    /// <summary>True while the push-to-talk chord is being held down (set by US-005 hook).</summary>
    public bool IsShortcutPressed
    {
        get => _isShortcutPressed;
        set
        {
            if (_isShortcutPressed == value) return;
            _isShortcutPressed = value;
            OnPropertyChanged();
        }
    }

    /// <summary>App version read from the entry assembly, shown in the panel header.</summary>
    public string AppVersion { get; } = ReadAppVersion();

    private static string ReadAppVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? "dev" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
