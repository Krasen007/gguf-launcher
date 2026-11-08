using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

public class ProgramStartupReviewTests
{
    [Theory]
    [InlineData("not a URI")]
    [InlineData("/relative/path")]
    public void TryCreateBrowserUri_RejectsInvalidOrRelativeUrls(string url)
    {
        Assert.Null(Program.TryCreateBrowserUri(url));
    }

    [Fact]
    public void TryCreateBrowserUri_AcceptsAbsoluteUrl()
    {
        Assert.Equal(
            new Uri("http://127.0.0.1:8080/"),
            Program.TryCreateBrowserUri("http://127.0.0.1:8080/"));
    }

    [Fact]
    public async Task ObserveReadinessAsync_LogsUnexpectedFaults()
    {
        List<string> logs = [];

        await Program.ObserveReadinessAsync(
            Task.FromException(new InvalidOperationException("readiness failed")),
            logs.Add);

        Assert.Equal(["browser readiness failed: readiness failed"], logs);
    }

    [Fact]
    public async Task ObserveReadinessAsync_DoesNotLogCancellation()
    {
        List<string> logs = [];

        await Program.ObserveReadinessAsync(
            Task.FromCanceled(new CancellationToken(canceled: true)),
            logs.Add);

        Assert.Empty(logs);
    }
}
