// Path: VadesDev.Rag/Agents/RagAgentFactory.cs
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

// Path: VadesDev.Rag/Chat/FakeExtractiveChatClient.cs
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

// Path: VadesDev.Rag/Cli/ConsoleRunner.cs
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
    /// Asks one question and prints a short message if the AI provider fails, instead of ending the program.
    /// </summary>
    private async Task TryAskAsync(string question)
    {
        try
        {
            await _pipeline.AskAsync(question);
        }
        catch (Exception exception) when (ProviderErrors.IsProviderError(exception))
        {
            Console.WriteLine($"  ! {ProviderErrors.Describe(exception)}");
        }
    }
}

// Path: VadesDev.Rag/Cli/IngestionPipeline.cs
using VadesDev.Rag.Documents;
using VadesDev.Rag.Models;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;

namespace VadesDev.Rag.Cli;

/// <summary>
/// One file from disk to the database: read, chunk, sanitize and fingerprint it,
/// then skip it if it is unchanged, or embed it and store it.
/// </summary>
public class IngestionPipeline
{
    private readonly MarkdownFileReader _reader;
    private readonly ChunkIndexer _indexer;
    private readonly PostgresChunkStore _store;
    private readonly string _embeddingModel;

    public IngestionPipeline(
        MarkdownFileReader reader, ChunkIndexer indexer, PostgresChunkStore store, string embeddingModel)
    {
        _reader = reader;
        _indexer = indexer;
        _store = store;
        _embeddingModel = embeddingModel;
    }

    /// <summary>
    /// Makes sure the file is stored for the current embedding model. Skips the embedding if nothing changed.
    /// </summary>
    public async Task IngestAsync(string path)
    {
        var document = await _reader.ReadAsync(path);
        var chunks = PrepareChunks(document);

        if (chunks.Length == 0)
        {
            Console.WriteLine($"No chunks found in {document.Source}, skipped.");
            return;
        }

        var sourceHash = ChunkHasher.Compute(chunks);

        if (await _store.IsUpToDateAsync(_embeddingModel, document.Source, sourceHash))
        {
            Console.WriteLine($"Up to date: {document.Source} ({chunks.Length} chunks), embedding skipped.");
            return;
        }

        await EmbedAndStoreAsync(chunks, sourceHash);
    }

    /// <summary>
    /// Embeds the chunks and stores them together with their fingerprint.
    /// </summary>
    private async Task EmbedAndStoreAsync(DocumentChunk[] chunks, string sourceHash)
    {
        Console.WriteLine($"Indexing {chunks[0].Source}...");

        var index = await _indexer.BuildIndexAsync(chunks);
        await _store.ReplaceChunksAsync(_embeddingModel, sourceHash, index);

        Console.WriteLine($"Stored {index.Count} chunks ({index[0].Vector.Length} dimensions).");
    }

    /// <summary>
    /// Splits the document into chunks first (the chunker needs the # markers), then sanitizes each chunk.
    /// </summary>
    private static DocumentChunk[] PrepareChunks(SourceDocument document)
    {
        return DocumentChunker.Chunk(document)
            .Select(c => c with { Text = TextSanitizer.Sanitize(c.Text) })
            .ToArray();
    }
}

// Path: VadesDev.Rag/Cli/ProviderErrors.cs
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

// Path: VadesDev.Rag/Cli/RagPipeline.cs
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

// Path: VadesDev.Rag/Configuration/AppConfiguration.cs
using Microsoft.Extensions.Configuration;

namespace VadesDev.Rag.Configuration;

/// <summary>
/// Builds the app configuration: appsettings.json, then appsettings.Local.json (secrets), then environment variables.
/// </summary>
public static class AppConfiguration
{
    /// <summary>
    /// Builds the configuration. Later sources override earlier ones.
    /// </summary>
    public static IConfiguration Build()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    /// <summary>
    /// Reads one section into a settings record, or throws a clear error if the section is missing.
    /// </summary>
    public static T GetRequired<T>(IConfiguration configuration, string sectionName) where T : class
    {
        return configuration.GetSection(sectionName).Get<T>()
               ?? throw new InvalidOperationException($"Section '{sectionName}' is missing in appsettings.json.");
    }
}

