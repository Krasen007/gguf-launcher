using System.Net;
using System.Net.Http;
using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

/// <summary>
/// The server-readiness wait now follows the server process and only surfaces real failures,
/// and the launcher-only flag split is one pass instead of a detect/strip ordering pair.
/// </summary>
public class AuditSuggestionsTests
{
    [Fact]
    public void HealthUri_WorksWithAndWithoutTrailingSlash()
    {
        Assert.Equal(
            "http://127.0.0.1:8080/health",
            ServerReadiness.HealthUri(new Uri("http://127.0.0.1:8080/")).AbsoluteUri);
        Assert.Equal(
            "http://127.0.0.1:8080/health",
            ServerReadiness.HealthUri(new Uri("http://127.0.0.1:8080")).AbsoluteUri);
    }

    [Fact]
    public async Task OpensBrowserOnceWhenHealthAnswers()
    {
        ScriptedHandler handler = new(ScriptedHandler.Status(HttpStatusCode.OK));
        List<Uri> opened = [];

        await ServerReadiness.OpenWhenReadyAsync(
            new Uri("http://127.0.0.1:8080/"),
            () => true,
            uri =>
            {
                opened.Add(uri);
                return true;
            },
            _ => { },
            handler: handler,
            deadline: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(1),
            stillWaitingAfter: TimeSpan.FromMinutes(1));

        Assert.Single(opened);
        Assert.Equal(new Uri("http://127.0.0.1:8080/"), opened[0]);
        Assert.Equal("/health", handler.LastUri!.AbsolutePath);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DoesNotPollWhenServerIsAlreadyGone()
    {
        ScriptedHandler handler = new(ScriptedHandler.Status(HttpStatusCode.OK));
        int opened = 0;

        await ServerReadiness.OpenWhenReadyAsync(
            new Uri("http://127.0.0.1:8080/"),
            () => false,
            _ =>
            {
                opened++;
                return true;
            },
            _ => { },
            handler: handler,
            deadline: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(1),
            stillWaitingAfter: TimeSpan.FromMinutes(1));

        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task StopsPollingWhenServerExitsDuringTheWait()
    {
        ScriptedHandler handler = new(
            ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable),
            ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable),
            ScriptedHandler.Status(HttpStatusCode.OK));
        int aliveChecks = 0;
        int opened = 0;

        await ServerReadiness.OpenWhenReadyAsync(
            new Uri("http://127.0.0.1:8080/"),
            () => ++aliveChecks <= 2,
            _ =>
            {
                opened++;
                return true;
            },
            _ => { },
            handler: handler,
            deadline: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(1),
            stillWaitingAfter: TimeSpan.FromMinutes(1));

        Assert.Equal(2, handler.Calls);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task LogsStillWaitingOnlyOnce()
    {
        ScriptedHandler handler = new(
            ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable),
            ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable));
        int aliveChecks = 0;
        List<string> logs = [];

        await ServerReadiness.OpenWhenReadyAsync(
            new Uri("http://127.0.0.1:8080/"),
            () => ++aliveChecks <= 3,
            _ => true,
            logs.Add,
            handler: handler,
            deadline: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(1),
            stillWaitingAfter: TimeSpan.Zero);

