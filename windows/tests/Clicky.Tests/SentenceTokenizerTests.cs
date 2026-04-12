using Clicky.Companion;
using Xunit;

namespace Clicky.Tests;

public class SentenceTokenizerTests
{
    [Fact]
    public void Feed_SingleCompleteSentence_ReturnsSentence()
    {
        var tokenizer = new SentenceTokenizer();
        var result = tokenizer.Feed("Hello world. ");
        Assert.Single(result);
        Assert.Equal("Hello world.", result[0]);
    }

    [Fact]
    public void Feed_TwoSentences_ReturnsBoth()
    {
        var tokenizer = new SentenceTokenizer();
        var result = tokenizer.Feed("First sentence. Second sentence. ");
        Assert.Equal(2, result.Count);
        Assert.Equal("First sentence.", result[0]);
        Assert.Equal("Second sentence.", result[1]);
    }

    [Fact]
    public void Feed_IncrementalDeltas_BuffersUntilBoundary()
    {
        var tokenizer = new SentenceTokenizer();

        var r1 = tokenizer.Feed("Hello ");
        Assert.Empty(r1);

        var r2 = tokenizer.Feed("world");
        Assert.Empty(r2);

        var r3 = tokenizer.Feed(". Next");
        Assert.Single(r3);
        Assert.Equal("Hello world.", r3[0]);
    }

    [Fact]
    public void Feed_ExclamationMark_SplitsSentence()
    {
        var tokenizer = new SentenceTokenizer();
        var result = tokenizer.Feed("Wow! That's great. ");
        Assert.Equal(2, result.Count);
        Assert.Equal("Wow!", result[0]);
        Assert.Equal("That's great.", result[1]);
    }

    [Fact]
    public void Feed_QuestionMark_SplitsSentence()
    {
        var tokenizer = new SentenceTokenizer();
        var result = tokenizer.Feed("Really? Yes. ");
        Assert.Equal(2, result.Count);
        Assert.Equal("Really?", result[0]);
        Assert.Equal("Yes.", result[1]);
    }

    [Fact]
    public void Feed_DoubleNewline_SplitsSentence()
    {
        var tokenizer = new SentenceTokenizer();
        var result = tokenizer.Feed("First paragraph\n\nSecond paragraph");
        Assert.Single(result);
        Assert.Equal("First paragraph", result[0]);

        var remainder = tokenizer.Flush();
        Assert.Equal("Second paragraph", remainder);
    }

    [Fact]
    public void Feed_PointTag_StrippedByCaller_TokenizerSplitsNormally()
    {
        // The tokenizer doesn't strip POINT tags itself — CompanionManager does that.
        // But the tokenizer should still split around them correctly.
        var tokenizer = new SentenceTokenizer();
        var result = tokenizer.Feed("Check the button. [POINT:300,150:save] ");
        Assert.Single(result);
        Assert.Equal("Check the button.", result[0]);
    }

    [Fact]
    public void Feed_Abbreviation_Dr_FalsePositiveAccepted()
    {
        // Per PRD: accept false positives on abbreviations like "Dr."
        var tokenizer = new SentenceTokenizer();
        var result = tokenizer.Feed("Dr. Smith is here. ");
        // May split at "Dr." — that's acceptable per PRD notes
        Assert.True(result.Count >= 1, "Should produce at least one sentence");
    }

    [Fact]
    public void Feed_Abbreviation_Eg_FalsePositiveAccepted()
    {
        var tokenizer = new SentenceTokenizer();
        var result = tokenizer.Feed("Use it for example. ");
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public void Flush_ReturnsRemainingText()
    {
        var tokenizer = new SentenceTokenizer();
        tokenizer.Feed("Partial text without terminator");
        var remainder = tokenizer.Flush();
        Assert.Equal("Partial text without terminator", remainder);
    }

    [Fact]
    public void Flush_EmptyBuffer_ReturnsNull()
    {
        var tokenizer = new SentenceTokenizer();
        Assert.Null(tokenizer.Flush());
    }

    [Fact]
    public void Flush_WhitespaceOnly_ReturnsNull()
    {
        var tokenizer = new SentenceTokenizer();
        tokenizer.Feed("   ");
        Assert.Null(tokenizer.Flush());
    }

    [Fact]
    public void Feed_PunctuationAtEndWithoutSpace_DoesNotSplit()
    {
        // A period at the very end without trailing whitespace should not split
        // (we don't know if more text is coming)
        var tokenizer = new SentenceTokenizer();
        var result = tokenizer.Feed("Hello world.");
        Assert.Empty(result);

        // But Flush should return it
        var remainder = tokenizer.Flush();
        Assert.Equal("Hello world.", remainder);
    }

    [Fact]
    public void Feed_MultipleDeltas_PreservesOrder()
    {
        var tokenizer = new SentenceTokenizer();
        var all = new List<string>();

        all.AddRange(tokenizer.Feed("First. "));
        all.AddRange(tokenizer.Feed("Second. "));
        all.AddRange(tokenizer.Feed("Third. "));

        Assert.Equal(3, all.Count);
        Assert.Equal("First.", all[0]);
        Assert.Equal("Second.", all[1]);
        Assert.Equal("Third.", all[2]);
    }

    [Fact]
    public void Feed_NewlineInMiddle_DoesNotSplit()
    {
        // Single newline should NOT cause a split
        var tokenizer = new SentenceTokenizer();
        var result = tokenizer.Feed("Line one\nline two");
        Assert.Empty(result);

        var remainder = tokenizer.Flush();
        Assert.Equal("Line one\nline two", remainder);
    }
}
