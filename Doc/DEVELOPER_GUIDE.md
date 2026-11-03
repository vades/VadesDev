# Developer Guide

How `VadesDev.Rag` works, how to test it and how to extend it. For the overview and quick start, see [README.md](README.md).

---

## 1. The pipeline in one picture

```
 INGESTION (once per start, IngestionPipeline.IngestAsync)
 Data/knowledge.md
   -> MarkdownFileReader      raw text + file name          (SourceDocument)
   -> DocumentChunker         one chunk per paragraph       (DocumentChunk[])
   -> TextSanitizer           clean each chunk's text
   -> ChunkHasher             fingerprint of the cleaned chunks
   -> store.IsUpToDateAsync   same fingerprint stored for this model? => stop, no embedding
   -> ChunkIndexer            embed all chunks              (List<IndexedChunk>)
   -> store.ReplaceChunksAsync  replace the rows of this source, one transaction (PostgreSQL)

 ASKING (for every question, RagPipeline.AskAsync)
 question
   -> embedder                question -> vector
   -> ChunkRetriever          store returns the TopK nearest rows; keep score >= MinScore   (List<SearchHit>)
   -> guardrail               no hits => "I don't know", no LLM call
   -> GroundedPromptBuilder   numbered sources + question
   -> agent (LLM)             answers only from the sources
```

`Program.cs` only wires these objects together and starts `ConsoleRunner`. It contains no logic.

**Order matters.** The chunker runs *before* the sanitizer, because it needs the `#` markers to join a heading with the paragraph below it. The sanitizer then removes the symbols from each finished chunk. The fingerprint is computed from the **sanitized** chunks.

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

The connection strings (`ConnectionStrings:Rag`, `ConnectionStrings:App`) are read directly from the configuration. They are not records, because they contain a password.

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
| `ChunkIndexer` | `BuildIndexAsync(chunks)` embeds all chunk texts in one batch and pairs each chunk with its vector. It does not store anything |
| `ChunkHasher` (static) | `Compute(chunks)` returns the SHA-256 of all chunk ids and texts (joined with `\0`, in order) as lowercase hex. The same chunks always give the same fingerprint |
| `ChunkRetriever` | Takes the store, the embedding model name and `RagSettings`. `SearchAsync(queryVector)` asks the store for the `TopK` nearest chunks of that model and keeps those with a score of at least `MinScore` |

### Storage: PostgreSQL

| Class | Job |
|---|---|
| `PostgresDataSourceFactory` (static) | `Create(configuration)` reads `ConnectionStrings:Rag`, enables the pgvector type mapping (`UseVector()`) and returns the `NpgsqlDataSource`. Create it once and dispose it at the end of the program |
| `PostgresChunkStore` | All SQL of the project. `EnsureSchemaAsync` creates the table, `IsUpToDateAsync` checks the fingerprint, `ReplaceChunksAsync` replaces one source in one transaction, `SearchAsync` returns the nearest chunks with similarity = 1 - cosine distance. It applies no `MinScore` |

The table `rag.chunks`:

| Column | Meaning |
|---|---|
| `embedding_model` | Name of the embedding model, for example `embeddinggemma:latest`. Part of the primary key |
| `id` | Chunk id, for example `knowledge.md-2`. Part of the primary key |
| `source`, `content` | File name and the cleaned text |
| `embedding` | Untyped `vector` column, so 768 and 3072 dimensions can live in one table |
| `source_hash` | Fingerprint of the source's cleaned chunks. Empty for rows stored before it was introduced |

There is no index, so the search is an exact scan and gives the same scores as an in-memory calculation. Every search filters by `embedding_model` **and** by `vector_dims(embedding)`, because PostgreSQL stops with an error when it compares vectors of different sizes.

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
| `AiProvider` | Record that travels together: name, chat client, embedding generator, **embedding model name** and the `MinScore` of that model |
| `AiProviderFactory` | Reads `Rag:Provider` and builds the right `AiProvider`. Only the selected provider's settings (and key) are read |

### Cli

