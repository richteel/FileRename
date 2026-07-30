using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FileRename;

/// <summary>
/// Kinds of tokens that make up a parsed rename mask.
/// </summary>
public enum MaskTokenKind
{
    Literal,
    Digits,
    Any
}

/// <summary>
/// A single parsed piece of an input or output mask.
/// </summary>
/// <param name="Kind">The kind of token.</param>
/// <param name="Text">The literal text (for <see cref="MaskTokenKind.Literal"/>) or the raw placeholder text.</param>
/// <param name="Width">For output <see cref="MaskTokenKind.Digits"/> tokens, the minimum zero-padded width.</param>
public readonly record struct MaskToken(MaskTokenKind Kind, string Text, int Width);

/// <summary>
/// Parses simple wildcard rename masks and computes new file names.
/// Mask syntax:
///   #  one or more digits (input) / insert captured number (output)
///   0# (or additional leading zeros before a #) sets the zero-padded width of the number in the output,
///      e.g. "0#" = 2 digits, "00#" = 3 digits. Numbers longer than the width are never truncated.
///   *  any run of text (input) / insert captured text (output)
///   Any other character is treated as literal text that must match exactly (case-insensitive).
/// </summary>
public static class MaskEngine
{
    /// <summary>Result of computing a new name for a single file against a mask pair.</summary>
    public enum MatchStatus
    {
        NoMatch,
        NoChange,
        Rename
    }

    public static List<MaskToken> Tokenize(string mask)
    {
        var tokens = new List<MaskToken>();
        var literal = new StringBuilder();
        int i = 0;

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                tokens.Add(new MaskToken(MaskTokenKind.Literal, literal.ToString(), 0));
                literal.Clear();
            }
        }

        while (i < mask.Length)
        {
            char c = mask[i];
            if (c == '*')
            {
                FlushLiteral();
                tokens.Add(new MaskToken(MaskTokenKind.Any, "*", 1));
                i++;
            }
            else if (c == '#' || c == '0')
            {
                int start = i;
                while (i < mask.Length && (mask[i] == '#' || mask[i] == '0'))
                {
                    i++;
                }

                string run = mask.Substring(start, i - start);
                if (run.Contains('#'))
                {
                    FlushLiteral();
                    tokens.Add(new MaskToken(MaskTokenKind.Digits, run, run.Length));
                }
                else
                {
                    // A run of plain '0' characters with no '#' is just literal zeros.
                    literal.Append(run);
                }
            }
            else
            {
                literal.Append(c);
                i++;
            }
        }

        FlushLiteral();
        return tokens;
    }

    /// <summary>Builds a case-insensitive, fully-anchored regex for an input mask.</summary>
    public static Regex BuildInputRegex(string inputMask, out List<MaskTokenKind> groupOrder)
    {
        var tokens = Tokenize(inputMask);
        var sb = new StringBuilder("^");
        groupOrder = new List<MaskTokenKind>();

        foreach (var t in tokens)
        {
            switch (t.Kind)
            {
                case MaskTokenKind.Literal:
                    sb.Append(Regex.Escape(t.Text));
                    break;
                case MaskTokenKind.Digits:
                    sb.Append("(\\d+)");
                    groupOrder.Add(MaskTokenKind.Digits);
                    break;
                case MaskTokenKind.Any:
                    sb.Append("(.+?)");
                    groupOrder.Add(MaskTokenKind.Any);
                    break;
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    /// <summary>
    /// Computes what <paramref name="fileName"/> (name only, no extension-specific handling - the mask
    /// itself should include any extension) would be renamed to, given the input/output masks.
    /// </summary>
    public static MatchStatus ComputeNewName(string fileName, string inputMask, string outputMask, out string newName, out string? error)
    {
        error = null;
        newName = fileName;

        Regex regex;
        List<MaskTokenKind> groupOrder;
        try
        {
            regex = BuildInputRegex(inputMask, out groupOrder);
        }
        catch (Exception ex)
        {
            error = $"Invalid input mask: {ex.Message}";
            return MatchStatus.NoMatch;
        }

        var match = regex.Match(fileName);
        if (!match.Success)
        {
            return MatchStatus.NoMatch;
        }

        var digitQueue = new Queue<string>();
        var anyQueue = new Queue<string>();
        for (int g = 0; g < groupOrder.Count; g++)
        {
            string value = match.Groups[g + 1].Value;
            if (groupOrder[g] == MaskTokenKind.Digits)
            {
                digitQueue.Enqueue(value);
            }
            else
            {
                anyQueue.Enqueue(value);
            }
        }

        List<MaskToken> outTokens;
        try
        {
            outTokens = Tokenize(outputMask);
        }
        catch (Exception ex)
        {
            error = $"Invalid output mask: {ex.Message}";
            return MatchStatus.NoMatch;
        }

        var sb = new StringBuilder();
        foreach (var t in outTokens)
        {
            switch (t.Kind)
            {
                case MaskTokenKind.Literal:
                    sb.Append(t.Text);
                    break;
                case MaskTokenKind.Digits:
                {
                    string value = digitQueue.Count > 0 ? digitQueue.Dequeue() : string.Empty;
                    sb.Append(value.Length < t.Width ? value.PadLeft(t.Width, '0') : value);
                    break;
                }
                case MaskTokenKind.Any:
                {
                    string value = anyQueue.Count > 0 ? anyQueue.Dequeue() : string.Empty;
                    sb.Append(value);
                    break;
                }
            }
        }

        newName = SanitizeFileName(sb.ToString());
        return string.Equals(newName, fileName, StringComparison.Ordinal) ? MatchStatus.NoChange : MatchStatus.Rename;
    }

    /// <summary>Replaces any characters that are illegal in Windows file names with an underscore.</summary>
    public static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
        {
            name = name.Replace(c, '_');
        }

        return name;
    }
}
