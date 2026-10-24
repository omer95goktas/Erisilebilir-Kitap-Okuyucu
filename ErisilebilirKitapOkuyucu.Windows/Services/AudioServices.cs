using System.IO;
using System.Media;
using System.Reflection;
using System.Windows.Threading;
using NAudio.Wave;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace ErisilebilirKitapOkuyucu.Services;

/// <summary>Sesli kitap oynatıcısı (Windows.Media.Playback: ses perdesi korunarak hız, medya tuşları desteği).</summary>
public sealed class AudioPlayerService : IDisposable
{
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private MediaPlayer? _player;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _sleep = new() { Interval = TimeSpan.FromSeconds(1) };
    private double _speed = 1.0;
    private double _volume = 1.0;
    private long _pendingStart;
    private long _sleepEndTicks;

    public bool IsPlaying { get; private set; }
    public bool IsLooping { get; private set; }
    public long PositionMs { get; private set; }
    public long DurationMs { get; private set; }
    public double Speed => _speed;
    public double Volume => _volume;
    public int SleepSecondsLeft { get; private set; }
    public string? CurrentPath { get; private set; }

    public event Action? StateChanged;
    public event Action? Tick;
    public event Action? Completed;
    public event Action<long>? DurationKnown;
    public event Action<string>? Error;
    public event Action? SleepTimerFired;

    public AudioPlayerService()
    {
        _tick.Tick += (_, _) => Poll();
        _sleep.Tick += (_, _) => SleepTick();
    }

    public void Play(string path, long startMs = 0, string? title = null)
    {
        DisposePlayer();
        CurrentPath = path;
        PositionMs = startMs; DurationMs = 0; _pendingStart = startMs;
        if (!File.Exists(path)) { Error?.Invoke("Ses dosyası bulunamadı: " + path); return; }
        try
        {
            var p = new MediaPlayer { AutoPlay = false, IsLoopingEnabled = IsLooping, Volume = _volume };
            p.MediaOpened += (_, _) => _ui.BeginInvoke(() => OnOpened(p));
            p.MediaEnded += (_, _) => _ui.BeginInvoke(() => { if (_player == p && !IsLooping) { IsPlaying = false; _tick.Stop(); StateChanged?.Invoke(); Completed?.Invoke(); } });
            p.MediaFailed += (_, e) => _ui.BeginInvoke(() => { if (_player == p) { IsPlaying = false; StateChanged?.Invoke(); Error?.Invoke("Ses dosyası oynatılamadı: " + e.ErrorMessage); } });
            _player = p;
            try
            {
                var u = p.SystemMediaTransportControls.DisplayUpdater;
                u.Type = MediaPlaybackType.Music;
                u.MusicProperties.Title = title ?? Path.GetFileNameWithoutExtension(path);
                u.Update();
            }
            catch { }
            p.Source = MediaSource.CreateFromUri(new Uri(path));
            p.Play();
            IsPlaying = true;
            _tick.Start();
            StateChanged?.Invoke();
        }
        catch (Exception e) { AppPaths.Log("audio play: " + e); Error?.Invoke("Ses dosyası oynatılamadı: " + e.Message); }
    }

    private void OnOpened(MediaPlayer p)
    {
        if (_player != p) return;
        try
        {
            var s = p.PlaybackSession;
            DurationMs = (long)s.NaturalDuration.TotalMilliseconds;
            s.PlaybackRate = _speed;
            p.Volume = _volume;
            if (_pendingStart > 0 && _pendingStart < DurationMs - 1000) s.Position = TimeSpan.FromMilliseconds(_pendingStart);
            _pendingStart = 0;
            DurationKnown?.Invoke(DurationMs);
        }
        catch (Exception e) { AppPaths.Log("audio opened: " + e); }
        StateChanged?.Invoke();
    }

    private void Poll()
    {
        if (_player == null) return;
        try
        {
            PositionMs = (long)_player.PlaybackSession.Position.TotalMilliseconds;
            if (DurationMs == 0) DurationMs = (long)_player.PlaybackSession.NaturalDuration.TotalMilliseconds;
        }
        catch { }
        Tick?.Invoke();
    }

    public void TogglePlayPause()
    {
        if (_player == null) return;
        if (IsPlaying) { _player.Pause(); IsPlaying = false; _tick.Stop(); Poll(); }
        else
        {
            if (DurationMs > 0 && PositionMs >= DurationMs - 500) SeekTo(0);
            _player.Play(); IsPlaying = true; _tick.Start();
        }
        StateChanged?.Invoke();
    }

    public void Pause() { if (IsPlaying) TogglePlayPause(); }

    public void SeekTo(long ms)
    {
        if (_player == null) return;
        ms = Math.Clamp(ms, 0, DurationMs > 0 ? DurationMs : long.MaxValue);
        try { _player.PlaybackSession.Position = TimeSpan.FromMilliseconds(ms); PositionMs = ms; } catch { }
        Tick?.Invoke();
    }

