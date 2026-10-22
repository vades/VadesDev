# Developer Guide

How `VadesDev.Rag` works, how to test it and how to extend it. For the overview and quick start, see [README.md](README.md).

---

## 1. The pipeline in one picture

```
 INDEXING (once, at startup)
 Data/knowledge.md
   -> MarkdownFileReader      raw text + file name         (SourceDocument)
   -> DocumentChunker         one chunk per paragraph      (DocumentChunk[])
   -> TextSanitizer           clean each chunk's text
   -> ChunkIndexer            embed all chunks, pair them  (List<IndexedChunk>)

 ASKING (for every question, in RagPipeline.AskAsync)
 question
   -> embedder                question -> vector
   -> ChunkRetriever          cosine similarity, MinScore, TopK   (List<SearchHit>)
   -> guardrail               no hits => "I don't know", no LLM call
   -> GroundedPromptBuilder   numbered sources + question
   -> agent (LLM)             answers only from the sources
```

`Program.cs` only wires these objects together and starts `ConsoleRunner`. It contains no logic.

**Order matters.** The chunker runs *before* the sanitizer, because it needs the `#` markers to join a heading with the paragraph below it. The sanitizer then removes the symbols from each finished chunk.

---

## 2. Classes by folder

Every class has one job. Namespaces follow the folders (`VadesDev.Rag.<Folder>`).

### Models: plain data (records)

| Type | Holds |
|---|---|
| `DocumentChunk(Id, Source, Text)` | One piece of knowledge, for example `knowledge.md-2` |
| `IndexedChunk(Chunk, Vector)` | A chunk with its embedding vector (`ReadOnlyMemory<float>`) |
| `SearchHit(Chunk, Score)` | A matching chunk with its cosine similarity |
| `SourceDocument(Source, Text)` | What a file reader returns: file name and text |

### Configuration

| Class | Job |
|---|---|
| `AppConfiguration` | `Build()` creates the layered configuration. `GetRequired<T>(configuration, section)` binds a section or throws a clear error |
| `RagSettings(MinScore, TopK = 2)` | Search settings. `MinScore` has no default on purpose, see section 6 |
| `GeminiSettings`, `OllamaSettings` | Endpoint, chat model, embedding model and the calibrated `MinScore`. The API key is **not** part of them |

### Documents: from file to chunks

| Class | Job |
|---|---|
| `MarkdownFileReader` | `ReadAsync(path)` returns the raw text and the file name. No cleaning |
| `DocumentChunker` (static) | `Chunk(document)` splits at blank lines (works for `\n` and `\r\n`) and joins each one-line heading with the paragraph after it. Ids look like `knowledge.md-1` |
| `TextSanitizer` (static) | `Sanitize(text)` applies four rules in order: remove links, remove Markdown symbols (`#`, `**`, `__`, backticks), remove control and zero-width characters, collapse spaces and trim lines. It works on a plain string, so it can serve other formats later |

### Embeddings: the fake embedder (offline)

| Class | Job |
|---|---|
| `HashingEmbeddingGenerator` | Fake `IEmbeddingGenerator`: a hashed bag of words with 4096 slots, scaled to length 1. Matches similar *words*, not similar *meaning* |
| `TextTokenizer` (static) | Lowercase, split into words, drop stop words, strip a plural `s` |
| `Fnv1aHasher` (static) | Stable hash, so the same word always lands in the same slot on every run |

### Search

| Class | Job |
|---|---|
| `ChunkIndexer` | `BuildIndexAsync(chunks)` embeds all chunk texts in one batch and pairs each chunk with its vector |
| `ChunkRetriever` | Takes the index and `RagSettings` in the constructor. `Search(queryVector)` scores every chunk, keeps scores at or above `MinScore`, sorts best first and takes `TopK` |

### Prompting, Agents, Chat

| Class | Job |
|---|---|
| `GroundedPromptBuilder` | `Build(question, hits)` returns `Context:`, one line per hit as `[1] text (source: file)`, then `Question: ...` |
| `RagAgentFactory` | `Create(IChatClient)` returns a `ChatClientAgent` named `RagAgent`. Its instructions: answer only from the context, otherwise say you don't know |
| `FakeExtractiveChatClient` | Fake `IChatClient` that returns the first `[1]` line of the prompt. Needs no model, only proves that retrieval and grounding work |

### Providers: real AI services

