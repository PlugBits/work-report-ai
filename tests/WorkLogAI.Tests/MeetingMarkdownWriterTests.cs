using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class MeetingFileNameBuilderTests
{
    [Fact]
    public void Base_name_combines_iso_date_and_sanitized_title()
    {
        var baseName = MeetingFileNameBuilder.BuildBaseName(new DateOnly(2026, 7, 31), "予算会議");

        Assert.Equal("2026-07-31_予算会議", baseName);
    }

    [Fact]
    public void Base_name_sanitizes_invalid_filename_characters_in_the_title()
    {
        // '/' is invalid on every OS (unlike e.g. ':', which only Windows rejects),
        // so this assertion holds on both the Linux test runner and Windows.
        var baseName = MeetingFileNameBuilder.BuildBaseName(new DateOnly(2026, 7, 31), "A/B/C");

        Assert.DoesNotContain('/', baseName);
        Assert.Equal("2026-07-31_A_B_C", baseName);
    }

    [Fact]
    public void Next_available_file_name_uses_the_plain_name_when_nothing_exists()
    {
        var name = MeetingFileNameBuilder.NextAvailableFileName("2026-07-31_会議", ".md", _ => false);

        Assert.Equal("2026-07-31_会議.md", name);
    }

    [Fact]
    public void Next_available_file_name_appends_numeric_suffix_on_collision()
    {
        var existing = new HashSet<string> { "2026-07-31_会議.md", "2026-07-31_会議_2.md" };

        var name = MeetingFileNameBuilder.NextAvailableFileName("2026-07-31_会議", ".md", existing.Contains);

        Assert.Equal("2026-07-31_会議_3.md", name);
    }
}

public sealed class MeetingMarkdownWriterTests
{
    [Fact]
    public void Write_creates_the_output_folder_and_file()
    {
        using var temporary = new TemporaryDirectory();
        var outputFolder = Path.Combine(temporary.Path, "minutes");
        var writer = new MeetingMarkdownWriter();

        var path = writer.Write(outputFolder, new DateOnly(2026, 7, 31), "定例会議", "# 定例会議\n");

        Assert.True(File.Exists(path));
        Assert.Equal("2026-07-31_定例会議.md", Path.GetFileName(path));
        Assert.Equal("# 定例会議\n", File.ReadAllText(path));
    }

    [Fact]
    public void Write_appends_numeric_suffix_when_the_file_already_exists()
    {
        using var temporary = new TemporaryDirectory();
        var writer = new MeetingMarkdownWriter();

        var first = writer.Write(temporary.Path, new DateOnly(2026, 7, 31), "定例会議", "first");
        var second = writer.Write(temporary.Path, new DateOnly(2026, 7, 31), "定例会議", "second");

        Assert.NotEqual(first, second);
        Assert.Equal("2026-07-31_定例会議.md", Path.GetFileName(first));
        Assert.Equal("2026-07-31_定例会議_2.md", Path.GetFileName(second));
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("second", File.ReadAllText(second));
    }
}