// Path: VadesDev.Rag/Configuration/GeminiSettings.cs
namespace VadesDev.Rag.Configuration;

/// <summary>
/// Gemini settings: OpenAI-compatible endpoint, model names and the MinScore calibrated for the embedding model.
/// The API key is not part of it, so it never appears in logs or in ToString().
/// </summary>
public record GeminiSettings(string Endpoint, string ChatModel, string EmbeddingModel, float MinScore);

// Path: VadesDev.Rag/Configuration/OllamaSettings.cs
namespace VadesDev.Rag.Configuration;

/// <summary>
/// Ollama settings: address of the local server, model names and the MinScore calibrated for the embedding model.
/// A local server needs no API key.
/// </summary>
public record OllamaSettings(string Endpoint, string ChatModel, string EmbeddingModel, float MinScore);

// Path: VadesDev.Rag/Configuration/RagSettings.cs
namespace VadesDev.Rag.Configuration;

/// <summary>
/// Search settings: the minimum similarity score to accept a chunk (MinScore, depends on the embedding model)
/// and how many chunks to keep (TopK).
/// </summary>
public record RagSettings(float MinScore, int TopK = 2);

// Path: VadesDev.Rag/Documents/DocumentChunker.cs
using System.Text.RegularExpressions;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Documents;

/// <summary>
/// Splits a document into chunks: one chunk per paragraph, with each heading joined to the paragraph below it.
/// </summary>
public static partial class DocumentChunker
{
    /// <summary>
    /// Returns one chunk per paragraph (headings joined), with ids like "knowledge.md-1".
    /// </summary>
    public static DocumentChunk[] Chunk(SourceDocument document)
    {
        var paragraphs = SplitParagraphs(document.Text);
        var joined = JoinHeadings(paragraphs);

        return [.. joined.Select((text, i) => new DocumentChunk($"{document.Source}-{i + 1}", document.Source, text))];
    }

    /// <summary>
    /// Splits a text into paragraphs at blank lines. Works for \n and \r\n.
    /// </summary>
    private static string[] SplitParagraphs(string text)
    {
        return BlankLineRegex()
            .Split(text)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Joins each heading paragraph with the paragraph that follows it.
    /// </summary>
    private static List<string> JoinHeadings(string[] paragraphs)
    {
        var result = new List<string>();

        for (int i = 0; i < paragraphs.Length; i++)
        {
            var isJoinable = IsHeading(paragraphs[i]) && i + 1 < paragraphs.Length;

            result.Add(isJoinable ? $"{paragraphs[i]}\n{paragraphs[i + 1]}" : paragraphs[i]);

            if (isJoinable)
            {
                i++;
            }
        }

        return result;
    }

    /// <summary>
    /// A heading is a paragraph of one line that starts with #.
    /// </summary>
    private static bool IsHeading(string paragraph)
    {
        return paragraph.StartsWith('#') && !paragraph.Contains('\n');
    }

    [GeneratedRegex(@"\r?\n[^\S\r\n]*\r?\n")]
    private static partial Regex BlankLineRegex();
}

// Path: VadesDev.Rag/Documents/MarkdownFileReader.cs
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Documents;

/// <summary>
/// Reads one Markdown file and returns its raw, unchanged text.
/// </summary>
public class MarkdownFileReader
{
    /// <summary>
    /// Reads the file at the given path and returns its file name and text.
    /// </summary>
    public async Task<SourceDocument> ReadAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path);

        return new SourceDocument(Path.GetFileName(path), text);
    }
}

// Path: VadesDev.Rag/Documents/TextSanitizer.cs
using System.Text.RegularExpressions;

