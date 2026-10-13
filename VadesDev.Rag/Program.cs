// =====================================================================
// 01.SimpleRag  -  Phase 1: offline RAG with dummy data (no LLM, no API key)
// .NET 10 / C# 14 / Microsoft Agent Framework (MAF)
//
// Pipeline:  Chunk -> Embed -> Retrieve (cosine) -> Ground prompt -> Agent
// Everything "AI" is faked behind the REAL Microsoft.Extensions.AI interfaces,
// so in Phase 2 you only swap two lines (embedder + chat client).
// =====================================================================

using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// ---------- 1. Settings ----------------------------------------------
const int TopK = 2;             // how many chunks we feed to the agent
const float MinScore = 0.20f;   // below this => "I don't know" (no hallucination)

// ---------- 2. Dummy knowledge base (already "chunked") ---------------
// Hardcoded knowledge base: each entry is one self-contained chunk with id, source file and text.
DocumentChunk[] chunks =
[
    new("faq-1", "refund-policy.md",   "Our refund window is strictly 30 days from the purchase date."),
    new("faq-2", "support.md",         "Customer support hours are Monday through Friday, 9 AM to 5 PM CET."),
    new("faq-3", "account.md",         "To reset your password click 'Forgot Password' on the login screen and check your email inbox."),
    new("faq-4", "shipping.md",        "Standard shipping takes 3 to 5 business days within the EU."),
    new("faq-5", "security.md",        "We support two-factor authentication using an authenticator app.")
];

// ---------- 3. Wire up services (fakes behind real abstractions) ------
// Fakes behind the real Microsoft.Extensions.AI interfaces: Phase 2 swaps only these two lines.
IEmbeddingGenerator<string, Embedding<float>> embedder = new HashingEmbeddingGenerator();
IChatClient chatClient = new FakeExtractiveChatClient();

// MAF agent: ChatClientAgent wraps ANY IChatClient. Instructions = system prompt.
AIAgent agent = new ChatClientAgent(
    chatClient,
    instructions: "Answer using ONLY the provided context. If the context is insufficient, say you don't know.",
    name: "RagAgent");

// ---------- 4. INDEXING: embed every chunk once -----------------------
Console.WriteLine("Indexing...");
// Indexing: embed all chunk texts in one batch, then pair each chunk with its vector (order is preserved).
var chunkEmbeddings = await embedder.GenerateAsync(chunks.Select(c => c.Text));
List<IndexedChunk> index = [.. chunks.Zip(chunkEmbeddings, (c, e) => new IndexedChunk(c, e.Vector))];
Console.WriteLine($"Indexed {index.Count} chunks ({index[0].Vector.Length} dimensions).\n");

// ---------- 5. QUERY LOOP ---------------------------------------------
string[] demoQuestions =
[
    "What are your support hours?",
    "How do I reset my password?",
    "Do you sell pizza?"            // out of scope on purpose
];

foreach (var q in demoQuestions)
{
    await AskAsync(q);
}
    

Console.WriteLine("Type your own question (empty line = exit):");
while (Console.ReadLine() is { Length: > 0 } input)
    await AskAsync(input);

