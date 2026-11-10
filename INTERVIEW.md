# Interview answers

## Phase 0 — Setup & first chat

### Walk me through what happens when a user asks your AI assistant a question.

The user types a question in the Angular app. The chat service sends it with `fetch` to `/api/chat`; in development the Angular dev server proxies that to the .NET API, so there is no CORS setup.

In the API, a `ChatController` receives the request and calls `IChatClient` from Microsoft.Extensions.AI. The controller doesn't know which model it talks to: the client is registered in a separate `Infrastructure.Ai` project, which is the only place that knows it's Ollama. Switching to Azure OpenAI later is a registration change, not a code change in the API.

The answer comes back as a stream. The API returns it as Server-Sent Events, one small JSON object per piece of text. I send objects instead of plain strings because plain strings lost their leading spaces in parsing, and objects let me add other event types later, like citations or tool status. The Angular service reads the stream chunk by chunk and appends each piece to a signal, so the answer appears word by word. If the user presses Stop, the request is aborted, the cancellation token reaches the controller, and the model stops generating.

Everything runs under Aspire, so one trace shows the whole path, including the model call with its token counts and duration.

**What I'd improve next:** the model has no real payroll data yet. In a test it explained a payslip with US taxes and health insurance that didn't exist in the data: the maths was right, the explanation was invented. The fix is grounding: a tool that returns the actual payslip breakdown, and retrieval over the policy documents.

## Phase 1 — How LLMs work

## Phase 2 — Microsoft.Extensions.AI

## Phase 3 — Embeddings & vector search

## Phase 4 — RAG

## Phase 5 — Evaluation

## Phase 6 — Agents

## Phase 7 — MCP

## Phase 8 — Multi-agent workflows

## Phase 9 — Production on Azure
