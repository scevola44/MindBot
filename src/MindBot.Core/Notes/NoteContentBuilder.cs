using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MindBot.Core.Notes;

/// <summary>Serializes note frontmatter with YamlDotNet; never build or patch YAML by string manipulation.</summary>
public static class NoteContentBuilder
{
    // Kept byte-identical to CreateNoteHandler's serializer, which the equivalence test in
    // CreateNoteHandlerTests pins: the legacy /new path and the operation path write into the same
    // vault, and they must not differ by YAML indentation alone.
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithIndentedSequences()
        .Build();

    public static string Build(string messageText, DateTimeOffset created)
    {
        var frontmatter = new NoteFrontmatter
        {
            Date = created.ToString("yyyy-MM-ddTHH:mm:sszzz"),
        };
        foreach (var tag in HashtagExtractor.Extract(messageText))
        {
            if (!frontmatter.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                frontmatter.Tags.Add(tag);
            }
        }

        var yaml = Serializer.Serialize(frontmatter);
        var body = WikilinkTransformer.Transform(messageText);
        return $"---\n{yaml}---\n\n{body}\n";
    }
}