        Assert.Single(logs);
        Assert.Contains("still waiting", logs[0]);
    }

    [Fact]
    public async Task RetriesAfterConnectionFailure()
    {
        ScriptedHandler handler = new(
            ScriptedHandler.Fail(new HttpRequestException("connection refused")),
            ScriptedHandler.Status(HttpStatusCode.OK));
        int opened = 0;

        await ServerReadiness.OpenWhenReadyAsync(
            new Uri("http://127.0.0.1:8080/"),
            () => true,
            _ =>
            {
                opened++;
                return true;
            },
            _ => { },
            handler: handler,
            deadline: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(1),
            stillWaitingAfter: TimeSpan.FromMinutes(1));

        Assert.Equal(2, handler.Calls);
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task RetriesAfterRequestTimeout()
    {
        ScriptedHandler handler = new(
            ScriptedHandler.Fail(new TaskCanceledException("request timed out")),
            ScriptedHandler.Status(HttpStatusCode.OK));
        int opened = 0;

        await ServerReadiness.OpenWhenReadyAsync(
            new Uri("http://127.0.0.1:8080/"),
            () => true,
            _ =>
            {
                opened++;
                return true;
            },
            _ => { },
            handler: handler,
            deadline: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(1),
            stillWaitingAfter: TimeSpan.FromMinutes(1));

        Assert.Equal(2, handler.Calls);
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task UnexpectedFailuresAreNotHidden()
    {
        ScriptedHandler handler = new(ScriptedHandler.Fail(new InvalidOperationException("bad request")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ServerReadiness.OpenWhenReadyAsync(
                new Uri("http://127.0.0.1:8080/"),
                () => true,
                _ => true,
                _ => { },
                handler: handler,
                deadline: TimeSpan.FromSeconds(5),
                pollInterval: TimeSpan.FromMilliseconds(1),
                stillWaitingAfter: TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task StopsAfterBrowserOpenFails()
    {
        ScriptedHandler handler = new(ScriptedHandler.Status(HttpStatusCode.OK));

        await ServerReadiness.OpenWhenReadyAsync(
            new Uri("http://127.0.0.1:8080/"),
            () => true,
            _ => false,
            _ => { },
            handler: handler,
            deadline: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(1),
            stillWaitingAfter: TimeSpan.FromMinutes(1));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void SplitLauncherFlags_DetectsAndStripsExactFlag()
    {
        (bool noBrowser, List<string> serverArgs) = LauncherArgs.SplitLauncherFlags(
            ["-c", "10", "--no-browser", "-t", "4"]);

        Assert.True(noBrowser);
        Assert.Equal(["-c", "10", "-t", "4"], serverArgs);
    }

    [Theory]
    [InlineData("--no-browser=true")]
    [InlineData("--no-browser=false")]
    [InlineData("--No-Browser")]
    public void SplitLauncherFlags_LeavesVariantsForLlamaServer(string variant)
    {
        (bool noBrowser, List<string> serverArgs) = LauncherArgs.SplitLauncherFlags([variant]);

        Assert.False(noBrowser);
        Assert.Equal([variant], serverArgs);
    }

    [Fact]
    public void ReadArgsFile_LockedFile_ReturnsEmptyInsteadOfThrowing()
    {
        string dir = NewTempDir();
        try
        {
            string path = Path.Combine(dir, "model.args");
            File.WriteAllText(path, "--host 127.0.0.1" + Environment.NewLine);

            using FileStream locked = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Empty(LauncherArgs.ReadArgsFile(path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DescribeLine_ExplainsTheIndexedLineOrReturnsEmpty()
    {
        string[] lines = ["# comment", "", "--host 0.0.0.0", "-c 4096"];

        Assert.Equal("", ArgumentNotes.DescribeLine(lines, -1));
        Assert.Equal("", ArgumentNotes.DescribeLine(lines, lines.Length));
        Assert.Equal("", ArgumentNotes.DescribeLine(lines, 0));
        Assert.Equal("", ArgumentNotes.DescribeLine(lines, 1));
        Assert.StartsWith("--host: ", ArgumentNotes.DescribeLine(lines, 2));
        Assert.StartsWith("-c: ", ArgumentNotes.DescribeLine(lines, 3));
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gguf-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<Task<HttpResponseMessage>>> _steps = new();

        public ScriptedHandler(params Func<Task<HttpResponseMessage>>[] steps)
        {
            foreach (Func<Task<HttpResponseMessage>> step in steps)
            {
                _steps.Enqueue(step);
            }
        }

        public int Calls { get; private set; }

        public Uri? LastUri { get; private set; }

        public static Func<Task<HttpResponseMessage>> Status(HttpStatusCode status)
        {
            return () => Task.FromResult(new HttpResponseMessage(status));
        }

        public static Func<Task<HttpResponseMessage>> Fail(Exception exception)
        {
            return () => Task.FromException<HttpResponseMessage>(exception);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;

            if (_steps.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return _steps.Dequeue()();
        }
    }
}