namespace VadesDev.Rag.Documents;

/// <summary>
/// Cleans a text before indexing: removes links, Markdown symbols, control characters and repeated spaces.
/// </summary>
public static partial class TextSanitizer
{
    /// <summary>
    /// Applies all cleaning rules in order and returns the cleaned text.
    /// </summary>
    public static string Sanitize(string text)
    {
        var withoutLinks = RemoveLinks(text);
        var withoutSymbols = RemoveMarkdownSymbols(withoutLinks);
        var withoutDangerousCharacters = RemoveDangerousCharacters(withoutSymbols);

        return CollapseWhitespace(withoutDangerousCharacters);
    }

    /// <summary>
    /// Replaces Markdown links [text](url) with just their text, and removes raw URLs.
    /// </summary>
    private static string RemoveLinks(string text)
    {
        var withoutMarkdownLinks = MarkdownLinkRegex().Replace(text, "$1");

        return RawUrlRegex().Replace(withoutMarkdownLinks, "");
    }

    /// <summary>
    /// Removes Markdown symbols but keeps the words: heading markers (#), bold/italic markers (** and __) and backticks.
    /// </summary>
    private static string RemoveMarkdownSymbols(string text)
    {
        var withoutHeadingMarkers = HeadingMarkerRegex().Replace(text, "");

        return EmphasisMarkerRegex().Replace(withoutHeadingMarkers, "");
    }

    /// <summary>
    /// Removes control characters (except new lines and tabs) and zero-width characters.
    /// </summary>
    private static string RemoveDangerousCharacters(string text)
    {
        var cleaned = text.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t')
                          .Where(c => c is not ('\u200B' or '\u200C' or '\u200D' or '\uFEFF'));

        return new string(cleaned.ToArray());
    }

    /// <summary>
    /// Collapses repeated spaces and tabs into one space and trims each line.
    /// </summary>
    private static string CollapseWhitespace(string text)
    {
        var collapsed = RepeatedSpacesRegex().Replace(text, " ");
        var lines = collapsed.Split('\n').Select(line => line.Trim());

        return string.Join("\n", lines);
    }

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex RawUrlRegex();

    [GeneratedRegex(@"^#{1,6}[^\S\r\n]*", RegexOptions.Multiline)]
    private static partial Regex HeadingMarkerRegex();

    [GeneratedRegex(@"\*\*|__|`")]
    private static partial Regex EmphasisMarkerRegex();

    [GeneratedRegex(@"[^\S\r\n]+")]
    private static partial Regex RepeatedSpacesRegex();
}

// Path: VadesDev.Rag/Embeddings/Fnv1aHasher.cs
namespace VadesDev.Rag.Embeddings;

/// <summary>
/// Stable FNV-1a hash: the same word gives the same number on every run (string.GetHashCode() changes per process).
/// </summary>
public static class Fnv1AHasher
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    /// <summary>
    /// Returns the FNV-1a hash of a text.
    /// </summary>
    public static uint Hash(string text)
    {
        uint hash = FnvOffsetBasis;

        foreach (char c in text)
        {
            hash = (hash ^ c) * FnvPrime;
        }

        return hash;
    }
}

// Path: VadesDev.Rag/Embeddings/HashingEmbeddingGenerator.cs
using System.Numerics.Tensors;
using Microsoft.Extensions.AI;

namespace VadesDev.Rag.Embeddings;

