using System.ClientModel;

namespace VadesDev.Rag.Cli;

/// <summary>
/// Recognizes errors that come from the AI provider (HTTP errors, connection problems, timeouts)
/// and describes each one in a single readable line.
/// </summary>
public static class ProviderErrors
{
    /// <summary>
    /// True for errors that should not end the program: HTTP errors, no connection, timeout.
    /// </summary>
    public static bool IsProviderError(Exception exception)
    {
        return exception is ClientResultException
            or HttpRequestException
            or TaskCanceledException
            or TimeoutException;
    }

    /// <summary>
    /// Returns one short message for a provider error, with a hint what to check.
    /// </summary>
    public static string Describe(Exception exception)
    {
        return exception switch
        {
            ClientResultException { Status: 429 } => "Rate limit or daily quota reached (HTTP 429). Try again later.",
            ClientResultException e => $"LLM request failed (HTTP {e.Status}). Check the API key, model name, endpoint and network.",
            HttpRequestException => "Cannot reach the AI server. Check that Ollama is running, or that you are online.",
            TaskCanceledException or TimeoutException => "The AI server did not answer in time. The model may still be loading, try again.",
            _ => exception.Message
        };
    }
}