// ---------- Local function: one full RAG round trip -------------------
async Task AskAsync(string question)
{
    Console.WriteLine($"\n? {question}");

    // 5a. Embed the question with the SAME embedder used for the documents
    // Embed the question with the same embedder used for the chunks, so the vectors can be compared.
    var queryVector = await embedder.GenerateVectorAsync(question);
    

    // 5b. Score every chunk, keep the best TopK above the threshold
    // Score every chunk against the question vector (cosine similarity), keeping each chunk with its score.
    // Cosine similarity: 1 = same direction (very similar), 0 = nothing in common. Vectors must have equal length.
    var hits = index
        .Select(i => new SearchHit(i.Chunk, TensorPrimitives.CosineSimilarity(queryVector.Span, i.Vector.Span)))
        .Where(h => h.Score >= MinScore)
        .OrderByDescending(h => h.Score)
        .Take(TopK)
        .ToList();

    foreach (var (chunk, score) in hits)
    {
        Console.WriteLine($"  retrieved [{chunk.Id}] score={score:F3}  ({chunk.Source})");
    }
        

    // 5c. Guardrail: nothing relevant => don't even call the agent
    if (hits.Count == 0)
    {
        Console.WriteLine("  ! No relevant context found. Answer: I don't know.");
        return;
    }

    // 5d. Ground the prompt with numbered sources
    var prompt = new StringBuilder().AppendLine("Context:");
    for (int n = 0; n < hits.Count; n++)
    {
        prompt.AppendLine($"[{n + 1}] {hits[n].Chunk.Text} (source: {hits[n].Chunk.Source})");
        prompt.AppendLine().AppendLine($"Question: {question}");
    }
        

    // 5e. Run the MAF agent (here backed by the fake chat client)
    var response = await agent.RunAsync(prompt.ToString());
    Console.WriteLine($"  Answer: {response.Text}");
}

// =====================================================================
// Types (must stay below top-level statements)
// =====================================================================

record DocumentChunk(string Id, string Source, string Text);
record IndexedChunk(DocumentChunk Chunk, ReadOnlyMemory<float> Vector);

record SearchHit(DocumentChunk Chunk, float Score); 

/// <summary>
/// Fake embedding model: "hashed bag of words". Each word is hashed to one of
/// 256 slots and counted; the vector is L2-normalized.
/// Similar WORDS => similar vectors. (Real models also match similar MEANING -
/// that is exactly what you'll see in Phase 2.)
/// </summary>
sealed partial class HashingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int Dimensions = 4096;   // was 256: far fewer hash collisions

    private static readonly HashSet<string> StopWords =
    [
        "the","is","are","a","an","of","to","on","in","your","our","you","do","does","what",
        "how","can","i","from","and","or","for","it","we","us","using",
        "who","when","where","why","which","long","much","many"   // question words added
    ];
    
    // Crude plural stemmer: takes -> take, days -> day, hours -> hour
    private static string Normalize(string word) => word.Length > 3 && word.EndsWith('s') ? word[..^1] : word;

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

    private static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        // Bag of words: hash each non-stop word (after stemming) into one of Dimensions slots and count occurrences.
        foreach (Match m in WordRegex().Matches(text.ToLowerInvariant()))
        {
            if (!StopWords.Contains(m.Value))
            {
                var slot = (int)(Fnv1a(Normalize(m.Value)) % Dimensions);
                vector[slot] += 1f;
            }
        }

        // L2 normalize: scale the vector to length 1 (cosine works without it, but Dot then becomes equal to cosine)
        float norm = TensorPrimitives.Norm<float>(vector);

        if (norm > 0)
        {
            TensorPrimitives.Divide(vector, norm, vector);
        }
        return vector;
    }

    // Stable hash (string.GetHashCode() changes between process runs!)
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;
    
    // Stable FNV-1a hash: same word => same number on every run (string.GetHashCode() changes per process).
    private static uint Fnv1a(string s)
    {
        uint hash = FnvOffsetBasis;

        foreach (char c in s)
        {
            hash = (hash ^ c) * FnvPrime;
        }

        return hash;
    }
    // Compile-time generated regex: splits text into lowercase words and digits.
    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordRegex();

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Fake "LLM": reads the grounded prompt and returns the first context line.
/// No intelligence - it only proves that retrieval + prompt grounding work.
/// </summary>
sealed class FakeExtractiveChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        
        // Fake LLM: take the latest user message and find the first numbered source line ("[1] ...") in the grounded prompt.
        var userText = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";

        var firstSource = userText
            .Split('\n', StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.StartsWith("[1]"));

        var answer = firstSource is null
            ? "I don't know."
            : $"(fake LLM) Based on the context: {firstSource[3..].Trim()}";

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Fake streaming: get the full answer, then emit it as a single streaming update.
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}