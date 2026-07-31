using MindBot.Core.Notes;

namespace MindBot.Tests;

public class WikilinkTransformerTests
{
    [Theory]
    [InlineData("$Project", "[[Project]]")]
    [InlineData("$(Some phrase)", "[[Some phrase]]")]
    [InlineData("$follow-up", "[[follow-up]]")]
    [InlineData("$50", "$50")]
    [InlineData("$12.99", "$12.99")]
    [InlineData("no dollar signs here", "no dollar signs here")]
    [InlineData("$", "$")]
    [InlineData("$ word", "$ word")]
    public void Transform_HandlesSingleToken(string input, string expected)
    {
        Assert.Equal(expected, WikilinkTransformer.Transform(input));
    }

    [Fact]
    public void Transform_LinksMultipleBareWordsInOneMessage()
    {
        var result = WikilinkTransformer.Transform("Talk to $Alice and $Bob.");

        Assert.Equal("Talk to [[Alice]] and [[Bob]].", result);
    }

    [Fact]
    public void Transform_PreservesPunctuationInsideParenthesizedPhrase()
    {
        var result = WikilinkTransformer.Transform("$(Alice's phrase, with punctuation!)");

        Assert.Equal("[[Alice's phrase, with punctuation!]]", result);
    }

    [Fact]
    public void Transform_MixesBareAndParenthesizedForms()
    {
        var result = WikilinkTransformer.Transform("Call $Alice about $(the trip)");

        Assert.Equal("Call [[Alice]] about [[the trip]]", result);
    }
}
