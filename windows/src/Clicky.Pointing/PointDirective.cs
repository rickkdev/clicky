using System.Drawing;

namespace Clicky.Pointing;

/// <summary>
/// A parsed [POINT:x,y:label:screenN] directive from Claude's response,
/// with coordinates still in screenshot pixel space.
/// </summary>
public sealed record PointDirective
{
    /// <summary>X coordinate in screenshot pixel space (top-left origin).</summary>
    public required int X { get; init; }

    /// <summary>Y coordinate in screenshot pixel space (top-left origin).</summary>
    public required int Y { get; init; }

    /// <summary>Short label describing the element (e.g. "save button").</summary>
    public required string Label { get; init; }

    /// <summary>
    /// 1-based screen number from the tag (e.g. 2 for ":screen2"),
    /// or <c>null</c> if the element is on the cursor's screen.
    /// </summary>
    public int? ScreenNumber { get; init; }
}

/// <summary>
/// Result of parsing a Claude response for [POINT:...] tags.
/// </summary>
public sealed record PointingParseResult
{
    /// <summary>The response text with all [POINT:...] tags stripped (for TTS).</summary>
    public required string SpokenText { get; init; }

    /// <summary>
    /// The parsed point directive, or <c>null</c> if the response contained
    /// [POINT:none] or no valid tag.
    /// </summary>
    public PointDirective? Directive { get; init; }
}