    public void SeekBy(int seconds) => SeekTo(PositionMs + seconds * 1000L);

    public void SetSpeed(double speed)
    {
        _speed = Math.Clamp(speed, 0.5, 3.0);
        try { if (_player != null) _player.PlaybackSession.PlaybackRate = _speed; } catch { }
        StateChanged?.Invoke();
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0.0, 1.0);
        try { if (_player != null) _player.Volume = _volume; } catch { }
        StateChanged?.Invoke();
    }

    public void SetLooping(bool loop)
    {
        IsLooping = loop;
        try { if (_player != null) _player.IsLoopingEnabled = loop; } catch { }
        StateChanged?.Invoke();
    }

    /// <summary>minutes = 0 zamanlayıcıyı kapatır.</summary>
    public void StartSleepTimer(int minutes)
    {
        _sleep.Stop();
        if (minutes <= 0) { SleepSecondsLeft = 0; StateChanged?.Invoke(); return; }
        SleepSecondsLeft = minutes * 60;
        _sleepEndTicks = Environment.TickCount64 + SleepSecondsLeft * 1000L;
        _sleep.Start();
        StateChanged?.Invoke();
    }

    private void SleepTick()
    {
        SleepSecondsLeft = (int)Math.Max(0, (_sleepEndTicks - Environment.TickCount64) / 1000);
        if (SleepSecondsLeft <= 0)
        {
            _sleep.Stop();
            Pause();
            SleepTimerFired?.Invoke();
        }
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        DisposePlayer();
        IsPlaying = false;
        StateChanged?.Invoke();
    }

    private void DisposePlayer()
    {
        _tick.Stop();
        var p = _player; _player = null;
        if (p != null) { try { p.Pause(); p.Source = null; p.Dispose(); } catch { } }
        IsPlaying = false;
    }

    public void Dispose() { DisposePlayer(); _sleep.Stop(); }
}

/// <summary>Sayfa çevirme efektleri (gömülü WAV kaynakları).</summary>
public static class PageTurnSounds
{
    public static readonly string[] Voices = { "Varsayılan", "Erva", "Facebook Beğeni Sesi", "Kapalı" };
    private static readonly Dictionary<string, byte[]> Cache = new();

    private static byte[]? Load(string name)
    {
        if (Cache.TryGetValue(name, out var b)) return b;
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        if (res == null) return null;
        using var s = asm.GetManifestResourceStream(res)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return Cache[name] = ms.ToArray();
    }

    public static void Play(string voice, bool isNext)
    {
        string? file = voice switch
        {
            "Erva" => isNext ? "erva_sonraki_sayfa.wav" : "erva_onceki_sayfa.wav",
            "Facebook Beğeni Sesi" => "facebook_begeni.wav",
            "Kapalı" => null,
            _ => "sayfa_degistirme.wav"
        };
        if (file == null) return;
        var data = Load(file);
        if (data == null) return;
        try { new SoundPlayer(new MemoryStream(data)).Play(); } catch (Exception e) { AppPaths.Log("sound: " + e.Message); }
    }

    /// <summary>Erva sesi daha uzun olduğundan TTS sonraki sayfaya geçmeden önce beklenecek süre.</summary>
    public static int DelayMs(string voice) => voice switch { "Kapalı" => 0, "Erva" => 1200, _ => 750 };
}

/// <summary>Sesli yer imi notu kaydı (NAudio, 16 bit mono 44.1 kHz WAV).</summary>
public sealed class VoiceRecorder : IDisposable
{
    private WaveInEvent? _in;
    private WaveFileWriter? _writer;
    private DateTime _started;
    public bool IsRecording { get; private set; }
    public string? FilePath { get; private set; }

    public static bool HasMicrophone => WaveInEvent.DeviceCount > 0;

    public void Start()
    {
        if (IsRecording) return;
        FilePath = Path.Combine(AppPaths.VoiceNotesDir, $"note_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");
        _in = new WaveInEvent { WaveFormat = new WaveFormat(44100, 16, 1) };
        _writer = new WaveFileWriter(FilePath, _in.WaveFormat);
        _in.DataAvailable += (_, e) => { try { _writer?.Write(e.Buffer, 0, e.BytesRecorded); } catch { } };
        _in.StartRecording();
        _started = DateTime.UtcNow;
        IsRecording = true;
    }

    /// <summary>Kaydı bitirir; süreyi (ms) döndürür.</summary>
    public long Stop()
    {
        if (!IsRecording) return 0;
        IsRecording = false;
        try { _in?.StopRecording(); } catch { }
        _in?.Dispose(); _in = null;
        _writer?.Dispose(); _writer = null;
        return (long)(DateTime.UtcNow - _started).TotalMilliseconds;
    }

    public void Discard()
    {
        Stop();
        try { if (FilePath != null) File.Delete(FilePath); } catch { }
        FilePath = null;
    }

    public void Dispose() => Stop();
}
