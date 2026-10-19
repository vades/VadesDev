using Microsoft.Extensions.Configuration;
using VadesDev.Rag.Agents;
using VadesDev.Rag.Chat;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Documents;
using VadesDev.Rag.Prompting;
using VadesDev.Rag.Providers;
using VadesDev.Rag.Search;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("Gemini").Get<GeminiSettings>()
               ?? throw new InvalidOperationException("Section 'Gemini' is missing in appsettings.json.");

var factory = new GeminiClientFactory(settings, configuration["Gemini:ApiKey"] ?? "");
var embedder = factory.CreateEmbeddingGenerator();

var path = Path.Combine(AppContext.BaseDirectory, "Data", "knowledge.md");
var document = await new MarkdownFileReader().ReadAsync(path);
var chunks = DocumentChunker.Chunk(document)
    .Select(c => c with { Text = TextSanitizer.Sanitize(c.Text) })
    .ToArray();

Console.WriteLine("Indexing...");
var index = await new ChunkIndexer(embedder).BuildIndexAsync(chunks);
Console.WriteLine($"Indexed {index.Count} chunks ({index[0].Vector.Length} dimensions).\n");

var retriever = new ChunkRetriever(index, new RagSettings());
var agent = new RagAgentFactory().Create(new FakeExtractiveChatClient());           // fake LLM, no quota used
var pipeline = new RagPipeline(embedder, retriever, new GroundedPromptBuilder(), agent);

await new ConsoleRunner(pipeline).RunAsync();