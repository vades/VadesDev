# SKILLS.md

Instructions for coding agents that build **RAG (retrieval-augmented generation)** and **agentic** applications with **.NET**.

The skills describe general best practices. The last section, **Project overrides**, lists where `VadesDev.Rag` deliberately differs because it is a learning project. **Overrides win over the skills.**

## How to use this file

- Read the skill that matches the task. Several can apply at once.
- **Verify before you write code for fast-changing APIs.** Microsoft Agent Framework, `Microsoft.Extensions.AI`, provider SDKs (OpenAI SDK, OllamaSharp), pgvector libraries and FastEndpoints change often. Names get renamed and methods move between packages. Check the current documentation or the installed package version first, and do not rely on memory. If you could not verify something, say so in your answer.
- **Ask for files you have not seen** (for example the `.csproj`). Never describe their content from guesswork.
- **Verify expected outputs** before you state them. If unsure, say so.
- Do not put secrets in code, logs, chat or commits.

---

## Skill: developing-csharp

**Use when:** working with `.cs` files or .NET projects, designing classes, or reviewing C# code.

### Naming

| Element | Convention | Example |
|---|---|---|
| Classes, structs, records | PascalCase | `ChunkRetriever` |
| Interfaces | `I` + PascalCase | `IChatClient` |
| Methods, properties | PascalCase | `BuildIndexAsync` |
| Private fields | `_camelCase` | `_embedder` |
| Parameters, locals | camelCase | `queryVector` |
| Async methods | PascalCase + `Async` | `AskAsync` |

### Rules

- Enable nullable reference types and implicit usings. Treat warnings seriously.
- Use `record` for data that is passed around (DTOs, settings, results). Use positional records for short ones.
- Prefer `readonly` fields and constructor injection. Keep classes small, with one reason to change.
- Use `var` when the type is obvious. Use collection expressions (`[1, 2, 3]`, `[.. items]`) and pattern matching where they make code shorter and clearer.
- `async`/`await` all the way. Do not block with `.Result` or `.Wait()`. No `async void` except for event handlers. Do not ignore tasks. Accept a `CancellationToken` in library and server code that does I/O.
- Materialize LINQ queries once (`ToList()`, `ToArray()`) when the result is used more than once.
- Use `[GeneratedRegex]` for fixed regular expressions (the class must be `partial`).
- Use `using` for disposable resources. Create `HttpClient` objects once and reuse them (or use `IHttpClientFactory` in hosted apps).
- Use `ArgumentException.ThrowIfNullOrWhiteSpace` and similar guards at public boundaries.
- Do not use `string.GetHashCode()` for anything that must be stable between runs. Use a stable hash.
- Keep methods short and focused. Extract a private method when a method does two jobs.

---

## Skill: rag-pipeline

**Use when:** building or changing document ingestion, chunking, embedding, retrieval, prompt grounding or answer generation.

### Two flows

```
INGESTION (offline or at startup)
read -> chunk -> sanitize -> embed -> store/index

QUERY (per question)
embed question -> retrieve (similarity, threshold, top-k) -> guardrail -> ground prompt -> generate answer
```

Keep each stage in its own class and folder. Do not mix reading, cleaning, chunking and embedding in one method.

### Reading

- One reader per file format, same shape: path in, a common document object out (text plus source name).
- Do format-specific work (extract PDF text, strip HTML tags, resolve Markdown front matter) **in the reader**.
- Keep the source name. It travels with every chunk and is used for citations.

### Chunking

