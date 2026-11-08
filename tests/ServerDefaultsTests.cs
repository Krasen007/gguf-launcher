using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

public class ServerDefaultsTests
{
    [Fact]
    public void ServerDefaultArgs_WithoutArgsFile_BindsAllInterfacesOn8080()
    {
        IReadOnlyList<string> defaults = LauncherArgs.ServerDefaultArgs(hasPerModelArgs: false);

        Assert.Equal(["--host", "0.0.0.0", "--port", "8080"], defaults);
    }

    [Fact]
    public void ServerDefaultArgs_WithArgsFile_AddsNothing()
    {
        IReadOnlyList<string> defaults = LauncherArgs.ServerDefaultArgs(hasPerModelArgs: true);

        Assert.Empty(defaults);
    }

    [Fact]
    public void BrowserEndpoint_WildcardBindMapsToLoopback()
    {
        (string host, string port) = LauncherArgs.BrowserEndpoint(
            ["--host", "0.0.0.0", "--port", "8080"]);

        Assert.Equal("127.0.0.1", host);
        Assert.Equal("8080", port);
    }

    [Fact]
    public void BrowserEndpoint_WithoutHostPort_UsesLlamaServerDefaults()
    {
        (string host, string port) = LauncherArgs.BrowserEndpoint(["-c", "1234"]);

        Assert.Equal("127.0.0.1", host);
        Assert.Equal("8080", port);
    }

    [Fact]
    public void BrowserEndpoint_LaterHostPortWin()
    {
        (string host, string port) = LauncherArgs.BrowserEndpoint(
            ["--host", "0.0.0.0", "--port", "8080", "--host", "10.0.0.2", "--port", "7000"]);

        Assert.Equal("10.0.0.2", host);
        Assert.Equal("7000", port);
    }
}
