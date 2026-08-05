using MindBot.Core.YouTube;

namespace MindBot.Tests;

public sealed class ChunkCountCalculatorTests
{
    [Fact]
    public void AShortTranscriptIsOneChunk()
    {
        Assert.Equal(1, ChunkCountCalculator.ForWordCount(200));
    }

    /// <summary>The ~3.2k-word sample transcript that n8n split into two chunks.</summary>
    [Fact]
    public void ReproducesTheObservedTwoChunkSplit()
    {
        Assert.Equal(2, ChunkCountCalculator.ForWordCount(3237));
    }

    [Fact]
    public void RoundsUpRatherThanTruncating()
    {
        Assert.Equal(2, ChunkCountCalculator.ForWordCount(1801));
        Assert.Equal(1, ChunkCountCalculator.ForWordCount(1800));
    }

    [Fact]
    public void ClampsAtBothEnds()
    {
        Assert.Equal(ChunkCountCalculator.MinChunks, ChunkCountCalculator.ForWordCount(0));
        Assert.Equal(ChunkCountCalculator.MaxChunks, ChunkCountCalculator.ForWordCount(1_000_000));
    }

    [Fact]
    public void CountsWordsAcrossSegments()
    {
        TranscriptSegment[] segments =
        [
            new("Some articles we've acquired from the", 0, 5.76),
            new("parents, grandparents, siblings, wives,", 1.96, 7.04),
        ];

        // 6 + 4 words, far below one chunk's worth.
        Assert.Equal(1, ChunkCountCalculator.ForSegments(segments));
    }

    [Fact]
    public void SegmentsAndWordCountAgree()
    {
        var segments = Enumerable.Range(0, 500)
            .Select(i => new TranscriptSegment("one two three four five six", i * 2, 2))
            .ToList();

        Assert.Equal(ChunkCountCalculator.ForWordCount(3000), ChunkCountCalculator.ForSegments(segments));
    }
}
