using System.Security.Cryptography;
using System.Text;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class GraphAuthCacheTests
{
    [Fact]
    public void Cache_file_round_trips_through_an_injected_protector()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "Auth", "msal.cache");
        var plainText = Encoding.UTF8.GetBytes("""{"Accounts":{"fake":true}}""");

        TokenCacheSerializer.WriteCache(path, plainText, ReverseBytes);
        var roundTripped = TokenCacheSerializer.ReadCache(path, ReverseBytes);

        Assert.True(File.Exists(path));
        Assert.Equal(plainText, roundTripped);
        Assert.NotEqual(plainText, File.ReadAllBytes(path));
    }

    [Fact]
    public void Missing_cache_file_reads_as_null_without_throwing()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "does-not-exist.cache");

        Assert.Null(TokenCacheSerializer.ReadCache(path, ReverseBytes));
    }

    [Fact]
    public void Corrupt_or_undecryptable_cache_is_treated_as_empty_not_a_crash()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "corrupt.cache");
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        var result = TokenCacheSerializer.ReadCache(path, FailingUnprotect);

        Assert.Null(result);
    }

    [Fact]
    public void Empty_cache_file_reads_as_null()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "empty.cache");
        File.WriteAllBytes(path, []);

        Assert.Null(TokenCacheSerializer.ReadCache(path, ReverseBytes));
    }

    [Fact]
    public void TryDelete_removes_an_existing_file_and_is_best_effort_when_missing()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "to-delete.cache");
        File.WriteAllBytes(path, [9]);

        Assert.True(TokenCacheSerializer.TryDelete(path));
        Assert.False(File.Exists(path));
        Assert.False(TokenCacheSerializer.TryDelete(path));
    }

    [Fact]
    public void Default_cache_file_paths_are_isolated_between_normal_and_sample_mode()
    {
        var normal = GraphAuthService.DefaultCacheFilePath(sampleMode: false);
        var sample = GraphAuthService.DefaultCacheFilePath(sampleMode: true);

        Assert.NotEqual(normal, sample);
        Assert.Contains(Path.Combine("WorkLog AI", "Auth"), normal);
        Assert.EndsWith("msal.cache", normal);
        Assert.EndsWith("msal.sample.cache", sample);
    }

    private static byte[] ReverseBytes(byte[] value) => value.Reverse().ToArray();

    private static byte[]? FailingUnprotect(byte[] value) => throw new CryptographicException("corrupt cache");
}