| Class | Job |
|---|---|
| `IngestionPipeline` | `IngestAsync(path)` takes one file to the database: read, chunk, sanitize, fingerprint, then skip (unchanged) or embed and store. A file without chunks is skipped with a message |
| `RagPipeline` | `AskAsync(question)` runs one full round trip and prints the hits and the answer |
| `ConsoleRunner` | Runs the demo questions, then an interactive loop until an empty line. Catches provider errors per question |
| `ProviderErrors` (static) | `IsProviderError` recognizes HTTP errors, no connection and timeouts. `Describe` returns one readable line |

`Knowledge/DummyKnowledgeBase` returns five hardcoded chunks. It is only used as test data now.

## 3. How the pieces are wired (`Program.cs`)

```
AppConfiguration.Build()
  -> AiProviderFactory.Create()          -> AiProvider (chat client, embedder, embedding model, MinScore)
  -> PostgresDataSourceFactory.Create()  -> PostgresChunkStore -> EnsureSchemaAsync()
  -> IngestionPipeline(MarkdownFileReader, ChunkIndexer(embedder), store, model).IngestAsync(path)
  -> ChunkRetriever(store, model, new RagSettings(provider.MinScore))
  -> RagAgentFactory.Create(provider.ChatClient)
  -> RagPipeline(...) -> ConsoleRunner(...).RunAsync()
```

Nothing after the `AiProvider` knows whether Ollama or Gemini is behind it. The embedder and chat client are typed with the framework interfaces `IEmbeddingGenerator<string, Embedding<float>>` and `IChatClient`. The retriever and the ingestion only know the store and the model name.

---

## 4. Running and configuring

Run with `dotnet run` from the project folder. Do **not** use `dotnet watch`: the interactive input does not work well, and every file save restarts the app and repeats the demo questions, which costs LLM requests.

### 4.1 The database (Docker)

PostgreSQL with pgvector runs in Docker. On Windows 11, use Docker Desktop with the WSL 2 backend and run the commands in PowerShell in the **solution folder** (the one with `VadesDev.slnx`).

**Files in the solution folder**

`docker-compose.yaml`:

```yaml
services:
  postgres:
    image: pgvector/pgvector:pg17
    container_name: vadesdev-postgres
    restart: unless-stopped
    environment:
      POSTGRES_USER: ${POSTGRES_USER:-vadesdev}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD in the .env file}
      POSTGRES_DB: ${POSTGRES_DB:-vadesdev}
    ports:
      - "127.0.0.1:5432:5432"
    volumes:
      - vadesdev-pgdata:/var/lib/postgresql/data
      - ./db/init:/docker-entrypoint-initdb.d:ro
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER:-vadesdev} -d ${POSTGRES_DB:-vadesdev}"]
      interval: 5s
      timeout: 5s
      retries: 10

volumes:
  vadesdev-pgdata:
```

`.env` (gitignored) and `.env.example` (committed, placeholder password):

```
POSTGRES_USER=vadesdev
POSTGRES_PASSWORD=choose-a-strong-password
POSTGRES_DB=vadesdev
```

`db/init/01-init.sql`:

```sql
CREATE EXTENSION IF NOT EXISTS vector;
CREATE SCHEMA IF NOT EXISTS app;
CREATE SCHEMA IF NOT EXISTS rag;
```

**Notes**
- The init scripts run **only on the first start with an empty data volume**. Changing them later has no effect on an existing volume.
- The port is bound to `127.0.0.1`, so only your machine can connect.
- PostgreSQL 17 was chosen on purpose: PostgreSQL 18 images keep their data in a versioned subfolder, so the volume would have to be mounted at `/var/lib/postgresql` instead.
- The password is stored in the data volume at the first start. Changing `.env` later does not change it.

**Daily commands**

| Task | Command |
|---|---|
| Start | `docker compose up -d` |
| Status (must be `healthy`) | `docker compose ps` |
| Stop (data stays) | `docker compose stop` |
| Remove the container (data stays in the volume) | `docker compose down` |
| **Delete everything, including the data** | `docker compose down -v` |
| Follow the log | `docker compose logs -f postgres` |
| SQL shell | `docker compose exec postgres psql -U vadesdev -d vadesdev` |

