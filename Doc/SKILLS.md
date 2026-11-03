# SKILLS.md

Instructions for coding agents that build **RAG (retrieval-augmented generation)** and **agentic** applications with **.NET**.

The skills describe general best practices and can be reused in other projects. Project-specific rules live in a separate file: **`SKILLS_RAG.md`** (for `VadesDev.Rag`). If a project file exists, read it too. **On conflicts, the project file wins.**

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

**Use when:** storing embeddings in a database (PostgreSQL with pgvector, or another vector store). Verify library names and versions against the current documentation. The facts below were checked in practice with pgvector 0.8.x, Npgsql and the `Pgvector` NuGet package.

### Data model

- Store, per chunk: id, source, text, vector, and the **embedding model name**. Put the model name in the primary key, so vectors of different models never clash.
- Also store a **fingerprint** of the source's cleaned chunks (for example SHA-256 over ids and texts), so an unchanged source is not embedded again. Check it per source **and** per model.
- A vector column without a fixed size accepts vectors of different dimensions. The database stops with an error when one query compares different sizes, so **every search must filter by the model and by the vector size** (`vector_dims(embedding) = <size>`).
- Replace the rows of one source in **one transaction** (delete the old rows, insert the new ones). A failed run must never leave a half-filled index.

### Search and similarity

- pgvector returns **distances**, not similarities. The cosine distance operator is `<=>`, and `similarity = 1 - distance`. Convert, so a calibrated `MinScore` keeps its meaning.
- Keep the policy (threshold, number of results) outside the store. The store returns the nearest rows, sorted. The retriever applies `MinScore` and `TopK`.
- The search is I/O, so it is `async`. Keep the contract stable: question vector in, ordered hits with scores out.

### Indexes

- Without an index, the search is an **exact scan**. It is fast for small data and gives the same scores as an in-memory calculation, so thresholds calibrated earlier stay valid.
- **Index size limits:** the plain `vector` type can be indexed only up to **2000 dimensions**, and `halfvec` up to **4000**. Vectors can be stored up to 16000 dimensions without an index. Options for larger vectors: an expression index with a `halfvec` cast (use the same cast in the query), a model with smaller vectors, or reduced output dimensions if the model supports it.
- Approximate indexes (HNSW, IVFFlat) can change results. Recalibrate the threshold after you add one.

### Access from .NET

- Use Npgsql with the `Pgvector` package. Enable the type mapping with `UseVector()` on the `NpgsqlDataSourceBuilder`, and pass vectors as `Vector` objects with positional parameters (`$1`, `$2`, ...). Read them back as `Vector` and call `ToArray()`.
- Create the `NpgsqlDataSource` once, share it, and dispose it at the end. Use short-lived connections and commands.
- Plain SQL is enough for a few statements. Use an ORM for the relational part of the app, not for vector search.
- Create the extension (`CREATE EXTENSION IF NOT EXISTS vector;`) with an init script. The pgvector images only install it.
- Keep connection strings and passwords in secrets, not in code or committed files.

### Local development with Docker

- Use a pgvector image (for example `pgvector/pgvector:pg17`), a named volume, a healthcheck, a port bound to `127.0.0.1`, and a gitignored `.env` for the password.
- Init scripts (`/docker-entrypoint-initdb.d`) run only on the first start with an empty volume. A changed password in `.env` does not change the stored one.
- PostgreSQL 18 images use a versioned data path. Check the image documentation before choosing the volume mount.
- `docker compose down -v` deletes the data. Do not run it casually.

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
- Package version conflicts (`NU1605`) after adding a provider or database package.
- Default `HttpClient` timeout (100 seconds) too short for local models.
- Running with `dotnet watch`: every save restarts the app and repeats demo questions, which burns quota. Interactive console input also behaves badly.
- Assuming a free-tier quota from documentation or memory. Read the usage page.
- A file that is not copied to the output folder (`knowledge.md`, `appsettings.json`).
- A fake chat client that answers with the first line of the first source. Judge retrieval by the printed scores and chunk ids.
- Disabling thinking on a model that always thinks, which puts the reasoning into the answer text.
- Changing a method to async (for example `Search` to `SearchAsync`) without updating every caller.
- Comparing vectors of different sizes in one query. Filter by model and vector size.
- Putting a secret file under version control: Git does not apply `.gitignore` to files that are already staged or tracked. Add the rule first, and check with `git check-ignore -v`.
- Expecting database init scripts to run again on an existing volume, or a changed `.env` password to change the stored one.
- Skipping embedding based on a fingerprint without including the model name. Another model must always embed again. A model that changes behind the same name is not noticed, so use explicit version tags.
- Treating the first test as proof of the calibration. Recalibrate when content grows, and compare scores before and after every storage or chunking change.

## Definition of done for a change

1. It builds without warnings you introduced.
2. The class has one job and its methods are short.
3. There is a quick check or test with the expected output.
4. Behavior changes compared with the previous version are named.
5. Secrets are not in code, logs or commits.
6. Anything you could not verify is marked as unverified.
7. Docs (`README.md`, `DEVELOPER_GUIDE.md`) are updated if setup, classes or commands changed.
