using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace VadesDev.Rag.Agents;

/// <summary>
/// Creates the RAG agent: wraps any chat client and sets the system prompt that forces grounded answers.
/// </summary>
public class RagAgentFactory
{
    private const string Instructions =
        "Answer using ONLY the provided context. If the context is insufficient, say you don't know.";

    /// <summary>
    /// Creates a ChatClientAgent around the given chat client.
    /// </summary>
    public AIAgent Create(IChatClient chatClient)
    {
        return new ChatClientAgent(
            chatClient,
            instructions: Instructions,
            name: "RagAgent");
    }
}