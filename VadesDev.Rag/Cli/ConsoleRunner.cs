using System.ClientModel;

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
            await TryAskAsync(question);
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
            await TryAskAsync(input);
        }
    }

    /// <summary>
    /// Asks one question and prints a short message if the LLM request fails, instead of ending the program.
    /// </summary>
    private async Task TryAskAsync(string question)
    {
        try
        {
            await _pipeline.AskAsync(question);
        }
        catch (ClientResultException exception)
        {
            Console.WriteLine($"  ! {GetErrorMessage(exception)}");
        }
    }

    /// <summary>
    /// Turns an HTTP error from the LLM into one short, readable line.
    /// </summary>
    private static string GetErrorMessage(ClientResultException exception)
    {
        return exception.Status == 429
            ? "LLM request failed: rate limit or daily quota reached (HTTP 429). Try again later."
            : $"LLM request failed (HTTP {exception.Status}). Check the API key, model name, endpoint and network.";
    }
}