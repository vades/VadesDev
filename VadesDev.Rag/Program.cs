using VadesDev.Rag.Agents;
using VadesDev.Rag.Chat;
using VadesDev.Rag.Cli;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Embeddings;
using VadesDev.Rag.Knowledge;
using VadesDev.Rag.Prompting;
using VadesDev.Rag.Search;

// Phase 2 wiring: fakes behind the real Microsoft.Extensions.AI types.
// Phase 3 swaps only the embedder and the chat client.
var embedder = new HashingEmbeddingGenerator();
var chatClient = new FakeExtractiveChatClient();

Console.WriteLine("Indexing...");
var chunks = new DummyKnowledgeBase().GetChunks();
var index = await new ChunkIndexer(embedder).BuildIndexAsync(chunks);
Console.WriteLine($"Indexed {index.Count} chunks ({index[0].Vector.Length} dimensions).\n");

var retriever = new ChunkRetriever(index, new RagSettings());
var agent = new RagAgentFactory().Create(chatClient);
var pipeline = new RagPipeline(embedder, retriever, new GroundedPromptBuilder(), agent);

await new ConsoleRunner(pipeline).RunAsync();

/*var agent = new RagAgentFactory().Create(new FakeExtractiveChatClient());
Console.WriteLine(agent.Name);

var prompt = "Context:\n[1] Customer support hours are Monday through Friday. (source: support.md)\n\nQuestion: What are your support hours?";
var response = await agent.RunAsync(prompt);
Console.WriteLine(response.Text);*/

/*var prompt = "Context:\n[1] Customer support hours are Monday through Friday. (source: support.md)\n\nQuestion: What are your support hours?";

IChatClient client = new FakeExtractiveChatClient();
var response = await client.GetResponseAsync(prompt);
Console.WriteLine(response.Text);

var empty = await client.GetResponseAsync("Question: Do you sell pizza?");
Console.WriteLine(empty.Text);*/

/*var embedder = new HashingEmbeddingGenerator();
var vector = await embedder.GenerateVectorAsync("What are your support hours?");
Console.WriteLine(vector.Length);
Console.WriteLine(TensorPrimitives.Norm(vector.Span));

var empty = await embedder.GenerateVectorAsync("What is the?");
Console.WriteLine(TensorPrimitives.Norm(empty.Span));*/

/*Console.WriteLine(Fnv1AHasher.Hash(""));
Console.WriteLine(Fnv1AHasher.Hash("a"));*/

/*var words = TextTokenizer.Tokenize("What are your support hours?");
Console.WriteLine(string.Join(", ", words));
Console.WriteLine(string.Join(", ", TextTokenizer.Tokenize("Standard shipping takes 3 to 5 business days within the EU.")));*/

/*var knowledgeBase = new DummyKnowledgeBase();
var chunks = knowledgeBase.GetChunks();
Console.WriteLine(chunks.Length);
Console.WriteLine(chunks[0]);*/

/*var chunk = new DocumentChunk("faq-1", "refund-policy.md", "Test");
var indexed = new IndexedChunk(chunk, new float[] { 0.1f, 0.2f, 0.3f });
var hit = new SearchHit(chunk, 0.85f);
var (c, score) = hit;

var defaults = new RagSettings();
var custom = new RagSettings(TopK: 3);
Console.WriteLine(defaults);
Console.WriteLine(custom.TopK);

Console.WriteLine(indexed.Chunk.Id);
Console.WriteLine(indexed.Vector.Length);
Console.WriteLine($"{c.Id} {score:F3}");*/