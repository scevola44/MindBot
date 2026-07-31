using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MindBot.Core.Notes;

/// <summary>Serializes note frontmatter with YamlDotNet; never build or patch YAML by string manipulation.</summary>
public static class NoteContentBuilder
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
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