| Class | Job |
|---|---|
| `OllamaClientFactory` | Creates chat and embedding clients with OllamaSharp. Each uses its own `HttpClient` with a 5-minute timeout, because a local model can need time to load |
| `GeminiClientFactory` | Creates both clients with the OpenAI SDK against Google's OpenAI-compatible endpoint. Rejects an empty API key |
| `AiProvider` | Record that travels together: name, chat client, embedding generator and the `MinScore` of that embedding model |
| `AiProviderFactory` | Reads `Rag:Provider` and builds the right `AiProvider`. Only the selected provider's settings (and key) are read |

### Cli

| Class | Job |
|---|---|
| `RagPipeline` | `AskAsync(question)` runs one full round trip and prints the hits and the answer |
| `ConsoleRunner` | Runs the demo questions, then an interactive loop until an empty line. Catches provider errors per question |
| `ProviderErrors` (static) | `IsProviderError` recognizes HTTP errors, no connection and timeouts. `Describe` returns one readable line |

`Knowledge/DummyKnowledgeBase` returns five hardcoded chunks. It has been unused since Phase 3.

---

## 3. How the pieces are wired (`Program.cs`)

```
AppConfiguration.Build()
  -> AiProviderFactory.Create()        -> AiProvider (chat client, embedder, MinScore)
MarkdownFileReader -> DocumentChunker -> TextSanitizer (per chunk)
  -> ChunkIndexer(provider.EmbeddingGenerator)
  -> ChunkRetriever(index, new RagSettings(provider.MinScore))
  -> RagAgentFactory.Create(provider.ChatClient)
  -> RagPipeline(...) -> ConsoleRunner(...).RunAsync()
```

Nothing after the `AiProvider` knows whether Ollama or Gemini is behind it. The embedder and chat client are typed with the framework interfaces `IEmbeddingGenerator<string, Embedding<float>>` and `IChatClient`.

---

## 4. Running and configuring

Run with `dotnet run` from the project folder. Do **not** use `dotnet watch`: the interactive input does not work well, and every file save restarts the app and repeats the demo questions, which costs LLM requests.

**Configuration layers** (later layers win):

1. `appsettings.json`: provider choice, endpoints, model names, `MinScore` per provider
2. `appsettings.Local.json`: gitignored, for secrets such as `Gemini:ApiKey`
3. Environment variables: a double underscore replaces the colon

```powershell
$env:Rag__Provider = "Gemini"           # switch provider for this terminal
$env:Ollama__ChatModel = "qwen3:4b"     # override any single value
Remove-Item Env:Rag__Provider           # remove it again
```

**Secrets**
- Keep `appsettings.Local.json` in `.gitignore` (the repository root `.gitignore` covers all projects).
- Check with `git check-ignore -v VadesDev.Rag/appsettings.Local.json`.
- Ignoring a file does not untrack it. If a key was ever committed, treat it as leaked and create a new one.
- Settings records never contain the key, so printing them is safe.

**Files copied to the output folder** (in the `.csproj`): `Data/knowledge.md`, `appsettings.json`, `appsettings.Local.json`. If a file is not found at run time, check `bin/Debug/net10.0/` first.

---

## 5. Testing

There is no test project yet (a first xUnit project is a candidate for Phase 6). Until then, each class is tested with a short snippet in a temporary `Program.cs`. Keep your working `Program.cs` somewhere safe while you do this.

### 5.1 Quick checks per class (no network)

```csharp
// TextTokenizer: stop words removed, plurals reduced
Console.WriteLine(string.Join(", ", TextTokenizer.Tokenize("What are your support hours?")));
// support, hour

// Fnv1aHasher: stable on every run
Console.WriteLine(Fnv1aHasher.Hash(""));    // 2166136261
Console.WriteLine(Fnv1aHasher.Hash("a"));   // 3826002220

// TextSanitizer
Console.WriteLine(TextSanitizer.Sanitize("# Support\nCall **us** at `9 AM`, see issue #5."));
// Support
// Call us at 9 AM, see issue #5.

// GroundedPromptBuilder
var chunk = new DocumentChunk("faq-2", "support.md", "Customer support hours are Monday through Friday.");
Console.WriteLine(new GroundedPromptBuilder().Build("What are your support hours?", [new SearchHit(chunk, 0.5f)]));
```

Chunker check with the real file:

```csharp
var path = Path.Combine(AppContext.BaseDirectory, "Data", "knowledge.md");
var document = await new MarkdownFileReader().ReadAsync(path);

foreach (var chunk in DocumentChunker.Chunk(document))
{
    Console.WriteLine($"[{chunk.Id}] {chunk.Text}\n---");
}
```

With the five-section sample file you should see five chunks, each heading joined to its paragraph.

### 5.2 Test the whole pipeline offline

Use the fakes. This needs no network, no model and no API key:

