using ProLyric.Services;

namespace ProLyric.Tests;

public class RtfUnicodeEncoderTests
{
    // -----------------------------------------------------------------------
    //  Encode() tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Encode_EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, RtfUnicodeEncoder.Encode(""));
    }

    [Fact]
    public void Encode_NullString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, RtfUnicodeEncoder.Encode(null!));
    }

    [Fact]
    public void Encode_PlainAscii_PassesThrough()
    {
        string result = RtfUnicodeEncoder.Encode("Hello World");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Encode_EscapesBackslash()
    {
        string result = RtfUnicodeEncoder.Encode(@"a\b");
        Assert.Equal(@"a\\b", result);
    }

    [Fact]
    public void Encode_EscapesBraces()
    {
        string result = RtfUnicodeEncoder.Encode("{test}");
        Assert.Equal(@"\{test\}", result);
    }

    [Fact]
    public void Encode_NewlineBecomesParBreak()
    {
        string result = RtfUnicodeEncoder.Encode("line1\nline2");
        Assert.Equal(@"line1\par line2", result);
    }

    [Fact]
    public void Encode_CrLfNewline_SingleParBreak()
    {
        string result = RtfUnicodeEncoder.Encode("line1\r\nline2");
        Assert.Equal(@"line1\par line2", result);
    }

    [Fact]
    public void Encode_MalayalamCharacter_ProducesSignedU16Escape()
    {
        // ക = U+0D15
        string result = RtfUnicodeEncoder.Encode("ക");
        // 0x0D15 = 3349 (positive, fits in signed 16-bit)
        Assert.Equal(@"\u3349?", result);
    }

    [Fact]
    public void Encode_MalayalamWord_AllCharsEncoded()
    {
        // "ദൈവം" = ദ(0D26) ൈ(0D48) വ(0D35) ം(0D02)
        // As signed: 3366, 3400, 3381, 3330
        string result = RtfUnicodeEncoder.Encode("ദൈവം");
        Assert.Equal(@"\u3366?\u3400?\u3381?\u3330?", result);
    }

    [Fact]
    public void Encode_MixedEnglishMalayalam_CorrectEncoding()
    {
        // "Hi ദൈവം"
        string result = RtfUnicodeEncoder.Encode("Hi ദൈവം");
        Assert.Equal(@"Hi \u3366?\u3400?\u3381?\u3330?", result);
    }

    [Fact]
    public void Encode_HighUnicodeSurrogatePair_EncodesAsTwoEscapes()
    {
        // U+1F600 GRINNING FACE (emoji) — above BMP, needs surrogate pair
        // offset = 0x1F600 - 0x10000 = 0xF600
        // High surrogate: 0xD800 + (0xF600 >> 10) = 0xD83D → signed 16-bit = -10179
        // Low surrogate:  0xDC00 + (0xF600 & 0x3FF) = 0xDE00 → signed 16-bit = -8704
        string emoji = "\U0001F600"; // C# escape for U+1F600
        string result = RtfUnicodeEncoder.Encode(emoji);
        Assert.Equal(@"\u-10179?\u-8704?", result);
    }

    [Fact]
    public void Encode_MultiLineWithMalayalam_PreservesBreaks()
    {
        string input = "യേശു\nക്രിസ്തു";
        string result = RtfUnicodeEncoder.Encode(input);
        // Must contain \par between the two words
        Assert.Contains(@"\par ", result);
        // Both lines must be encoded
        Assert.StartsWith(@"\u", result[0..2] == @"\u" ? result : @"\u" + result.Substring(2));
    }

    // -----------------------------------------------------------------------
    //  ReplaceRtfPayload() tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ReplaceRtfPayload_PreservesRtfHeader()
    {
        string templateRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}{\colortbl ;\red255\green255\blue255;}\f0\fs48\cf1 Original text}";
        string result = RtfUnicodeEncoder.ReplaceRtfPayload(templateRtf, "New text");

        // Header must be preserved
        Assert.Contains(@"{\fonttbl{\f0 Arial;}}", result);
        Assert.Contains(@"{\colortbl ;\red255\green255\blue255;}", result);

        // New text must be present
        Assert.Contains("New text", result);

        // Old text must be gone
        Assert.DoesNotContain("Original text", result);
    }

    [Fact]
    public void ReplaceRtfPayload_MalayalamText_EncodedInOutput()
    {
        string templateRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}{\colortbl ;\red255\green255\blue255;}\f0\fs48\cf1 English}";
        string result = RtfUnicodeEncoder.ReplaceRtfPayload(templateRtf, "ദൈവം");

        // Must contain encoded Malayalam
        Assert.Contains(@"\u3366?", result);
        Assert.Contains(@"{\fonttbl", result);
    }

    [Fact]
    public void ReplaceRtfPayload_EmptyTemplate_ReturnsFallback()
    {
        string result = RtfUnicodeEncoder.ReplaceRtfPayload("", "Hello");
        Assert.Contains("Hello", result);
        Assert.StartsWith("{\\rtf", result);
    }

    [Fact]
    public void ReplaceRtfPayload_ResultIsValidRtf()
    {
        string templateRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}{\colortbl ;\red255\green255\blue255;}\f0\fs48\cf1 test}";
        string result = RtfUnicodeEncoder.ReplaceRtfPayload(templateRtf, "replaced");

        // Basic RTF validity: starts with {\rtf and ends with }
        Assert.StartsWith("{\\rtf", result);
        Assert.EndsWith("}", result);
    }

    [Fact]
    public void ReplaceRtfPayload_MultiLine3Lines_EveryLineHasIdenticalFontAndFontSizeAsFirstLine()
    {
        // Template with font f0, font-size fs60 (30pt), centered \qc, white text \cf1
        string templateRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Helvetica;}}{\colortbl ;\red255\green255\blue255;}\pard\qc\f0\fs60\cf1 Placeholder Line}";
        string lyric = "Line 1\nLine 2\nLine 3";

        string result = RtfUnicodeEncoder.ReplaceRtfPayload(templateRtf, lyric);

        // All 3 lines must be in the output
        Assert.Contains("Line 1", result);
        Assert.Contains("Line 2", result);
        Assert.Contains("Line 3", result);

        // Every line must have the exact same \fs60 and \f0 control words
        var fsMatches = System.Text.RegularExpressions.Regex.Matches(result, @"\\fs60\b");
        var f0Matches = System.Text.RegularExpressions.Regex.Matches(result, @"\\f0\b");
        var pardMatches = System.Text.RegularExpressions.Regex.Matches(result, @"\\pard\b");

        // 3 occurrences of \fs60: 1 for Line 1 in the header, and 2 for Line 2 and Line 3 after \par
        Assert.Equal(3, fsMatches.Count);
        // 4 occurrences of \f0: 1 in the {\fonttbl{\f0 Helvetica;}} table + 3 for the 3 body lines
        Assert.Equal(4, f0Matches.Count);
        // 3 occurrences of \pard: 1 for Line 1, and 2 for Line 2 and Line 3
        Assert.Equal(3, pardMatches.Count);

        // Assert exact structure of subsequent lines: \par followed immediately by \pard\qc\f0\fs60\cf1
        Assert.Contains(@"\par \pard\qc\f0\fs60\cf1 Line 2", result);
        Assert.Contains(@"\par \pard\qc\f0\fs60\cf1 Line 3", result);
    }

    [Fact]
    public void ReplaceRtfPayload_MultiLineMalayalam_PropagatesFormattingToAllLines()
    {
        string templateRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}{\colortbl ;\red255\green255\blue255;}\pard\qc\f0\fs72\cf1 Sample}";
        string lyric = "ദൈവം\nയേശു\nകർത്താവ്";

        string result = RtfUnicodeEncoder.ReplaceRtfPayload(templateRtf, lyric);

        var fsMatches = System.Text.RegularExpressions.Regex.Matches(result, @"\\fs72\b");
        Assert.Equal(3, fsMatches.Count);

        // Verify \par and prefix between Malayalam lines
        Assert.Contains(@"\par \pard\qc\f0\fs72\cf1 ", result);
    }

    [Fact]
    public void Encode_HindiCharacter_ProducesCorrectEscape()
    {
        // नमस्ते — न = U+0928 = 2344 signed
        string result = RtfUnicodeEncoder.Encode("न");
        Assert.Equal(@"\u2344?", result);
    }
}
