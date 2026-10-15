namespace VadesDev.Rag.Cli;

/// <summary>
/// Runs the console session: demo questions first, then an interactive loop until an empty line.
/// </summary>
public class ConsoleRunner
{
    private static readonly string[] DemoQuestions =
    [
        "What are your support hours?",
        "How do I reset my password?",
        "Do you sell pizza?"            // out of scope on purpose
    ];

    private readonly RagPipeline _pipeline;

    public ConsoleRunner(RagPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <summary>
    /// Runs the whole session: demo questions, then the interactive loop.
    /// </summary>
    public async Task RunAsync()
    {
        await RunDemoQuestionsAsync();
        await RunInteractiveLoopAsync();
    }

    /// <summary>
    /// Asks each demo question in order.
    /// </summary>
    private async Task RunDemoQuestionsAsync()
    {
        foreach (var question in DemoQuestions)
        {
            await _pipeline.AskAsync(question);
        }
    }

    /// <summary>
    /// Reads questions from the console until the user enters an empty line.
    /// </summary>
    private async Task RunInteractiveLoopAsync()
    {
        Console.WriteLine("\nType your own question (empty line = exit):");

        while (Console.ReadLine() is { Length: > 0 } input)
        {
            await _pipeline.AskAsync(input);
        }
    }
}