- Prefer **structure-aware** chunks (paragraphs, sections under headings) over fixed character counts.
- A heading alone is a poor chunk. Join it with the content below it.
- Add limits when documents grow: a maximum chunk size (in tokens or characters, within the embedding model's input limit), and optionally a small overlap between neighboring chunks.
- Give every chunk a stable id and its source (for example `file.md-3`).
- Split on blank lines in a way that works for both `\n` and `\r\n`.
- Chunk **before** you remove structural markers (headings), sanitize **after**.

### Sanitizing

- Work on plain text and know no file format. Remove noise that does not help retrieval: link targets and raw URLs, formatting symbols, control and zero-width characters, repeated whitespace. Keep paragraph breaks until chunking is done.
- Keep the words of headings and link texts. Only remove the symbols.
- Sanitizing is **not** a defense against prompt injection (see "Security").

### Embedding

- **Use the same embedding model for indexing and for questions.** Vectors from different models cannot be compared.
- Embed in batches, and respect the provider's batch and input-size limits and rate limits.
- Record the model name and vector dimension next to the stored vectors. A model change means a **full re-index and a new threshold calibration**.
- Cosine similarity ignores vector length. Normalizing to length 1 makes dot product equal to cosine, which is useful for databases and fast search.
- Some embedding models expect a task prefix or different input forms for queries and documents. Check the model's documentation if retrieval quality is unexpectedly poor.

### Retrieval

- Return a small, ordered list: the best `TopK` chunks whose score is at least `MinScore`.
- **`MinScore` is specific to the embedding model.** Never copy a threshold from another model. Calibrate it:
    1. Use the real embedder, and a fake or cheap chat client.
    2. Turn the threshold off (`0`) and print all scores.
    3. Ask questions the knowledge base can answer (include paraphrases with no shared keywords) and questions it cannot.
    4. Choose the threshold in the gap between the weakest correct top-1 and the strongest false top-1, closer to the false side.
    5. Re-check when the content grows or the model changes.
- Short queries (one or two words) retrieve less reliably. Consider rewriting them into full questions later.
- Later improvements, only when needed: hybrid search (keyword plus vector), reranking, query rewriting, metadata filters.
- Keep the retrieval contract stable when you change storage: question vector in, ordered hits with scores out.

### Grounding and generation

- Number the sources in the prompt (`[1]`, `[2]`, ...) and include their source names, so the model can cite them.
- Put the question **once**, after all sources.
- The system instructions must say: answer only from the provided context, and say "I don't know" if the context is not enough.
- **Guardrail:** if retrieval finds nothing above the threshold, do not call the LLM. Answer "I don't know" directly. This saves cost and time and does not depend on the model obeying its prompt.
- Test the case that is hard for the model: a chunk is found, but the answer is not in it.

### Evaluation

- Keep a small fixed set of questions with the expected chunk or answer, and re-run it after every change to chunking, the embedding model, `MinScore` or the prompt.
- Compare retrieval (right chunk in top-k?) separately from generation (right answer?). Most RAG problems are retrieval problems.

### Security

- **Retrieved text is untrusted data**, not instructions. A document can contain text like "ignore your instructions". Keep the system instructions separate, label the context as data, and do not give the agent powerful tools on the basis of retrieved text alone.
- Do not log sensitive content or full prompts in production.
- Free tiers of external providers may use your data to improve their products. Do not send sensitive or production data to them.

---

## Skill: ai-providers

**Use when:** connecting an LLM or an embedding model (local or external), or switching providers.

### Principles

- Program against the **framework abstractions**: `IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI`. The pipeline must not know which provider is behind them.
- Keep provider-specific code (SDKs, endpoints, special options) in **one factory class per provider**, with the same two methods: create the chat client and create the embedding generator.
- Use one **settings record per provider** (endpoint, chat model, embedding model, calibrated `MinScore`). The API key is **not** in the record, so printing it never leaks it.
- Select the provider by configuration (for example `Rag:Provider`). Build only the selected provider, so an offline run does not need another provider's key.
- Group what the pipeline needs into one object (name, chat client, embedder, `MinScore`), so the model and its threshold cannot get out of sync.

### Extension methods

`GenerateVectorAsync(string)` and `GetResponseAsync(string)` are extension methods in `Microsoft.Extensions.AI`. Without `using Microsoft.Extensions.AI;` the compiler only sees the lower-level overloads and reports confusing errors.

### OpenAI-compatible endpoints (for example Gemini)

Verify the current API names first. At the time this file was written, the pattern was:

```csharp
var client = new OpenAIClient(
    new ApiKeyCredential(apiKey),
    new OpenAIClientOptions { Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/") });

IChatClient chat = client.GetChatClient("model-name").AsIChatClient();
IEmbeddingGenerator<string, Embedding<float>> embedder = client.GetEmbeddingClient("embedding-model").AsIEmbeddingGenerator();
```

- Package: `Microsoft.Extensions.AI.OpenAI`. Older code uses `AsChatClient`, which was renamed to `AsIChatClient`.
- Keep the **trailing slash** on the endpoint.
- Keep `Microsoft.Extensions.AI.Abstractions` at the version the OpenAI package requires, or you get `NU1605`.

### Ollama (local)

- Use OllamaSharp. `OllamaApiClient` implements both `IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>`, so it can be returned directly.
- The model name is part of the client, so create one client per model (chat and embeddings).
- The default `HttpClient` timeout of 100 seconds is too short for a cold model start. Pass your own `HttpClient` with a longer timeout (for example 5 minutes) and the base address.
- Check models with `ollama list`, loaded models and CPU/GPU split with `ollama ps`.
- **Thinking models** (for example `qwen3`) return their reasoning as a separate content part (`TextReasoningContent`). `response.Text` contains only the answer. Do not try to disable thinking with a request flag unless you tested it on that exact model: some thinking-only variants then write the reasoning into the answer text.

### Quotas, limits and errors

- Free tiers have small, changing quotas (per minute, per day, per project). Check the provider's usage page. Design tests so they spend few chat requests: calibrate with a fake chat client, and let the guardrail stop out-of-scope questions before any call.
- SDKs retry transient errors, so an HTTP 429 that still reaches your code usually means the quota is used up, not a short burst.
- Catch only the errors you expect (HTTP errors, no connection, timeouts), with a `when` filter, and show one readable line. Let real bugs crash with a stack trace.
- A failed model call during startup (for example indexing) needs its own handling, because it happens outside the question loop.

---

## Skill: agent-framework

**Use when:** creating agents with the Microsoft Agent Framework (MAF), adding instructions, tools, memory or sessions.

- **Verify names and signatures against the installed package version.** MAF is evolving quickly, and helper methods are renamed between versions (for example `new ChatClientAgent(...)` versus extension methods that create an agent from an `IChatClient`).
- A `ChatClientAgent` wraps any `IChatClient`. The `instructions` parameter is the system prompt. The agent's `RunAsync(string)` returns a response whose `Text` property holds the answer.
- Create agents in a small factory class that takes an `IChatClient`. Then the agent setup (instructions, name, tools, memory) lives in one place.
- Keep instructions as a named constant or setting, not hidden inside a method call.
- Keep retrieval and prompt building **outside** the agent at first (explicit, testable steps). Move to tools or context providers only when you need the agent to decide when to search, and then test the effect on answer quality.
- An agent that can call tools can act on text it reads. Treat retrieved text as data (see "Security").
- For conversations over several turns, use the framework's session or thread concept. Do not accumulate history in your own strings.

---

## Skill: vector-storage

**Use when:** storing embeddings in a database (PostgreSQL with pgvector, or another vector store). This skill is forward-looking. Verify every detail against the current library and database documentation.

- Store, per chunk: id, source, text, vector, and the **embedding model name and dimension**.
- The vector column dimension must equal the embedding model's dimension. Different models need different columns or tables, or a full re-index when you switch.
- Check the **index size limits** of your pgvector version for the vector type you use, because high-dimensional vectors (several thousand) may not be indexable with the plain type. Options when it matters: a lower-dimension model, reduced output dimensions if the model supports it, or another vector type. (Verify before choosing.)
- pgvector uses distance operators, not similarity. Cosine distance and cosine similarity are related (`similarity = 1 - distance`). Convert so that your `MinScore` keeps its meaning.
- Use a concrete store class behind the same search contract as the in-memory retriever (question vector in, ordered hits out).
- Use migrations or a schema script, and a local container for development.
- Keep connection strings in secrets, not in code.

---

## Skill: configuration-and-secrets

**Use when:** reading settings, adding API keys, or preparing a repository for commits.

- Layer the configuration, later layers win: `appsettings.json`, then a gitignored `appsettings.Local.json`, then environment variables. In environment variables, a double underscore replaces the colon (`Rag__Provider`).
- Put the committed, non-secret values in `appsettings.json`. Put keys only in the gitignored file or in environment variables (or a secret manager in production).
- Bind sections into records. Fail fast with a clear message when a required section is missing.
- Add the secret file to the repository root `.gitignore` before creating it, and verify with `git check-ignore -v <path>`. A tracked file stays tracked even if it is listed in `.gitignore`.
- If a key was ever committed or pasted somewhere public, treat it as leaked and rotate it.
- Mark the files that must be available at run time with `CopyToOutputDirectory` in the `.csproj`.
- Never print keys. Settings records that contain a key must not be written to logs.

---

## Skill: testing-ai-apps

**Use when:** writing tests or manual checks for RAG and agent code.

- Keep **fakes** behind the real framework interfaces: a deterministic embedder (hashed bag of words) and a fake chat client. They make the pipeline testable offline, without keys, cost or flaky output.
- Unit-test the deterministic parts: tokenizer, hasher, sanitizer, chunker, prompt builder, retriever (with the fake embedder), error mapping.
- Do not call real LLMs in unit tests. Use a separate, small, manual or opt-in check for the real model.
- Test the guardrail and the "chunk found but answer not in it" case.
- Test error paths: server not running, wrong model name, missing key, unknown provider.
- Keep the evaluation question set (see `rag-pipeline`) in the repository and re-run it after changes.
- If a test project is added, use xUnit and the fakes. Do not mock your own types heavily.

---

## Common pitfalls (seen in practice)

- Appending `Question:` inside the loop over sources, so it appears once per source. Add it once after the loop.
- Splitting paragraphs on `"\n\n"` only, which fails for Windows line endings (`\r\n`).
- Reusing a `MinScore` from another embedding model. Everything is rejected, or everything is accepted.
- Missing `using Microsoft.Extensions.AI;`, which hides extension methods.
- Package version conflicts (`NU1605`) after adding a provider package.
- Default `HttpClient` timeout (100 seconds) too short for local models.
- Running with `dotnet watch`: every save restarts the app and repeats demo questions, which burns quota. Interactive console input also behaves badly.
- Assuming a free-tier quota from documentation or memory. Read the usage page.
- A file that is not copied to the output folder (`knowledge.md`, `appsettings.json`).
- A fake chat client that answers with the first line of the first source. Judge retrieval by the printed scores and chunk ids.
- Disabling thinking on a model that always thinks, which puts the reasoning into the answer text.

---

## Definition of done for a change

1. It builds without warnings you introduced.
2. The class has one job and its methods are short.
3. There is a quick check or test with the expected output.
4. Behavior changes compared with the previous version are named.
5. Secrets are not in code, logs or commits.
6. Anything you could not verify is marked as unverified.
7. Docs (`README.md`, `DEVELOPER_GUIDE.md`) are updated if setup, classes or commands changed.

---

# Project overrides: VadesDev.Rag

`VadesDev.Rag` is a **learning project**. These rules override the skills above where they differ.

## Precedence

Constraints > Instructions (workflow) > Project overrides (this section) > Skills.

## Constraints

- Best practices, but simple. Keep it simple.
- Single responsibility per class and per method.
- Methods between 20 and 30 lines (shorter is fine).
- **No interfaces of our own.** The framework interfaces (`IChatClient`, `IEmbeddingGenerator`) may be used and implemented. (The skills above would normally add interfaces for testability and dependency injection. Here we use concrete classes and the fakes.)
- No repository pattern, also not with Entity Framework or pgvector.
- No over-engineering: no extra projects, layers or settings before a phase needs them.
- No secrets in code.

## Workflow: one class at a time

For every class, never skip a step:

1. Propose the **structure** only: class, fields, constructor, method signatures, placeholder bodies and XML summaries. Explain it briefly (responsibility and notes).
2. Wait for approval.
3. Write the **method bodies** one by one, then the full file for copy and paste.
4. Give a **test snippet** with the expected output.
5. Wait for "works" or "approved" before starting the next class.

Rules for the steps:

- One class per step. Do not describe several classes at once.
- Order: folder structure, class structure, bodies, test, next class.
- Declarations that are already final (records, constants, regex attributes, constructors) are shown in the structure step and marked as final. Records and methods without logic (`GetService`, `Dispose`) skip the placeholder stage.
- Placeholder values: string `""`, collection `[]`, bool `false`, number `0`, `Task.CompletedTask`, object `null!`. Avoid compiler warnings in placeholders, or mention them.
- Explain **why** briefly for each design decision, and flag every intentional behavior change.
- End each step with one approval question and the name of the next class.
- After each phase, give a short summary: new files, flow, decisions, known limits, next phase.

## Structure and style

- Solution `VadesDev`, project `VadesDev.Rag` (project folder directly under the solution, no `src/` folder). Namespaces start with `VadesDev.Rag.<Folder>`.
- Folder = pipeline stage (`Models`, `Configuration`, `Documents`, `Embeddings`, `Search`, `Prompting`, `Chat`, `Agents`, `Providers`, `Cli`). One type per file. No `Services`, `Helpers` or `Utils` folders.
- `Program.cs` is wiring only.
- Classes are `public`. Stateless classes are `static`. Framework implementations are `sealed`.
- **Every class and method has an XML `<summary>`** (user decision, for tooltips).
- Use `""` instead of `string.Empty`.
- Only `CancellationToken`-free signatures in the console app (Ctrl+C ends the process). Add tokens in Phase 7, where HTTP requests can be cancelled.

## Project-specific design rules

- Pipeline order: read, chunk, sanitize each chunk, embed, index, search, prompt, agent.
- `MinScore` lives in each provider's settings. `RagSettings.MinScore` has no default.
- One client factory per provider (`CreateChatClient`, `CreateEmbeddingGenerator`), one settings record per provider, and `AiProviderFactory` selects by `Rag:Provider`.
- `ChunkIndexer` returns the index list and `Program.cs` passes it to `ChunkRetriever`. There is no hidden shared state. In Phase 5 only these two classes change, and `Search(queryVector)` keeps its signature.
- The prompt numbering stays `[1]`, `[2]`, ... because the fake chat client reads `[1]`.
- The fakes stay in the project as offline test doubles.
- Use `dotnet run`, not `dotnet watch`.

## Phases

1. Done: simple one-file app with dummy data
2. Done: split into folders, classes, methods
3. Done: read Markdown (reader, chunker, sanitizer)
4. Done: real LLM chat and embeddings (Ollama, Gemini)
5. Planned: PostgreSQL and pgvector
6. Planned: RAG MVP as a CLI
7. Planned: REST API with FastEndpoints (`RagPipeline.AskAsync` should return the answer and hits instead of printing)

## Open items

- Handle a startup failure (AI server not running during indexing) with a clear message (Phase 6).
- The chunker has no size limit or overlap.
- The sanitizer does not defend against prompt injection. Bullet markers and tables are not handled.
- Only one hard-coded knowledge file. Folder support comes in Phase 6.
- OllamaSharp's exception type for a wrong model name is not verified yet.
- `Knowledge/DummyKnowledgeBase` is unused and can be deleted.
- `DataIngestion` libraries from Microsoft may replace the own reader and chunker later. Check their state in Phase 6.