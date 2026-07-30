using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class WindowsStartupRegistrarTests
{
    [Fact]
    public void Resolve_run_value_quotes_a_published_executable_path()
    {
        var value = WindowsStartupRegistrar.ResolveRunValue(@"C:\Program Files\WorkLog AI\WorkLogAI.App.exe");

        Assert.Equal("\"C:\\Program Files\\WorkLog AI\\WorkLogAI.App.exe\"", value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_run_value_rejects_missing_process_path(string? processPath)
    {
        Assert.Throws<InvalidOperationException>(() => WindowsStartupRegistrar.ResolveRunValue(processPath));
    }

    [Theory]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData("dotnet")]
    [InlineData(@"/usr/bin/dotnet")]
    public void Resolve_run_value_rejects_the_dotnet_host(string processPath)
    {
        Assert.Throws<InvalidOperationException>(() => WindowsStartupRegistrar.ResolveRunValue(processPath));
    }
}
