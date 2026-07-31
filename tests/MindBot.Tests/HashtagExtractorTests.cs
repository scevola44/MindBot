using MindBot.Core.Notes;

namespace MindBot.Tests;

public class HashtagExtractorTests
{
    [Fact]
    public void Extract_SingleHashtag_ReturnsTagWithoutHash()
    {
        var result = HashtagExtractor.Extract("Remember to water the plants #chore");

        Assert.Equal(["chore"], result);
    }

    [Fact]
    public void Extract_MultipleHashtags_ReturnsAllInOrder()
    {
        var result = HashtagExtractor.Extract("#work meeting notes #followup #urgent");

        Assert.Equal(["work", "followup", "urgent"], result);
    }

    [Fact]
    public void Extract_DuplicateHashtags_ReturnsDistinct()
    {
        var result = HashtagExtractor.Extract("#chore stuff #Chore more #CHORE");

        Assert.Equal(["chore"], result);
    }

    [Fact]
    public void Extract_NoHashtags_ReturnsEmpty()
    {
        var result = HashtagExtractor.Extract("just a plain message");

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("#1")]
    [InlineData("#")]
    [InlineData("issue #42")]
    public void Extract_NumericOrBareHash_IsIgnored(string text)
    {
        var result = HashtagExtractor.Extract(text);

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_HyphenatedTag_KeepsHyphen()
    {
        var result = HashtagExtractor.Extract("#follow-up needed");

        Assert.Equal(["follow-up"], result);
    }
}
