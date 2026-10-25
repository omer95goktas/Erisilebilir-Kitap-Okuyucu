using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Security;
using System.Text;
using NAudio.Wave;

namespace ErisilebilirKitapOkuyucu.Services;

/// <summary>
/// Microsoft Edge TTS (Emel doğal sesi).
/// İnternet bağlantısı varken stüdyo kalitesinde nöral seslendirme sunar.
/// İnternet olmadığında otomatik olarak sistem seslerine fallback yapılır.
/// </summary>
public sealed class EdgeTtsEngine : ITtsEngine
{
    public string Id => "edge";
    public string DisplayName => "Microsoft Doğal Sesi (Çevrim İçi)";

    private static readonly IReadOnlyList<VoiceDescriptor> _voices = new List<VoiceDescriptor>
    {
        new("tr-TR-EmelNeural", "Microsoft Emel Doğal (Kadın - Çevrim İçi)", "tr-TR", "edge")
    };

    private const long WinEpoch = 11644473600;
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string ChromiumVersion = "143.0.3650.75";
    private static readonly string SecMsGecVersion = $"2-{ChromiumVersion}";

    // Fallback için yerel OneCore motoru
    private readonly OneCoreEngine _fallbackEngine = new();

    private WaveOutEvent? _currentWaveOut;
    private readonly object _lock = new();

    private ClientWebSocket? _cachedWs;
    private readonly SemaphoreSlim _wsLock = new(1, 1);

    private string? _prefetchedText;
    private Task<byte[]>? _prefetchTask;
    private CancellationTokenSource? _prefetchCts;

    public EdgeTtsEngine()
    {
        PreWarm();
    }

    public void PreWarm()
    {
        Task.Run(async () =>
        {
            try
            {
                await _wsLock.WaitAsync();
                try
                {
                    await GetConnectedWsAsync(CancellationToken.None);
                    AppPaths.Log("Edge TTS: Ön bağlantı (pre-warm) başarıyla kuruldu.");
                }
                finally
                {
                    _wsLock.Release();
                }
            }
            catch (Exception ex)
            {
                AppPaths.Log($"Edge TTS: Ön bağlantı hatası: {ex.Message}");
            }
        });
    }

    public Task PreFetchAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;

        string selectedVoice = "tr-TR-EmelNeural";

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

    public IReadOnlyList<VoiceDescriptor> GetVoices() => _voices;

    private static string GetDateString() =>
        DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'", CultureInfo.InvariantCulture);

