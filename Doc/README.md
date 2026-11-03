# VadesDev.Rag

A learning project: build a **RAG (retrieval-augmented generation)** application step by step with **.NET 10**, **C# 14** and the **Microsoft Agent Framework (MAF)**.

The app reads a knowledge file, stores it as vectors in **PostgreSQL with pgvector**, finds the passages that match a question, and lets an LLM answer **only from those passages**. If nothing relevant is found, it says "I don't know" and does not even call the LLM.

> This is a learning project. The code favors clarity over cleverness: small classes, short methods, no abstractions before they are needed.

---

## What it does today

```
INGESTION (at startup, skipped when the file is unchanged)
Markdown file -> chunk -> sanitize -> fingerprint -> embed -> store in PostgreSQL (pgvector)

QUESTION
question -> embed -> nearest chunks from PostgreSQL (cosine similarity)
         -> keep hits >= MinScore, at most TopK
         -> guardrail (nothing left = "I don't know", no LLM call)
         -> grounded prompt -> agent (LLM) -> answer
```

1. **Read** a Markdown file (`Data/knowledge.md`).
2. **Chunk** it by paragraph, joining each heading with the paragraph below it.
3. **Sanitize** each chunk: remove links, Markdown symbols, control characters and repeated spaces.
4. **Fingerprint** the cleaned chunks (SHA-256). If the file is already stored for the current embedding model with the same fingerprint, embedding is **skipped**.
5. **Embed and store** the chunks with an embedding model into the table `rag.chunks`, in one transaction.
6. **Search**: embed the question and let PostgreSQL return the nearest chunks of the same embedding model (exact cosine search, no index).
7. **Guardrail**: no hit at or above the threshold (`MinScore`) means no LLM call, and the answer is "I don't know".
8. **Answer**: build a prompt with numbered sources and ask the agent. The agent's instructions say to answer only from the context.

Example session (Ollama, second start):

```
Provider: Ollama (model embeddinggemma:latest, MinScore 0,4)
Up to date: knowledge.md (5 chunks), embedding skipped.

? What are your support hours?
  retrieved [knowledge.md-2] score=0,665  (knowledge.md)
  Answer: Monday through Friday, 9 AM to 5 PM CET.

? Do you sell pizza?
  ! No relevant context found. Answer: I don't know.
```

---

## Technology

