using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

/// <summary>
/// Every argument the launcher writes, and the flags users commonly add, gets an explanatory
/// note in the quick-pick window; blank and comment lines have no note.
/// </summary>
public class ArgumentInfoNoteTests
{
    [Fact]
    public void EveryArgumentInTheFirstRunPreviewHasANote()
    {
        string dir = NewTempDir();
        try
        {
            string modelPath = Path.Combine(dir, "model.gguf");
            File.WriteAllText(modelPath, "");
            string preview = LauncherArgs.RenderArgsFile(
                LaunchPlanner.Create(dir, modelPath, [], null).FinalArgs);

            List<string> argumentLines = preview
                .Split(Environment.NewLine)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToList();

            Assert.NotEmpty(argumentLines);
            foreach (string line in argumentLines)
            {
                string note = ArgumentNotes.Describe(line);
                Assert.StartsWith(line.Split(' ')[0] + ": ", note);
                Assert.DoesNotContain(ArgumentNotes.UnknownNote, note);
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("--temperature 0.8")]
    [InlineData("-ctk q8_0")]
    [InlineData("--mmproj vision.gguf")]
    [InlineData("--spec-draft-model draft.gguf")]
    [InlineData("--alias my-model")]
    [InlineData("--no-browser")]
    [InlineData("--seed 42")]
    [InlineData("--lora adapter.bin")]
    public void CommonUserFlagsHaveANote(string line)
    {
        string note = ArgumentNotes.Describe(line);

        Assert.StartsWith(line.Split(' ')[0] + ": ", note);
        Assert.DoesNotContain(ArgumentNotes.UnknownNote, note);
    }

    [Fact]
    public void UnknownFlagFallsBackToThePassThroughNote()
    {
        string note = ArgumentNotes.Describe("--not-a-real-flag value");

        Assert.Equal("--not-a-real-flag: " + ArgumentNotes.UnknownNote, note);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# a comment")]
    [InlineData("98192")]
    public void BlankCommentAndValueOnlyLinesHaveNoNote(string line)
    {
        Assert.Equal("", ArgumentNotes.Describe(line));
    }

    [Fact]
    public void NoteDescribesTheFirstFlagOnTheLine()
    {
        string note = ArgumentNotes.Describe("-b 512 -ub 512");

        Assert.StartsWith("-b: ", note);
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gguf-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
