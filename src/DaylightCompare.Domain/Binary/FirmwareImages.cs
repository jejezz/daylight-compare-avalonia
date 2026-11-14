using System.Text;

namespace DaylightCompare.Domain.Binary;

/// <summary>
/// 펌웨어 파일(Intel HEX·Motorola S-Record)을 풀어 만든 이미지: <see cref="BaseAddress"/>부터 <see cref="Data"/>가 이어지고, 레코드가 없는 주소는 채움 값이다.
/// </summary>
public sealed record FirmwareImage(byte[] Data, long BaseAddress, int Records, IReadOnlyList<string> Warnings)
{
    public long EndAddress => BaseAddress + Data.Length;
}

/// <summary>변환 결과: 이미지, 또는 쓸 수 없는 이유(<see cref="Error"/>). 일부 레코드의 오류는 <see cref="FirmwareImage.Warnings"/>에 남기고 나머지로 이미지를 만든다.</summary>
public sealed record FirmwareResult(FirmwareImage? Image, string? Error);

/// <summary>
/// Intel HEX / S-Record → 바이너리 이미지 (ROADMAP 5단계 내장 변환기 <c>ihex-to-bin</c>·<c>srec-to-bin</c>). UI·IO 없음.
/// 줄마다 체크섬을 확인하고, 틀린 줄은 건너뛰며 경고로 알린다. 주소 사이의 빈틈은 <c>fill</c>(정책 <c>binary.fill</c>, 기본 0xFF — 지운 플래시 값)로 채운다.
/// </summary>
public static class FirmwareFormats
{
    /// <summary>이미지 하나의 크기 상한 — 주소가 아주 멀리 떨어진 파일이 메모리를 다 쓰지 않게.</summary>
    public const long MaxImageBytes = 512L * 1024 * 1024;

    /// <summary>변환기 이름(<c>builtin:</c> 뒤)으로 변환한다. 모르는 이름이면 오류.</summary>
    public static FirmwareResult Convert(string builtin, ReadOnlySpan<byte> file, byte fill)
    {
        var text = Encoding.Latin1.GetString(file);
        return builtin switch
        {
            "ihex-to-bin" => FromIntelHex(text, fill),
            "srec-to-bin" => FromSRecord(text, fill),
            _ => new FirmwareResult(null, $"모르는 변환: {builtin}"),
        };
    }

    // --- Intel HEX ---

