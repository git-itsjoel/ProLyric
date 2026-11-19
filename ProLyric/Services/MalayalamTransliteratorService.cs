using System.Text;

namespace ProLyric.Services;

/// <summary>
/// High-quality offline phonetic Malayalam → Manglish (Roman) transliterator.
/// Implements the comprehensive phonetic engine and bug-fixes from Aksharam (https://github.com/Ajishkbn/aksharam):
///  - Correct vowel signs (matras), including the matra-ai fix (കൈ → Kai, ദൈവം → Daivam)
///  - Anusvaram (ം) contextual assimilation (ഗംഗ → Ganga, സംഗീതം → Sangeetham)
///  - Retroflex voicing of inter-vocalic/non-initial ട/ഠ (ഓടുക → Oduka, കൂട് → Koodu)
///  - Chillu letters mapped correctly (ർ → r, never rr; കർത്താവ് → Karthavu)
///  - Multi-consonant conjuncts (ജ്ഞ → jn, ച്ഛ → chh, ക്ഷ → ksh, ന്റ → nt, റ്റ → tt, etc.)
///  - Word-final virama / samvruthokaram trailing 'u' (ആണ് → Aanu, ഉണ്ട് → Undu, നല്ലത് → Nallathu)
///  - Clean token-based capitalization (Yeshu Karthavu)
///  - Exact line-break and whitespace preservation
/// </summary>
public static class MalayalamTransliteratorService
{
    private const int MalayalamStart = 0x0D00;
    private const int MalayalamEnd   = 0x0D7F;

    private const char Virama   = '\u0D4D'; // ്
    private const char Anusvara = '\u0D02'; // ം
    private const char Visarga  = '\u0D03'; // ഃ

    // -----------------------------------------------------------------------
    //  Independent Vowels (Swarangal)
    // -----------------------------------------------------------------------
    private static readonly Dictionary<char, string> IndependentVowels = new()
    {
        { '\u0D05', "a"  },  // അ
        { '\u0D06', "aa" },  // ആ
        { '\u0D07', "i"  },  // ഇ
        { '\u0D08', "ee" },  // ഈ
        { '\u0D09', "u"  },  // ഉ
        { '\u0D0A', "oo" },  // ഊ
        { '\u0D0B', "ru" },  // ഋ
        { '\u0D0C', "lu" },  // ഌ
        { '\u0D0E', "e"  },  // എ
        { '\u0D0F', "e"  },  // ഏ
        { '\u0D10', "ai" },  // ഐ
        { '\u0D12', "o"  },  // ഒ
        { '\u0D13', "o"  },  // ഓ
        { '\u0D14', "au" },  // ഔ
    };

    // -----------------------------------------------------------------------
    //  Chillu Letters (Atomic U+0D7A..U+0D7F and legacy combinations)
    // -----------------------------------------------------------------------
    private static readonly Dictionary<char, string> ChilluLetters = new()
    {
        { '\u0D7A', "n" },  // ൺ
        { '\u0D7B', "n" },  // ൻ
        { '\u0D7C', "r" },  // ർ -> r (CRITICAL FIX: NOT rr)
        { '\u0D7D', "l" },  // ൽ
        { '\u0D7E', "l" },  // ൾ
        { '\u0D7F', "k" },  // ൿ
    };

    // -----------------------------------------------------------------------
    //  Vowel Signs (Matras)
    // -----------------------------------------------------------------------
    private static readonly Dictionary<char, string> VowelSigns = new()
    {
        { '\u0D3E', "aa" }, // ാ
        { '\u0D3F', "i"  }, // ി
        { '\u0D40', "ee" }, // ീ
        { '\u0D41', "u"  }, // ു
        { '\u0D42', "oo" }, // ൂ
        { '\u0D43', "ru" }, // ൃ
        { '\u0D46', "e"  }, // െ
        { '\u0D47', "e"  }, // േ
        { '\u0D48', "ai" }, // ൈ (CRITICAL AKSHARAM FIX: "ai", NOT "y")
        { '\u0D4A', "o"  }, // ൊ
        { '\u0D4B', "o"  }, // ോ
        { '\u0D4C', "au" }, // ൌ
        { '\u0D57', "au" }, // ൗ (au length mark)
    };

