using VadesDev.Rag.Agents;
using VadesDev.Rag.Chat;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Documents;
using VadesDev.Rag.Embeddings;
using VadesDev.Rag.Prompting;
using VadesDev.Rag.Search;

// Fakes behind the real Microsoft.Extensions.AI types. Phase 4 swaps only these two lines.
var embedder = new HashingEmbeddingGenerator();
var chatClient = new FakeExtractiveChatClient();

// Phase 3: read the Markdown file, chunk it, then sanitize each chunk.
var path = Path.Combine(AppContext.BaseDirectory, "Data", "knowledge.md");
var document = await new MarkdownFileReader().ReadAsync(path);
var chunks = DocumentChunker.Chunk(document)
    .Select(c => c with { Text = TextSanitizer.Sanitize(c.Text) })
    .ToArray();

Console.WriteLine("Indexing...");
var index = await new ChunkIndexer(embedder).BuildIndexAsync(chunks);
Console.WriteLine($"Indexed {index.Count} chunks ({index[0].Vector.Length} dimensions).\n");

var retriever = new ChunkRetriever(index, new RagSettings());
var agent = new RagAgentFactory().Create(chatClient);
var pipeline = new RagPipeline(embedder, retriever, new GroundedPromptBuilder(), agent);

await new ConsoleRunner(pipeline).RunAsync();