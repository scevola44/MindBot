using System.Text.Json.Serialization;

namespace MindBot.Core.YouTube;

/// <summary>
/// The request and response shapes of the five n8n webhooks, spelled out so the wire format lives
/// in one file.
/// <para>
/// Every property carries an explicit <see cref="JsonPropertyNameAttribute"/> rather than leaning on
/// a naming policy: the workflows mix conventions (snake_case responses, a hyphenated
/// "chunks-number" request field) and no single policy produces all of them.
/// </para>
/// </summary>
public sealed record TranscriptRequest(
    [property: JsonPropertyName("url")] string Url);

/// <summary>One caption cue. Passed back to text-chunker verbatim, so the shape must round-trip exactly.</summary>
public sealed record TranscriptSegment(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("start")] double Start,
    [property: JsonPropertyName("duration")] double Duration);

public sealed record TranscriptResult(
    [property: JsonPropertyName("transcript")] string Transcript,
    [property: JsonPropertyName("segments")] IReadOnlyList<TranscriptSegment> Segments,
    [property: JsonPropertyName("video_id")] string VideoId,
    [property: JsonPropertyName("video_url")] string VideoUrl);

public sealed record ChunkerRequest(
    [property: JsonPropertyName("segments")] IReadOnlyList<TranscriptSegment> Segments,
    [property: JsonPropertyName("chunks-number")] int ChunksNumber);

public sealed record TextChunk(
    [property: JsonPropertyName("chunk_index")] int ChunkIndex,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("start_time")] double StartTime,
    [property: JsonPropertyName("end_time")] double EndTime,
    [property: JsonPropertyName("word_count")] int WordCount);

/// <summary>The text-chunker response, which is also the summarize-chunks request body.</summary>
public sealed record ChunkerResult(
    [property: JsonPropertyName("num_chunks")] int NumChunks,
    [property: JsonPropertyName("chunks")] IReadOnlyList<TextChunk> Chunks);

/// <summary>Timestamps here are display strings ("8:26"), not the seconds used by <see cref="TextChunk"/>.</summary>
public sealed record ChunkSummary(
    [property: JsonPropertyName("chunk_index")] int ChunkIndex,
    [property: JsonPropertyName("start_time")] string StartTime,
    [property: JsonPropertyName("end_time")] string EndTime,
    [property: JsonPropertyName("summary")] string Summary);

/// <summary>The summarize-chunks response, which is also the chunks-reducer request body.</summary>
public sealed record SummarizeResult(
    [property: JsonPropertyName("num_chunks")] int NumChunks,
    [property: JsonPropertyName("chunk_summaries")] IReadOnlyList<ChunkSummary> ChunkSummaries);

/// <summary>The chunks-reducer response, which is also the extract-keywords request body.</summary>
public sealed record ReduceResult(
    [property: JsonPropertyName("summary")] string Summary);

public sealed record KeywordsResult(
    [property: JsonPropertyName("keywords")] IReadOnlyList<string> Keywords);
