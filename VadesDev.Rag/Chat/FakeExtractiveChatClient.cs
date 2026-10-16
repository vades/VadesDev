using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace VadesDev.Rag.Chat;

/// <summary>
/// Fake "LLM": reads the grounded prompt and returns the first context line.
/// No intelligence, it only proves that retrieval and prompt grounding work.
/// </summary>
public sealed class FakeExtractiveChatClient : IChatClient
{
    /// <summary>
    /// Returns the complete answer for the latest user message.
    /// </summary>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var userText = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";

        var firstSource = userText
            .Split('\n', StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.StartsWith("[1]"));

        var answer = firstSource is null
            ? "I don't know."
            : $"(fake LLM) Based on the context: {firstSource[3..].Trim()}";

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
    }

    /// <summary>
    /// Returns the same answer as one single streaming update.
    /// </summary>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);

        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}