    private static Dictionary<string, string> ParseHeaders(string headersStr)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in headersStr.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2) headers[parts[0].Trim()] = parts[1].Trim();
        }
        return headers;
    }

    #region Token Generation (Sec-MS-GEC v2)
    private static readonly uint[] _SHA256_INITIAL = {
        0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
        0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19
    };
    private static readonly uint[] _SHA256_STATE = {
        0xFD9FBEA2, 0x91243CE7, 0xBE499094, 0xCD2E1E0A,
        0x6C006F49, 0x04BD35AB, 0xFEB0A7E4, 0x03EFFBD2
    };
    private static readonly uint[] _SHA256_SCHEDULE = {
        0xEB24BF9E, 0x1558691F, 0xA0C09EE2, 0x0CFA32BA,
        0xA622EE8C, 0xD1DD9491, 0xC0DD25A0, 0xF9E70AB6,
        0xF0C31573, 0x7058CE4E, 0x7188E606, 0x00E5AB33,
        0xD258B060, 0x72766564
    };
    private static readonly uint[] _SHA256_ROUND_CONSTANTS = {
        0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5, 0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
        0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3, 0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
        0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC, 0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
        0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7, 0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
        0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13, 0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
        0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3, 0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
        0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5, 0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
        0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208, 0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2
    };
    private const uint _SHA256_ADJUSTMENT = 0xC9DC06A7;
    private static readonly uint[] _SHA256_BLOCK_CONSTANTS = InitBlockConstants();
    private static uint[] InitBlockConstants()
    {
        var arr = (uint[])_SHA256_ROUND_CONSTANTS.Clone();
        arr[14] = _SHA256_ADJUSTMENT;
        return arr;
    }
    private const uint _SHA256_SIGMA0_ADJUSTMENT = 0x059C0B80;
    private const uint _SHA256_SIGMA1_ADJUSTMENT = 0x000B92C0;

    private static readonly (uint coeff, uint offset)[] _CS64_COEFFICIENTS = {
        (0xE82E91B1, 0xB32700D0), (0x44DE24C6, 0x88DFF51D), (0x3BEB65BC, 0x29514038), (0xC89C5933, 0xB328CBDF),
        (0x8D1EC211, 0xD405B4D5), (0x314EF5D5, 0x26E55F3C), (0xE92491B1, 0x607801C6), (0xF0A5B9D5, 0x82B2C1FB),
        (0x06064CF3, 0xFA51A3DA), (0xDA79FA60, 0x207CC464), (0xBED20560, 0x3C3C74F3), (0x89F9C9F8, 0xD2D14B61),
        (0x35FA38C6, 0xBB04A2C1), (0xCB49E3C6, 0xC5626167)
    };
    private const uint _CS64_FACTOR = 0x82893933;
    private const uint _CS64_ADJUSTMENT0 = 0xF2F90B1A;
    private const uint _CS64_ADJUSTMENT1 = 0x0B1A0000;

    private static uint Rol16(uint val) => (val << 16) | (val >> 16);
    private static uint Sigma0(uint val) => (val >> 7 | val << 25) ^ (val >> 18 | val << 14) ^ (val >> 3);
    private static uint Sigma1(uint val) => (val >> 17 | val << 15) ^ (val >> 19 | val << 13) ^ (val >> 10);

    private static List<uint> WordsLe4Even(byte[] data)
    {
        int pad = (4 - (data.Length % 4)) % 4;
        var padded = new byte[data.Length + pad];
        Buffer.BlockCopy(data, 0, padded, 0, data.Length);
        var list = new List<uint>(padded.Length / 4 + 1);
        for (int i = 0; i < padded.Length; i += 4) list.Add(BitConverter.ToUInt32(padded, i));
        if (list.Count % 2 != 0) list.Add(0);
        return list;
    }

    private static void Sha256Rounds(ref uint a, ref uint b, ref uint c, ref uint d,
                                     ref uint e, ref uint f, ref uint g, ref uint h,
                                     List<uint> words, int firstRound = 0)
    {
        var constants = firstRound == 14 ? _SHA256_BLOCK_CONSTANTS : _SHA256_ROUND_CONSTANTS;
        int aligned = Math.Min(64, ((firstRound + 7) / 8) * 8);

        for (int i = firstRound; i < aligned; i++)
        {
            ulong rot_e = (ulong)e * 0x100000001UL;
            ulong rot_a = (ulong)a * 0x100000001UL;
            uint s1 = (uint)((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25));
            uint ch = g ^ (e & (f ^ g));
            uint t1 = h + s1 + ch + constants[i] + words[i];
            uint s0 = (uint)((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22));
            uint maj = (a & b) | (c & (a | b));
            uint t2 = s0 + maj;
            h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
        }

        for (int index = aligned; index < 64; index += 8)
        {
            ulong rot_e = (ulong)e * 0x100000001UL;
            ulong rot_a = (ulong)a * 0x100000001UL;
            uint t1 = h + (uint)((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25)) + (g ^ (e & (f ^ g))) + constants[index] + words[index];
            d += t1;
            h = t1 + (uint)((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22)) + ((a & b) | (c & (a | b)));

            rot_e = (ulong)d * 0x100000001UL;
            rot_a = (ulong)h * 0x100000001UL;
            t1 = g + (uint)((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25)) + (f ^ (d & (e ^ f))) + constants[index + 1] + words[index + 1];
            c += t1;
            g = t1 + (uint)((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22)) + ((h & a) | (b & (h | a)));

            rot_e = (ulong)c * 0x100000001UL;
            rot_a = (ulong)g * 0x100000001UL;
            t1 = f + (uint)((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25)) + (e ^ (c & (d ^ e))) + constants[index + 2] + words[index + 2];
            b += t1;
            f = t1 + (uint)((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22)) + ((g & h) | (a & (g | h)));

            rot_e = (ulong)b * 0x100000001UL;
            rot_a = (ulong)f * 0x100000001UL;
            t1 = e + (uint)((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25)) + (d ^ (b & (c ^ d))) + constants[index + 3] + words[index + 3];
            a += t1;
            e = t1 + (uint)((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22)) + ((f & g) | (h & (f | g)));

            rot_e = (ulong)a * 0x100000001UL;
            rot_a = (ulong)e * 0x100000001UL;
            t1 = d + (uint)((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25)) + (c ^ (a & (b ^ c))) + constants[index + 4] + words[index + 4];
            h += t1;
            d = t1 + (uint)((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22)) + ((e & f) | (g & (e | f)));

            rot_e = (ulong)h * 0x100000001UL;
            rot_a = (ulong)d * 0x100000001UL;
            t1 = c + (uint)((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25)) + (b ^ (h & (a ^ b))) + constants[index + 5] + words[index + 5];
            g += t1;
            c = t1 + (uint)((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22)) + ((d & e) | (f & (d | e)));

            rot_e = (ulong)g * 0x100000001UL;
            rot_a = (ulong)c * 0x100000001UL;
            t1 = b + (uint)((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25)) + (a ^ (g & (h ^ a))) + constants[index + 6] + words[index + 6];
            f += t1;
            b = t1 + (uint)((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22)) + ((c & d) | (e & (c | d)));

            rot_e = (ulong)f * 0x100000001UL;
            rot_a = (ulong)b * 0x100000001UL;
            t1 = a + (uint)((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25)) + (h ^ (f & (g ^ h))) + constants[index + 7] + words[index + 7];
            e += t1;
            a = t1 + (uint)((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22)) + ((b & c) | (d & (b | c)));
        }
    }

    private static void ExpandSha256Schedule(List<uint> words, int firstWord = 16)
    {
        for (int i = firstWord; i < 64; i++)
        {
            uint s1 = Sigma1(words[i - 2]);
            uint s0 = Sigma0(words[i - 15]);
            words.Add(s1 + words[i - 7] + s0 + words[i - 16]);
        }
    }

    private static uint[] Sha256Init(byte[] data)
    {
        var words = new List<uint>(64);
        for (int i = 0; i < 14; i++) words.Add(0);
        words.Add((uint)((data[0] << 16) | (data[1] << 8) | data[2]));
        words.Add((uint)((data[3] << 24) | (data[4] << 16) | (data[5] << 8) | data[6]));

        for (int i = 16; i < 30; i++)
        {
            uint val = _SHA256_SCHEDULE[i - 16];
            if (i - 2 >= 14) val += (i == 16) ? (Sigma1(words[i - 2]) ^ _SHA256_SIGMA1_ADJUSTMENT) : Sigma1(words[i - 2]);
            if (i - 7 >= 14) val += words[i - 7];
            if (i - 15 >= 14) val += (i == 29) ? (Sigma0(words[i - 15]) ^ _SHA256_SIGMA0_ADJUSTMENT) : Sigma0(words[i - 15]);
            words.Add(val);
        }

        words.Add(Sigma1(words[28]) + words[23] + Sigma0(words[15]) + words[14] + _SHA256_ADJUSTMENT - _SHA256_ROUND_CONSTANTS[14]);
        ExpandSha256Schedule(words, 31);

        uint a = _SHA256_STATE[0], b = _SHA256_STATE[1], c = _SHA256_STATE[2], d = _SHA256_STATE[3];
        uint e = _SHA256_STATE[4], f = _SHA256_STATE[5], g = _SHA256_STATE[6], h = _SHA256_STATE[7];
        Sha256Rounds(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h, words, 14);

        return new uint[] {
            _SHA256_INITIAL[0] + a, _SHA256_INITIAL[1] + b, _SHA256_INITIAL[2] + c, _SHA256_INITIAL[3] + d,
            _SHA256_INITIAL[4] + e, _SHA256_INITIAL[5] + f, _SHA256_INITIAL[6] + g, _SHA256_INITIAL[7] + h
        };
    }

    private static byte[] Sha256Digest(byte[] data)
    {
        int length = 57 + data.Length;
        int padLen = (55 - length) % 64;
        if (padLen < 0) padLen += 64;

        using var ms = new MemoryStream();
        ms.Write(data, 7, data.Length - 7);
        ms.WriteByte(0x80);
        ms.Write(new byte[padLen], 0, padLen);
        ulong bitLen = (ulong)length * 8;
        byte[] bitLenBytes = BitConverter.GetBytes(bitLen);
        if (BitConverter.IsLittleEndian) Array.Reverse(bitLenBytes);
        ms.Write(bitLenBytes, 0, 8);

        byte[] padded = ms.ToArray();
        uint[] state = Sha256Init(data);

        for (int offset = 0; offset < padded.Length; offset += 64)
        {
            var words = new List<uint>(64);
            for (int i = 0; i < 16; i++)
            {
                byte[] w = new byte[4];
                Buffer.BlockCopy(padded, offset + i * 4, w, 0, 4);
                if (BitConverter.IsLittleEndian) Array.Reverse(w);
                words.Add(BitConverter.ToUInt32(w, 0));
            }
            ExpandSha256Schedule(words);

            uint a = state[0], b = state[1], c = state[2], d = state[3];
            uint e = state[4], f = state[5], g = state[6], h = state[7];
            Sha256Rounds(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h, words);

            state[0] += a; state[1] += b; state[2] += c; state[3] += d;
            state[4] += e; state[5] += f; state[6] += g; state[7] += h;
        }

        var res = new byte[32];
        for (int i = 0; i < 8; i++)
        {
            byte[] bytes = BitConverter.GetBytes(state[i]);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, res, i * 4, 4);
        }
        return res;
    }

    private static (uint, uint) Cs64Digest(byte[] data, byte[] digest)
    {
        uint key_a = BitConverter.ToUInt32(digest, 0) | 1;
        uint key_b = BitConverter.ToUInt32(digest, 4) | 1;

        uint[] factors = { key_a * _CS64_FACTOR, key_b * _CS64_FACTOR };
        var terms = new List<uint>();
        var offsets = new List<uint>();

        for (int i = 0; i < _CS64_COEFFICIENTS.Length; i++)
        {
            terms.Add(_CS64_COEFFICIENTS[i].coeff * factors[i % 2]);
            offsets.Add(_CS64_COEFFICIENTS[i].offset);
        }

        byte[] withZero = new byte[data.Length + 1];
        Buffer.BlockCopy(data, 0, withZero, 1, data.Length);
        var words = WordsLe4Even(withZero);

        for (int i = 0; i < words.Count; i++)
        {
            uint word = words[i];
            uint term = word * (i % 2 == 0 ? key_a : key_b);
            uint offset = Rol16(word) * (i % 2 == 0 ? 0xEF0569FBu : 0xC31713DBu);
            if (i == 0)
            {
                term += _CS64_ADJUSTMENT0 * factors[0];
                offset += _CS64_ADJUSTMENT1;
            }
            terms.Add(term);
            offsets.Add(offset);
        }

        uint swap_acc = 0, swap_last = 0, state = 0, acc = 0;
        for (int i = 0; i < terms.Count; i += 2)
        {
            uint x = terms[i], y = terms[i + 1];
            uint t = Rol16(offsets[i] + x);
            uint q = t * 0x76F50F34;
            swap_acc += Rol16(q) * 0xC3EFEA97 + q;

            t = Rol16(offsets[i + 1] + y);
            q = t * 0x1881CE3C;
            swap_last = Rol16(q) * 0x35BD1EC9 + q;
            swap_acc += swap_last;

            state = state * key_a + x;
            state = Rol16(state) * 0xCF98B111;
            state = Rol16(state) * 0x87085B9F;
            state = Rol16(state) * 0x12CEB96D;
            state = Rol16(state) * 0x257E1D83;
            acc += state;

            t = state * key_b + y;
            t = Rol16(t) * 0xA27416F5;
            t = Rol16(t) * 0xD38396FF;
            state = Rol16(Rol16(t) * 0x7C932B89) * 0xBFA49F69;
            acc += state;
        }

        return (swap_last ^ state, swap_acc ^ acc);
    }

    private static string GenerateSecMsGec()
    {
        double ticks = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + WinEpoch;
        ticks -= ticks % 300;
        ticks *= 1e9 / 100;
        string strToHash = $"{ticks:F0}{TrustedClientToken}";
        byte[] data = Encoding.ASCII.GetBytes(strToHash);

        byte[] shaDigest = Sha256Digest(data);
        var (c1, c2) = Cs64Digest(data, shaDigest);

        byte[] cs64Bytes = new byte[8];
        Buffer.BlockCopy(BitConverter.GetBytes(c1), 0, cs64Bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(c2), 0, cs64Bytes, 4, 4);

        string cs64Hex = BitConverter.ToString(cs64Bytes).Replace("-", "").ToUpperInvariant();
        string shaHex = BitConverter.ToString(shaDigest).Replace("-", "").ToUpperInvariant();

        return $"{cs64Hex}-{shaHex}";
    }
    #endregion

    private async Task<ClientWebSocket> GetConnectedWsAsync(CancellationToken ct)
    {
        if (_cachedWs != null && _cachedWs.State == WebSocketState.Open) return _cachedWs;

        try { _cachedWs?.Dispose(); } catch { }
        _cachedWs = new ClientWebSocket();
        _cachedWs.Options.DangerousDeflateOptions = new WebSocketDeflateOptions
        {
            ClientMaxWindowBits = 15,
            ServerMaxWindowBits = 15
        };
        _cachedWs.Options.SetRequestHeader("Pragma", "no-cache");
        _cachedWs.Options.SetRequestHeader("Cache-Control", "no-cache");
        _cachedWs.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        _cachedWs.Options.SetRequestHeader("User-Agent", $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{ChromiumVersion.Split('.')[0]}.0.0.0 Safari/537.36 Edg/{ChromiumVersion.Split('.')[0]}.0.0.0");
        _cachedWs.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");

        string token = GenerateSecMsGec();
        string connectId = Guid.NewGuid().ToString("N");
        string wssUrl = $"wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken={TrustedClientToken}&ConnectionId={connectId}&Sec-MS-GEC={token}&Sec-MS-GEC-Version={SecMsGecVersion}";

        using var connCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connCts.CancelAfter(TimeSpan.FromSeconds(12));
        await _cachedWs.ConnectAsync(new Uri(wssUrl), connCts.Token);

        var speechConfig = "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";
        var configMsg = $"X-Timestamp:{GetDateString()}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n{speechConfig}\r\n";
        await _cachedWs.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(configMsg)), WebSocketMessageType.Text, true, ct);

        return _cachedWs;
    }

    private async Task<byte[]> SynthesizeMp3Async(string text, string voice, float rate, float pitch, CancellationToken ct)
    {
        await _wsLock.WaitAsync(ct);
        try
        {
            var ws = await GetConnectedWsAsync(ct);

            // Prosody mapping
            int ratePct = (int)Math.Round((rate - 1.0f) * 100f);
            string rateStr = ratePct >= 0 ? $"+{ratePct}%" : $"{ratePct}%";
            int pitchHz = (int)Math.Round((pitch - 1.0f) * 50f);
            string pitchStr = pitchHz >= 0 ? $"+{pitchHz}Hz" : $"{pitchHz}Hz";

            string reqId = Guid.NewGuid().ToString("N");
            string ssml = $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'><voice name='{voice}'><prosody pitch='{pitchStr}' rate='{rateStr}' volume='+0%'>{SecurityElement.Escape(text)}</prosody></voice></speak>";
            string ssmlMsg = $"X-RequestId:{reqId}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:{GetDateString()}Z\r\nPath:ssml\r\n\r\n{ssml}";
            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(ssmlMsg)), WebSocketMessageType.Text, true, ct);

            using var audioMs = new MemoryStream();
            var buffer = new byte[8192];
            bool turnEnd = false;

            while (ws.State == WebSocketState.Open && !turnEnd && !ct.IsCancellationRequested)
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(TimeSpan.FromSeconds(12));

                using var msgMs = new MemoryStream();
                WebSocketReceiveResult res;
                do
                {
                    res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), readCts.Token);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    msgMs.Write(buffer, 0, res.Count);
                } while (!res.EndOfMessage);

                if (res.MessageType == WebSocketMessageType.Close) break;

                byte[] msgBytes = msgMs.ToArray();
                if (res.MessageType == WebSocketMessageType.Binary && msgBytes.Length > 2)
                {
                    int headerLen = (msgBytes[0] << 8) | msgBytes[1];
                    if (headerLen <= msgBytes.Length - 2)
                    {
                        var headersStr = Encoding.UTF8.GetString(msgBytes, 2, headerLen);
                        var headers = ParseHeaders(headersStr);
                        if (headers.TryGetValue("Path", out var path) && path == "audio")
                        {
                            int audioOffset = 2 + headerLen;
                            int audioLen = msgBytes.Length - audioOffset;
                            if (audioLen > 0)
                            {
                                audioMs.Write(msgBytes, audioOffset, audioLen);
                            }
                        }
                    }
                }
                else if (res.MessageType == WebSocketMessageType.Text)
                {
                    string textStr = Encoding.UTF8.GetString(msgBytes);
                    if (textStr.Contains("Path:turn.end"))
                    {
                        turnEnd = true;
                    }
                }
            }

            return audioMs.ToArray();
        }
        catch
        {
            try { _cachedWs?.Dispose(); } catch { }
            _cachedWs = null;
            throw;
        }
        finally
        {
            _wsLock.Release();
        }
    }

    public async Task SpeakAsync(string text, string? voiceId, float rate, float pitch, CancellationToken ct)
    {
        string selectedVoice = "tr-TR-EmelNeural";

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
                AppPaths.Log($"Edge TTS: '{selectedVoice}' için önceden hazırlanan ses kullanılıyor (anında oynatma)...");
                mp3Data = await existingTask;
            }
            else
            {
                AppPaths.Log($"Edge TTS: '{selectedVoice}' ile sentezleniyor (metin: {text.Length} karakter)...");
                mp3Data = await SynthesizeMp3Async(text, selectedVoice, rate, pitch, ct);
            }

            if (mp3Data == null || mp3Data.Length == 0)
            {
                throw new InvalidOperationException($"'{selectedVoice}' ses sunucusundan ses verisi alınamadı.");
            }

            ct.ThrowIfCancellationRequested();

            AppPaths.Log($"Edge TTS: {mp3Data.Length} bayt ses alındı, çalınıyor...");

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
                tcs.TrySetCanceled();
            });

            waveOut.Init(mp3Reader);
            waveOut.Play();

            await tcs.Task;
            AppPaths.Log("Edge TTS: Oynatma tamamlandı.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Edge TTS hatası ('{selectedVoice}'): {ex.Message}. Yerel OneCore sesine geçiliyor.");
            await _fallbackEngine.SpeakAsync(text, null, rate, pitch, ct);
        }
    }

    public async Task SynthesizeToWavAsync(string text, string? voiceId, float rate, float pitch, string path, CancellationToken ct)
    {
        string selectedVoice = "tr-TR-EmelNeural";

        byte[] mp3Data = await SynthesizeMp3Async(text, selectedVoice, rate, pitch, ct);
        if (mp3Data.Length == 0)
        {
            await _fallbackEngine.SynthesizeToWavAsync(text, null, rate, pitch, path, ct);
            return;
        }

        // MP3'ü WAV formatına dönüştürüp kaydet
        using var mp3Stream = new MemoryStream(mp3Data);
        using var mp3Reader = new Mp3FileReader(mp3Stream);
        WaveFileWriter.CreateWaveFile(path, mp3Reader);
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
        try { _cachedWs?.Dispose(); } catch { }
        _cachedWs = null;
        _wsLock.Dispose();
        _fallbackEngine.Dispose();
    }
}
