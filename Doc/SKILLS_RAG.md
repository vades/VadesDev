# SKILLS_RAG.md

Project-specific instructions for coding agents working on **`VadesDev.Rag`**.

`VadesDev.Rag` is a **learning project**: a RAG app built step by step with .NET 10, C# 14 and the Microsoft Agent Framework. Read [`SKILLS.md`](SKILLS.md) for the general best practices. **This file wins on conflicts.**

Precedence: **Constraints > Workflow > Project conventions > general `SKILLS.md`**.

For humans: [`README.md`](README.md) (overview) and [`DEVELOPER_GUIDE.md`](DEVELOPER_GUIDE.md) (classes, tests, troubleshooting). Do not repeat their class tables here.

## Constraints (never break these)

- Best practices, but **simple**. Keep it simple.
- **Single responsibility** per class and per method.
- Methods stay **between 20 and 30 lines** (shorter is fine).
- **No interfaces of our own.** Use and implement only the framework interfaces (`IChatClient`, `IEmbeddingGenerator<string, Embedding<float>>`). Use concrete classes and the fakes for tests.
- **No repository pattern**, also not with Entity Framework or pgvector.
- **No over-engineering:** no extra projects, layers or settings before a phase needs them.
- **No secrets in code**, logs, chat or commits.

---

## Skill: vadesdev-workflow

**Use when:** the user asks you to build, refactor or extend anything in this project.

Work **one class at a time**. Never skip a step:

1. **Structure first.** Propose the class: fields, constructor, method signatures, placeholder bodies, XML summaries. Explain briefly: responsibility, notes, and why.
2. **Wait for approval.**
3. **Bodies.** Write the method bodies one by one, then the full file for copy and paste.
4. **Test.** Give a short test snippet with the expected output.
5. **Wait for "works" or "approved"** before the next class.

Rules for the steps:

- One class per step. Never describe several classes at once.
- Order: folder structure, class structure, bodies, test, next class.
- Declarations that are already final (records, constants, regex attributes, constructors) are shown in the structure step and marked as final. Records and methods without logic (`GetService`, `Dispose`) skip the placeholder stage.
- Placeholder values: string `""`, collection `[]`, bool `false`, number `0`, `Task.CompletedTask`, object `null!`. Avoid compiler warnings in placeholders, or mention them.
- Explain **why** briefly for every design decision, and **flag every intentional behavior change**.
- Do not claim facts about files you have not seen (for example the `.csproj`). Ask for them.
- Verify expected test outputs before you state them. If unsure, say so.
- Before code for fast-changing APIs (Agent Framework, `Microsoft.Extensions.AI`, OpenAI SDK, OllamaSharp, pgvector libraries, FastEndpoints), check the current documentation and say what you could not verify.
- End each step with **one** approval question and the name of the next class.
- After each phase: a short summary (new files, flow, decisions, known limits, next phase), and update `README.md`, `DEVELOPER_GUIDE.md` and this file.

---

## Skill: vadesdev-conventions

**Use when:** writing or reviewing any code in this project.

- Solution `VadesDev`, project `VadesDev.Rag`, project folder directly under the solution (no `src/` folder). Namespaces: `VadesDev.Rag.<Folder>`, file-scoped.
- Folder = pipeline stage: `Models`, `Configuration`, `Documents`, `Embeddings`, `Search`, `Storage`, `Prompting`, `Chat`, `Agents`, `Providers`, `Cli`. One type per file. **No** `Services`, `Helpers` or `Utils` folders.
- `Program.cs` is **wiring only**: create objects, connect them, start the runner. No logic, no data. Keep it at about 30 lines or less.
- Data and settings are positional `public record`s.
- Dependencies go through the constructor into `private readonly _camelCase` fields, as **concrete classes**.
- Classes are `public`. Stateless classes are `static` (`TextTokenizer`, `Fnv1aHasher`, `TextSanitizer`, `DocumentChunker`, `ChunkHasher`, `AppConfiguration`, `PostgresDataSourceFactory`, `ProviderErrors`). Framework implementations are `sealed`.
- **Every class and every method has an XML `<summary>`** (user decision, for IDE tooltips).
- Use `""` instead of `string.Empty`. Use `var` for obvious types. Use collection expressions. Materialize LINQ once.
- `Async` suffix on async methods.
- **All SQL lives in `Storage/`**, as `const` strings with positional parameters (`$1`, `$2`, ...) and schema-qualified table names. Values always go through parameters. No ORM for the vector data.
- **No `CancellationToken`** in the console app (Ctrl+C ends the process). Add tokens in Phase 7.
- Settings records never contain the API key or a password.