/// <summary>
/// Fake embedding model: "hashed bag of words". Each word is hashed to one of 4096 slots and counted.
/// Similar WORDS give similar vectors (real models also match similar MEANING).
/// </summary>
public sealed class HashingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int Dimensions = 4096;

    /// <summary>
    /// Creates one embedding for each text, in the same order.
    /// </summary>
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var text in values)
        {
            result.Add(new Embedding<float>(Embed(text)));
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Builds the vector of one text: counts each word in its hashed slot, then scales the vector to length 1.
    /// </summary>
    private static float[] Embed(string text)
    {
        var vector = new float[Dimensions];

        foreach (var word in TextTokenizer.Tokenize(text))
        {
            var slot = (int)(Fnv1AHasher.Hash(word) % Dimensions);
            vector[slot] += 1f;
        }

        ScaleToUnitLength(vector);
        return vector;
    }

    /// <summary>
    /// L2 normalization: scales the vector to length 1, so the dot product equals the cosine similarity.
    /// </summary>
    private static void ScaleToUnitLength(float[] vector)
    {
        float norm = TensorPrimitives.Norm<float>(vector);

        if (norm > 0)
        {
            TensorPrimitives.Divide(vector, norm, vector);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}

// Path: VadesDev.Rag/Embeddings/TextTokenizer.cs
using System.Text.RegularExpressions;

namespace VadesDev.Rag.Embeddings;

/// <summary>
/// Splits a text into lowercase words, removes stop words and reduces plurals (days -> day).
/// </summary>
public static partial class TextTokenizer
{
    private static readonly HashSet<string> StopWords =
    [
        "the","is","are","a","an","of","to","on","in","your","our","you","do","does","what",
        "how","can","i","from","and","or","for","it","we","us","using",
        "who","when","where","why","which","long","much","many"
    ];

    /// <summary>
    /// Returns the normalized words of a text, without stop words.
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        var words = new List<string>();

        foreach (Match match in WordRegex().Matches(text.ToLowerInvariant()))
        {
            if (!StopWords.Contains(match.Value))
            {
                words.Add(Normalize(match.Value));
            }
        }

        return words;
    }

    /// <summary>
    /// Crude plural stemmer: It removes a trailing s from words longer than 3 characters, so hours becomes hour while is and bus stay unchanged.
    /// </summary>
    private static string Normalize(string word)
    {
        return word.Length > 3 && word.EndsWith('s') ? word[..^1] : word;
    }

    /// <summary>
    /// Compile-time generated regex: finds lowercase words and digits.
    /// </summary>
    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordRegex();
}

// Path: VadesDev.Rag/Knowledge/DummyKnowledgeBase.cs
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Knowledge;

/// <summary>
/// Dummy knowledge base: returns a few hardcoded chunks instead of reading real documents.
/// </summary>
public class DummyKnowledgeBase
{
    /// <summary>
    /// Returns all chunks of the knowledge base.
    /// </summary>
    public DocumentChunk[] GetChunks()
    {
        return
        [
            new("faq-1", "refund-policy.md", "Our refund window is strictly 30 days from the purchase date."),
            new("faq-2", "support.md",       "Customer support hours are Monday through Friday, 9 AM to 5 PM CET."),
            new("faq-3", "account.md",       "To reset your password click 'Forgot Password' on the login screen and check your email inbox."),
            new("faq-4", "shipping.md",      "Standard shipping takes 3 to 5 business days within the EU."),
            new("faq-5", "security.md",      "We support two-factor authentication using an authenticator app.")
        ];
    }
}

// Path: VadesDev.Rag/Models/DocumentChunk.cs
namespace VadesDev.Rag.Models;

/// <summary>
/// One piece of knowledge: its id, the file it came from, and its text.
/// </summary>
public record DocumentChunk(string Id, string Source, string Text);

// Path: VadesDev.Rag/Models/IndexedChunk.cs
namespace VadesDev.Rag.Models;

/// <summary>
/// A chunk together with its embedding vector. This is what the search keeps in memory.
/// </summary>
public record IndexedChunk(DocumentChunk Chunk, ReadOnlyMemory<float> Vector);

// Path: VadesDev.Rag/Models/SearchHit.cs
namespace VadesDev.Rag.Models;

/// <summary>
/// A chunk that matched a question, together with its cosine similarity score (higher = more similar).
/// </summary>
public record SearchHit(DocumentChunk Chunk, float Score);

// Path: VadesDev.Rag/Models/SourceDocument.cs
namespace VadesDev.Rag.Models;

/// <summary>
/// The result of reading one file: the file name and its text.
/// </summary>
public record SourceDocument(string Source, string Text);

// Path: VadesDev.Rag/Program.cs
using VadesDev.Rag.Agents;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Documents;
using VadesDev.Rag.Prompting;
using VadesDev.Rag.Providers;
using VadesDev.Rag.Search;
using VadesDev.Rag.Storage;

var configuration = AppConfiguration.Build();
var provider = new AiProviderFactory(configuration).Create();
Console.WriteLine($"Provider: {provider.Name} (model {provider.EmbeddingModel}, MinScore {provider.MinScore})");

// Phase 5: the vector database (PostgreSQL + pgvector).
await using var dataSource = PostgresDataSourceFactory.Create(configuration);
var store = new PostgresChunkStore(dataSource);
await store.EnsureSchemaAsync();

// Read, chunk, sanitize, embed and store the file, unless it is unchanged.
var path = Path.Combine(AppContext.BaseDirectory, "Data", "knowledge.md");
var indexer = new ChunkIndexer(provider.EmbeddingGenerator);
await new IngestionPipeline(new MarkdownFileReader(), indexer, store, provider.EmbeddingModel).IngestAsync(path);
Console.WriteLine();

var retriever = new ChunkRetriever(store, provider.EmbeddingModel, new RagSettings(provider.MinScore));
var agent = new RagAgentFactory().Create(provider.ChatClient);
var pipeline = new RagPipeline(provider.EmbeddingGenerator, retriever, new GroundedPromptBuilder(), agent);

await new ConsoleRunner(pipeline).RunAsync();

// Path: VadesDev.Rag/Prompting/GroundedPromptBuilder.cs
using System.Text;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Prompting;

/// <summary>
/// Builds the grounded prompt: numbered sources from the hits, followed by the question.
/// </summary>
public class GroundedPromptBuilder
{
    /// <summary>
    /// Returns the prompt text: "Context:", one numbered line per hit, then "Question: ...".
    /// </summary>
    public string Build(string question, List<SearchHit> hits)
    {
        var prompt = new StringBuilder().AppendLine("Context:");

        for (int n = 0; n < hits.Count; n++)
        {
            prompt.AppendLine($"[{n + 1}] {hits[n].Chunk.Text} (source: {hits[n].Chunk.Source})");
        }

        prompt.AppendLine().AppendLine($"Question: {question}");

        return prompt.ToString();
    }
}

// Path: VadesDev.Rag/Providers/AiProvider.cs
using Microsoft.Extensions.AI;

namespace VadesDev.Rag.Providers;

/// <summary>
/// Everything the pipeline needs from one AI provider: name, chat client, embedding generator,
/// the name of the embedding model and the MinScore calibrated for that model.
/// </summary>
public record AiProvider(
    string Name,
    IChatClient ChatClient,
    IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator,
    string EmbeddingModel,
    float MinScore);

// Path: VadesDev.Rag/Providers/AiProviderFactory.cs
using Microsoft.Extensions.Configuration;
using VadesDev.Rag.Configuration;

namespace VadesDev.Rag.Providers;

/// <summary>
/// Builds the AI provider selected in the configuration (Rag:Provider): chat client, embedder, model name and MinScore.
/// </summary>
public class AiProviderFactory
{
    private readonly IConfiguration _configuration;

    public AiProviderFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Creates the provider named in Rag:Provider. Throws a clear error for an unknown name.
    /// </summary>
    public AiProvider Create()
    {
        var name = _configuration["Rag:Provider"] ?? "";

        return name.ToLowerInvariant() switch
        {
            "ollama" => CreateOllama(),
            "gemini" => CreateGemini(),
            _ => throw new InvalidOperationException($"Unknown provider '{name}'. Use 'Gemini' or 'Ollama'.")
        };
    }

    /// <summary>
    /// Builds the Gemini provider from the Gemini section and the API key.
    /// </summary>
    private AiProvider CreateGemini()
    {
        var settings = AppConfiguration.GetRequired<GeminiSettings>(_configuration, "Gemini");
        var factory = new GeminiClientFactory(settings, _configuration["Gemini:ApiKey"] ?? "");

        return new AiProvider(
            "Gemini",
            factory.CreateChatClient(),
            factory.CreateEmbeddingGenerator(),
            settings.EmbeddingModel,
            settings.MinScore);
    }

    /// <summary>
    /// Builds the Ollama provider from the Ollama section.
    /// </summary>
    private AiProvider CreateOllama()
    {
        var settings = AppConfiguration.GetRequired<OllamaSettings>(_configuration, "Ollama");
        var factory = new OllamaClientFactory(settings);

        return new AiProvider(
            "Ollama",
            factory.CreateChatClient(),
            factory.CreateEmbeddingGenerator(),
            settings.EmbeddingModel,
            settings.MinScore);
    }
}

// Path: VadesDev.Rag/Providers/GeminiClientFactory.cs
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using VadesDev.Rag.Configuration;

namespace VadesDev.Rag.Providers;

/// <summary>
/// Creates the Gemini clients (chat and embeddings) through Google's OpenAI-compatible endpoint.
/// </summary>
public class GeminiClientFactory
{
    private readonly GeminiSettings _settings;
    private readonly OpenAIClient _client;

    public GeminiClientFactory(GeminiSettings settings, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _settings = settings;
        _client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint) });
    }

    /// <summary>
    /// Creates the chat client for the model named in the settings.
    /// </summary>
    public IChatClient CreateChatClient()
    {
        return _client.GetChatClient(_settings.ChatModel).AsIChatClient();
    }

    /// <summary>
    /// Creates the embedding generator for the model named in the settings.
    /// </summary>
    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator()
    {
        return _client.GetEmbeddingClient(_settings.EmbeddingModel).AsIEmbeddingGenerator();
    }
}