    // -----------------------------------------------------------------------
    //  Multi-character Compounds / Conjuncts (matched longest-first)
    // -----------------------------------------------------------------------
    private static readonly (string seq, string roman)[] Compounds = new (string, string)[]
    {
        // 5-character combinations
        ("ന്ത്യ", "nthy"),
        ("ത്സ്യ", "thsy"),
        ("സ്ത്യ", "sthy"),

        // Traditional post-chillu geminates (phonetically single in Manglish)
        ("ർക്ക", "rk"),
        ("ർത്ത", "rth"),
        ("ർഗ്ഗ", "rg"),
        ("ർണ്ണ", "rn"),
        ("ർമ്മ", "rm"),
        ("ർപ്പ", "rp"),
        ("ർവ്വ", "rv"),

        // Common conjuncts (C1 + virama + C2)
        ("ജ്ഞ", "jn"),    // ജ + ് + ഞ -> jn (Jnaanam, aksharam fix)
        ("ച്ഛ", "chh"),   // ച + ് + ഛ -> chh (Achhan, aksharam fix)
        ("ക്ഷ", "ksh"),   // ക് + ഷ
        ("ക്സ", "ks"),    // ക് + സ
        ("ക്ക", "kk"),    // ക് + ക
        ("ഗ്ഗ", "gg"),    // ഗ് + ഗ
        ("ങ്ങ", "ng"),    // ങ് + ങ
        ("ങ്ക", "nk"),    // ങ് + ക
        ("ച്ച", "cch"),   // ച് + ച
        ("ജ്ജ", "jj"),    // ജ് + ജ
        ("ഞ്ഞ", "nj"),    // ഞ് + ഞ
        ("ഞ്ച", "nch"),   // ഞ് + ച
        ("ട്ട", "tt"),    // ട് + ട
        ("ണ്ണ", "nn"),    // ണ് + ണ
        ("ണ്ട", "nd"),    // ണ് + ട
        ("ത്ത", "tth"),   // ത് + ത
        ("ത്ഥ", "tth"),   // ത് + ഥ
        ("ത്സ", "ths"),   // ത് + സ
        ("ത്ഭ", "thbh"),  // ത് + ഭ
        ("ത്മ", "thm"),   // ത് + മ
        ("ദ്ദ", "dd"),    // ദ് + ദ
        ("ദ്ധ", "ddh"),   // ദ് + ധ
        ("ന്ന", "nn"),    // ന് + ന
        ("ന്ത", "nth"),   // ന് + ത
        ("ന്ദ", "nd"),    // ന് + ദ
        ("ന്ധ", "ndh"),   // ന് + ധ
        ("മ്പ", "mp"),    // മ് + പ
        ("മ്മ", "mm"),    // മ് + മ
        ("ബ്ബ", "bb"),    // ബ് + ബ
        ("പ്പ", "pp"),    // പ് + പ
        ("യ്യ", "yy"),    // യ് + യ
        ("ല്ല", "ll"),    // ല് + ല
        ("ള്ള", "ll"),    // ള് + ള
        ("വ്വ", "vv"),    // വ് + വ
        ("ശ്ശ", "sh"),    // ശ് + ശ
        ("ശ്ച", "shch"),  // ശ് + ച
        ("ഷ്ട", "sht"),   // ഷ് + ട
        ("സ്സ", "s"),     // സ് + സ
        ("സ്ത", "sth"),   // സ് + ത
        ("സ്പ", "sp"),    // സ് + പ
        ("സ്ന", "sn"),    // സ് + ന
        ("സ്മ", "sm"),    // സ് + മ
        ("സ്വ", "sw"),    // സ് + വ
        ("സ്യ", "sy"),    // സ് + യ
        ("പ്ര", "pr"),    // പ് + ര
        ("ത്ര", "thr"),   // ത് + ര
        ("ക്ര", "kr"),    // ക് + ര
        ("ഗ്ര", "gr"),    // ഗ് + ര
        ("ദ്ര", "dr"),    // ദ് + ര
        ("ശ്ര", "shr"),   // ശ് + ര
        ("റ്റ", "tt"),    // റ് + റ
        ("ന്റ", "nt"),    // ൻ + റ
        ("\u0D7B\u0D31", "nt"), // ൻ + റ
        ("\u0D28\u0D4D\u0D31", "nt"), // ന + ് + റ
    };