## Skill: vadesdev-architecture

**Use when:** deciding where code belongs, or changing how the stages connect.

**Pipeline order:** read, chunk, sanitize each chunk, fingerprint, (skip or) embed, store, then per question: embed, search, guardrail, prompt, agent.

```
AppConfiguration -> AiProviderFactory -> AiProvider (chat client, embedder, embedding model, MinScore)
PostgresDataSourceFactory -> PostgresChunkStore (all SQL)
IngestionPipeline: MarkdownFileReader -> DocumentChunker -> TextSanitizer -> ChunkHasher
                   -> store.IsUpToDateAsync -> ChunkIndexer -> store.ReplaceChunksAsync
RagPipeline: embed question -> ChunkRetriever.SearchAsync -> guardrail -> GroundedPromptBuilder -> agent
```

Rules:

- **Chunk first, sanitize after.** The chunker needs the `#` markers to join headings with their paragraphs. The fingerprint is computed from the **sanitized** chunks.
- Readers: one class per format, `ReadAsync(path)` returns a `SourceDocument`. Format-specific cleaning (HTML tags, PDF text) belongs to the reader. `TextSanitizer` works on a plain string and knows no format.
- **The store only persists and searches.** `PostgresChunkStore` has no `MinScore` and no `TopK` policy. **`ChunkRetriever` applies the policy:** it asks the store for `TopK` nearest rows and keeps those with a score of at least `MinScore`.
- **Retriever contract:** `SearchAsync(ReadOnlyMemory<float> queryVector)` returns `Task<List<SearchHit>>` (above `MinScore`, best first, at most `TopK`). It became async in Phase 5 because it does I/O. Keep this contract stable when storage changes.
- `ChunkIndexer.BuildIndexAsync(chunks)` only embeds and returns the list. `IngestionPipeline` hands it to the store.
- `IngestionPipeline` and `RagPipeline` only call other classes in order, and print to the console (this changes in Phase 7).
- The guardrail runs **before** the LLM. No hits means no call and the answer "I don't know".
- The prompt numbering stays `[1]`, `[2]`, ... (the fake chat client reads `[1]`), and `Question:` appears **once**, after the sources.
- The embedder used for ingestion and for questions must be the same model, and the model name is the database key.
- The fakes (`HashingEmbeddingGenerator`, `FakeExtractiveChatClient`) stay as offline test doubles.

## Skill: vadesdev-providers

**Use when:** changing models, adding a provider, or touching `Providers/` or provider settings.

**Current providers**

| | Ollama (local) | Gemini (external) |
|---|---|---|
| Library | OllamaSharp | OpenAI SDK + `Microsoft.Extensions.AI.OpenAI` |
| Endpoint | `http://localhost:11434` | `https://generativelanguage.googleapis.com/v1beta/openai/` (keep the trailing slash) |
| Chat model | `qwen3:4b` | `gemini-2.5-flash` |
| Embedding model | `embeddinggemma:latest` | `gemini-embedding-001` |
| Vector size | 768 | 3072 |
| **MinScore** | **0.40** | **0.60** |
| Key | none | `Gemini:ApiKey` in `appsettings.Local.json` or `Gemini__ApiKey` |

**Rules**

