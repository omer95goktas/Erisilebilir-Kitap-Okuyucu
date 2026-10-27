using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAudio.Wave;

namespace ErisilebilirKitapOkuyucu.Services;

/// <summary>
/// Google Cloud Text-to-Speech Motoru.
/// Kullanıcının kendi tanımladığı kişisel API anahtarı ile stüdyo kalitesinde Türkçe seslendirme sunar.
/// API anahtarı veya internet olmadığında otomatik olarak yerel seslere geçiş yapar.
/// </summary>
public sealed class GoogleCloudTtsEngine : ITtsEngine
{
    public string Id => "google";
    public string DisplayName => "Google Doğal Sesleri (Çevrim İçi - API)";

    // Google Cloud Text-to-Speech API'sinde Türkçe (tr-TR) için aktif olan gerçek sesler:
    // WaveNet (A, B, C, D, E) ve Standart (A, B, C, D, E)
    private static readonly IReadOnlyList<VoiceDescriptor> _voices = new List<VoiceDescriptor>
    {
        new("tr-TR-Wavenet-A", "Google WaveNet A (Kadın - Doğal)", "tr-TR", "google"),
        new("tr-TR-Wavenet-B", "Google WaveNet B (Erkek - Doğal)", "tr-TR", "google"),
        new("tr-TR-Wavenet-C", "Google WaveNet C (Kadın - Doğal)", "tr-TR", "google"),
        new("tr-TR-Wavenet-D", "Google WaveNet D (Kadın - Doğal)", "tr-TR", "google"),
        new("tr-TR-Wavenet-E", "Google WaveNet E (Erkek - Doğal)", "tr-TR", "google"),
        new("tr-TR-Standard-A", "Google Standart A (Kadın)", "tr-TR", "google"),
        new("tr-TR-Standard-B", "Google Standart B (Erkek)", "tr-TR", "google"),
        new("tr-TR-Standard-C", "Google Standart C (Kadın)", "tr-TR", "google"),
        new("tr-TR-Standard-D", "Google Standart D (Kadın)", "tr-TR", "google"),
        new("tr-TR-Standard-E", "Google Standart E (Erkek)", "tr-TR", "google")
    };

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly OneCoreEngine _fallbackEngine = new();

    private WaveOutEvent? _currentWaveOut;
    private readonly object _lock = new();

    private string? _prefetchedText;
    private Task<byte[]>? _prefetchTask;
    private CancellationTokenSource? _prefetchCts;

    public IReadOnlyList<VoiceDescriptor> GetVoices() => _voices;

    public static string CleanApiKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return string.Empty;
        var s = rawKey.Trim();

        // Fazladan tırnak işaretlerini temizle ("..." veya '...')
        if ((s.StartsWith("\"") && s.EndsWith("\"")) || (s.StartsWith("'") && s.EndsWith("'")))
        {
            if (s.Length >= 2) s = s.Substring(1, s.Length - 2).Trim();
        }