    // -----------------------------------------------------------------------
    //  Base Consonants
    // -----------------------------------------------------------------------
    private static readonly Dictionary<char, string> ConsonantBases = new()
    {
        { '\u0D15', "k"   },  // ക
        { '\u0D16', "kh"  },  // ഖ
        { '\u0D17', "g"   },  // ഗ
        { '\u0D18', "gh"  },  // ഘ
        { '\u0D19', "ng"  },  // ങ
        { '\u0D1A', "ch"  },  // ച
        { '\u0D1B', "chh" },  // ഛ
        { '\u0D1C', "j"   },  // ജ
        { '\u0D1D', "jh"  },  // ഝ
        { '\u0D1E', "nj"  },  // ഞ
        { '\u0D1F', "t"   },  // ട (retroflex: t initial, d non-initial)
        { '\u0D20', "th"  },  // ഠ (retroflex: th initial, dh non-initial)
        { '\u0D21', "d"   },  // ഡ
        { '\u0D22', "dh"  },  // ഢ
        { '\u0D23', "n"   },  // ണ
        { '\u0D24', "th"  },  // ത
        { '\u0D25', "th"  },  // ഥ
        { '\u0D26', "d"   },  // ദ
        { '\u0D27', "dh"  },  // ധ
        { '\u0D28', "n"   },  // ന
        { '\u0D29', "n"   },  // ഩ
        { '\u0D2A', "p"   },  // പ
        { '\u0D2B', "ph"  },  // ഫ
        { '\u0D2C', "b"   },  // ബ
        { '\u0D2D', "bh"  },  // ഭ
        { '\u0D2E', "m"   },  // മ
        { '\u0D2F', "y"   },  // യ
        { '\u0D30', "r"   },  // ര
        { '\u0D31', "r"   },  // റ
        { '\u0D32', "l"   },  // ല
        { '\u0D33', "l"   },  // ള
        { '\u0D34', "zh"  },  // ഴ
        { '\u0D35', "v"   },  // വ
        { '\u0D36', "sh"  },  // ശ
        { '\u0D37', "sh"  },  // ഷ
        { '\u0D38', "s"   },  // സ
        { '\u0D39', "h"   },  // ഹ
    };

    // -----------------------------------------------------------------------
    //  Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns true if the string contains any Malayalam Unicode characters.
    /// </summary>
    public static bool ContainsMalayalam(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
        {
            if (c >= MalayalamStart && c <= MalayalamEnd) return true;
        }
        return false;
    }