    public static FirmwareResult FromIntelHex(string text, byte fill)
    {
        var warnings = new List<string>();
        var segments = new List<(long Address, byte[] Data)>();
        long upper = 0; // 확장 주소 (세그먼트: ×16, 선형: ×65536)
        var records = 0;
        var lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] != ':') { warnings.Add($"{lineNo}번째 줄: ':'로 시작하지 않아 건너뜀"); continue; }
            if (!TryHex(line.AsSpan(1), out var b) || b.Length < 5 || b.Length != b[0] + 5)
            {
                warnings.Add($"{lineNo}번째 줄: 레코드 길이가 맞지 않아 건너뜀");
                continue;
            }
            var sum = 0;
            foreach (var v in b) sum += v;
            if ((sum & 0xFF) != 0) { warnings.Add($"{lineNo}번째 줄: 체크섬이 틀려 건너뜀"); continue; }
            records++;
            int len = b[0], address = b[1] << 8 | b[2], type = b[3];
            switch (type)
            {
                case 0x00:
                    segments.Add((upper + address, b[4..(4 + len)]));
                    break;
                case 0x01:
                    goto done; // 끝 레코드
                case 0x02 when len == 2:
                    upper = (b[4] << 8 | b[5]) * 16L;
                    break;
                case 0x04 when len == 2:
                    upper = (long)(b[4] << 8 | b[5]) << 16;
                    break;
                case 0x03:
                case 0x05:
                    break; // 시작 주소 (실행 위치) — 이미지에는 영향이 없다
                default:
                    warnings.Add($"{lineNo}번째 줄: 모르는 레코드 종류 {type:X2} 를 건너뜀");
                    break;
            }
        }
    done:
        return Build(segments, records, warnings, fill, "Intel HEX");
    }

    // --- Motorola S-Record ---

    public static FirmwareResult FromSRecord(string text, byte fill)
    {
        var warnings = new List<string>();
        var segments = new List<(long Address, byte[] Data)>();
        var records = 0;
        var lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.Length < 4 || (line[0] != 'S' && line[0] != 's')) { warnings.Add($"{lineNo}번째 줄: 'S'로 시작하지 않아 건너뜀"); continue; }
            var type = line[1];
            if (!TryHex(line.AsSpan(2), out var b) || b.Length < 2 || b.Length != b[0] + 1)
            {
                warnings.Add($"{lineNo}번째 줄: 레코드 길이가 맞지 않아 건너뜀");
                continue;
            }
            var sum = 0;
            for (var i = 0; i < b.Length - 1; i++) sum += b[i];
            if ((~sum & 0xFF) != b[^1]) { warnings.Add($"{lineNo}번째 줄: 체크섬이 틀려 건너뜀"); continue; }
            var addressBytes = type switch { '1' or '9' => 2, '2' or '8' => 3, '3' or '7' => 4, _ => 0 };
            if (type is '1' or '2' or '3')
            {
                records++;
                if (b.Length < 2 + addressBytes) { warnings.Add($"{lineNo}번째 줄: 데이터가 없어 건너뜀"); continue; }
                long address = 0;
                for (var i = 0; i < addressBytes; i++) address = address << 8 | b[1 + i];
                segments.Add((address, b[(1 + addressBytes)..^1]));
            }
            else if (type is '0' or '5' or '6' or '7' or '8' or '9') records++; // 머리글·개수·시작 주소 — 이미지에는 영향이 없다
            else warnings.Add($"{lineNo}번째 줄: 모르는 레코드 종류 S{type} 를 건너뜀");
        }
        return Build(segments, records, warnings, fill, "S-Record");
    }

    // --- 공통 ---

    static FirmwareResult Build(List<(long Address, byte[] Data)> segments, int records, List<string> warnings, byte fill, string kind)
    {
        var data = segments.Where(s => s.Data.Length > 0).ToList();
        if (data.Count == 0) return new FirmwareResult(null, $"{kind} 데이터 레코드가 없습니다");
        var start = data.Min(s => s.Address);
        var end = data.Max(s => s.Address + s.Data.Length);
        if (end - start > MaxImageBytes) return new FirmwareResult(null, $"{kind} 주소 범위가 너무 넓습니다 ({end - start:N0}바이트)");
        var image = new byte[end - start];
        Array.Fill(image, fill);
        foreach (var (address, bytes) in data) bytes.CopyTo(image, address - start);
        return new FirmwareResult(new FirmwareImage(image, start, records, warnings), null);
    }

    static bool TryHex(ReadOnlySpan<char> hex, out byte[] bytes)
    {
        bytes = [];
        if (hex.Length == 0 || hex.Length % 2 != 0) return false;
        var result = new byte[hex.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            var hi = Nibble(hex[2 * i]);
            var lo = Nibble(hex[2 * i + 1]);
            if (hi < 0 || lo < 0) return false;
            result[i] = (byte)(hi << 4 | lo);
        }
        bytes = result;
        return true;
    }

    static int Nibble(char c) => c switch { >= '0' and <= '9' => c - '0', >= 'A' and <= 'F' => c - 'A' + 10, >= 'a' and <= 'f' => c - 'a' + 10, _ => -1 };

    /// <summary>
    /// 두 이미지를 같은 주소 범위로 맞춘다 (합친 범위, 없는 곳은 <paramref name="fill"/>) — 시작 주소가 다른 두 펌웨어를 주소끼리 비교하려고.
    /// 이미 같은 범위면 그대로 돌려준다.
    /// </summary>
    public static (FirmwareImage Left, FirmwareImage Right) Align(FirmwareImage left, FirmwareImage right, byte fill)
    {
        var start = Math.Min(left.BaseAddress, right.BaseAddress);
        var end = Math.Max(left.EndAddress, right.EndAddress);
        if (end - start > MaxImageBytes) return (left, right); // 너무 멀리 떨어져 있으면 맞추지 않는다
        if (left.BaseAddress == start && right.BaseAddress == start && left.EndAddress == end && right.EndAddress == end) return (left, right);
        FirmwareImage Pad(FirmwareImage i)
        {
            var data = new byte[end - start];
            Array.Fill(data, fill);
            i.Data.CopyTo(data, i.BaseAddress - start);
            return i with { Data = data, BaseAddress = start };
        }
        return (Pad(left), Pad(right));
    }
}
