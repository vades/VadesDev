using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using VadesDev.Rag.Models;
using VadesDev.Rag.Prompting;
using VadesDev.Rag.Search;

namespace VadesDev.Rag.Cli;

/// <summary>
/// One full RAG round trip: embed the question, search, build the grounded prompt and ask the agent.
/// </summary>
public class RagPipeline
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embedder;
    private readonly ChunkRetriever _retriever;
    private readonly GroundedPromptBuilder _promptBuilder;
    private readonly AIAgent _agent;

    public RagPipeline(
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        ChunkRetriever retriever,
        GroundedPromptBuilder promptBuilder,
        AIAgent agent)
    {
        _embedder = embedder;
        _retriever = retriever;
        _promptBuilder = promptBuilder;
        _agent = agent;
    }

    /// <summary>
    /// Answers one question and prints the retrieved chunks and the answer to the console.
    /// </summary>
    public async Task AskAsync(string question)
    {
        Console.WriteLine($"\n? {question}");

        var queryVector = await _embedder.GenerateVectorAsync(question);
        var hits = await _retriever.SearchAsync(queryVector);
        PrintHits(hits);

        if (hits.Count == 0)
        {
            Console.WriteLine("  ! No relevant context found. Answer: I don't know.");
            return;
        }

        var prompt = _promptBuilder.Build(question, hits);
        var response = await _agent.RunAsync(prompt);
        Console.WriteLine($"  Answer: {response.Text}");
    }

    /// <summary>
    /// Prints one line per retrieved chunk: id, score and source file.
    /// </summary>
    private static void PrintHits(List<SearchHit> hits)
    {
        foreach (var (chunk, score) in hits)
        {
            Console.WriteLine($"  retrieved [{chunk.Id}] score={score:F3}  ({chunk.Source})");
        }
    }
}