    /// <summary>
    /// Transliterates Malayalam text to Manglish, preserving exact line breaks.
    /// Non-Malayalam characters pass through unchanged.
    /// By default applies title/word capitalization (aksharam standard).
    /// </summary>
    public static string Transliterate(string text, bool titleCase = true)
    {
        if (string.IsNullOrEmpty(text)) return text;

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        var result = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) result.Append('\n');
            result.Append(TransliterateLine(lines[i], titleCase));
        }

        return result.ToString();
    }

    // -----------------------------------------------------------------------
    //  Line processing
    // -----------------------------------------------------------------------

    private static string TransliterateLine(string line, bool titleCase)
    {
        if (string.IsNullOrEmpty(line)) return line;

        var sb = new StringBuilder();
        int i = 0;

        while (i < line.Length)
        {
            // Delimiters (whitespace, punctuation, ASCII non-alphanumeric)
            if (char.IsWhiteSpace(line[i]) || (line[i] < 0x0D00 && !char.IsLetter(line[i])))
            {
                sb.Append(line[i]);
                i++;
                continue;
            }

            // Pure non-Malayalam text (English words, etc.)
            if (line[i] < 0x0D00 || line[i] > 0x0D7F)
            {
                int start = i;
                while (i < line.Length && (line[i] < 0x0D00 || line[i] > 0x0D7F) && !char.IsWhiteSpace(line[i]))
                {
                    i++;
                }
                sb.Append(line.AsSpan(start, i - start));
                continue;
            }

            // Malayalam token: collect contiguous Malayalam characters and joiners
            int wordStart = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i]) &&
                   ((line[i] >= 0x0D00 && line[i] <= 0x0D7F) || line[i] == '\u200C' || line[i] == '\u200D'))
            {
                i++;
            }

            string malayalamWord = line.Substring(wordStart, i - wordStart);
            string transliterated = TransliterateWord(malayalamWord);

            if (titleCase && transliterated.Length > 0)
            {
                transliterated = char.ToUpperInvariant(transliterated[0]) + transliterated.Substring(1);
            }

            sb.Append(transliterated);
        }

        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    //  Word processing (Aksharam phonetic engine)
    // -----------------------------------------------------------------------

    private static string TransliterateWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        // Normalize legacy chillu representations (consonant + virama + ZWJ)
        word = word
            .Replace("\u0D23\u0D4D\u200D", "\u0D7A") // ൺ
            .Replace("\u0D28\u0D4D\u200D", "\u0D7B") // ൻ
            .Replace("\u0D30\u0D4D\u200D", "\u0D7C") // ർ
            .Replace("\u0D32\u0D4D\u200D", "\u0D7D") // ൽ
            .Replace("\u0D33\u0D4D\u200D", "\u0D7E") // ൾ
            .Replace("\u0D15\u0D4D\u200D", "\u0D7F") // ൿ
            .Replace("\u0D30\u0D4D", "\u0D7C")       // legacy ര് -> ർ
            .Replace("\u200C", "")
            .Replace("\u200D", "");

        var sb = new StringBuilder(word.Length * 2);
        int j = 0;

        while (j < word.Length)
        {
            char c = word[j];

            // 1. Visarga (ഃ)
            if (c == Visarga)
            {
                // If followed by voiceless stop ക or ഖ, it assimilates (skip to avoid duplicate h)
                if (j + 1 < word.Length && (word[j + 1] == '\u0D15' || word[j + 1] == '\u0D16'))
                {
                    j++;
                    continue;
                }
                sb.Append("h");
                j++;
                continue;
            }

            // 2. Anusvara (ം)
            if (c == Anusvara)
            {
                sb.Append(AssimilateAnusvara(word, j + 1));
                j++;
                continue;
            }

            // 3. Isolated Virama (should not appear standalone, skip safely)
            if (c == Virama)
            {
                j++;
                continue;
            }

            // 4. Multi-character Compounds / Conjuncts (matched longest-first, before single chillu letters)
            bool matchedCompound = false;
            foreach (var (seq, romanBase) in Compounds)
            {
                if (word.AsSpan(j).StartsWith(seq))
                {
                    j += seq.Length;
                    matchedCompound = true;
                    ProcessConsonantEnding(word, ref j, romanBase, sb);
                    break;
                }
            }
            if (matchedCompound) continue;

            // 5. Chillu letters (never get trailing inherent 'a' or samvruthokaram 'u')
            if (ChilluLetters.TryGetValue(c, out string? chilluRoman))
            {
                sb.Append(chilluRoman);
                j++;
                continue;
            }

            // 6. Independent vowels
            if (IndependentVowels.TryGetValue(c, out string? vowelRoman))
            {
                sb.Append(vowelRoman);
                j++;
                continue;
            }

            // 7. Base Consonants
            if (ConsonantBases.TryGetValue(c, out string? baseRoman))
            {
                // Retroflex voicing rule for ട / ഠ:
                // Word-initial: 't' / 'th'; non-initial / intervocalic: 'd' / 'dh'
                if (c == '\u0D1F') // ട
                {
                    baseRoman = (j == 0) ? "t" : "d";
                }
                else if (c == '\u0D20') // ഠ
                {
                    baseRoman = (j == 0) ? "th" : "dh";
                }

                j++; // consume consonant
                ProcessConsonantEnding(word, ref j, baseRoman, sb);
                continue;
            }

            // 8. Standalone vowel signs (matras without base consonant, fallback)
            if (VowelSigns.TryGetValue(c, out string? orphanMatra))
            {
                sb.Append(orphanMatra);
                j++;
                continue;
            }

            // 9. Passthrough for anything else
            sb.Append(c);
            j++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Processes whatever follows a consonant or compound (matra, virama, anusvara, or inherent 'a').
    /// </summary>
    private static void ProcessConsonantEnding(string word, ref int j, string consonantRoman, StringBuilder sb)
    {
        sb.Append(consonantRoman);

        // If consonant is at end of word: appends inherent 'a'
        if (j >= word.Length)
        {
            sb.Append('a');
            return;
        }

        // Check for ു് (samvruthokaram sign: U+0D41 + U+0D4D)
        if (j + 1 < word.Length && word[j] == '\u0D41' && word[j + 1] == Virama)
        {
            sb.Append('u');
            j += 2;
            return;
        }

        // Check for word-final "-ാവ്" (-avu) as in കർത്താവ്, പിതാവ്, രാജാവ്, ആത്മാവ്
        if (word[j] == '\u0D3E' && j + 2 < word.Length && word[j + 1] == '\u0D35' && word[j + 2] == Virama &&
            j + 3 >= word.Length)
        {
            sb.Append("avu");
            j += 3;
            return;
        }

        // Check for dependent vowel signs (matras)
        char next = word[j];
        if (VowelSigns.TryGetValue(next, out string? matraRoman))
        {
            sb.Append(matraRoman);
            j++;
            return;
        }

        // Check for Virama (്)
        if (next == Virama)
        {
            j++; // consume virama

            // Word-final virama rule (Samvruthokaram):
            // Non-chillu consonant + virama at end of word produces colloquial trailing 'u'
            // (ആണ് → Aanu, ഉണ്ട് → Undu, നല്ലത് → Nallathu, കൂട് → Koodu)
            if (j >= word.Length)
            {
                sb.Append('u');
                return;
            }

            // Non-final virama suppresses inherent 'a' (consonant cluster)
            return;
        }

        // Check for Anusvara immediately following consonant:
        // Consonant retains inherent 'a', anusvara will be processed in next iteration
        if (next == Anusvara)
        {
            sb.Append('a');
            return;
        }

        // Default: consonant has inherent 'a'
        sb.Append('a');
    }

    /// <summary>
    /// Contextual assimilation of Anusvaram (ം):
    /// Velars (ക, ഖ, ഗ, ഘ) → n (as in Ganga, Sangeetham)
    /// Palatals / Dentals / Retroflex → n (as in Sancharam, Santhosham)
    /// Labials (പ, ഫ, ബ, ഭ, മ) → m (as in Sampathu)
    /// Word-final or default → m (as in Daivam, Sneham)
    /// </summary>
    private static string AssimilateAnusvara(string word, int nextCharIndex)
    {
        if (nextCharIndex >= word.Length)
            return "m";

        char next = word[nextCharIndex];

        // Velars: ക (0D15), ഖ (0D16), ഗ (0D17), ഘ (0D18)
        // In Manglish/English orthography, anusvara before velars produces 'n' (e.g. n + ga = Ganga)
        if (next >= '\u0D15' && next <= '\u0D18')
            return "n";

        // Palatals: ച (0D1A), ഛ (0D1B), ജ (0D1C), ഝ (0D1D), ഞ (0D1E)
        if (next >= '\u0D1A' && next <= '\u0D1E')
            return "n";

        // Retroflex: ട (0D1F), ഠ (0D20), ഡ (0D21), ഢ (0D22), ണ (0D23)
        if (next >= '\u0D1F' && next <= '\u0D23')
            return "n";

        // Dentals: ത (0D24), ഥ (0D25), ദ (0D26), ധ (0D27), ന (0D28)
        if (next >= '\u0D24' && next <= '\u0D28')
            return "n";

        // Labials: പ (0D2A), ഫ (0D2B), ബ (0D2C), ഭ (0D2D), മ (0D2E)
        if (next >= '\u0D2A' && next <= '\u0D2E')
            return "m";

        // Default (semivowels, other letters, or word boundary)
        return "m";
    }
}