// Path: VadesDev.Rag/Providers/OllamaClientFactory.cs
using Microsoft.Extensions.AI;
using OllamaSharp;
using VadesDev.Rag.Configuration;

namespace VadesDev.Rag.Providers;

/// <summary>
/// Creates the Ollama clients (chat and embeddings) for a local Ollama server.
/// </summary>
public class OllamaClientFactory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    private readonly OllamaSettings _settings;

    public OllamaClientFactory(OllamaSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Creates the chat client for the model named in the settings.
    /// </summary>
    public IChatClient CreateChatClient()
    {
        return CreateClient(_settings.ChatModel);
    }

    /// <summary>
    /// Creates the embedding generator for the model named in the settings.
    /// </summary>
    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator()
    {
        return CreateClient(_settings.EmbeddingModel);
    }

    /// <summary>
    /// Creates an Ollama client for one model, with a long timeout because a local model can need time to load.
    /// </summary>
    private OllamaApiClient CreateClient(string model)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.Endpoint),
            Timeout = RequestTimeout
        };

        return new OllamaApiClient(httpClient, model);
    }
}

// Path: VadesDev.Rag/Search/ChunkHasher.cs
using System.Security.Cryptography;
using System.Text;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Search;

/// <summary>
/// Computes a fingerprint (SHA-256) of a set of chunks. If a chunk id or text changes, the fingerprint changes.
/// </summary>
public static class ChunkHasher
{
    /// <summary>
    /// Returns the SHA-256 hash of all chunk ids and texts, as a lowercase hex string.
    /// </summary>
    public static string Compute(DocumentChunk[] chunks)
    {
        var text = string.Join('\0', chunks.Select(c => $"{c.Id}\0{c.Text}"));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        return Convert.ToHexStringLower(bytes);
    }
}