        // Bearer veya key= gibi önekleri kaldır
        if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(7).Trim();
        }
        else if (s.StartsWith("key=", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(4).Trim();
        }

        return s;
    }

    private static string? GetApiKey()
    {
        // 1. Ayarlardan kullanıcının girdiği anahtarı al
        try
        {
            var saved = CleanApiKey(App.Repo?.Settings?.GoogleApiKey);
            if (!string.IsNullOrEmpty(saved)) return saved;
        }
        catch { }

        // 2. Ortam değişkeninden kontrol et
        var env = CleanApiKey(Environment.GetEnvironmentVariable("GOOGLE_TTS_API_KEY"));
        if (!string.IsNullOrEmpty(env)) return env;

        return null;
    }

    /// <summary>
    /// Verilen veya kayıtlı Google Cloud TTS API anahtarını 1 kelimelik hafif bir sentez isteğiyle test eder.
    /// Türkçe ve anlaşılır sonuç mesajı döndürür.
    /// </summary>
    public static async Task<(bool success, string message)> ValidateApiKeyAsync(string? rawKey = null)
    {
        var key = CleanApiKey(rawKey ?? GetApiKey());
        if (string.IsNullOrWhiteSpace(key))
        {
            return (false, "API anahtarı girilmemiş. Lütfen Google Cloud Console'dan aldığınız API anahtarını yapıştırın.");
        }

        try
        {
            // Hem URL parametresi hem header ekleyerek sorgula
            string endpoint = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={Uri.EscapeDataString(key)}";
            var requestBody = new
            {
                input = new { text = "Test" },
                voice = new { languageCode = "tr-TR", name = "tr-TR-Standard-A" },
                audioConfig = new { audioEncoding = "MP3" }
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Goog-Api-Key", key);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _http.SendAsync(request, cts.Token);
            var respString = await response.Content.ReadAsStringAsync(cts.Token);

            if (response.IsSuccessStatusCode)
            {
                return (true, "Tebrikler! Google Cloud API anahtarınız geçerli ve başarıyla doğrulandı. Google seslerini kullanabilirsiniz.");
            }

            // Hata kodunu ve açıklamasını Türkçeleştir
            string errorDetail = "";
            string reason = "";
            try
            {
                var doc = JsonNode.Parse(respString);
                errorDetail = doc?["error"]?["message"]?.ToString() ?? "";
                reason = doc?["error"]?["status"]?.ToString() ?? "";
            }
            catch { }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                if (errorDetail.Contains("API has not been used", StringComparison.OrdinalIgnoreCase) ||
                    errorDetail.Contains("SERVICE_DISABLED", StringComparison.OrdinalIgnoreCase) ||
                    errorDetail.Contains("not enabled", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "HATA: Google Cloud projenizde 'Cloud Text-to-Speech API' servisi henüz etkinleştirilmemiş. Lütfen Google Cloud Console -> APIs & Services -> Library bölümünden Text-to-Speech API'yi etkinleştirin.");
                }
                if (errorDetail.Contains("billing", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "HATA: Google Cloud projenizde faturalandırma hesabı (Billing) tanımlı veya aktif değil.");
                }
                return (false, $"HATA: API Anahtarı yetkisiz veya geçersiz. ({response.StatusCode}: {errorDetail})");
            }
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                return (false, $"HATA: İstek biçimi geçersiz veya anahtar hatalı kopyalanmış. ({errorDetail})");
            }

            return (false, $"HATA ({response.StatusCode}): {errorDetail}");
        }
        catch (OperationCanceledException)
        {
            return (false, "Bağlantı zaman aşımına uğradı. Lütfen internet bağlantınızı kontrol edin.");
        }
        catch (Exception ex)
        {
            return (false, $"Bağlantı hatası: {ex.Message}");
        }
    }

    private async Task<byte[]> SynthesizeMp3Async(string text, string voice, float rate, float pitch, CancellationToken ct)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Google Cloud API anahtarı tanımlanmamış. Lütfen Ayarlar bölümünden kişisel API anahtarınızı girin.");
        }

        // Endpoint: Google API anahtarlarını hem X-Goog-Api-Key başlığında hem query parametresinde destekler.
        string endpoint = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={Uri.EscapeDataString(apiKey)}";

        // Rate: 0.25 - 4.0
        double speakingRate = Math.Clamp(rate, 0.25, 4.0);

        // Pitch: -20.0 to 20.0 semitones
        double pitchSemitones = Math.Clamp((pitch - 1.0) * 10.0, -20.0, 20.0);

        // Güvenli varsayılan ses
        string targetVoice = string.IsNullOrEmpty(voice) || voice == "tr-TR-Neural2-A" ? "tr-TR-Wavenet-A" : voice;

        var requestBody = new
        {
            input = new { text },
            voice = new { languageCode = "tr-TR", name = targetVoice },
            audioConfig = new
            {
                audioEncoding = "MP3",
                speakingRate,
                pitch = pitchSemitones
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Goog-Api-Key", apiKey);

        using var response = await _http.SendAsync(request, ct);
        var respString = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Hata mesajını ayrıştır
            string detail = respString;
            try
            {
                var doc = JsonNode.Parse(respString);
                var errMsg = doc?["error"]?["message"]?.ToString();
                if (!string.IsNullOrEmpty(errMsg)) detail = errMsg;
            }
            catch { }

            throw new InvalidOperationException($"Google TTS Hatası ({response.StatusCode}): {detail}");
        }

        var resNode = JsonNode.Parse(respString);
        var audioBase64 = resNode?["audioContent"]?.ToString();
        if (string.IsNullOrEmpty(audioBase64))
        {
            throw new InvalidOperationException("Google TTS sunucusundan ses içeriği alınamadı.");
        }

        return Convert.FromBase64String(audioBase64);
    }

    public Task PreFetchAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        string selectedVoice = string.IsNullOrEmpty(voiceId) || voiceId == "tr-TR-Neural2-A" ? "tr-TR-Wavenet-A" : voiceId;

        lock (_lock)
        {
            if (_prefetchedText == text && _prefetchTask != null && !_prefetchTask.IsFaulted)
            {
                return Task.CompletedTask;
            }

            try { _prefetchCts?.Cancel(); _prefetchCts?.Dispose(); } catch { }
            _prefetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _prefetchedText = text;
            _prefetchTask = Task.Run(() => SynthesizeMp3Async(text, selectedVoice, rate, pitch, _prefetchCts.Token));
        }

        return Task.CompletedTask;
    }

    public async Task SpeakAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct)
    {
        string selectedVoice = string.IsNullOrEmpty(voiceId) || voiceId == "tr-TR-Neural2-A" ? "tr-TR-Wavenet-A" : voiceId;

        try
        {
            byte[]? mp3Data = null;
            Task<byte[]>? existingTask = null;

            lock (_lock)
            {
                if (_prefetchedText == text && _prefetchTask != null)
                {
                    existingTask = _prefetchTask;
                    _prefetchedText = null;
                    _prefetchTask = null;
                }
            }

            if (existingTask != null)
            {
                AppPaths.Log($"Google TTS: '{selectedVoice}' için önceden hazırlanan ses kullanılıyor (anında oynatma)...");
                mp3Data = await existingTask;
            }
            else
            {
                AppPaths.Log($"Google TTS: '{selectedVoice}' ile sentezleniyor (metin: {text.Length} karakter)...");
                mp3Data = await SynthesizeMp3Async(text, selectedVoice, rate, pitch, ct);
            }

            if (mp3Data == null || mp3Data.Length == 0)
            {
                throw new InvalidOperationException($"'{selectedVoice}' ses sunucusundan ses verisi alınamadı.");
            }

            ct.ThrowIfCancellationRequested();

            AppPaths.Log($"Google TTS: {mp3Data.Length} bayt ses alındı, çalınıyor...");

            var tcs = new TaskCompletionSource();
            using var mp3Stream = new MemoryStream(mp3Data);
            using var mp3Reader = new Mp3FileReader(mp3Stream);
            using var waveOut = new WaveOutEvent();

            lock (_lock) { _currentWaveOut = waveOut; }

            waveOut.PlaybackStopped += (_, _) =>
            {
                lock (_lock) { if (_currentWaveOut == waveOut) _currentWaveOut = null; }
                tcs.TrySetResult();
            };

            using var reg = ct.Register(() =>
            {
                try { waveOut.Stop(); } catch { }
                tcs.TrySetCanceled(ct);
            });

            waveOut.Init(mp3Reader);
            waveOut.Play();

            await tcs.Task;
            AppPaths.Log("Google TTS: Oynatma tamamlandı.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Google TTS hatası ('{selectedVoice}'): {ex.Message}. Yerel sisteme geçiliyor.");
            App.Announce("Google ses hatası: " + ex.Message);
            await _fallbackEngine.SpeakAsync(text, null, rate, pitch, ct);
        }
    }

    public async Task SynthesizeToWavAsync(string text, string? voiceId, float rate, float pitch, string path, CancellationToken ct)
    {
        string selectedVoice = string.IsNullOrEmpty(voiceId) || voiceId == "tr-TR-Neural2-A" ? "tr-TR-Wavenet-A" : voiceId;

        try
        {
            byte[] mp3Data = await SynthesizeMp3Async(text, selectedVoice, rate, pitch, ct);
            if (mp3Data.Length == 0)
            {
                await _fallbackEngine.SynthesizeToWavAsync(text, null, rate, pitch, path, ct);
                return;
            }

            using var mp3Stream = new MemoryStream(mp3Data);
            using var mp3Reader = new Mp3FileReader(mp3Stream);
            WaveFileWriter.CreateWaveFile(path, mp3Reader);
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Google TTS WAV dışa aktarma hatası: {ex.Message}. Yerel sisteme geçiliyor.");
            await _fallbackEngine.SynthesizeToWavAsync(text, null, rate, pitch, path, ct);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try { _currentWaveOut?.Stop(); _currentWaveOut?.Dispose(); _currentWaveOut = null; } catch { }
            try { _prefetchCts?.Cancel(); _prefetchCts?.Dispose(); _prefetchCts = null; } catch { }
            _prefetchedText = null;
            _prefetchTask = null;
        }
        _fallbackEngine.Dispose();
    }
}
