using VadesDev.Rag.Models;
using VadesDev.Rag.Prompting;

namespace VadesDev.Rag.Tests.Prompting;

/// <summary>
/// Tests for GroundedPromptBuilder: header, numbered sources, one question after the sources, no scores.
/// </summary>
public class GroundedPromptBuilderTests
{
    /// <summary>
    /// Creates a search hit with a fixed id, so each test only writes the text, the source and the score.
    /// </summary>
    private static SearchHit Hit(string text, string source, float score = 0.5f)
    {
        return new SearchHit(new DocumentChunk("id-1", source, text), score);
    }

    /// <summary>
    /// Builds the prompt and splits it into lines. The builder uses AppendLine, so the line break
    /// is Environment.NewLine (\r\n on Windows). The last element is the empty text after the final line break.
    /// </summary>
    private static string[] BuildLines(string question, params SearchHit[] hits)
    {
        var prompt = new GroundedPromptBuilder().Build(question, [.. hits]);

        return prompt.Split(Environment.NewLine);
    }

    /// <summary>
    /// The first line is "Context:".
    /// </summary>
    [Fact]
    public void Build_StartsWithContextHeader()
    {
        var lines = BuildLines("Q?", Hit("Some text.", "a.md"));

        Assert.Equal("Context:", lines[0]);
    }

    /// <summary>
    /// A source line looks like "[1] text (source: file)".
    /// </summary>
    [Fact]
    public void Build_FormatsEachSourceLine()
    {
        var lines = BuildLines("Q?", Hit("Customer support hours are Monday through Friday.", "support.md"));

        Assert.Equal("[1] Customer support hours are Monday through Friday. (source: support.md)", lines[1]);
    }

    /// <summary>
    /// Sources are numbered from 1, in the order of the hits (best first).
    /// </summary>
    [Fact]
    public void Build_NumbersSourcesFromOneInHitOrder()
    {
        var lines = BuildLines("Q?", Hit("Best.", "a.md"), Hit("Second.", "b.md"), Hit("Third.", "c.md"));

        Assert.Equal("[1] Best. (source: a.md)", lines[1]);
        Assert.Equal("[2] Second. (source: b.md)", lines[2]);
        Assert.Equal("[3] Third. (source: c.md)", lines[3]);
    }

    /// <summary>
    /// "Question:" appears exactly once, after the last source. With several hits this catches the old mistake
    /// of adding the question inside the loop over the hits.
    /// </summary>
    [Fact]
    public void Build_PutsQuestionOnceAfterAllSources()
    {
        var lines = BuildLines("What are your support hours?", Hit("One.", "a.md"), Hit("Two.", "b.md"));

        var questionLine = Assert.Single(lines, l => l.StartsWith("Question:"));

        Assert.Equal("Question: What are your support hours?", questionLine);
        Assert.True(Array.IndexOf(lines, questionLine) > Array.FindLastIndex(lines, l => l.StartsWith('[')));
    }

    /// <summary>
    /// One blank line separates the last source from the question.
    /// </summary>
    [Fact]
    public void Build_SeparatesSourcesAndQuestionWithBlankLine()
    {
        var lines = BuildLines("Q?", Hit("One.", "a.md"), Hit("Two.", "b.md"));

        Assert.Equal(["Context:", "[1] One. (source: a.md)", "[2] Two. (source: b.md)", "", "Question: Q?", ""], lines);
    }

    /// <summary>
    /// Without hits the prompt has only the header, a blank line and the question.
    /// </summary>
    [Fact]
    public void Build_WithoutHitsContainsOnlyHeaderAndQuestion()
    {
        var lines = BuildLines("Q?");

        Assert.Equal(["Context:", "", "Question: Q?", ""], lines);
    }

    /// <summary>
    /// The similarity score is not part of the prompt.
    /// </summary>
    [Fact]
    public void Build_DoesNotIncludeScores()
    {
        var prompt = new GroundedPromptBuilder().Build("Q?", [Hit("Some text.", "a.md", 0.123f)]);

        Assert.DoesNotContain("123", prompt);
    }

    /// <summary>
    /// A chunk text with a line break (a heading joined with its paragraph) is inserted unchanged.
    /// The fake chat client only reads the first of these lines, which is a known limit of the fake.
    /// </summary>
    [Fact]
    public void Build_KeepsMultiLineChunkTextUnchanged()
    {
        var prompt = new GroundedPromptBuilder().Build("Q?", [Hit("Support\nHours are 9 to 5.", "a.md")]);

        Assert.Contains("[1] Support\nHours are 9 to 5. (source: a.md)", prompt);
    }
}