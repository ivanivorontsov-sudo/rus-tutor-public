namespace RusTutor.Core;

/// <summary>Простой энергетический детектор речи: режет поток PCM16 моно на фразы.</summary>
public sealed class VadSegmenter
{
    readonly int _rate;
    public double Threshold { get; set; } = 500;     // RMS
    public int SilenceMs { get; set; } = 700;
    public int MaxSegmentMs { get; set; } = 12000;
    public int PreRollMs { get; set; } = 300;

    readonly List<short> _seg = new();
    readonly Queue<short> _pre = new();
    bool _inSpeech;
    int _silentSamples;

    public event Action<short[]>? Segment;
    public bool InSpeech => _inSpeech;
    public double LastLevel { get; private set; }

    public VadSegmenter(int rate = 16000) { _rate = rate; }

    public void Feed(ReadOnlySpan<short> samples)
    {
        // окна по 20 мс
        int win = _rate / 50;
        for (int off = 0; off < samples.Length; off += win)
        {
            var w = samples.Slice(off, Math.Min(win, samples.Length - off));
            double sum = 0; foreach (var s in w) sum += (double)s * s;
            double rms = Math.Sqrt(sum / Math.Max(1, w.Length));
            LastLevel = rms;
            bool loud = rms > Threshold;
            if (!_inSpeech)
            {
                foreach (var s in w) { _pre.Enqueue(s); }
                while (_pre.Count > _rate * PreRollMs / 1000) _pre.Dequeue();
                if (loud) { _inSpeech = true; _silentSamples = 0; _seg.Clear(); _seg.AddRange(_pre); _pre.Clear(); }
                continue;
            }
            foreach (var s in w) _seg.Add(s);
            _silentSamples = loud ? 0 : _silentSamples + w.Length;
            if (_silentSamples >= _rate * SilenceMs / 1000 || _seg.Count >= _rate * MaxSegmentMs / 1000) Emit();
        }
    }

    public void Flush() { if (_inSpeech) Emit(); _pre.Clear(); }

    void Emit()
    {
        _inSpeech = false; _silentSamples = 0;
        var arr = _seg.ToArray(); _seg.Clear();
        if (arr.Length > _rate / 5) Segment?.Invoke(arr); // короче 200 мс — шум
    }
}

/// <summary>Минимальный FLAC‑кодер (verbatim‑подкадры) — для бесплатного endpoint Google.</summary>
public static class FlacEncoder
{
    public static byte[] Encode(short[] pcm, int rate = 16000)
    {
        var ms = new MemoryStream();
        ms.Write("fLaC"u8);
        const int block = 4096;
        // STREAMINFO (last metadata block)
        var bw = new BitWriter();
        bw.Write(1, 1); bw.Write(0, 7); bw.Write(34, 24);
        bw.Write(block, 16); bw.Write(block, 16);
        bw.Write(0, 24); bw.Write(0, 24);
        bw.Write((uint)rate, 20); bw.Write(0, 3); bw.Write(15, 5);
        bw.Write((ulong)pcm.Length >> 32, 4); bw.Write((uint)pcm.Length, 32);
        for (int i = 0; i < 16; i++) bw.Write(0, 8); // MD5 = 0 (не задан)
        ms.Write(bw.ToArray());
        int frame = 0;
        for (int off = 0; off < pcm.Length; off += block, frame++)
        {
            int n = Math.Min(block, pcm.Length - off);
            var f = new BitWriter();
            f.Write(0x3FFE, 14); f.Write(0, 1); f.Write(0, 1);
            f.Write(7, 4);                 // размер блока: 16 бит (n-1) в конце заголовка
            int rc = rate switch { 8000 => 4, 16000 => 5, 22050 => 6, 24000 => 7, 32000 => 8, 44100 => 9, 48000 => 10, _ => 0 };
            f.Write((uint)rc, 4);
            f.Write(0, 4);                 // mono
            f.Write(4, 3);                 // 16 bit
            f.Write(0, 1);
            WriteUtf8Num(f, (uint)frame);
            f.Write((uint)(n - 1), 16);
            f.Write(Crc8(f.ToArray()), 8);
            f.Write(0, 1); f.Write(1, 6); f.Write(0, 1); // VERBATIM
            for (int i = 0; i < n; i++) f.Write((ushort)pcm[off + i], 16);
            f.Align();
            var bytes = f.ToArray();
            ms.Write(bytes);
            ushort crc = Crc16(bytes);
            ms.WriteByte((byte)(crc >> 8)); ms.WriteByte((byte)crc);
        }
        return ms.ToArray();
    }

    static void WriteUtf8Num(BitWriter f, uint v)
    {
        if (v < 0x80) { f.Write(v, 8); return; }
        if (v < 0x800) { f.Write(0xC0 | (v >> 6), 8); f.Write(0x80 | (v & 0x3F), 8); return; }
        f.Write(0xE0 | (v >> 12), 8); f.Write(0x80 | ((v >> 6) & 0x3F), 8); f.Write(0x80 | (v & 0x3F), 8);
    }

    static byte Crc8(byte[] d)
    {
        byte crc = 0;
        foreach (var b in d) { crc ^= b; for (int i = 0; i < 8; i++) crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1); }
        return crc;
    }
    static ushort Crc16(byte[] d)
    {
        ushort crc = 0;
        foreach (var b in d) { crc ^= (ushort)(b << 8); for (int i = 0; i < 8; i++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x8005 : crc << 1); }
        return crc;
    }

    sealed class BitWriter
    {
        readonly List<byte> _b = new(); ulong _acc; int _n;
        public void Write(ulong v, int bits)
        {
            for (int i = bits - 1; i >= 0; i--)
            {
                _acc = (_acc << 1) | ((v >> i) & 1); _n++;
                if (_n == 8) { _b.Add((byte)_acc); _acc = 0; _n = 0; }
            }
        }
        public void Write(uint v, int bits) => Write((ulong)v, bits);
        public void Write(int v, int bits) => Write((ulong)(uint)v, bits);
        public void Align() { while (_n != 0) Write(0, 1); }
        public byte[] ToArray() { var c = new List<byte>(_b); if (_n > 0) c.Add((byte)(_acc << (8 - _n))); return c.ToArray(); }
    }
}

/// <summary>Разбор ответа бесплатного endpoint google speech-api/v2 (несколько JSON построчно).</summary>
public static class GoogleFreeParser
{
    public static string? Parse(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("result", out var res) || res.GetArrayLength() == 0) continue;
                var alt = res[0].GetProperty("alternative");
                if (alt.GetArrayLength() == 0) continue;
                return alt[0].GetProperty("transcript").GetString();
            }
            catch (System.Text.Json.JsonException) { }
        }
        return null;
    }
}

