using System.Text;
using DbcView.Domain;

namespace DbcView.Web.Services;

public sealed record DbcTextDecodeResult(string Content, DbcFileEncoding Encoding);

/// <summary>
/// Explicit upload decoding order: BOM, strict UTF-8, then Windows-1252 with
/// its undefined bytes counted. No silent replacement happens in UTF-8 mode.
/// </summary>
public static class DbcEncodingDetector
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding Utf16LittleEndian = new(false, false);
    private static readonly UnicodeEncoding Utf16BigEndian = new(true, false);

    private static readonly char[] Windows1252High =
    {
        '\u20AC', '\uFFFD', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\uFFFD', '\u017D', '\uFFFD',
        '\uFFFD', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\uFFFD', '\u017E', '\u0178',
    };

    public static DbcTextDecodeResult Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            try
            {
                return new DbcTextDecodeResult(
                    StrictUtf8.GetString(bytes[3..]),
                    new DbcFileEncoding("UTF-8 (BOM)", false, 0));
            }
            catch (DecoderFallbackException)
            {
                return DecodeWindows1252(bytes);
            }
        }

        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return new DbcTextDecodeResult(
                Utf16LittleEndian.GetString(bytes[2..]),
                new DbcFileEncoding("UTF-16 LE (BOM)", false, 0));
        }

        if (bytes.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return new DbcTextDecodeResult(
                Utf16BigEndian.GetString(bytes[2..]),
                new DbcFileEncoding("UTF-16 BE (BOM)", false, 0));
        }

        try
        {
            return new DbcTextDecodeResult(
                StrictUtf8.GetString(bytes),
                new DbcFileEncoding("UTF-8", false, 0));
        }
        catch (DecoderFallbackException)
        {
            return DecodeWindows1252(bytes);
        }
    }

    private static DbcTextDecodeResult DecodeWindows1252(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        var undefined = 0;
        foreach (var value in bytes)
        {
            if (value is < 0x80 or >= 0xA0)
            {
                builder.Append((char)value);
                continue;
            }

            var character = Windows1252High[value - 0x80];
            if (character == '\uFFFD')
            {
                undefined++;
            }

            builder.Append(character);
        }

        return new DbcTextDecodeResult(
            builder.ToString(),
            new DbcFileEncoding("Windows-1252", true, undefined));
    }
}