| Area | Technology |
|---|---|
| Runtime and language | .NET 10, C# 14 |
| Agent framework | Microsoft Agent Framework (`Microsoft.Agents.AI`) |
| AI abstractions | `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`) |
| Local LLM | [Ollama](https://ollama.com) via OllamaSharp (`qwen3:4b` for chat, `embeddinggemma` for embeddings) |
| External LLM | Google Gemini via its OpenAI-compatible endpoint (`gemini-2.5-flash`, `gemini-embedding-001`) |
| Vector database | PostgreSQL 17 with pgvector, in Docker (image `pgvector/pgvector:pg17`) |
| Database access | Npgsql and the `Pgvector` package, plain SQL |
| Vector math (fake embedder) | `System.Numerics.Tensors` |
| Configuration | `Microsoft.Extensions.Configuration` (JSON files and environment variables) |
| Planned | FastEndpoints (REST API), Entity Framework Core and ASP.NET Core Identity (users, chat history) |

The pipeline only knows the framework interfaces `IChatClient` and `IEmbeddingGenerator`. That is why switching between Ollama and Gemini is a configuration change, not a code change.

---

## Goals and principles

**What we want to achieve**
- Understand every part of a RAG pipeline by building it in small, testable steps.
- Learn the Microsoft Agent Framework and `Microsoft.Extensions.AI` with real providers, local and external.
- End with a small but complete app: a CLI first, then a REST API on top of a database.

**Principles**
- **Keep it simple.** Best practices, but no over-engineering.
- **Single responsibility.** One class, one job. One method, one job.
- **Short methods.** About 20 to 30 lines at most.
- **No interfaces of our own.** Only the framework interfaces are used and implemented.
- **One class at a time.** Structure first, then method bodies, then a test.
- **Measure, don't guess.** For example, `MinScore` is calibrated per embedding model with real questions, and the database scores are compared with the in-memory scores.
- **Plain SQL for vectors.** Entity Framework is planned only for the relational part (Phase 8).
- **No secrets in code.** API keys and passwords live in gitignored files or in environment variables.

---

## Development phases

| Phase | Goal | Status |
|---|---|---|
| 1 | Simple one-file app with dummy data and fake AI | Done |
| 2 | Split the code into folders, classes and methods | Done |
| 3 | Read text from a Markdown file (reader, chunker, sanitizer) | Done |
| 4 | Real LLM chat and real embeddings (Ollama and Gemini) | Done |
| 5 | PostgreSQL and pgvector to store the data | Done |
| 6 | RAG MVP as a CLI application | Planned |
| 7 | REST API with FastEndpoints | Planned |
| 8 | Relational database with Entity Framework Core (users, login, chat history) | Planned |

---

## Project layout

```
VadesDev/                           solution folder (VadesDev.slnx)
├── docker-compose.yaml             PostgreSQL + pgvector for local development
├── .env                            local database credentials (gitignored)
├── .env.example                    template for .env
├── db/init/01-init.sql             extension vector, schemas app and rag (first start only)
└── VadesDev.Rag/
    ├── Program.cs                  wiring only
    ├── appsettings.json            provider choice, models, MinScore per provider
    ├── appsettings.Local.json      secrets: Gemini key, connection strings (gitignored)
    ├── Data/knowledge.md           the knowledge file
    ├── Models/                     DocumentChunk, IndexedChunk, SearchHit, SourceDocument
    ├── Configuration/              AppConfiguration, RagSettings, GeminiSettings, OllamaSettings
    ├── Documents/                  MarkdownFileReader, DocumentChunker, TextSanitizer
    ├── Embeddings/                 fake embedder (HashingEmbeddingGenerator), TextTokenizer, Fnv1aHasher
    ├── Search/                     ChunkIndexer, ChunkHasher, ChunkRetriever
    ├── Storage/                    PostgresDataSourceFactory, PostgresChunkStore (all SQL)
    ├── Prompting/                  GroundedPromptBuilder
    ├── Providers/                  GeminiClientFactory, OllamaClientFactory, AiProvider, AiProviderFactory
    ├── Chat/                       fake chat client (FakeExtractiveChatClient)
    ├── Agents/                     RagAgentFactory
    ├── Cli/                        IngestionPipeline, RagPipeline, ConsoleRunner, ProviderErrors
    └── Knowledge/                  DummyKnowledgeBase (test data only)
```

The fake embedder and the fake chat client work offline. They stay in the project as test doubles, for example to test the database classes or to calibrate the search without waiting for a real LLM.

---

## Quick start

**Prerequisites**
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (on Windows with the WSL 2 backend)
- Either **Ollama** (local) or a **Gemini API key** (external)

**1. Start the database**

In the solution folder, create `.env` from the template and set a password:

```
POSTGRES_USER=vadesdev
POSTGRES_PASSWORD=choose-a-strong-password
POSTGRES_DB=vadesdev
```

```bash
docker compose up -d
docker compose ps        # the container must be "healthy"
```

The first start creates the extension `vector` and the schemas `app` and `rag`. See [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) for checks and daily commands.

**2. Create `VadesDev.Rag/appsettings.Local.json`** (gitignored, never commit it)

```json
{
  "ConnectionStrings": {
    "Rag": "Host=localhost;Port=5432;Database=vadesdev;Username=vadesdev;Password=your-password",
    "App": "Host=localhost;Port=5432;Database=vadesdev;Username=vadesdev;Password=your-password"
  },
  "Gemini": {
    "ApiKey": "your-key-here"
  }
}
```

The `Gemini` section is only needed for Gemini. `ConnectionStrings:App` is reserved for Phase 8.

**3. Choose the provider and run**

*Option A: local with Ollama (the default in `appsettings.json`)*

```bash
ollama pull qwen3:4b
ollama pull embeddinggemma
cd VadesDev.Rag
dotnet run
```

*Option B: external with Gemini*

```bash
# PowerShell
$env:Rag__Provider = "Gemini"
dotnet run
```

**Notes**
- The first start embeds the file and stores it. Later starts with an unchanged file print `Up to date` and make no embedding request.
- Use `dotnet run`, not `dotnet watch`: the interactive input does not work well under `dotnet watch`.
- Free Gemini tiers have small and changing quotas. On the author's project, the chat model allowed only 20 requests per day at the time of writing. Out-of-scope questions cost no chat request, because the guardrail stops them first.
- A thinking model such as `qwen3:4b` needs roughly 20 seconds or more per answer on modest hardware.
- Press Enter on an empty line to exit.

---

## Configuration

| Setting | Meaning |
|---|---|
| `Rag:Provider` | `Ollama` or `Gemini` |
| `<Provider>:Endpoint` | Server address |
| `<Provider>:ChatModel` | Model used for answers |
| `<Provider>:EmbeddingModel` | Model used for vectors. Its name is part of the key in the database |
| `<Provider>:MinScore` | Minimum cosine similarity to accept a chunk. Calibrated for that embedding model |
| `Gemini:ApiKey` | Only in `appsettings.Local.json` or the `Gemini__ApiKey` environment variable |
| `ConnectionStrings:Rag` | PostgreSQL connection for the vector data. Only in `appsettings.Local.json` or the `ConnectionStrings__Rag` environment variable |
| `ConnectionStrings:App` | Reserved for the relational data (Phase 8) |

Values are read in layers, and later layers win: `appsettings.json`, then `appsettings.Local.json`, then environment variables (a double underscore replaces the colon, for example `Rag__Provider`).

> `MinScore` depends on the embedding model: 0.60 works for `gemini-embedding-001`, 0.40 for `embeddinggemma`. If you change the embedding model, calibrate again.

---

## The database

- **One PostgreSQL server in Docker, one database, two schemas:** `rag` (chunks and vectors, plain SQL) and `app` (reserved for users and chat history in Phase 8).
- **One table `rag.chunks`** holds the vectors of all embedding models. The model name is part of the primary key, and every search filters by model and by vector size. This allows Ollama (768 dimensions) and Gemini (3072 dimensions) to share the table.
- **No vector index.** With a small knowledge base, an exact scan is fast, and it gives exactly the same scores as the in-memory version of earlier phases. pgvector can only index plain `vector` columns up to 2000 dimensions, so a larger index would need another type or smaller vectors.
- **Data survives restarts** in a Docker volume. `docker compose down -v` deletes it.

---

## Known limits

- One fixed knowledge file, no folder support yet (Phase 6).
- The fingerprint does not notice when the model behind a name changes (for example after `ollama pull`). Use an explicit version tag in `appsettings.json`, or clear the table.
- A file that is removed or becomes empty keeps its old rows in the database.
- If the database is not reachable at startup, the app ends with an error from the framework (planned for Phase 6). A stopped AI server is handled per question with a readable message.
- The chunker splits by paragraph only: no size limits and no overlap.
- Very short queries (one or two words) match less reliably with the local embedding model.
- The strict system prompt makes models refuse when the chunk does not contain the complete answer, for example a question about a purchase date. An evaluation question set is on the backlog.
- The sanitizer removes formatting noise but does **not** protect against prompt injection.

---

## More documentation

- [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md): how the classes work, how to test and extend the project, Docker commands and troubleshooting.
- [SKILLS.md](SKILLS.md): best practices for coding agents that build RAG and agent apps with .NET.
- [SKILLS_RAG.md](SKILLS_RAG.md): project-specific rules, decisions and workflow for coding agents working on this repository.
