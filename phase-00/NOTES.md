# Phase 0 notes

## How the app works
- One question = 3 model calls: decide to load docs -> decide to search -> write the answer. The model decides when to search, not my code. (Phase 6)
- `UseFunctionInvocation()` runs the tool loop; `ChatOptions.Tools` only describes the tools to the model. (Phase 2)
- Every call resends the whole conversation, so long chats cost more. Seen in the traces. (Phase 1)
- There is a hidden 4th call after each answer: the follow-up suggestions. It costs tokens every turn. (Phase 9 cost)
- RAG = retrieve relevant chunks -> add them to the prompt -> generate an answer with citations. (Phase 4)

## Things that broke and why
- A `string?` tool parameter became `["string","null"]` in the JSON schema; the Ollama client couldn't read it. A tool's C# signature is a contract with the model, and providers handle schemas differently. (Phase 2)
- The vector size must match the embedding model: all-minilm = 384, nomic-embed-text = 768, text-embedding-3-small = 1536. Changing the model means re-embedding everything. (Phase 3)
- Typed output (`GetResponseAsync<string[]>`) is not guaranteed: the suggestions call crashed the whole chat. An optional AI feature must never break the main one. (Phase 2)
- GitHub Models was retired on 30 July 2026; the old endpoint just answers "OK". Providers can disappear, so never depend on one. Check current status instead of trusting a tutorial or memory.

## Local models on my laptop
- Intel Iris Xe means Ollama runs 100% on the CPU, so it's slow. `ollama ps` shows it.
- Qwen3 "thinks" before answering by default; `/no_think` in the prompt turns that off. That's model-specific, so it belongs in config, not hard-coded. (Phase 1)
- Ollama's default context is 4096 tokens and silently cuts off the oldest text, including the system prompt. (Phase 1)
- Smaller model = faster but worse at tool calling. Staying on qwen3:4b. (Phase 5: measure it)
- Temperature 0 still doesn't give identical answers every time. (Phase 1)

## Tooling
- Aspire = docker-compose in C# + observability. AppHost declares resources; any `AddX` you declare gets started, even if nothing uses it.
- The Aspire dashboard -> Traces shows every model call, tool call, token count and duration. Look there first when debugging.
- Windows PowerShell 5.1: `curl` is not real curl and JSON quoting breaks. Use PowerShell 7 (`pwsh`) and `Invoke-RestMethod`.
- Open a new terminal after installing a CLI (to pick up PATH); no restart needed.
- Azure budgets only alert; they never stop spending. Delete unused deployments.

## Days 2–4: building PayPilot's skeleton

- Angular 22: `ng g s` creates `chat.ts` with no suffix; use `--type=service`. New `@Service()` decorator = `@Injectable({providedIn: 'root'})`, inject() only, no constructor DI.
- Angular 22 apps are zoneless by default: the UI updates through signals.
- `angular.json` in Angular 22 has no `serve.options`; add it yourself for `proxyConfig`.
- Aspire "Unresolved parameters" = a declared resource (e.g. `AddConnectionString("chat")`) with no value configured.
- ServiceDefaults' standard resilience handler cancels HTTP calls after 10 s per attempt / 30 s total. It aborted Ollama mid-stream (IOException). Fix: longer timeouts, registered after AddServiceDefaults(). Open point: applies to every HttpClient, not only the model's.
- Visual Studio's .http client gives up after 20 s by default. Use Postman for slow model calls.
- SSE with plain strings lost the spaces between words. Send JSON objects ({"text": "..."}), never raw strings.
- Central package management + two NuGet feeds = NU1507. Fixed with a repo-level nuget.config that only uses nuget.org.
- An open IDE tab can save its old copy over a file changed on disk.
- qwen3:4b reasons before every answer and ignores both `/no_think` and `think=false`: ~85% of output tokens are hidden reasoning.
- CPU speed: ~9–10 output tokens/s. Output tokens drive time (and cost); input tokens barely matter.
- Without real data the model invented US taxes and health insurance for an MK/RS payslip. The maths was right, the explanation made up. Grounding needed (tools in Phase 2, RAG in Phase 4).
