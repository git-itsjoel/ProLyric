using System.Text;
using System.Text.RegularExpressions;

namespace ProLyric.Services;

/// <summary>
/// Converts arbitrary Unicode strings to valid RTF-escaped text,
/// and replaces the visible text payload inside an existing RTF block
/// while preserving the RTF header, font table, color table, and paragraph formatting.
/// Ensures that multi-line lyrics propagate identical paragraph and character formatting
/// (\pard, \qc, \fN, \fsN, \cfN) to every line so that ProPresenter Themes apply uniformly.
/// </summary>
public static class RtfUnicodeEncoder
{
    // -----------------------------------------------------------------------
    //  Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Encodes a plain-text string (may contain any Unicode, including Malayalam)
    /// into RTF-escaped text suitable for embedding inside an RTF body.
    /// </summary>
    /// <param name="text">The plain text to encode.</param>
    /// <param name="lineBreak">The string to emit for newline characters (\n). Defaults to @"\par ".</param>
    /// <returns>RTF-escaped string.</returns>
    public static string Encode(string text, string lineBreak = @"\par ")
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length * 2);

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            // Newline → RTF paragraph break with active formatting prefix
            if (c == '\n')
            {
                sb.Append(lineBreak);
                i++;
                continue;
            }

            // Carriage return — skip (we handle \n above)
            if (c == '\r')
            {
                i++;
                continue;
            }

            // Escape RTF special ASCII characters
            if (c == '\\') { sb.Append(@"\\"); i++; continue; }
            if (c == '{')  { sb.Append(@"\{"); i++; continue; }
            if (c == '}')  { sb.Append(@"\}"); i++; continue; }

            // Plain ASCII (0x20–0x7E) — pass through
            if (c >= 0x20 && c <= 0x7E)
            {
                sb.Append(c);
                i++;
                continue;
            }

            // Control characters below 0x20 (tab, etc.) — skip
            if (c < 0x20)
            {
                i++;
                continue;
            }

            // Non-ASCII (> 127): encode as RTF Unicode escape \uN?
            // Handle surrogate pairs (supplementary characters)
            int codePoint;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(c, text[i + 1]);
                i += 2;
            }
            else
            {
                codePoint = c;
                i++;
            }

            // RTF uses signed 16-bit \uN for BMP, but for supplementary characters
            // we must encode as two surrogate values in RTF (since RTF only supports
            // signed 16-bit values). We re-encode as UTF-16 surrogate pair escapes.
            if (codePoint <= 0xFFFF)
            {
                // BMP: convert to signed 16-bit
                short signed16 = (short)(codePoint);
                sb.Append($@"\u{signed16}?");
            }
            else
            {
                // Supplementary plane: encode as two surrogate RTF escapes
                int offset = codePoint - 0x10000;
                int high = 0xD800 + (offset >> 10);
                int low  = 0xDC00 + (offset & 0x3FF);
                sb.Append($@"\u{(short)high}?\u{(short)low}?");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Replaces the visible text payload inside an existing ProPresenter RTF data block.
    /// Preserves the RTF header, \fonttbl, \colortbl, and paragraph-level formatting.
    /// Ensures that every line in multi-line text shares identical paragraph and character
    /// formatting control words (\pard, \qc, \fN, \fsN, \cfN) so that ProPresenter Themes
    /// apply consistently across all lines without subsequent lines shrinking.
    /// </summary>
    /// <param name="existingRtf">The original RTF string from the template.</param>
    /// <param name="newPlainText">The new plain-text content to inject.</param>
    /// <returns>Updated RTF string with new payload.</returns>
    public static string ReplaceRtfPayload(string existingRtf, string newPlainText)
    {
        if (string.IsNullOrEmpty(existingRtf))
            return BuildMinimalRtf(newPlainText);

        int bodyStart = FindBodyStart(existingRtf);

        if (bodyStart < 0)
        {
            return BuildMinimalRtf(newPlainText);
        }

        string header = existingRtf[..bodyStart];
        int lastBrace = existingRtf.LastIndexOf('}');
        string footer = lastBrace >= 0 ? existingRtf[lastBrace..] : "}";

        // Extract the active paragraph & character formatting prefix from the header.
        // This is everything after the last table group closing brace '}' up to bodyStart.
        string formattingPrefix = ExtractFormattingPrefix(header);

        // For multi-line text, each newline must end the paragraph (\par) and then
        // reapply the exact same paragraph & character formatting prefix so that
        // all lines in ProPresenter share identical font size, style, and Theme behavior.
        string lineBreak = string.IsNullOrEmpty(formattingPrefix)
            ? @"\par "
            : $@"\par {formattingPrefix}";

        string encodedText = Encode(newPlainText, lineBreak);

        return $"{header}{encodedText}{footer}";
    }

    // -----------------------------------------------------------------------
    //  Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts the active paragraph and character formatting control words right before the text body
    /// (e.g. \pard\qc\f0\fs48\cf1 ).
    /// </summary>
    private static string ExtractFormattingPrefix(string header)
    {
        if (string.IsNullOrEmpty(header)) return string.Empty;

        // Find the last closing brace '}' of preamble tables (\fonttbl, \colortbl, \expandedcolortbl, etc.)
        int lastBrace = header.LastIndexOf('}');
        string prefix = lastBrace >= 0 ? header[(lastBrace + 1)..] : header;

        prefix = prefix.TrimStart('\r', '\n', ' ');

        // If prefix starts after opening {\rtf..., ensure we only capture control words
        if (prefix.StartsWith(@"{\rtf"))
        {
            int rtfHeaderEnd = prefix.IndexOf('\\', 5);
            if (rtfHeaderEnd >= 0)
                prefix = prefix[rtfHeaderEnd..];
        }

        // Clean up trailing whitespace and ensure a trailing space after control words
        prefix = prefix.TrimEnd('\r', '\n', ' ');
        if (!string.IsNullOrEmpty(prefix))
        {
            prefix += " ";
        }

        return prefix;
    }

    /// <summary>
    /// Finds the index in the RTF string where the visible body text begins.
    /// This is after the preamble tables: {\rtf1 ... \colortbl ...}
    /// and after any paragraph/character formatting tokens (\pard, \qc, \f0, \fs48, etc.).
    /// </summary>
    private static int FindBodyStart(string rtf)
    {
        int colortblPos = FindTableGroupEnd(rtf, "\\colortbl");
        int fonttblPos  = FindTableGroupEnd(rtf, "\\fonttbl");
        int stylesheetPos = FindTableGroupEnd(rtf, "\\stylesheet");
        int expandedColorPos = FindTableGroupEnd(rtf, "\\expandedcolortbl");

        int pos = Math.Max(Math.Max(Math.Max(colortblPos, fonttblPos), stylesheetPos), expandedColorPos);

        if (pos < 0)
        {
            var m = Regex.Match(rtf, @"\\pard\b");
            if (m.Success)
                return m.Index + m.Length;

            var open = Regex.Match(rtf, @"\{\\rtf\d");
            if (open.Success)
                return open.Index + open.Length;

            return -1;
        }

        // Skip any immediately following whitespace and RTF paragraph/character formatting tokens
        // (\pard, \plain, \f0, \fs24, \cf0, \qc, etc.) but NOT text content or Unicode escapes (\uN?)
        int i = pos;
        while (i < rtf.Length)
        {
            if (rtf[i] == ' ' || rtf[i] == '\r' || rtf[i] == '\n')
            {
                i++;
                continue;
            }

            // RTF control word starting with backslash
            if (rtf[i] == '\\' && i + 1 < rtf.Length && char.IsLetter(rtf[i + 1]))
            {
                // If control word is \u followed by digits/minus, it's Unicode text, NOT formatting!
                if (rtf[i + 1] == 'u' && i + 2 < rtf.Length && (char.IsDigit(rtf[i + 2]) || rtf[i + 2] == '-'))
                {
                    break;
                }

                // Read the control word
                int j = i + 1;
                while (j < rtf.Length && char.IsLetterOrDigit(rtf[j]))
                    j++;

                // If control word is \par, body text or paragraph break has started
                if (j - (i + 1) == 3 && rtf[i + 1] == 'p' && rtf[i + 2] == 'a' && rtf[i + 3] == 'r')
                {
                    break;
                }

                // Skip optional numeric parameter
                if (j < rtf.Length && (rtf[j] == '-' || char.IsDigit(rtf[j])))
                {
                    if (rtf[j] == '-') j++;
                    while (j < rtf.Length && char.IsDigit(rtf[j])) j++;
                }

                // Skip optional trailing space delimiter in RTF
                if (j < rtf.Length && rtf[j] == ' ')
                    j++;

                i = j;
                continue;
            }

            // Anything else is the start of the body text
            break;
        }

        return i;
    }

    /// <summary>
    /// Finds the index just after the closing brace of a named RTF group (e.g. \colortbl).
    /// </summary>
    private static int FindTableGroupEnd(string rtf, string groupName)
    {
        int start = rtf.LastIndexOf(groupName);
        if (start < 0) return -1;

        int bracePos = rtf.LastIndexOf('{', start);
        if (bracePos < 0) return -1;

        int depth = 0;
        for (int i = bracePos; i < rtf.Length; i++)
        {
            if (rtf[i] == '{') depth++;
            else if (rtf[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return i + 1; // position just after the closing '}'
            }
        }
        return -1;
    }

    /// <summary>
    /// Builds a minimal valid RTF document with formatting applied to all lines.
    /// Used as a fallback when the template RTF cannot be parsed.
    /// </summary>
    private static string BuildMinimalRtf(string plainText)
    {
        const string prefix = @"\pard\qc\f0\fs48\cf1 ";
        string encoded = Encode(plainText, $@"\par {prefix}");
        return $@"{{\rtf1\ansi\deff0{{\fonttbl{{\f0 Arial;}}}}{{\colortbl ;\red255\green255\blue255;}}{prefix}{encoded}}}";
    }
}
