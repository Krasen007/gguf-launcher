using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

/// <summary>
/// Covers the args formatting used by the quick-pick window's args editor: one flag per row,
/// backslash continuations joined, comment lines preserved.
/// </summary>
public class ArgsConverterTests
{
    [Fact]
    public void Format_JoinsBackslashContinuation_OntoPreviousLine()
    {
        string result = ArgsConverter.FormatArgs("--alias Kestrel-4B-Coder \\\n--spec-type draft-mtp");
        Assert.Equal("--alias Kestrel-4B-Coder" + Environment.NewLine + "--spec-type draft-mtp", result);
    }

    [Fact]
    public void Format_HandlesWindowsCrlfContinuation()
    {
        string result = ArgsConverter.FormatArgs("--host 127.0.0.1 \\\r\n--port 8080");
        Assert.Equal("--host 127.0.0.1" + Environment.NewLine + "--port 8080", result);
    }

    [Fact]
    public void Format_MultipleContinuations_AreAllJoined()
    {
        string result = ArgsConverter.FormatArgs("-c 10 \\\n-b 5 \\\n-t 2");
        Assert.Equal("-c 10" + Environment.NewLine + "-b 5" + Environment.NewLine + "-t 2", result);
    }

    [Fact]
    public void Format_PreservesQuotedValuesAndPaths()
    {
        string result = ArgsConverter.FormatArgs(
            "--model \"C:\\Models\\my model.gguf\" \\\n--alias \"My Model\"");
        Assert.Equal("--model \"C:\\Models\\my model.gguf\"" + Environment.NewLine + "--alias \"My Model\"", result);
    }

    [Fact]
    public void Format_ConsumesLeadingWhitespaceOnContinuationLine()
    {
        string result = ArgsConverter.FormatArgs("--alias Kestrel-4B-Coder \\\n  --spec-type draft-mtp");
        Assert.Equal("--alias Kestrel-4B-Coder" + Environment.NewLine + "--spec-type draft-mtp", result);
    }

    [Fact]
    public void Format_BackslashInPath_NotFollowedByNewline_IsFormattedPerRow()
    {
        string result = ArgsConverter.FormatArgs("C:\\Models\\model.gguf -c 10");
        Assert.Equal("C:\\Models\\model.gguf" + Environment.NewLine + "-c 10", result);
    }

    [Fact]
    public void Format_TrailingBackslashAtEndOfFile_WithoutNewline_IsNotTouched()
    {
        string result = ArgsConverter.FormatArgs("-c 10 \\");
        Assert.Equal("-c 10 \\", result);
    }

    [Fact]
    public void Format_NoContinuation_StillRegroupsOneFlagPerRow()
    {
        string result = ArgsConverter.FormatArgs("-c 4096  -t 6");
        Assert.Equal("-c 4096" + Environment.NewLine + "-t 6", result);
    }

    [Fact]
    public void Format_RegularLineBreaks_ArePreservedAndRegrouped()
    {
        string result = ArgsConverter.FormatArgs("--host 0.0.0.0\n--port 8080");
        Assert.Equal("--host 0.0.0.0" + Environment.NewLine + "--port 8080", result);
    }

    [Fact]
    public void Format_CommentLines_ArePreserved()
    {
        string input = "# comment" + Environment.NewLine + "--host 0.0.0.0";
        string result = ArgsConverter.FormatArgs(input);
        Assert.Equal("# comment" + Environment.NewLine + "--host 0.0.0.0", result);
    }

    [Fact]
    public void Format_EmptyAndNull_ReturnEmpty()
    {
        Assert.Equal(string.Empty, ArgsConverter.FormatArgs(string.Empty));
        Assert.Equal(string.Empty, ArgsConverter.FormatArgs(null));
    }

    [Fact]
    public void Format_WhitespaceOnly_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ArgsConverter.FormatArgs("   \r\n   "));
    }
    [Fact]
    public void Format_PutsEachFlagOnItsOwnRow()
    {
        string input = "--alias Kestrel-4B-Coder --spec-type draft-mtp --spec-draft-n-max 2 --ctx-size 8192";
        string result = ArgsConverter.FormatArgs(input);
        Assert.Equal(
            "--alias Kestrel-4B-Coder" + Environment.NewLine +
            "--spec-type draft-mtp" + Environment.NewLine +
            "--spec-draft-n-max 2" + Environment.NewLine +
            "--ctx-size 8192", result);
    }
}