- The provider is chosen by `Rag:Provider` (`Ollama` or `Gemini`, case-insensitive). Only the selected provider is built, so Ollama runs without a Gemini key.
- `AiProvider` carries the chat client, the embedder, **the embedding model name** and `MinScore`. The model name is part of the primary key in the database, so a changed name (even only the tag) means a new embedding run.
- **`MinScore` belongs to the embedding model.** It lives in the provider settings. `RagSettings.MinScore` has no default on purpose. Never copy a value from another model. Recalibrate when the model, the content size or the chunking changes (see `vadesdev-testing`).
- Adding a provider: (1) settings record, (2) `appsettings.json` section, (3) client factory with `CreateChatClient()` and `CreateEmbeddingGenerator()`, (4) one `case` and one private method in `AiProviderFactory` (including `EmbeddingModel`), (5) calibrate `MinScore`. Nothing else changes.
- Ollama: each client uses its **own `HttpClient` with a 5-minute timeout**. The default 100 seconds is too short for a cold model start. The model name is part of the client, so chat and embeddings are two clients.
- **`qwen3:4b` is a thinking model.** Its reasoning comes back as `TextReasoningContent`, and `response.Text` holds only the answer. **Do not send `think=false` to this model.** In our test it then wrote the reasoning into the answer. Expect about 20 seconds or more per answer on the author's hardware (the model runs about 66% on the GPU).
- Gemini free tier: the author's project showed **20 chat requests per day** for `gemini-2.5-flash` and 1,000 per day for embeddings, at the time of writing. These limits change, so check the usage page in Google AI Studio. Out-of-scope questions cost no chat request. Unchanged files cost no embedding request.
- The fingerprint does not see a model change behind the same name (for example `ollama pull`). Prefer explicit version tags, and delete the rows of a model when its content changed.
- Package versions must match: `Microsoft.Extensions.AI.Abstractions` is set to the version that `Microsoft.Extensions.AI.OpenAI` requires, or `NU1605` appears.
- `GenerateVectorAsync(string)` and `GetResponseAsync(string)` need `using Microsoft.Extensions.AI;`.

## Skill: vadesdev-database

**Use when:** touching Docker, PostgreSQL, the `Storage/` folder, the table `rag.chunks`, the fingerprint, or connection strings.

**Setup (Windows 11, Docker Desktop with WSL 2)**
- Files in the solution folder: `docker-compose.yaml` (image `pgvector/pgvector:pg17`, port `127.0.0.1:5432`, named volume, healthcheck), `.env` (gitignored, credentials), `.env.example` (committed, placeholder), `db/init/01-init.sql` (`CREATE EXTENSION vector`, schemas `app` and `rag`).
- The init scripts run **only on the first start with an empty volume**. The password is stored in the volume at first start.
- **Never run `docker compose down -v` without telling the user that it deletes all data.** Use `down` (data stays) or `stop` for normal work.
- PostgreSQL 17 on purpose: PostgreSQL 18 images use a versioned data path, so the volume mount would change. A major version change needs dump and restore.
- Verified in the author's setup: pgvector 0.8.7, schemas `app`, `rag`, `public`, cosine distance of `[1,2,3]` and `[3,2,1]` is 0.2857.

**One database, two schemas:** `rag` (vectors, plain SQL, this project) and `app` (users, login, chat history, Entity Framework, Phase 8). Connection strings: `ConnectionStrings:Rag` and `ConnectionStrings:App`, both in `appsettings.Local.json` or environment variables (`ConnectionStrings__Rag`). Never in `appsettings.json`.

**Table `rag.chunks`**
- Columns: `embedding_model`, `id`, `source`, `content`, `embedding vector` (no fixed size), `source_hash`. Primary key `(embedding_model, id)`.
- Created by `EnsureSchemaAsync` with `CREATE TABLE IF NOT EXISTS` and `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`. No migration tool for this schema.
- **Every search filters by `embedding_model` and by `vector_dims(embedding)`.** PostgreSQL raises an error when it compares vectors of different sizes. Verified: without the filter, a query fails with "different vector dimensions".
- Similarity = 1 - cosine distance (`<=>`). Parameters are positional and added in the order of the `$n` numbers.
- **No vector index.** Exact scan, so the scores equal the in-memory scores and the calibration stays valid. pgvector can index the plain `vector` type only up to 2000 dimensions (4000 with `halfvec`), so the 3072 dimensions of Gemini cannot use a plain index. Approximate indexes change results, so recalibrate `MinScore` if one is ever added. Check the current pgvector documentation first.
- `ReplaceChunksAsync(model, hash, chunks)` accepts chunks of **exactly one source**, deletes that source's rows for the model and inserts the new ones in **one transaction**.

