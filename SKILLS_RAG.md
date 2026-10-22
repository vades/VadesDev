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
- Folder = pipeline stage: `Models`, `Configuration`, `Documents`, `Embeddings`, `Search`, `Prompting`, `Chat`, `Agents`, `Providers`, `Cli`. One type per file. **No** `Services`, `Helpers` or `Utils` folders.
- `Program.cs` is **wiring only**: create objects, connect them, start the runner. No logic, no data.
- Data and settings are positional `public record`s.
- Dependencies go through the constructor into `private readonly _camelCase` fields, as **concrete classes**.
- Classes are `public`. Stateless classes are `static` (`TextTokenizer`, `Fnv1aHasher`, `TextSanitizer`, `DocumentChunker`, `AppConfiguration`, `ProviderErrors`). Framework implementations are `sealed`.
- **Every class and every method has an XML `<summary>`** (user decision, for IDE tooltips).
- Use `""` instead of `string.Empty`. Use `var` for obvious types. Use collection expressions. Materialize LINQ once.
- `Async` suffix on async methods.
- **No `CancellationToken`** in the console app (Ctrl+C ends the process). Add tokens in Phase 7.
- Settings records never contain the API key.

---

## Skill: vadesdev-architecture

**Use when:** deciding where code belongs, or changing how the stages connect.

**Pipeline order:** read, chunk, sanitize each chunk, embed, index, search, prompt, agent.

```
AppConfiguration -> AiProviderFactory -> AiProvider (chat client, embedder, MinScore)
MarkdownFileReader -> DocumentChunker -> TextSanitizer -> ChunkIndexer -> ChunkRetriever
RagPipeline: embed question -> ChunkRetriever.Search -> guardrail -> GroundedPromptBuilder -> agent
```

Rules:

- **Chunk first, sanitize after.** The chunker needs the `#` markers to join headings with their paragraphs.
- Readers: one class per format, `ReadAsync(path)` returns a `SourceDocument`. Format-specific cleaning (HTML tags, PDF text) belongs to the reader. `TextSanitizer` works on a plain string and knows no format.
- `ChunkIndexer.BuildIndexAsync(chunks)` **returns** the index. `Program.cs` passes it to `ChunkRetriever`. No hidden shared state.
- **Keep `ChunkRetriever.Search(ReadOnlyMemory<float> queryVector)` returning `List<SearchHit>`** (above `MinScore`, best first, at most `TopK`), also when storage changes in Phase 5.
- `RagPipeline` only calls other classes in order. It has no logic of its own, and it currently prints to the console (changes in Phase 7).
- The guardrail runs **before** the LLM. No hits means no call and the answer "I don't know".
- The prompt numbering stays `[1]`, `[2]`, ... (the fake chat client reads `[1]`), and `Question:` appears **once**, after the sources.
- The embedder used for indexing and for questions must be the same model.
- The fakes (`HashingEmbeddingGenerator`, `FakeExtractiveChatClient`) stay as offline test doubles.

---

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
- **`MinScore` belongs to the embedding model.** It lives in the provider settings. `RagSettings.MinScore` has no default on purpose. Never copy a value from another model. Recalibrate when the model, the content size or the chunking changes (see `vadesdev-testing`).
- Adding a provider: (1) settings record, (2) `appsettings.json` section, (3) client factory with `CreateChatClient()` and `CreateEmbeddingGenerator()`, (4) one `case` and one private method in `AiProviderFactory`, (5) calibrate `MinScore`. Nothing else changes.
- Ollama: each client uses its **own `HttpClient` with a 5-minute timeout**. The default 100 seconds is too short for a cold model start. The model name is part of the client, so chat and embeddings are two clients.
- **`qwen3:4b` is a thinking model.** Its reasoning comes back as `TextReasoningContent`, and `response.Text` holds only the answer. **Do not send `think=false` to this model.** In our test it then wrote the reasoning into the answer. Expect about 20 seconds or more per answer on the author's hardware (the model runs about 66% on the GPU).
- Gemini free tier: the author's project showed **20 chat requests per day** for `gemini-2.5-flash` and 1,000 per day for embeddings, at the time of writing. These limits change, so check the usage page in Google AI Studio. Out-of-scope questions cost no chat request. Embeddings were not a problem.
- Package versions must match: `Microsoft.Extensions.AI.Abstractions` is set to the version that `Microsoft.Extensions.AI.OpenAI` requires, or `NU1605` appears.
- `GenerateVectorAsync(string)` and `GetResponseAsync(string)` need `using Microsoft.Extensions.AI;`.

---

## Skill: vadesdev-testing

**Use when:** testing a class, calibrating the search, or checking error handling.

- There is **no test project yet** (candidate for Phase 6: xUnit, using the fakes, no mocks of own types). Until then, test each class with a short snippet in a temporary `Program.cs` and keep the working `Program.cs` safe. The snippets are in `DEVELOPER_GUIDE.md`, section 5.
- Run with `dotnet run`, **not** `dotnet watch` (interactive input, and every save repeats the demo questions and burns quota).
- **Calibration routine** for `MinScore`: real embedder, `FakeExtractiveChatClient`, `new RagSettings(TopK: 3, MinScore: 0f)`, ask in-scope questions (include paraphrases and a typo) and out-of-scope questions, write down the top-1 scores, choose the threshold in the gap (closer to the false side), then rerun with the guardrail on.
- Measured results (5 chunks from `knowledge.md`):

