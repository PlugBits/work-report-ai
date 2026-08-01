using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class DailyNoteWriterTests
{
    [Fact]
    public void Writes_the_note_using_the_fixed_yyyy_MM_dd_file_name_and_creates_the_folder()
    {
        using var temporary = new TemporaryDirectory();
        var folder = Path.Combine(temporary.Path, "vault", "daily");
        var writer = new DailyNoteWriter();

        var path = writer.Write(folder, new DateOnly(2026, 7, 31), "# content");

        Assert.Equal(Path.Combine(folder, "2026-07-31.md"), path);
        Assert.Equal("# content", File.ReadAllText(path));
    }

    [Fact]
    public void Rewriting_the_same_date_overwrites_the_existing_file()
    {
        using var temporary = new TemporaryDirectory();
        var writer = new DailyNoteWriter();
        var date = new DateOnly(2026, 7, 31);

        writer.Write(temporary.Path, date, "first version");
        var path = writer.Write(temporary.Path, date, "second version");

        Assert.Equal("second version", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(temporary.Path));
    }
}

public sealed class ExclusionFileParserTests
{
    [Fact]
    public void Parses_one_name_per_line_stripping_wikilink_brackets_and_whitespace()
    {
        var content = "[[ACME Corp]]\n  Beta Inc  \n[[K3071]]\n";

        var names = ExclusionFileParser.Parse(content);

        Assert.Equal(["ACME Corp", "Beta Inc", "K3071"], names);
    }

    [Fact]
    public void Skips_blank_lines_and_comment_lines()
    {
        var content = "# 除外リスト\n\nACME Corp\n   \n# コメント\nBeta Inc\n";

        var names = ExclusionFileParser.Parse(content);

        Assert.Equal(["ACME Corp", "Beta Inc"], names);
    }

    [Fact]
    public void Null_or_blank_content_returns_an_empty_list()
    {
        Assert.Empty(ExclusionFileParser.Parse(null));
        Assert.Empty(ExclusionFileParser.Parse(string.Empty));
        Assert.Empty(ExclusionFileParser.Parse("   \n  \n"));
    }

    [Fact]
    public void A_bracket_only_line_shorter_than_four_characters_is_kept_as_is()
    {
        var names = ExclusionFileParser.Parse("[[]]\n[[]");

        // "[[]]" is exactly 4 chars but strips to an empty inner name, so it is
        // dropped; "[[]" doesn't end with "]]" so it is kept verbatim.
        Assert.Equal(["[[]"], names);
    }
}