**Fingerprint (`ChunkHasher`, `source_hash`)**
- SHA-256 over the ids and texts of the **sanitized** chunks (joined with `\0`, which the sanitizer removes from texts), as lowercase hex.
- Checked per source and per embedding model with `IsUpToDateAsync`. Equal means the embedding is skipped.
- A changed chunker or sanitizer changes the fingerprint, so files are embedded again automatically.
- Rows stored before the column existed have no fingerprint and are embedded once.
- Known limits: removed or emptied files keep their rows (Phase 6), and a changed model behind the same name is not noticed.

**Entity Framework** is only for the `app` schema in Phase 8. Do not use it for `rag.chunks`.

---

## Skill: vadesdev-testing

**Use when:** testing a class, calibrating the search, or checking error handling.

- There is **no test project yet** (candidate for Phase 6: xUnit, using the fakes, no mocks of own types). Until then, test each class with a short snippet in a temporary `Program.cs` and keep the working `Program.cs` safe. The snippets are in `DEVELOPER_GUIDE.md`, section 5.
- Run with `dotnet run`, **not** `dotnet watch` (interactive input, and every save repeats the demo questions and burns quota).
- Database tests use the **fake embedder** and a **made-up model name** (for example `"fake"`), so they never touch real rows. Clean up with `DELETE FROM rag.chunks WHERE embedding_model = 'fake';`.
- **Calibration routine** for `MinScore`: real embedder, `FakeExtractiveChatClient`, `new RagSettings(MinScore: 0f, TopK: 3)`, ask in-scope questions (include paraphrases and a typo) and out-of-scope questions, write down the top-1 scores, choose the threshold in the gap (closer to the false side), then rerun with the guardrail on.
- Measured results (5 chunks from `knowledge.md`):

| | Gemini | Ollama |
|---|---|---|
| Weakest correct top-1 | 0.657 | 0.462 |
| Strongest false top-1 | 0.554 | 0.305 |
| Chosen `MinScore` | 0.60 | 0.40 |

- **Parity values** (top-1 scores that must not change when storage, SQL, chunker, sanitizer or model change): Ollama 0.665 support hours, 0.771 password, 0.789 2FA, 0.580 money back, 0.548 package, 0.462 reach your team. Gemini 0.747, 0.740, 0.792, 0.654, 0.659, 0.704.
- Reference questions that worked: `What are your support hours?`, `How do I reset my password?`, `Do you support 2FA?`, `When can I reach your team?`, `Can I get my money back?`, `How long until my package arrives?`. Out of scope: `Do you sell pizza?`, `Who is the CEO of the company?`, `What is the capital of France?`.
- Known weak spots: one or two word queries were rejected at 0.40 with `embeddinggemma`. Both models are very cautious when the chunk does not contain the complete answer, and Gemini refused the package question although the right chunk was retrieved.
- Error checks: stop Ollama while the app waits for input (expect a `!` line and the prompt again), wrong provider name (expect the unknown-provider message), missing Gemini key (expect `ArgumentException`), missing connection string (expect the clear message).

**Status at the end of Phase 5**
- **Verified:** connection and vector round trip, mixed sizes in one column, the error without the size filter, score parity with Ollama and Gemini after the move to PostgreSQL, skipping unchanged files (second start) with both providers, 5 rows per model with fingerprints and no duplicates after repeated runs.
- **Not checked yet:** (1) change detection (an edited `knowledge.md` is embedded again, and the table keeps 5 rows), (2) Ollama stopped at startup (app starts, `!` line at the first question), (3) database stopped at startup (expected stack trace, known gap), (4) the exception type OllamaSharp throws for a wrong model name, (5) `Data/eval-questions.md` (question, expected chunk, in scope or not) is not created yet, and it is worth adding before the next change to retrieval.

