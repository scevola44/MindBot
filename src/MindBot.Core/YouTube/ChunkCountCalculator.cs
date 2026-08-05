namespace MindBot.Core.YouTube;

/// <summary>
/// Picks how many pieces text-chunker should split a transcript into when the user did not say.
/// <para>
/// Word count, not duration: what the per-chunk summariser is bounded by is how much text fits in
/// one prompt, and a fast talker packs far more of it into the same minute. <see cref="WordsPerChunk"/>
/// reproduces the two-chunk split observed on a ~3.2k-word transcript.
/// </para>
/// </summary>
public static class ChunkCountCalculator
{
    /// <summary>
    /// Calibrated against the observed n8n output: a 3237-word transcript came back as two chunks
    /// of 1510 and 1727 words, so the workflow is comfortable with roughly 1800 words per chunk.
    /// </summary>
    private const int WordsPerChunk = 1800;

    public const int MinChunks = 1;

    /// <summary>An upper bound on fan-out: each chunk is one LLM call, and a multi-hour video would otherwise run away.</summary>
    public const int MaxChunks = 12;

    public static int ForSegments(IReadOnlyList<TranscriptSegment> segments)
    {
        var words = segments.Sum(s => CountWords(s.Text));
        return ForWordCount(words);
    }

    public static int ForWordCount(int words) =>
        Math.Clamp((int)Math.Ceiling(words / (double)WordsPerChunk), MinChunks, MaxChunks);

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
