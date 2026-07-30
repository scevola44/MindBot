using MindBot.Core.Notes;

namespace MindBot.Tests;

public class FilenameSanitiserTests
{
    [Fact]
    public void SlugFromText_PathTraversalBody_ProducesFilenameInsideVault()
    {
        var slug = FilenameSanitiser.SlugFromText("../../etc/passwd");
        var filename = $"2026-07-30T120000-{slug}.md";

        var vaultRoot = Path.Combine(Path.GetTempPath(), "mindbot-vault-test");
        var resolved = VaultPathResolver.ResolveNotePath(vaultRoot, filename);

        var rootFull = Path.GetFullPath(vaultRoot);
        Assert.StartsWith(rootFull + Path.DirectorySeparatorChar, resolved);
        Assert.DoesNotContain("..", filename);
        Assert.DoesNotContain('/', slug);
        Assert.DoesNotContain('\\', slug);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32")]
    [InlineData("....//....//etc/passwd")]
    public void Sanitise_StripsPathSeparatorsAndDots(string input)
    {
        var result = FilenameSanitiser.Sanitise(input);

        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain('\\', result);
        Assert.False(result.StartsWith('.'));
    }

    [Fact]
    public void Sanitise_StripsControlCharacters()
    {
        var input = "hello" + '\u0000' + "world" + '\n' + "test" + '\u0001' + "end";

        var result = FilenameSanitiser.Sanitise(input);

        Assert.DoesNotContain('\u0000', result);
        Assert.DoesNotContain('\n', result);
        Assert.DoesNotContain('\u0001', result);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void Sanitise_ReservedDeviceName_IsRewritten(string reservedName)
    {
        var result = FilenameSanitiser.Sanitise(reservedName);

        Assert.NotEqual(reservedName, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitise_CapsLength()
    {
        var input = new string('a', 500);
        var result = FilenameSanitiser.Sanitise(input, maxLength: 40);

        Assert.True(result.Length <= 40);
    }

    [Fact]
    public void Sanitise_EmptyOrWhitespace_FallsBackToNote()
    {
        Assert.Equal("note", FilenameSanitiser.Sanitise(""));
        Assert.Equal("note", FilenameSanitiser.Sanitise("   "));
        Assert.Equal("note", FilenameSanitiser.Sanitise("..."));
        Assert.Equal("note", FilenameSanitiser.Sanitise("///"));
    }

    [Fact]
    public void SlugFromText_UsesFirstSixWordsOnly()
    {
        var slug = FilenameSanitiser.SlugFromText("one two three four five six seven eight");

        Assert.DoesNotContain("seven", slug);
        Assert.DoesNotContain("eight", slug);
    }
}
