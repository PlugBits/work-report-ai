using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class GitCollectorTests
{
    [Fact]
    public async Task Git_collector_uses_argument_list_and_collects_metadata_without_diff()
    {
        using var temporary = new TemporaryDirectory();
        var repository = Path.Combine(temporary.Path, "repo & harmless");
        Directory.CreateDirectory(repository);
        var occurredAt = DateTimeOffset.Now.AddMinutes(-10);
        var runner = new FakeGitRunner(occurredAt);
        var range = new WeekRangeCalculator().GetWeekRange(
            DateOnly.FromDateTime(occurredAt.DateTime));
        var collector = new LocalGitCollector([repository], runner);

        var result = await collector.CollectAsync(range);

        Assert.Empty(result.Errors);
        var commit = Assert.Single(result.Events.Where(item => item.Title == "safe subject"));
        Assert.Contains("src/Feature.cs", commit.Body);
        Assert.Contains("+12 / -3", commit.Body);
        Assert.DoesNotContain(".env", commit.Body);
        Assert.DoesNotContain(".env", commit.Evidence);
        Assert.DoesNotContain("DIFF_CONTENT_MARKER", commit.Body);
        Assert.All(runner.Requests, request =>
        {
            Assert.Equal("git", request.FileName);
            Assert.Equal("-C", request.Arguments[0]);
            Assert.Equal(repository, request.Arguments[1]);
        });
        Assert.Contains(
            runner.Requests,
            request => request.Arguments.Any(argument => argument.StartsWith("--author=")));
        Assert.DoesNotContain(
            runner.Requests.SelectMany(request => request.Arguments),
            argument => argument.Contains("DIFF_CONTENT_MARKER"));
    }

    [Fact]
    public async Task Missing_repository_is_reported_without_throwing()
    {
        var collector = new LocalGitCollector(
            [Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))],
            new FakeGitRunner(DateTimeOffset.Now));
        var range = new WeekRangeCalculator().GetWeekRange(DateOnly.FromDateTime(DateTime.Today));

        var result = await collector.CollectAsync(range);

        Assert.Empty(result.Events);
        Assert.Single(result.Errors);
    }

    private sealed class FakeGitRunner(DateTimeOffset occurredAt) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var arguments = request.Arguments;
            if (arguments.Contains("rev-parse"))
            {
                return Result("true\n");
            }
            if (arguments.Contains("config") && arguments.Contains("user.email"))
            {
                return Result("worker@example.com\n");
            }
            if (arguments.Contains("config") && arguments.Contains("user.name"))
            {
                return Result("Worker\n");
            }
            if (arguments.Contains("log"))
            {
                return Result(
                    $"abc123\u001f{occurredAt:O}\u001fsafe subject\u001fbody summary\u001e");
            }
            if (arguments.Contains("show"))
            {
                return Result("12\t3\tsrc/Feature.cs\n99\t99\t.env\n");
            }
            if (arguments.Contains("status"))
            {
                return Result(string.Empty);
            }

            return Task.FromResult(new ProcessResult(1, "", "unexpected"));
        }

        private static Task<ProcessResult> Result(string output) =>
            Task.FromResult(new ProcessResult(0, output, ""));
    }
}
