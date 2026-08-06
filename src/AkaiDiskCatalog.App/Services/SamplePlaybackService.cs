using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AkaiDiskCatalog.App.Services;

/// <summary>
/// Plays a WAV byte array via a small OS-native player process, so no audio-engine
/// dependency is needed. Both platforms use a killable Process handle so Stop() behaves
/// uniformly: macOS shells out to afplay, Windows shells out to PowerShell's SoundPlayer
/// (avoiding System.Media.SoundPlayer directly, which needs Windows-specific APIs not
/// available on this project's plain net10.0 TFM).
/// </summary>
public sealed class SamplePlaybackService : IDisposable
{
    public event EventHandler? PlaybackStopped;

    private Process? _process;
    private string? _tempPath;

    public bool IsPlaying => _process is { HasExited: false };

    public void Play(byte[] wavBytes)
    {
        Stop();

        string tempPath = Path.Combine(Path.GetTempPath(), $"samplerdiskcatalog-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(tempPath, wavBytes);
        _tempPath = tempPath;

        var psi = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new ProcessStartInfo("afplay", Quote(tempPath))
            : new ProcessStartInfo("powershell", $"-NoProfile -Command \"(New-Object Media.SoundPlayer {Quote(tempPath)}).PlaySync()\"");
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += OnProcessExited;
        _process = process;
        process.Start();
    }

    public void Stop()
    {
        var process = _process;
        if (process is null) return;

        process.Exited -= OnProcessExited;
        try
        {
            if (!process.HasExited) process.Kill();
        }
        catch (InvalidOperationException) { /* already exited - ignore */ }
        _process = null;

        CleanUpTempFile();
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _process = null;
        CleanUpTempFile();
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    private void CleanUpTempFile()
    {
        if (_tempPath is null) return;
        try { File.Delete(_tempPath); } catch { /* best-effort */ }
        _tempPath = null;
    }

    private static string Quote(string path) => $"\"{path}\"";

    public void Dispose() => Stop();
}