**Checks after the first start**

```powershell
docker compose exec postgres psql -U vadesdev -d vadesdev -c "SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';"
docker compose exec postgres psql -U vadesdev -d vadesdev -c "\dn"
docker compose exec postgres psql -U vadesdev -d vadesdev -c "SELECT '[1,2,3]'::vector <=> '[3,2,1]'::vector AS cosine_distance;"
```

Expected: the extension `vector` (version 0.8.7 in the author's setup), the schemas `app`, `rag` and `public`, and a cosine distance of about `0.2857` (similarity = 1 - distance = 0.7143).

**See what is stored**

```powershell
docker compose exec postgres psql -U vadesdev -d vadesdev -c "SELECT embedding_model, source, count(*), left(max(source_hash), 12) AS hash FROM rag.chunks GROUP BY 1, 2 ORDER BY 1;"
```

One row per model and source, with the chunk count (5 for the sample file) and a fingerprint prefix.

### 4.2 Configuration

**Configuration layers** (later layers win):

1. `appsettings.json`: provider choice, endpoints, model names, `MinScore` per provider
2. `appsettings.Local.json`: gitignored, for secrets (`Gemini:ApiKey`, `ConnectionStrings:Rag`, `ConnectionStrings:App`)
3. Environment variables: a double underscore replaces the colon

```powershell
$env:Rag__Provider = "Gemini"           # switch provider for this terminal
$env:Ollama__ChatModel = "qwen3:4b"     # override any single value
Remove-Item Env:Rag__Provider           # remove it again
```

**Secrets**
- Keep `appsettings.Local.json` and `.env` in `.gitignore` (the repository root `.gitignore` covers all projects). Add the rule **before** you run `git add`, because Git does not apply ignore rules to files that are already staged or tracked.
- Check with `git check-ignore -v .env` and `git check-ignore -v VadesDev.Rag/appsettings.Local.json`.
- If a secret file is already staged, run `git restore --staged <file>`. If it is tracked, `git rm --cached <file>`. If a secret was ever committed or pushed, treat it as leaked and replace it.
- Settings records never contain the key or the password, so printing them is safe.

**Files copied to the output folder** (in the `.csproj`): `Data/knowledge.md`, `appsettings.json`, `appsettings.Local.json`. If a file is not found at run time, check `bin/Debug/net10.0/` first.

---

## 5. Testing

There is no test project yet (a first xUnit project is a candidate for Phase 6). Until then, each class is tested with a short snippet in a temporary `Program.cs`. Keep your working `Program.cs` somewhere safe while you do this. Tests with the database need the Docker container to be running, and they use a **made-up model name** (for example `"fake"`), so they never touch the rows of a real model. Clean up afterwards with `DELETE FROM rag.chunks WHERE embedding_model = 'fake';`.

### 5.1 Quick checks per class (no network, no database)

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

// ChunkHasher: same chunks, same fingerprint; any change, another fingerprint
var a = new DocumentChunk("a-1", "a.md", "Hello");
var b = new DocumentChunk("a-2", "a.md", "World");
Console.WriteLine(ChunkHasher.Compute([a, b]) == ChunkHasher.Compute([a, b]));   // True
Console.WriteLine(ChunkHasher.Compute([a, b]) == ChunkHasher.Compute([b, a]));   // False
Console.WriteLine(ChunkHasher.Compute([]));
// e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 (SHA-256 of empty input)

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

### 5.2 Test the database classes (fake embedder, no LLM)

The fake embedder needs no network, so the store, the retriever and the ingestion can be tested without quota.

**Store: replace, no duplicates, model filter, fingerprint**

```csharp
await using var dataSource = PostgresDataSourceFactory.Create(AppConfiguration.Build());
var store = new PostgresChunkStore(dataSource);
await store.EnsureSchemaAsync();

var chunks = new DummyKnowledgeBase().GetChunks().Where(c => c.Source == "support.md").ToArray();
var index = await new ChunkIndexer(new HashingEmbeddingGenerator()).BuildIndexAsync(chunks);
var hash = ChunkHasher.Compute(chunks);

Console.WriteLine(await store.IsUpToDateAsync("fake", "support.md", hash));        // False: nothing stored yet
await store.ReplaceChunksAsync("fake", hash, index);
Console.WriteLine(await store.IsUpToDateAsync("fake", "support.md", hash));        // True
Console.WriteLine(await store.IsUpToDateAsync("fake", "support.md", "other"));     // False: other fingerprint
Console.WriteLine(await store.IsUpToDateAsync("other-model", "support.md", hash)); // False: other model
```

**Retriever: policy on top of the store**

```csharp
var retriever = new ChunkRetriever(store, "fake", new RagSettings(MinScore: 0.20f));
var embedder = new HashingEmbeddingGenerator();

foreach (var question in new[] { "What are your support hours?", "Do you sell pizza?" })
{
    Console.WriteLine($"? {question}");

    foreach (var (chunk, score) in await retriever.SearchAsync(await embedder.GenerateVectorAsync(question)))
    {
        Console.WriteLine($"  {chunk.Id} {score:F3}");
    }
}
```

The pizza question must print nothing, because no chunk reaches `MinScore`.

**Ingestion: first run, unchanged, changed, empty**

```csharp
var ingestion = new IngestionPipeline(
    new MarkdownFileReader(), new ChunkIndexer(new HashingEmbeddingGenerator()), store, "fake");

var path = Path.Combine(Path.GetTempPath(), "ingest-test.md");

File.WriteAllText(path, "# A\n\nText one.\n\n# B\n\nText two.");
await ingestion.IngestAsync(path);   // Indexing ... Stored 2 chunks (4096 dimensions).
await ingestion.IngestAsync(path);   // Up to date: ... embedding skipped.

File.WriteAllText(path, "# A\n\nText one changed.\n\n# B\n\nText two.");
await ingestion.IngestAsync(path);   // Indexing ... Stored 2 chunks again

File.WriteAllText(path, "");
await ingestion.IngestAsync(path);   // No chunks found ..., skipped.
```

Afterwards the table must show **2 rows** for that source and model, not 4 or 6.

### 5.3 Calibrate `MinScore` (required for every new embedding model)

Scores have a different range for every embedding model, so the threshold must be measured, not guessed.

1. Use the final `Program.cs`, with two temporary changes: the retriever gets `new RagSettings(MinScore: 0f, TopK: 3)` so nothing is filtered, and the agent uses `new FakeExtractiveChatClient()` (saves quota and waiting time).
2. Run it. The first start embeds the file for the new model name.
3. Ask 5 to 10 questions the file **can** answer (include paraphrases without shared keywords), and 3 to 5 it **cannot** answer. Write down the top-1 score of each.
4. Pick `MinScore` in the gap: above the strongest false match and below the weakest correct match. Stay closer to the false side, because losing a correct answer is worse than sending one extra chunk.
5. Put the value in `appsettings.json`, restore the real settings and run again. Out-of-scope questions must print `! No relevant context found.`

Results from this project (5 chunks, `knowledge.md`):

| | Gemini | Ollama |
|---|---|---|
| Embedding model | `gemini-embedding-001` | `embeddinggemma:latest` |
| Vector size | 3072 | 768 |
| Weakest correct top-1 | 0.657 | 0.462 |
| Strongest false top-1 | 0.554 | 0.305 |
| **MinScore chosen** | **0.60** | **0.40** |

Recalibrate when the knowledge base grows or the embedding model changes. The small samples here are a starting point, not proof.

### 5.4 Parity check: database versus in-memory

When the storage changed in Phase 5, the top scores had to stay exactly the same, because the search is an exact scan. They did (differences of 0.001 would be rounding):

| Question | Ollama | Gemini |
|---|---|---|
| What are your support hours? | 0.665 | 0.747 |
| How do I reset my password? | 0.771 | 0.740 |
| Do you support 2FA? | 0.789 | 0.792 |
| Can I get my money back? | 0.580 | 0.654 |
| How long until my package arrives? | 0.548 | 0.659 |
| When can I reach your team? | 0.462 | 0.704 |

Repeat this check after any change to the SQL, the chunker, the sanitizer or the embedding model. A larger difference means that something in the pipeline changed.

### 5.5 Test with the real LLM

- Ask in-scope questions (answer comes from the chunk) and out-of-scope questions (guardrail answers without a call).
- Also try a question that **finds** a chunk but whose answer is **not** in it (for example, shipping cost when the file only gives shipping time). The model should say it doesn't know. This checks whether the model obeys the instructions. Both models were very cautious here: a question about a purchase date got "context is insufficient", and Gemini refused the package question although the shipping chunk was retrieved.
- Gemini free tier: daily chat limits are small, so keep runs short and press Ctrl+C after the startup lines when you only want to test ingestion. Embeddings were not a problem.
- Ollama: the first call after a pause includes model loading. A thinking model such as `qwen3:4b` needs roughly 20 seconds or more per answer on modest hardware.

### 5.6 Test the error handling

- **Stop Ollama while the app waits for input**, then ask a question. Expected: one `! Cannot reach the AI server...` line and the prompt again.
- **Wrong model name:** `$env:Ollama__ChatModel = "qwen3:4bx"`. If this ends in a stack trace instead of a `!` line, note the exception type from the first line and add it to `ProviderErrors`.
- **Unknown provider:** `$env:Rag__Provider = "Foo"` stops with a message that lists the valid names.
- **Missing connection string:** an empty `ConnectionStrings:Rag` stops with a message that names both ways to set it.

### 5.7 Status of the checks (end of Phase 5)

| Check | Status |
|---|---|
| Connection, vector round trip, mixed sizes in one column, error without the size filter | Verified |
| Retriever with the fake embedder against the database | Verified |
| Score parity with Ollama and Gemini after the move to PostgreSQL | Verified |
| Skip when unchanged (second start prints `Up to date`) with Ollama and Gemini | Verified |
| Table: 5 rows per model, no duplicates after repeated runs, fingerprints stored | Verified |
| **Change detection:** an edited `knowledge.md` is embedded again | Not checked yet |
| **Ollama stopped at startup:** app starts and shows a `!` line at the first question | Not checked yet |
| **Database stopped at startup:** expected to end with a stack trace (known gap) | Not checked yet |
| Exception type of OllamaSharp for a wrong model name | Not checked yet |

---

## 6. Design decisions and why

- **No interfaces of our own.** The pipeline uses the framework interfaces `IChatClient` and `IEmbeddingGenerator`, which is enough to swap providers. A learning project does not need more layers.
- **`MinScore` belongs to the embedding model**, not to the app. It lives in each provider's settings and `RagSettings.MinScore` has no default, so no provider can use another one's value.
- **One database, two schemas.** `rag` for vectors (plain SQL), `app` for users and chat history later (Entity Framework). Two connection-string keys from the start make a later split into two databases a configuration change.
- **Plain SQL for vectors.** The store needs about five statements. Writing them shows how pgvector works (`<=>` distance, vector parameter). Entity Framework is kept for the relational part.
- **One table for all models.** The model name is part of the primary key, the vector column has no fixed size, and every search filters by model and by vector size. A new model needs no new table and no migration.
- **No index.** An exact scan is fast for a small knowledge base, and it gives the same scores as an in-memory calculation, so the calibrated `MinScore` stays valid. Approximate indexes can change results. pgvector can index plain `vector` columns only up to 2000 dimensions (4000 with `halfvec`), so Gemini's 3072 dimensions would need another type or smaller vectors.
- **The store has no threshold.** It returns the nearest rows. `ChunkRetriever` applies `MinScore` and `TopK`. Search became `SearchAsync`, because it now does I/O.
- **Replace per source, in one transaction.** A failed run never leaves a half-filled index, and one file can be refreshed without touching the others.
- **The fingerprint covers the cleaned chunks**, not the raw file. If the chunker or the sanitizer changes, the fingerprint changes and the file is embedded again. It is checked per source and per embedding model.
- **The fingerprint does not know the model's content.** If the model behind a name changes (for example `ollama pull`), the vectors are stale but the app says "up to date". Use explicit version tags, or clear the table.
- **PostgreSQL 17.** PostgreSQL 18 images use a different data path, so the volume mount would have to change.
- **The index is returned, not stored inside the indexer.** `ChunkIndexer` returns a list, and the ingestion hands it to the store.
- **The sanitizer knows no format.** Format-specific work (stripping HTML tags, extracting PDF text) belongs to the reader of that format.
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
4. `AiProviderFactory`: one new `case` in `Create()` and one private method that builds the `AiProvider` (including `EmbeddingModel`).
5. **Calibrate `MinScore`** (section 5.3) and put the result in the settings.

Nothing else changes: the pipeline works with any `IChatClient` and `IEmbeddingGenerator`, and the new model gets its own rows in `rag.chunks` at the first start.

### Change the embedding model

Change `EmbeddingModel` in `appsettings.json`. The new name is a different key, so the file is embedded again at the next start. The rows of the old model stay in the table. Remove them if you no longer need them: `DELETE FROM rag.chunks WHERE embedding_model = '<old name>';`. Then calibrate `MinScore` for the new model.

### Add a new file format (PDF, HTML, ...)

1. Add a reader class next to `MarkdownFileReader` with the same shape: `ReadAsync(path)` returns a `SourceDocument`.
2. Do format-specific work inside the reader (extract PDF text, strip HTML tags).
3. Give the reader to `IngestionPipeline` (today it takes a `MarkdownFileReader`, so this needs a small change when the second reader arrives). The chunker, sanitizer, fingerprint and store stay unchanged.
4. Add a sample file and check the chunk output (section 5.1).

### Change the chunking or the sanitizer

Change only `DocumentChunker` or `TextSanitizer` and keep their signatures. The fingerprint changes automatically, so the file is embedded again. Then repeat the parity check (section 5.4). Possible next steps for the chunker: a maximum chunk size, overlap, or merging very short paragraphs.

### Add a vector index later

Only needed when the data grows. Check the current pgvector documentation first. For vectors with more than 2000 dimensions, use an expression index on a `halfvec` cast (and cast in the query too), or choose a model with smaller vectors. An index makes the search approximate, so recalibrate `MinScore` afterwards.

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
- **SQL lives only in `Storage/`**, as `const` strings with positional parameters (`$1`, `$2`, ...) and schema-qualified table names (`rag.chunks`). Values always go through parameters, never into the SQL text.
- `NpgsqlDataSource` is created once and shared. Connections and commands are short-lived (`await using`).
- Database access is a concrete class. No repository pattern and no interface of our own.
- No secrets in code.

---

## 9. Troubleshooting

### Build and code

| Symptom | Likely cause and fix |
|---|---|
| `NU1605` package downgrade | A package needs a newer version of a package you reference directly (for example `Microsoft.Extensions.AI.Abstractions` or `Npgsql`). Raise the version in the `.csproj` to the one named in the error and restart |
| `CS1061` for `GenerateVectorAsync` / `GetResponseAsync(string)` | Missing `using Microsoft.Extensions.AI;`. These are extension methods |
| `CS1061` for `Search` on `ChunkRetriever` | Old call. The method is now `SearchAsync`, so use `await _retriever.SearchAsync(queryVector)` |
| `CS0103` for a variable or `Stopwatch` | A variable from an earlier `Program.cs` is missing, or `using System.Diagnostics;` is missing |
| Compile error at `new Vector(chunk.Vector)` | Use `new Vector(chunk.Vector.ToArray())`. The constructor with `ReadOnlyMemory<float>` may not exist in your package version |
| `Pgvector.Npgsql` namespace not found | Remove that `using`. If `UseVector` is then not found, put it back. It depends on the package version |
| `FileNotFoundException` for `knowledge.md` or `appsettings.json` | The file is not copied to `bin`. Check the `CopyToOutputDirectory` entry in the `.csproj` |
| `Stored`/`Indexed 3 chunks` when the file has 5 sections | The updated `knowledge.md` was not copied to `bin`. Rebuild and check `bin/Debug/net10.0/Data/` |
| Interactive input does not react | You are using `dotnet watch`. Use `dotnet run` |

### Configuration and providers

| Symptom | Likely cause and fix |
|---|---|
| `ArgumentException` about an empty key | `Gemini:ApiKey` is missing. Set it in `appsettings.Local.json` or `Gemini__ApiKey` |
| `Unknown provider '...'` | `Rag:Provider` is not `Ollama` or `Gemini` |
| `ConnectionStrings:Rag is missing` | Set it in `appsettings.Local.json` or `ConnectionStrings__Rag` |
| HTTP 429 from Gemini | Per-minute or per-day quota used up. Check the usage page in Google AI Studio, wait, or use another model. The daily quota resets around midnight Pacific time |
| `HttpClient.Timeout of 100 seconds` | A client without our longer timeout. The Ollama factory sets 5 minutes. A cold model start can still be slow |
| `HttpRequestException` at startup | The AI server is not running (for Ollama, start it). With an unchanged file no embedding is needed at startup, so the error then appears at the first question as a `!` line |
| Everything is rejected after switching embedding model | `MinScore` is calibrated for another model. Recalibrate (section 5.3) |
| Fake answer shows only a heading | Limit of `FakeExtractiveChatClient`: it returns the first line of the first source. The retrieval itself is correct |
| Slow answers with Ollama | Thinking model plus a CPU/GPU split. `ollama ps` shows where the model runs. A smaller or non-thinking model is a configuration change |

### Database and Docker

| Symptom | Likely cause and fix |
|---|---|
| `failed to connect to the docker API ... dockerDesktopLinuxEngine` | Docker Desktop is not running. Start it and wait until the engine is running |
| "port is already allocated" | Another PostgreSQL uses 5432. Change the mapping to `"127.0.0.1:5433:5432"` and use `Port=5433` in the connection strings |
| `Set POSTGRES_PASSWORD in the .env file` | `.env` is missing or not in the same folder as the compose file |
| No extension `vector`, no schemas `app` / `rag` | The data volume already existed, so the init script did not run. `docker compose down -v`, then `docker compose up -d` (this deletes the data) |
| Password changed in `.env` has no effect | The password is stored in the volume at first start. Reset the volume, or run `ALTER USER` |
| Stack trace at `EnsureSchemaAsync` or `Npgsql` connection errors at startup | The database is not running or not healthy (`docker compose ps`), or the connection string is wrong. This case is not handled with a friendly message yet |
| `different vector dimensions 5 and 3` | A query compared vectors of different sizes. Every search must filter by `embedding_model` and `vector_dims(embedding)`, as `SearchAsync` does |
| Container `unhealthy` | Read `docker compose logs postgres` |
| `.env` appears in `git status` | It was staged or tracked before the `.gitignore` rule existed. `git restore --staged .env` (or `git rm --cached .env`) |
| The app says "Up to date" but the answers seem wrong after a model update | The model behind the name changed, so the stored vectors are stale. Delete the rows of that model and start again |
| Moving to a newer PostgreSQL major version | Not an in-place change. Dump and restore |

Useful Ollama commands: `ollama list`, `ollama ps`, `ollama stop <model>` (unload a model). Check the server with `Invoke-RestMethod http://localhost:11434`.

---

## 10. What comes next

- **Phase 6, CLI MVP.** Read a folder of documents, ingest file by file (the fingerprint already works per source), remove rows of files that were deleted or became empty, handle a startup failure (AI server or database not running) with a clear message, consider the first test project, and check Microsoft's `DataIngestion` libraries before writing more reader and chunker code.
- **Phase 7, FastEndpoints.** `RagPipeline.AskAsync` should return the answer and hits instead of printing, so the CLI and the API can share it. Provider errors map to HTTP status codes. Add `CancellationToken`.
- **Phase 8, relational database.** Entity Framework Core in the `app` schema of the same database (users, login, chat history), with `ConnectionStrings:App`. For login, check ASP.NET Core Identity first.
- **Backlog.** An evaluation question set (`Data/eval-questions.md`) to re-run after every change, query rewriting for short queries, a chunk size limit and overlap, and an explicit embedding model version tag.