## Skill: vadesdev-phases

**Use when:** planning the next step, or checking what a phase may touch.

| Phase | Goal | Status |
|---|---|---|
| 1 | One-file app with dummy data and fakes | Done |
| 2 | Folders, classes, methods | Done |
| 3 | Read Markdown (reader, chunker, sanitizer) | Done |
| 4 | Real LLM and embeddings (Ollama, Gemini) | Done |
| 5 | PostgreSQL and pgvector | Done |
| 6 | RAG MVP as a CLI | Planned |
| 7 | REST API with FastEndpoints | Planned |
| 8 | Relational database with Entity Framework (users, login, chat history) | Planned |

**Phase 6 (CLI MVP)**
- Read a folder of documents and ingest file by file (the fingerprint already works per source). Remove the rows of files that were deleted or became empty.
- Handle a startup failure (AI server or database not running) with a clear message instead of a stack trace. `IngestionPipeline` currently takes a `MarkdownFileReader`, so the second reader needs a small change.
- Consider the first test project (xUnit, the fakes, no mocks of own types). Check Microsoft's `DataIngestion` libraries before writing more reader and chunker code.

**Phase 7 (FastEndpoints)**
- Change `RagPipeline.AskAsync` (and the ingestion output) to return results, and move printing to `ConsoleRunner`.
- `Program.cs` becomes a web host with dependency injection of concrete classes. One endpoint class per use case. Map provider errors to HTTP status codes. Add `CancellationToken` here.

**Phase 8 (relational database)**
- Entity Framework Core in the `app` schema, with `ConnectionStrings:App`. Migrations for this schema only. For login, check ASP.NET Core Identity first, and verify package versions and setup in the current documentation. No repository on top of `DbContext`.

**Backlog**
- An evaluation question set (`Data/eval-questions.md`).
- The chunker has no size limit and no overlap.
- The sanitizer does not defend against prompt injection. Bullet markers and tables are not handled.
- Query rewriting for very short queries.
- Explicit embedding model version tags, or a check for changed model content.
- `Knowledge/DummyKnowledgeBase` is only test data now and can be deleted.
- `PdfFileReader` and `HtmlFileReader` have no phase yet.
- The strict agent instructions make models refuse when a chunk does not hold the complete answer. Evaluate with the question set before changing them.

## Skill: vadesdev-pitfalls

**Use when:** something fails, or before you suggest a change to a working area.

Mistakes that actually happened here:

- `Question:` appended inside the loop over the hits, so it appeared once per hit.
- Splitting paragraphs on `"\n\n"`, which fails for Windows line endings. The chunker uses a blank-line regex.
- `NU1605`: `Microsoft.Extensions.AI.Abstractions` pinned lower than the OpenAI package requires.
- Missing `using Microsoft.Extensions.AI;` (errors about `GenerateVectorAsync` and `GetResponseAsync`).
- A variable from an older test `Program.cs` missing, or `using System.Diagnostics;` missing for `Stopwatch`.
- Reusing `MinScore = 0.60` for the Ollama embedder: almost everything would be rejected.
- Default `HttpClient` timeout of 100 seconds with a cold local model.
- Gemini daily chat quota used up by testing and `dotnet watch` restarts (HTTP 429).
- `knowledge.md` or `appsettings.json` not copied to `bin` (`CopyToOutputDirectory`).
- Judging retrieval by the answer of `FakeExtractiveChatClient`, which only returns the first line (the heading).
- Disabling thinking on `qwen3:4b`, which put the reasoning into the answer text.
- **Changing the retriever to `SearchAsync` without updating `RagPipeline`:** the build fails with CS1061 on `Search`.
- **`.env` staged before the `.gitignore` rule was saved.** Git does not apply ignore rules to staged or tracked files. Use `git restore --staged .env`, and check with `git check-ignore -v .env`. The secret was never committed in this case.
- Docker Desktop installed but not running (`dockerDesktopLinuxEngine` pipe error), and the `docker-desktop` WSL distro shows `Stopped`.
- Comparing vectors of different sizes in one query (`different vector dimensions`): always filter by model and `vector_dims`.
- Running `docker compose down -v` deletes the data and re-runs the init scripts.
- The init script does not run on an existing volume, and a changed password in `.env` does not change the stored one.
- The fingerprint does not notice a changed model behind the same name, and an unchanged file prints `Up to date` even then.
- A failure during startup (database down) is outside `ConsoleRunner` and still ends with a stack trace (planned for Phase 6).
- An unchanged file does not embed anything at startup, so a stopped Ollama only shows up at the first question.

