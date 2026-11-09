# Evaluation questions

A small fixed set of questions for `Data/knowledge.md`. Re-run it after every change to the chunker, the sanitizer, the embedding model, `MinScore`, the prompt, the agent instructions or the storage.

This file is documentation for humans and coding agents. The app does not read it, so it does not need to be copied to the output folder.

## Assumptions

- `Data/knowledge.md` has five sections in this order: Refund Policy, Support, Shipping, Account, Security. That gives the chunk ids `knowledge.md-1` to `knowledge.md-5` in the same order.
- Scores were measured at the end of Phase 5, with the data coming from PostgreSQL (exact scan, no index).
- If you change `knowledge.md`, the chunk ids and scores can change. Update this file in the same commit.

| Chunk id | Section |
|---|---|
| `knowledge.md-1` | Refund Policy |
| `knowledge.md-2` | Support |
| `knowledge.md-3` | Shipping |
| `knowledge.md-4` | Account |
| `knowledge.md-5` | Security |

## How to run

Retrieval and generation are checked separately. Most RAG problems are retrieval problems, so start with retrieval.

**Retrieval check (no LLM, no quota)**
1. Use the real embedder of the provider you test, and `FakeExtractiveChatClient` as the chat client.
2. Set `new RagSettings(MinScore: 0f, TopK: 3)` so nothing is filtered, and ask every question.
3. Compare the top-1 chunk id and score with the tables below. For the same model and the same chunks, a difference of 0.001 is rounding. A bigger difference means something in the pipeline changed.
4. Switch back to the real settings (`MinScore` from `appsettings.json`). In-scope questions must keep their top-1 hit above the threshold, and out-of-scope questions must print `! No relevant context found.`

**Generation check (real LLM, costs time and quota)**
- Ask the in-scope questions with the real chat client and check that the answer contains the expected facts. Out-of-scope questions must not reach the LLM.
- Gemini free tier: only about 20 chat requests per day were available at the time of writing. Use few questions, or test generation with Ollama.

## In scope: the answer is in a chunk

| # | Question | Expected top chunk | Ollama top-1 (MinScore 0.40) | Gemini top-1 (MinScore 0.60) | Answer must contain |
|---|---|---|---|---|---|
| 1 | What are your support hours? | `knowledge.md-2` | 0.665 | 0.747 | Monday through Friday, 9 AM to 5 PM CET |
| 2 | How do I reset my password? | `knowledge.md-4` | 0.771 | 0.740 | "Forgot Password" on the login screen, check the email inbox |
| 3 | Do you support 2FA? | `knowledge.md-5` | 0.789 | 0.792 | Yes, with an authenticator app |
| 4 | When can I reach your team? | `knowledge.md-2` | 0.462 | 0.704 | Monday through Friday, 9 AM to 5 PM CET |
| 5 | How long until my package arrives? | `knowledge.md-3` | 0.548 | 0.659 | 3 to 5 business days within the EU |

Notes:
- Question 4 has no keyword in common with the chunk. It is the paraphrase test, and it is the weakest score for Ollama (0.462, only 0.062 above the threshold).
- Question 3 with Gemini also returns `knowledge.md-4` as a second hit (0.603, just above 0.60). That is harmless, but it shows how close the threshold is.
- Question 5 with Gemini: retrieval is correct, but the model once answered "I don't know" although the chunk was found. Ollama answered normally. Treat this as a generation problem, not a retrieval problem.

## In scope, but the chunk only answers part of it

| # | Question | Expected top chunk | Ollama top-1 | Gemini top-1 | Expected behavior |
|---|---|---|---|---|---|
| 6 | Can I get my money back? | `knowledge.md-1` | 0.580 | 0.654 | Retrieval finds the Refund chunk. The chunk only gives the 30-day window and not the person's purchase date, so a cautious answer is acceptable: it mentions the 30 days and says the context does not decide the case |
| 7 | an I get my money back? (typo) | `knowledge.md-1` | not measured | 0.657 | Same as question 6. Checks that a small typing mistake does not break retrieval |

Observed: both models were cautious on question 6. `qwen3:4b` answered with a long explanation that the context is insufficient, and Gemini said "I don't know" and added the 30-day fact.

## Out of scope: the guardrail must stop them

| # | Question | Ollama top-1 | Gemini top-1 | Expected behavior |
|---|---|---|---|---|
| 8 | Who is the CEO of the company? | 0.305 | 0.554 | `! No relevant context found. Answer: I don't know.` with no LLM call |
| 9 | Do you sell pizza? | 0.299 | 0.534 | same |
| 10 | What is the capital of France? | 0.233 | 0.539 | same |

These are the strongest false matches. The gap to the weakest in-scope question is 0.157 for Ollama (0.305 vs 0.462) and 0.103 for Gemini (0.554 vs 0.657). If a change makes the gap smaller than about 0.05, recalibrate `MinScore`.

## Known weak spot: very short queries (Ollama)

| Query | Ollama result at MinScore 0.40 | Gemini |
|---|---|---|
| Support hours | `knowledge.md-2` 0.649 | not measured |
| Password | `knowledge.md-4` 0.583 | not measured |
| 2FA | `knowledge.md-5` 0.657 | not measured |
| Money back | `knowledge.md-1` 0.519 | not measured |
| Reach your team | rejected (below 0.40) | not measured |
| Package | rejected (below 0.40) | not measured |

Two of six short queries are rejected with `embeddinggemma`, and the second hits of the other queries sit only 0.001 to 0.006 above the threshold. This is a known limit and not a failure. Query rewriting for short queries is on the backlog.

## Not run yet

| Question | Why it matters |
|---|---|
| How much does shipping cost? | The Shipping chunk is found, but the price is not in it. The model must say it does not know. This is the "chunk found, answer not in it" case that tests whether a model obeys the instructions |
| A question that matches two sections (for example refund and shipping together) | Checks that `TopK = 2` brings both chunks and that the prompt numbers them `[1]` and `[2]` |

## Adding questions

Add a row whenever a bug or a surprise shows up (a wrong answer, a rejected question, a score that moved). Write down the question, the expected chunk, both scores and what the answer must contain. Keep the file small: about 10 to 15 questions are enough for a knowledge base of this size.