// Path: VadesDev.Rag/Search/ChunkIndexer.cs
using Microsoft.Extensions.AI;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Search;

/// <summary>
/// Builds the index: embeds every chunk and pairs it with its vector.
/// </summary>
public class ChunkIndexer
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embedder;

    public ChunkIndexer(IEmbeddingGenerator<string, Embedding<float>> embedder)
    {
        _embedder = embedder;
    }

    /// <summary>
    /// Embeds all chunks in one batch and returns them together with their vectors.
    /// </summary>
    public async Task<List<IndexedChunk>> BuildIndexAsync(DocumentChunk[] chunks)
    {
        var embeddings = await _embedder.GenerateAsync(chunks.Select(c => c.Text));

        return [.. chunks.Zip(embeddings, (chunk, embedding) => new IndexedChunk(chunk, embedding.Vector))];
    }
}

// Path: VadesDev.Rag/Search/ChunkRetriever.cs
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Models;
using VadesDev.Rag.Storage;

namespace VadesDev.Rag.Search;

/// <summary>
/// Searches the stored chunks: asks the database for the nearest chunks of one embedding model
/// and keeps the hits that reach MinScore (best first, at most TopK).
/// </summary>
public class ChunkRetriever
{
    private readonly PostgresChunkStore _store;
    private readonly string _embeddingModel;
    private readonly RagSettings _settings;