```csharp
var embedder = new HashingEmbeddingGenerator();
var index = await new ChunkIndexer(embedder).BuildIndexAsync(chunks);
var retriever = new ChunkRetriever(index, new RagSettings(MinScore: 0.20f));
var agent = new RagAgentFactory().Create(new FakeExtractiveChatClient());
var pipeline = new RagPipeline(embedder, retriever, new GroundedPromptBuilder(), agent);

await new ConsoleRunner(pipeline).RunAsync();
```

The fake chat client answers with the first line of the first source, which is now the heading. That is a limit of the fake. Read the `retrieved [...] score=...` lines, not the answer text.

### 5.3 Calibrate `MinScore` (required for every new embedding model)

Scores have a different range for every embedding model, so the threshold must be measured, not guessed.

1. Build the pipeline with the **real embedder** and the **fake chat client** (saves quota and waiting time).
2. Set `new RagSettings(TopK: 3, MinScore: 0f)` so nothing is filtered and all scores are printed.
3. Ask 5 to 10 questions the file **can** answer (include paraphrases without shared keywords), and 3 to 5 it **cannot** answer.
4. Write down the top-1 score of each question.
5. Pick `MinScore` in the gap: above the strongest false match and below the weakest correct match. Stay closer to the false side, because losing a correct answer is worse than sending one extra chunk.
6. Put the value in `appsettings.json` and rerun with the guardrail on. Out-of-scope questions must print `! No relevant context found.`

Results from this project (5 chunks, `knowledge.md`):

| | Gemini | Ollama |
|---|---|---|
| Embedding model | `gemini-embedding-001` | `embeddinggemma:latest` |
| Vector size | 3072 | 768 |
| Weakest correct top-1 | 0.657 | 0.462 |
| Strongest false top-1 | 0.554 | 0.305 |
| **MinScore chosen** | **0.60** | **0.40** |

Recalibrate when the knowledge base grows or the embedding model changes. The small samples here are a starting point, not proof.

### 5.4 Test with the real LLM

- Ask in-scope questions (answer comes from the chunk) and out-of-scope questions (guardrail answers without a call).
- Also try a question that **finds** a chunk but whose answer is **not** in it (for example, shipping cost when the file only gives shipping time). The model should say it doesn't know. This checks whether the model obeys the instructions.
- Gemini free tier: daily chat limits are small, so keep runs short. Embeddings were not a problem.
- Ollama: the first call after a pause includes model loading. A thinking model such as `qwen3:4b` needs roughly 20 seconds or more per answer on modest hardware.

### 5.5 Test the error handling

- **Stop Ollama while the app waits for input**, then ask a question. Expected: one `! Cannot reach the AI server...` line and the prompt again.
- **Wrong model name:** `$env:Ollama__ChatModel = "qwen3:4bx"`. If this ends in a stack trace instead of a `!` line, note the exception type from the first line and add it to `ProviderErrors`.
- **Unknown provider:** `$env:Rag__Provider = "Foo"` stops with a message that lists the valid names.

---

## 6. Design decisions and why

- **No interfaces of our own.** The pipeline uses the framework interfaces `IChatClient` and `IEmbeddingGenerator`, which is enough to swap providers. A learning project does not need more layers.
- **`MinScore` belongs to the embedding model**, not to the app. It lives in each provider's settings and `RagSettings.MinScore` has no default, so no provider can use another one's value.
- **The index is returned, not stored inside the indexer.** `ChunkIndexer` returns a list and `Program.cs` passes it to `ChunkRetriever`. There is no hidden shared state, and Phase 5 replaces only these two classes.
- **The sanitizer knows no format.** Format-specific work (stripping HTML tags, extracting PDF text) belongs to the reader of that format. The sanitizer handles what all formats share.
- **The guardrail runs before the LLM.** It saves quota and time, and the answer does not depend on the model obeying its prompt.
- **Narrow error handling.** Only provider errors are caught (`when` filter), so real bugs still crash with a stack trace.
- **Reasoning stays out of the answer.** `qwen3:4b` returns its thinking as a separate `TextReasoningContent`. `response.Text` contains only the answer. Do not send `think=false` to this model: it then writes the reasoning into the answer text.
- **Prompt numbering must stay `[1]`, `[2]`, ...** The fake chat client looks for the `[1]` line.

---

## 7. How to extend

### Add a new AI provider

1. `Configuration/<Name>Settings.cs`: a record with `Endpoint`, `ChatModel`, `EmbeddingModel`, `MinScore`.
2. `appsettings.json`: a new section with the values.
3. `Providers/<Name>ClientFactory.cs`: two methods, `CreateChatClient()` and `CreateEmbeddingGenerator()`, returning the framework interfaces. Keep the SDK details inside this class.
4. `AiProviderFactory`: one new `case` in `Create()` and one private method that builds the `AiProvider`.
5. **Calibrate `MinScore`** (section 5.3) and put the result in the settings.

