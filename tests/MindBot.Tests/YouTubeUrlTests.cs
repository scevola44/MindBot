using MindBot.Core.YouTube;

namespace MindBot.Tests;

public sealed class YouTubeUrlTests
{
    [Fact]
    public void ParsesAStandardWatchUrl()
    {
        Assert.Equal("qIeJ7Gw9v_I", YouTubeUrl.TryParseVideoId("https://www.youtube.com/watch?v=qIeJ7Gw9v_I"));
    }

    [Fact]
    public void ParsesAWatchUrlWithExtraQueryParameters()
    {
        Assert.Equal("qIeJ7Gw9v_I", YouTubeUrl.TryParseVideoId("https://www.youtube.com/watch?list=PL123&v=qIeJ7Gw9v_I&t=42s"));
    }

    [Fact]
    public void ParsesAShortLinkWithATrackingParameter()
    {
        Assert.Equal("qIeJ7Gw9v_I", YouTubeUrl.TryParseVideoId("https://youtu.be/qIeJ7Gw9v_I?si=AbCdEfGhIjK"));
    }

    [Fact]
    public void ParsesShortsLiveAndEmbedPaths()
    {
        Assert.Equal("qIeJ7Gw9v_I", YouTubeUrl.TryParseVideoId("https://www.youtube.com/shorts/qIeJ7Gw9v_I"));
        Assert.Equal("qIeJ7Gw9v_I", YouTubeUrl.TryParseVideoId("https://www.youtube.com/live/qIeJ7Gw9v_I"));
        Assert.Equal("qIeJ7Gw9v_I", YouTubeUrl.TryParseVideoId("https://www.youtube.com/embed/qIeJ7Gw9v_I"));
    }

    [Fact]
    public void ParsesMobileAndMusicHosts()
    {
        Assert.Equal("qIeJ7Gw9v_I", YouTubeUrl.TryParseVideoId("https://m.youtube.com/watch?v=qIeJ7Gw9v_I"));
        Assert.Equal("qIeJ7Gw9v_I", YouTubeUrl.TryParseVideoId("https://music.youtube.com/watch?v=qIeJ7Gw9v_I"));
    }

    [Fact]
    public void ParsesAUrlWithNoScheme()
    {
        Assert.Equal("qIeJ7Gw9v_I", YouTubeUrl.TryParseVideoId("youtube.com/watch?v=qIeJ7Gw9v_I"));
    }

    [Fact]
    public void AcceptsABareVideoId()
    {
        Assert.Equal("qIeJ7Gw9v_I", YouTubeUrl.TryParseVideoId("qIeJ7Gw9v_I"));
    }

    [Fact]
    public void RejectsNonYouTubeHosts()
    {
        Assert.Null(YouTubeUrl.TryParseVideoId("https://vimeo.com/watch?v=qIeJ7Gw9v_I"));
        Assert.Null(YouTubeUrl.TryParseVideoId("https://notyoutube.com/watch?v=qIeJ7Gw9v_I"));
    }

    [Fact]
    public void RejectsAChannelOrPlaylistUrlThatNamesNoVideo()
    {
        Assert.Null(YouTubeUrl.TryParseVideoId("https://www.youtube.com/@SomeChannel"));
        Assert.Null(YouTubeUrl.TryParseVideoId("https://www.youtube.com/playlist?list=PL123"));
    }

    [Fact]
    public void RejectsAnIdOfTheWrongLength()
    {
        Assert.Null(YouTubeUrl.TryParseVideoId("https://www.youtube.com/watch?v=tooshort"));
        Assert.Null(YouTubeUrl.TryParseVideoId("https://youtu.be/waaaaaaaaaaaaaytoolong"));
    }

    [Fact]
    public void RejectsJunk()
    {
        Assert.Null(YouTubeUrl.TryParseVideoId(null));
        Assert.Null(YouTubeUrl.TryParseVideoId(""));
        Assert.Null(YouTubeUrl.TryParseVideoId("   "));
        Assert.Null(YouTubeUrl.TryParseVideoId("just some words"));
    }

    /// <summary>Tracking and playlist parameters must not survive into the note or into n8n.</summary>
    [Fact]
    public void CanonicalUrlDropsEverythingButTheId()
    {
        var id = YouTubeUrl.TryParseVideoId("https://youtu.be/qIeJ7Gw9v_I?si=AbCdEfGhIjK&t=90");
        Assert.Equal("https://www.youtube.com/watch?v=qIeJ7Gw9v_I", YouTubeUrl.CanonicalUrl(id!));
    }
}
