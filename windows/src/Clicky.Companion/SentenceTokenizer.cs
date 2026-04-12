using System.Text;

namespace Clicky.Companion;

/// <summary>
/// Splits streaming LLM text deltas into sentence-sized chunks for pipelined TTS.
/// Uses a simple heuristic: split on sentence-ending punctuation (.!?) followed by
/// whitespace or end-of-stream, plus double newlines. Accepts false positives on
/// abbreviations like "Dr." or "e.g." — a short extra TTS call is acceptable.
/// </summary>
internal sealed class SentenceTokenizer
{
    private readonly StringBuilder _buffer = new();

    /// <summary>
    /// Feeds a text delta and returns any complete sentences found.
    /// Sentences are returned in order. Remaining partial text stays buffered.
    /// </summary>
    public List<string> Feed(string delta)
    {
        var sentences = new List<string>();
        _buffer.Append(delta);

        while (true)
        {
            var text = _buffer.ToString();
            var splitIndex = FindSentenceBoundary(text);
            if (splitIndex < 0) break;

            var sentence = text[..splitIndex].Trim();
            if (sentence.Length > 0)
            {
                sentences.Add(sentence);
            }
            _buffer.Clear();
            _buffer.Append(text[splitIndex..]);
        }

        return sentences;
    }

    /// <summary>
    /// Flushes any remaining buffered text as the final sentence.
    /// Returns null if the buffer is empty/whitespace.
    /// </summary>
    public string? Flush()
    {
        var remaining = _buffer.ToString().Trim();
        _buffer.Clear();
        return remaining.Length > 0 ? remaining : null;
    }

    /// <summary>
    /// Finds the index of the first character AFTER a sentence boundary.
    /// Returns -1 if no boundary is found.
    /// </summary>
    private static int FindSentenceBoundary(string text)
    {
        // Look for double newline first (paragraph break)
        var doubleNewline = text.IndexOf("\n\n", StringComparison.Ordinal);
        if (doubleNewline >= 0)
        {
            // Return index after the double newline
            var afterNewline = doubleNewline + 2;
            // Skip any extra whitespace
            while (afterNewline < text.Length && char.IsWhiteSpace(text[afterNewline]))
                afterNewline++;
            return afterNewline;
        }

        // Look for sentence-ending punctuation followed by whitespace
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '.' or '!' or '?')
            {
                // Must be followed by whitespace or end of string with more chars
                int next = i + 1;
                if (next < text.Length && char.IsWhiteSpace(text[next]))
                {
                    // Skip whitespace after punctuation
                    while (next < text.Length && char.IsWhiteSpace(text[next]))
                        next++;
                    return next;
                }
            }
        }

        return -1;
    }
}