Nothing else changes: the pipeline already works with any `IChatClient` and `IEmbeddingGenerator`.

### Add a new file format (PDF, HTML, ...)

1. Add a reader class next to `MarkdownFileReader` with the same shape: `ReadAsync(path)` returns a `SourceDocument`.
2. Do format-specific work inside the reader (extract PDF text, strip HTML tags).
3. In `Program.cs`, use the new reader. The chunker and sanitizer stay unchanged.
4. Add a sample file and check the chunk output (section 5.1).

### Change the chunking

Change only `DocumentChunker` and keep `Chunk(SourceDocument) -> DocumentChunk[]`. Possible next steps: a maximum chunk size, overlap, or merging very short paragraphs.

---

## 8. Conventions

- Folder = pipeline stage or feature. One type per file. File-scoped namespaces. No `Services`, `Helpers` or `Utils` folders.
- `Program.cs` is wiring only.
- Records for data. Dependencies are passed through the constructor into `private readonly _camelCase` fields.
- Classes are `public`. Stateless classes are `static`. Framework implementations are `sealed`.
- Every class and method has an XML `<summary>` comment (it shows as a tooltip in the IDE).
- Methods stay at about 20 to 30 lines. Split them into private methods with one job each.
- `Async` suffix on async methods. `var` for obvious types. `""` instead of `string.Empty`. Collection expressions (`[..]`).
- Materialize LINQ once with `ToList()` or `ToArray()`.
- No secrets in code.

---

## 9. Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| `NU1605` package downgrade | A package needs a newer `Microsoft.Extensions.AI.Abstractions` than your direct reference. Raise the version in the `.csproj` to the one named in the error and restart |
| `CS1061` for `GenerateVectorAsync` / `GetResponseAsync(string)` | Missing `using Microsoft.Extensions.AI;`. These are extension methods |
| `CS0103` for a variable or `Stopwatch` | A variable from an earlier `Program.cs` is missing, or `using System.Diagnostics;` is missing |
| `FileNotFoundException` for `knowledge.md` or `appsettings.json` | The file is not copied to `bin`. Check the `CopyToOutputDirectory` entry in the `.csproj` |
| `ArgumentException` about an empty key | `Gemini:ApiKey` is missing. Set it in `appsettings.Local.json` or `Gemini__ApiKey` |
| `Unknown provider '...'` | `Rag:Provider` is not `Ollama` or `Gemini` |
| HTTP 429 from Gemini | Per-minute or per-day quota used up. Check the usage page in Google AI Studio, wait, or use another model. The daily quota resets around midnight Pacific time |
| `HttpClient.Timeout of 100 seconds` | A client without our longer timeout. The Ollama factory sets 5 minutes. A cold model start can still be slow |
| `HttpRequestException` at `Indexing...` | The AI server is not running (for Ollama, start it). Errors during a question are shown as one `!` line, but a failure during startup indexing still ends with a stack trace (planned for Phase 6) |
| `Indexed 3 chunks` when the file has 5 sections | The updated `knowledge.md` was not copied to `bin`. Rebuild and check `bin/Debug/net10.0/Data/` |
| Everything is rejected after switching embedding model | `MinScore` is calibrated for another model. Recalibrate (section 5.3) |
| Fake answer shows only a heading | Limit of `FakeExtractiveChatClient`: it returns the first line of the first source. The retrieval itself is correct |
| Interactive input does not react | You are using `dotnet watch`. Use `dotnet run` |
| Slow answers with Ollama | Thinking model plus a CPU/GPU split. `ollama ps` shows where the model runs. A smaller or non-thinking model is a configuration change |

Useful Ollama commands: `ollama list`, `ollama ps`, `ollama stop <model>` (unload a model). Check the server with `Invoke-RestMethod http://localhost:11434`.

---

## 10. What comes next

- **Phase 5, PostgreSQL and pgvector.** Only `ChunkIndexer` and `ChunkRetriever` change, and `Search(queryVector)` keeps its signature. Vector size depends on the embedding model (768 or 3072 here), so the table must match the model, or you need one table per model. Index size limits for pgvector must be checked in the current documentation before choosing the column type. No repository pattern: a concrete store class.
- **Phase 6, CLI MVP.** Read a folder of documents, index once and reuse, handle a startup failure with a clear message, consider the first test project.
- **Phase 7, FastEndpoints.** `RagPipeline.AskAsync` should return the answer and hits instead of printing, so the CLI and the API can share it. Provider errors map to HTTP status codes.