| | Gemini | Ollama |
|---|---|---|
| Weakest correct top-1 | 0.657 | 0.462 |
| Strongest false top-1 | 0.554 | 0.305 |
| Chosen `MinScore` | 0.60 | 0.40 |

- Reference questions that worked: `What are your support hours?`, `How do I reset my password?`, `Do you support 2FA?`, `When can I reach your team?`, `Can I get my money back?`, `How long until my package arrives?`. Out of scope: `Do you sell pizza?`, `Who is the CEO of the company?`, `What is the capital of France?`.
- Known weak spot: one or two word queries (`Reach your team`, `Package`) were rejected at 0.40 with `embeddinggemma`.
- Error checks: stop Ollama while the app waits for input (expect a `!` line and the prompt again), wrong provider name (expect the unknown-provider message), missing Gemini key (expect `ArgumentException`).
- **Open items:** (1) the exception type OllamaSharp throws for a wrong model name is not verified (set `Ollama__ChatModel` to a wrong name and read the first line of the stack trace), (2) the case "chunk found but answer not in it" is not tested with the real models, (3) `Data/eval-questions.md` (question, expected chunk, in scope or not) is not created yet. It is worth adding before Phase 5.

---

## Skill: vadesdev-phases

**Use when:** planning the next step, or checking what a phase may touch.

| Phase | Goal | Status |
|---|---|---|
| 1 | One-file app with dummy data and fakes | Done |
| 2 | Folders, classes, methods | Done |
| 3 | Read Markdown (reader, chunker, sanitizer) | Done |
| 4 | Real LLM and embeddings (Ollama, Gemini) | Done |
| 5 | PostgreSQL and pgvector | Planned |
| 6 | RAG MVP as a CLI | Planned |
| 7 | REST API with FastEndpoints | Planned |

**Phase 5 (pgvector)**
- Only `ChunkIndexer` and `ChunkRetriever` change. `Search(queryVector)` keeps its signature.
- The vector size depends on the model (768 or 3072). One table holds one size, so use one table per model or re-index on a switch. Store the model name and dimension with the vectors.
- Before choosing the column type, **check the current pgvector documentation** for index size limits. From memory, the plain `vector` type is limited to about 2,000 dimensions for indexes, which matters for the 3072-dimension Gemini vectors. Unverified.
- pgvector returns distances. Convert to similarity so `MinScore` keeps its meaning.
- Concrete store class, no repository pattern, no interface. Connection string in secrets. Confirm a docker-compose for PostgreSQL with the user.

**Phase 6 (CLI MVP)**
- Read a folder of documents, index once and reuse. Handle a startup failure (AI server not running during indexing) with a clear message. Consider the first test project. Check Microsoft's `DataIngestion` libraries before writing more reader and chunker code.

**Phase 7 (FastEndpoints)**
- Change `RagPipeline.AskAsync` to return the answer and hits, and move printing to `ConsoleRunner`.
- `Program.cs` becomes a web host with dependency injection of concrete classes. One endpoint class per use case. Map provider errors to HTTP status codes. Add `CancellationToken` here.

**Backlog**
- The chunker has no size limit and no overlap.
- The sanitizer does not defend against prompt injection. Bullet markers and tables are not handled.
- `Knowledge/DummyKnowledgeBase` is unused and can be deleted.
- `PdfFileReader` and `HtmlFileReader` have no phase yet.
- Short queries retrieve less reliably (query rewriting is a later idea).

---

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
- A failure during startup indexing is outside `ConsoleRunner` and still ends with a stack trace (planned for Phase 6).

---

## Decision log

Do not reopen these without a concrete reason, and say what changed.

| Decision | Reason |
|---|---|
| Chunk first, sanitize each chunk after | The chunker needs `#` markers to join headings with their paragraphs |
| Sanitizer works on plain strings | Reusable for PDF and HTML text later. Format-specific work belongs to the readers |
| One reader class per format, same shape | `Program.cs` picks the reader. No interface needed |
| OllamaSharp for Ollama | Implements both framework interfaces directly. Native API instead of raw HTTP or a compatibility layer |
| Own `HttpClient` (5 minutes) for Ollama | Cold model starts exceed the 100-second default |
| No `think=false` for `qwen3:4b` | The reasoning leaked into the answer text |
| `MinScore` in each provider's settings, no default in `RagSettings` | The threshold is only valid for one embedding model |
| API key outside the settings records | Printing a record must never leak the key |
| Provider chosen by `Rag:Provider`, only that provider is built | Runs offline without another provider's key |
| One factory per provider, no common interface | Constraint: no interfaces of our own. `Program.cs` and `AiProviderFactory` pick the class |
| `ChunkIndexer` returns the index list | No hidden shared state. Phase 5 replaces two classes only |
| Guardrail before the LLM | Saves quota and time, and does not depend on the model's obedience |
| Narrow error handling with a `when` filter (`ProviderErrors`) | Only expected provider errors are caught. Real bugs still crash with a stack trace |
| No `CancellationToken` in the console app | Ctrl+C ends the process. Tokens are added in Phase 7 |
| Startup indexing failure left for Phase 6 | Keeps `Program.cs` wiring only. Phase 6 restructures indexing anyway |
| Keep the fakes | Offline tests and cheap calibration |
| Threshold `0.40` kept although short queries are weaker | Lower values let noise through. Query rewriting is the better fix later |