## Decision log

Do not reopen these without a concrete reason, and say what changed.

| Decision | Reason |
|---|---|
| Chunk first, sanitize each chunk after | The chunker needs `#` markers to join headings with their paragraphs |
| Sanitizer works on plain strings | Reusable for PDF and HTML text later. Format-specific work belongs to the readers |
| One reader class per format, same shape | `Program.cs` (or the ingestion) picks the reader. No interface needed |
| OllamaSharp for Ollama | Implements both framework interfaces directly. Native API instead of raw HTTP or a compatibility layer |
| Own `HttpClient` (5 minutes) for Ollama | Cold model starts exceed the 100-second default |
| No `think=false` for `qwen3:4b` | The reasoning leaked into the answer text |
| `MinScore` in each provider's settings, no default in `RagSettings` | The threshold is only valid for one embedding model |
| `AiProvider` carries the embedding model name | The name is the database key, so Gemini and Ollama vectors cannot be mixed up |
| API key and passwords outside the settings records | Printing a record must never leak a secret |
| Provider chosen by `Rag:Provider`, only that provider is built | Runs offline without another provider's key |
| One factory per provider, no common interface | Constraint: no interfaces of our own |
| Plain SQL with Npgsql and the `Pgvector` package for vectors | About five statements, shows how pgvector works, no extra layer. Entity Framework only for the `app` schema in Phase 8 |
| One PostgreSQL database with two schemas (`rag`, `app`) | One server and one backup. Two connection-string keys allow a later split by configuration |
| One table `rag.chunks` for all models, model name in the key, untyped `vector` column | A new model needs no new table. Safe because every search filters by model and vector size |
| No vector index in Phase 5 | Exact scan is fast for small data, keeps scores identical to the in-memory version and the calibration valid. Plain `vector` indexes stop at 2000 dimensions |
| The store has no `MinScore` or `TopK` policy | Policy belongs to the retriever. The store only persists and searches |
| `SearchAsync` instead of `Search` | The search does I/O now. The contract (vector in, ordered hits out) is the same |
| Replace one source per transaction | A failed run never leaves a half-filled index. One file can be refreshed alone |
| Fingerprint over the sanitized chunks, per source and model | Detects changes in the file, the chunker and the sanitizer. A new model always re-embeds |
| `IngestionPipeline` in `Cli/` next to `RagPipeline` | Same kind of class: a round trip that calls other classes and prints. Both change in Phase 7 |
| PostgreSQL 17, Docker Desktop with WSL 2, ports bound to `127.0.0.1` | PostgreSQL 18 changes the data path. Local development only |
| Credentials in a gitignored `.env`, connection strings in `appsettings.Local.json` | No secrets in committed files |
| Narrow error handling with a `when` filter (`ProviderErrors`) | Only expected provider errors are caught. Real bugs still crash with a stack trace |
| No `CancellationToken` in the console app | Ctrl+C ends the process. Tokens are added in Phase 7 |
| Startup failure handling left for Phase 6 | Keeps `Program.cs` wiring only. Phase 6 restructures ingestion anyway |
| Keep the fakes | Offline tests and cheap calibration |
| Threshold `0.40` kept although short queries are weaker | Lower values let noise through. Query rewriting is the better fix later |