    public ChunkRetriever(PostgresChunkStore store, string embeddingModel, RagSettings settings)
    {
        _store = store;
        _embeddingModel = embeddingModel;
        _settings = settings;
    }

    /// <summary>
    /// Returns the best chunks for a question vector: above MinScore, highest score first, at most TopK.
    /// </summary>
    public async Task<List<SearchHit>> SearchAsync(ReadOnlyMemory<float> queryVector)
    {
        var nearest = await _store.SearchAsync(_embeddingModel, queryVector, _settings.TopK);

        return nearest.Where(h => h.Score >= _settings.MinScore).ToList();
    }
}

// Path: VadesDev.Rag/Storage/PostgresChunkStore.cs
using Npgsql;
using Pgvector;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Storage;

/// <summary>
/// Stores chunks and their vectors in PostgreSQL (schema rag) and finds the nearest chunks for a question vector.
/// Vectors from different embedding models share one table and are kept apart by the model name.
/// </summary>
public class PostgresChunkStore
{
    private const string EnsureSchemaSql = """
        CREATE TABLE IF NOT EXISTS rag.chunks (
            embedding_model text   NOT NULL,
            id              text   NOT NULL,
            source          text   NOT NULL,
            content         text   NOT NULL,
            embedding       vector NOT NULL,
            PRIMARY KEY (embedding_model, id)
        );
        ALTER TABLE rag.chunks ADD COLUMN IF NOT EXISTS source_hash text;
        """;

    private const string IsUpToDateSql = """
        SELECT EXISTS (
            SELECT 1 FROM rag.chunks
            WHERE embedding_model = $1 AND source = $2 AND source_hash = $3)
        """;

    private const string DeleteSql =
        "DELETE FROM rag.chunks WHERE embedding_model = $1 AND source = ANY($2)";

    private const string InsertSql =
        "INSERT INTO rag.chunks (embedding_model, id, source, content, embedding, source_hash) VALUES ($1, $2, $3, $4, $5, $6)";

    private const string SearchSql = """
        SELECT id, source, content, 1 - (embedding <=> $1) AS similarity
        FROM rag.chunks
        WHERE embedding_model = $2 AND vector_dims(embedding) = $3
        ORDER BY embedding <=> $1
        LIMIT $4
        """;

    private readonly NpgsqlDataSource _dataSource;

