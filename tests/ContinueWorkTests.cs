using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

public class ContinueWorkTests
{
    [Fact]
    public void Continuation_KeepsBareLaunchInTheUiAndModelPathsHeadless()
    {
        Assert.Equal(AppMode.Ui, Program.GetMode([]));
        Assert.Equal(AppMode.Launch, Program.GetMode([@"C:\models\a.gguf"]));
        Assert.Equal(AppMode.Launch, Program.GetMode(["-m", @"C:\models\a.gguf"]));
    }
}
