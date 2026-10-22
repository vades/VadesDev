# VadesDev.Rag

A learning project: build a **RAG (retrieval-augmented generation)** application step by step with **.NET 10**, **C# 14** and the **Microsoft Agent Framework (MAF)**.

The app reads a knowledge file, finds the passages that match a question, and lets an LLM answer **only from those passages**. If nothing relevant is found, it says "I don't know" and does not even call the LLM.

> This is a learning project. The code favors clarity over cleverness: small classes, short methods, no abstractions before they are needed.

---

## What it does today

```
Markdown file -> chunk -> sanitize -> embed -> index (in memory)
                                                    |
question -> embed -> search (cosine similarity, MinScore, TopK)
                                                    |
                          grounded prompt -> agent (LLM) -> answer
```

1. **Read** a Markdown file (`Data/knowledge.md`).
2. **Chunk** it by paragraph, joining each heading with the paragraph below it.
3. **Sanitize** each chunk: remove links, Markdown symbols, control characters and repeated spaces.
4. **Embed** the chunks with an embedding model and keep the vectors in memory.
5. **Search**: embed the question, compare it with every chunk (cosine similarity), keep the best matches above a threshold (`MinScore`).
6. **Guardrail**: no match above the threshold means no LLM call, and the answer is "I don't know".
7. **Answer**: build a prompt with numbered sources and ask the agent. The agent's instructions say to answer only from the context.

Example session (Ollama):

```
Provider: Ollama (MinScore 0,4)
Indexing...
Indexed 5 chunks (768 dimensions).

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
| Vector math | `System.Numerics.Tensors` (`TensorPrimitives`) |
| Configuration | `Microsoft.Extensions.Configuration` (JSON files and environment variables) |
| Planned | PostgreSQL with pgvector, FastEndpoints |

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
- **Measure, don't guess.** For example, `MinScore` is calibrated per embedding model with real questions.
- **No secrets in code.** API keys live in a gitignored file or in environment variables.

---

## Development phases

| Phase | Goal | Status |
|---|---|---|
| 1 | Simple one-file app with dummy data and fake AI | Done |
| 2 | Split the code into folders, classes and methods | Done |
| 3 | Read text from a Markdown file (reader, chunker, sanitizer) | Done |
| 4 | Real LLM chat and real embeddings (Ollama and Gemini) | Done |
| 5 | PostgreSQL and pgvector to store the data | Planned |
| 6 | RAG MVP as a CLI application | Planned |
| 7 | REST API with FastEndpoints | Planned |

---

## Project layout

```
VadesDev.Rag/
├── Program.cs                  wiring only
├── appsettings.json            provider choice, models, MinScore per provider
├── Data/knowledge.md           the knowledge file
├── Models/                     DocumentChunk, IndexedChunk, SearchHit, SourceDocument
├── Configuration/              AppConfiguration, RagSettings, GeminiSettings, OllamaSettings
├── Documents/                  MarkdownFileReader, DocumentChunker, TextSanitizer
├── Embeddings/                 fake embedder (HashingEmbeddingGenerator), TextTokenizer, Fnv1aHasher
├── Search/                     ChunkIndexer, ChunkRetriever
├── Prompting/                  GroundedPromptBuilder
├── Providers/                  GeminiClientFactory, OllamaClientFactory, AiProvider, AiProviderFactory
├── Chat/                       fake chat client (FakeExtractiveChatClient)
├── Agents/                     RagAgentFactory
├── Cli/                        RagPipeline, ConsoleRunner, ProviderErrors
└── Knowledge/                  DummyKnowledgeBase (unused, kept as test data)
```

The fake embedder and the fake chat client work offline. They stay in the project as test doubles, for example to calibrate the search without waiting for a real LLM.

---

## Quick start

**Prerequisites**
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Either **Ollama** (local) or a **Gemini API key** (external)

**Option A: local with Ollama**

```bash
ollama pull qwen3:4b
ollama pull embeddinggemma
```

Make sure the Ollama server is running (`http://localhost:11434`), then:

```bash
cd VadesDev.Rag
dotnet run
```

`appsettings.json` uses `Ollama` by default (`Rag:Provider`).

**Option B: external with Gemini**

Create `appsettings.Local.json` next to `appsettings.json` (it is gitignored, never commit it):

```json
{
  "Gemini": {
    "ApiKey": "your-key-here"
  }
}
```

Then switch the provider and run:

```bash
# PowerShell
$env:Rag__Provider = "Gemini"
dotnet run
```

**Notes**
- Use `dotnet run`, not `dotnet watch`: the interactive input does not work well under `dotnet watch`.
- Free Gemini tiers have small and changing quotas. On the author's project, the chat model allowed only 20 requests per day at the time of writing. Out-of-scope questions cost no chat request, because the guardrail stops them first.
- Press Enter on an empty line to exit.

---

## Configuration

| Setting | Meaning |
|---|---|
| `Rag:Provider` | `Ollama` or `Gemini` |
| `<Provider>:Endpoint` | Server address |
| `<Provider>:ChatModel` | Model used for answers |
| `<Provider>:EmbeddingModel` | Model used for vectors |
| `<Provider>:MinScore` | Minimum cosine similarity to accept a chunk. Calibrated for that embedding model |
| `Gemini:ApiKey` | Only in `appsettings.Local.json` or the `Gemini__ApiKey` environment variable |

Values are read in layers, and later layers win: `appsettings.json`, then `appsettings.Local.json`, then environment variables (a double underscore replaces the colon, for example `Rag__Provider`).

> `MinScore` depends on the embedding model. Different models give different score ranges (0.60 works for `gemini-embedding-001`, 0.40 for `embeddinggemma`). If you change the embedding model, calibrate again.

---

## Known limits

- The index is in memory and is rebuilt on every start (changes with Phase 5).
- One fixed knowledge file, no folder support yet (Phase 6).
- The chunker splits by paragraph only: no size limits and no overlap.
- Very short queries (one or two words) match less reliably with the local embedding model.
- The sanitizer removes formatting noise but does **not** protect against prompt injection.
- If the AI server is not reachable at startup, the indexing step ends with an error message from the framework.

---

## More documentation

- [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md): how the classes work, how to test and extend the project.
- [SKILLS.md](SKILLS.md): best practices for coding agents that build RAG and agent apps with .NET.
- [SKILLS_RAG.md](SKILLS_RAG.md): project-specific rules, decisions and workflow for coding agents working on this repository.