    public PostgresChunkStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>
    /// Creates the table rag.chunks (and its source_hash column) if they do not exist yet. Call once at startup.
    /// </summary>
    public async Task EnsureSchemaAsync()
    {
        await using var command = _dataSource.CreateCommand(EnsureSchemaSql);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// True if this source is already stored for this embedding model with exactly this fingerprint.
    /// </summary>
    public async Task<bool> IsUpToDateAsync(string embeddingModel, string source, string sourceHash)
    {
        await using var command = _dataSource.CreateCommand(IsUpToDateSql);
        command.Parameters.AddWithValue(embeddingModel);
        command.Parameters.AddWithValue(source);
        command.Parameters.AddWithValue(sourceHash);

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Replaces the stored chunks of one source for one embedding model and saves the fingerprint, in a single transaction.
    /// All chunks must belong to the same source.
    /// </summary>
    public async Task ReplaceChunksAsync(string embeddingModel, string sourceHash, List<IndexedChunk> chunks)
    {
        var sources = chunks.Select(c => c.Chunk.Source).Distinct().ToArray();

        if (sources.Length != 1)
        {
            throw new ArgumentException("The chunks must belong to exactly one source.", nameof(chunks));
        }

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await DeleteSourcesAsync(connection, transaction, embeddingModel, sources);

        foreach (var chunk in chunks)
        {
            await InsertChunkAsync(connection, transaction, embeddingModel, sourceHash, chunk);
        }

        await transaction.CommitAsync();
    }

    /// <summary>
    /// Returns the nearest chunks of one embedding model, best first, with similarity = 1 - cosine distance.
    /// Applies no MinScore: that decision belongs to the retriever.
    /// </summary>
    public async Task<List<SearchHit>> SearchAsync(string embeddingModel, ReadOnlyMemory<float> queryVector, int limit)
    {
        await using var command = _dataSource.CreateCommand(SearchSql);
        command.Parameters.AddWithValue(new Vector(queryVector));
        command.Parameters.AddWithValue(embeddingModel);
        command.Parameters.AddWithValue(queryVector.Length);
        command.Parameters.AddWithValue(limit);

        var hits = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var chunk = new DocumentChunk(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            hits.Add(new SearchHit(chunk, (float)reader.GetDouble(3)));
        }

        return hits;
    }

    /// <summary>
    /// Deletes the existing rows of the given sources for one embedding model.
    /// </summary>
    private static async Task DeleteSourcesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string embeddingModel, string[] sources)
    {
        await using var command = new NpgsqlCommand(DeleteSql, connection, transaction);
        command.Parameters.AddWithValue(embeddingModel);
        command.Parameters.AddWithValue(sources);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts one chunk with its vector and the fingerprint of its source.
    /// </summary>
    private static async Task InsertChunkAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string embeddingModel, string sourceHash, IndexedChunk chunk)
    {
        await using var command = new NpgsqlCommand(InsertSql, connection, transaction);
        command.Parameters.AddWithValue(embeddingModel);
        command.Parameters.AddWithValue(chunk.Chunk.Id);
        command.Parameters.AddWithValue(chunk.Chunk.Source);
        command.Parameters.AddWithValue(chunk.Chunk.Text);
        command.Parameters.AddWithValue(new Vector(chunk.Vector));
        command.Parameters.AddWithValue(sourceHash);
        await command.ExecuteNonQueryAsync();
    }
}

// Path: VadesDev.Rag/Storage/PostgresDataSourceFactory.cs
using Microsoft.Extensions.Configuration;
using Npgsql;
using Pgvector.Npgsql;

namespace VadesDev.Rag.Storage;

/// <summary>
/// Creates the PostgreSQL data source for the vector database, with pgvector support enabled.
/// </summary>
public static class PostgresDataSourceFactory
{
    /// <summary>
    /// Reads ConnectionStrings:Rag and builds the data source. Throws a clear error if the connection string is missing.
    /// </summary>
    public static NpgsqlDataSource Create(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rag");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Rag is missing. Set it in appsettings.Local.json or the ConnectionStrings__Rag environment variable.");
        }

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();

        return builder.Build();
    }
}

