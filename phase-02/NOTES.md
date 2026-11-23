# Phase 2 notes

## Concepts: the two abstractions
- Two packages. `Microsoft.Extensions.AI.Abstractions` = the interfaces and content types; providers implement only this. `Microsoft.Extensions.AI` = everything built on top: the builder, middleware (tool calling, caching, telemetry, logging) and DI helpers. Apps reference `Microsoft.Extensions.AI` + one provider package (Labs.Common: OllamaSharp).
- `IChatClient` has four members: `GetResponseAsync`, `GetStreamingResponseAsync`, `GetService`, `Dispose`. Agent Framework and the MCP SDK are built on it. (Phases 6, 7)
- `IEmbeddingGenerator<TInput, TEmbedding>` is the same idea for embeddings. (Phase 3)
- `GetService<T>()` asks a client for something underneath it (metadata, the provider's own client, an inner middleware) without casting. It's the escape hatch: using it in business code to reach a provider type undoes Lab 2.1.

## Messages, options, responses
- `ChatMessage` = role (System, User, Assistant, Tool) + a list of contents: `TextContent`, `DataContent` (bytes: images, files), `UriContent`, `FunctionCallContent`, `FunctionResultContent`, `UsageContent`, `TextReasoningContent`. `.Text` joins only the text parts.
- `ChatOptions`: `Temperature`, `TopP`, `MaxOutputTokens`, `ModelId`, `Tools`, `ToolMode`, `ResponseFormat`, `ConversationId`. Anything provider-specific goes in `AdditionalProperties` (weakly typed) or `RawRepresentationFactory` (the provider's own options object).
- `ConfigureOptions` on the builder sets defaults for every call; a value set per call wins. PayPilot: temperature 0 as the default (Phase 1).
- `ChatResponse`: `Messages` (the last one is the answer; with tools the call and result messages are in there too), `Text`, `Usage` (input/output tokens), `FinishReason`, `ModelId`, `ConversationId`.
- Streaming returns `ChatResponseUpdate` pieces. `updates.ToChatResponse()` joins them into one response; `history.AddMessages(response)` or `history.AddMessages(updates)` keeps the history.
- Stateless vs stateful. Ollama and chat completions are stateless: the app keeps a `List<ChatMessage>` and resends it (Phase 1 step 4). Stateful services (e.g. OpenAI Responses, hosted agents) keep the history and return a `ConversationId`: then send only the new message. Generic code: if `response.ConversationId` is set, put it in the options and clear the local history; otherwise `AddMessages`.

## The pipeline
- `inner.AsBuilder().UseA().UseB().Build()` (or `new ChatClientBuilder(inner)`). Each `Use` wraps the client. The first `Use` is the outermost layer: it sees the request first and the response last. Same mental model as ASP.NET Core middleware.
- Built in: `UseFunctionInvocation` (the tool loop), `UseDistributedCache`, `UseOpenTelemetry`, `UseLogging`, `UseChatReducer`, `ConfigureOptions`.
- Cache: a history it has seen before gets the stored response, no model call. Needs an `IDistributedCache` (`AddDistributedMemoryCache()` locally). Good for evals and repeated questions; wrong for anything that must reflect fresh data.
- `UseOpenTelemetry` follows the OpenTelemetry gen-AI conventions: spans + token metrics, which is what the Aspire dashboard shows (Phase 0). `EnableSensitiveData = true` puts prompts and answers into the traces. For payroll that means payslips in the tracing backend: off outside local dev. (Phase 9)
- Order changes behaviour: each layer only sees what passes through it. Checkpoint question: what breaks with `UseFunctionInvocation` outside `UseDistributedCache`, and the other way round.
- Experimental routing clients: `RoutingChatClient`, `OrderedFailoverChatClient` (try the next client when one fails before producing output). The answer to "a provider disappears" (GitHub Models, Phase 0).

## Own middleware
- Derive from `DelegatingChatClient`. It forwards `GetResponseAsync`, `GetStreamingResponseAsync` and `Dispose` to the inner client; override only what you change.
- Override both response methods. Forgetting the streaming one means the SSE endpoint silently skips the middleware.
- Ship it as a `UseX()` extension on `ChatClientBuilder`. `Use((inner, services) => new X(inner, services.GetRequiredService<ILogger<X>>()))` gets dependencies from DI.
- Inline alternative: `Use((messages, options, next, ct) => ...)` is one delegate for both paths, for work before/after the call that doesn't change the response. Two delegates when the response itself has to change.
- `ReducingChatClient` (source) is the whole pattern in a few lines: change the messages, call `base`. Lab 2.2's redactor has the same shape.
- Microsoft's sample is a `RateLimitingChatClient` (a lease before every call). Same pattern for cost guards and quotas per tenant. (Phase 9)

## Tool calling
- Flow: tools described in `ChatOptions.Tools` → the model returns `FunctionCallContent` (name + JSON arguments) → code runs the method → `FunctionResultContent` goes back as a Tool message → the model answers or calls again. `UseFunctionInvocation()` runs this loop.
- One question = several model calls (Phase 0: 3). Every round trip resends the whole history, tool results included. (Phase 1 cost)
- The model can invent arguments. Validate inside the tool. Never take the employee or tenant from the model's arguments: take it from the logged-in user on the server. (Phase 4 security trimming)
- `AIFunctionFactory.Create(method)`: the method name and `[Description]` on the method and its parameters become the JSON schema. Descriptions are prompt text and are sent on every call, so they cost tokens and take context. Few tools, short descriptions, only the ones relevant to the request.
- `IServiceProvider`, `AIFunctionArguments` and `CancellationToken` parameters are filled in by the framework and not shown to the model. Keep model-facing parameters simple; no `string?` for Ollama (Phase 0).
- `FunctionInvokingChatClient` settings (defaults): `MaximumIterationsPerRequest` 40, `MaximumConsecutiveErrorsPerRequest` 3, `AllowConcurrentInvocation` false, `IncludeDetailedErrors` false, `TerminateOnUnknownCalls` false, plus `AdditionalTools` and `FunctionInvoker` (replace how each function is invoked). 40 round trips at ~10 tokens/s on my CPU is a disaster: set a low limit for PayPilot.
- A tool that throws: the model gets a generic error message and the loop continues; after 3 failing iterations in a row the exception is rethrown. The exception itself is on `FunctionResultContent.Exception`. `IncludeDetailedErrors = true` sends exception details to the model (and the provider): leaks internals.
- `FunctionInvokingChatClient.CurrentContext` inside a tool: which call, which iteration. Useful for logging calls (Lab 2.3).
- Parallel calls: a model can ask for several tools in one response; all results go back in one message.
- Streaming with tools: text streams until a tool call appears, then updates are held until the call is done. In PayPilot the user sees nothing during a tool round: send a status event over SSE (why SSE sends JSON objects, Phase 0).
- `ToolMode`: `ChatToolMode.Auto` (default), `RequireAny`, `RequireSpecific(name)`, `None`.
- `ApprovalRequiredAIFunction` wraps a tool so it needs a human yes before it runs. (Phase 6)

## Typed output
- `client.GetResponseAsync<T>(prompt)` → `ChatResponse<T>`. A JSON schema is generated from `T` and sent as the response format (`useJsonSchemaResponseFormat`).
- Not guaranteed. `.Result` throws when the output isn't parseable as `T`; `TryGetResult(out var value)` returns false instead, and the raw output stays on `response.Message`. The Phase 0 suggestions crash was this. Use `TryGetResult`.
- Valid JSON ≠ valid data. After parsing, check the business rules (end date after start date, half day inside the range, year from today's date). Kata 05/06: the app gives today's date, and `null` beats a guess.
- Enums restrict values like the closed label list in kata 03. Free-text fields attract filler ("Certainly, I have analysed…").
- An optional AI feature must never break the main one (Phase 0): a failed parse is a handled result, not an exception in the request.

## DI and providers
- `builder.Services.AddChatClient(inner)` returns a builder: `.UseFunctionInvocation().UseOpenTelemetry()...` registers the whole pipeline as `IChatClient`. `AddKeyedChatClient("fast", ...)` + `[FromKeyedServices("fast")] IChatClient` for several models. Locally: fast = qwen3:4b-instruct, reasoning = qwen3:4b (Phase 1).
- Providers: OllamaSharp's `OllamaApiClient` implements `IChatClient` directly. `Microsoft.Extensions.AI.OpenAI` turns an OpenAI SDK client into one (`GetChatClient(model).AsIChatClient()`) for OpenAI, Azure OpenAI / Foundry and any OpenAI-compatible endpoint, including Ollama's `http://localhost:11434/v1`. Moving to the cloud = endpoint + key + model name in config.
- Only the composition root (`ServiceRegistration.cs`, PayPilot's Infrastructure.Ai) knows the provider. Business code sees `IChatClient` only. (Lab 2.1 test)
- One interface, different behaviour: tool calling, JSON schema support, token counts and reasoning vary per provider and model (Phase 0 `string?` schema). Same code ≠ same quality: evals per model. (Phase 5)

## Images and files
- `DataContent(bytes, "image/png")` or `UriContent` in a `ChatMessage`. Needs a vision model; qwen3:4b isn't one, and on CPU it would be very slow. Images cost tokens too. Scanned payslips: Phase 3 (Document Intelligence).

## Chat reducers
- `IChatReducer` + `UseChatReducer`. Both built-in reducers are experimental: `#pragma warning disable MEAI001`.
- `MessageCountingChatReducer(n)`: keeps the first system message and the last n non-system messages. Fixes the Phase 1 problem of Ollama silently dropping the system prompt.
- `SummarizingChatReducer(client, targetCount, threshold)`: summarizes older messages, keeps the system messages, leaves tool calls and results out of the summary. A summary is another model call (time and tokens, seconds on CPU), and it can drop or invent facts.
- Counting messages isn't counting tokens: one message with a payslip is many short questions. Trimming must keep a tool call together with its result.

## Open questions for the labs
- Lab 2.1: does qwen3:4b-instruct behave the same through OllamaSharp and through `/v1` (token counts, tool calls)?
- Lab 2.2: where does the redactor go relative to `UseFunctionInvocation` so tool results get redacted too? Does it work when streaming? What do the traces still show?
- Lab 2.3: does the 4B instruct model pick the right tool with the right arguments? How many model calls per question? Does grounding stop the invented US taxes from Phase 0?
- Lab 2.4: how often does `TryGetResult` fail, with and without the JSON schema?

## Lab 2.1: provider switch
Same `PayslipExplainer` for every provider, question "Why is my net pay lower than my gross pay?", temperature 0, a warm-up call, then 3 runs.

| Provider | Warm-up | Run 1 | Run 2 | Run 3 | Input tokens | Output tokens |
| --- | --- | --- | --- | --- | --- | --- |
| Ollama (OllamaSharp) | 7.3 s | 275.3 s | 345.2 s | 324.6 s | 195 | 2143 / 2525 / 2525 |
| OpenAI SDK → Ollama `/v1` | 38.8 s | 272.2 s | 332.6 s | 324.5 s | 195 | 2143 / 2525 / 2525 |
| Fake | 0.0 s | 0.0 s | 0.0 s | 0.0 s | 0 | 0 |
| `--Ai:Provider=Gemini` | fails at startup: "Failed to convert configuration value 'Gemini' at 'Ai:Provider' to type 'Labs.Common.AiProvider'" | | | | | |

- `appsettings.json` was never read. Every run used qwen3:4b (the thinking default in `LabAiOptions`), not qwen3:4b-instruct. `dotnet run --project` from the repo root keeps the repo root as the working directory, `Host.CreateApplicationBuilder` uses it as the content root, and the copy in `bin/` is ignored. No error, no warning: missing config silently falls back to defaults. The "Client reports" line (`GetService<ChatClientMetadata>()`) is what showed it. Fix: content root = `AppContext.BaseDirectory` in `LabHost`.
- Two config failures, opposite behaviour: a typo in an enum value fails at startup (good), a missing file fails silently and costs 5 minutes per answer. Defaults in an options class should be safe ones, or required values should be validated at startup.
- Thinking hides cost: 2,100–2,500 output tokens for a two-sentence answer. The reasoning tokens are in `Usage` (and on the bill) but not in `response.Text`. About 5 minutes per answer on CPU (Phase 1: 60× slower).
- Same server and model through two protocols: identical answers, identical token counts, same times. The protocol didn't change the behaviour; the model and the server do.
- Temperature 0 still isn't reproducible: run 1 differs from runs 2 and 3 (2,143 vs 2,525 tokens, one paragraph vs two lines), with exactly the same pattern in both providers. So it comes from the Ollama server's state, not the client. Likely cause: run 1 processes the prompt from scratch and runs 2–3 reuse the cached prompt, which shifts the maths slightly.
- Warm-up 7.3 s vs 38.8 s: the OpenAI SDK sends `MaxOutputTokens` as `max_completion_tokens`, and Ollama's `/v1` only supports `max_tokens`. The 1-token limit was silently ignored, so the thinking model wrote a whole answer to "Hi". One abstraction, one option, and one provider quietly drops it.
- Fake: the business code runs with no model at all, in 0 s.

### Rerun with the config fix (qwen3:4b-instruct)

| Provider | Warm-up | Run 1 | Run 2 | Run 3 | Input tokens | Output tokens |
| --- | --- | --- | --- | --- | --- | --- |
| Ollama (OllamaSharp) | 6.7 s | 9.8 s | 5.2 s | 5.3 s | 193 | 57 |
| OpenAI SDK → Ollama `/v1` | 3.3 s | 5.1 s | 5.3 s | 5.3 s | 193 | 57 |

- Answer to the lab question: the instruct model behaves the same through OllamaSharp and through `/v1`. Same answer word for word in all 6 runs, same tokens, same speed. Behind `IChatClient` the protocol is invisible.
- The instruct model is ~60× faster than the thinking one for the same answer (5 s vs ~300 s), the same ratio as Phase 1.
- 193 input tokens here vs 195 with qwen3:4b for the same messages: each model's chat template wraps the messages differently.
- Temperature 0 was reproducible this time: a short answer has fewer close calls between two tokens where it could tip.
- Supports the prompt-cache explanation: Ollama run 1 took 9.8 s (the prompt processed for the first time), every run after took ~5 s, including the OpenAI process's run 1, because Ollama still had that prompt cached from the previous process. The cache belongs to the server, not to the client or the protocol.
- Warm-up gap mostly gone (6.7 s includes loading the instruct model; 3.3 s was already loaded). `max_completion_tokens` is still ignored by `/v1`; the instruct model just answers "Hi" briefly, so it costs little here.

### Step 2: boundary tests
- 6 tests, all green: Core references no provider assembly, `PayslipExplainer` works with any `IChatClient`, the template is in the user message next to the payslip and not in the system prompt, temperature 0, `Provider=Fake` from config needs no model, an unknown provider fails at startup.
- Seen failing on purpose: adding the OllamaSharp package to Core alone kept the tests green (the compiler drops references to assemblies the code never uses). Using `OllamaApiClient` in Core turned the reference test red. The test catches use; a review of the `.csproj` catches the dependency.
- `FakeChatClient` tests what the code sends to the model, not what the model answers: milliseconds, free, deterministic.
- xUnit 4 runs on Microsoft Testing Platform v2; on the .NET 10 SDK `dotnet test` needs `global.json` with `"test": { "runner": "Microsoft.Testing.Platform" }`.

## Lab 2.2: PII redaction middleware

### Predictions before measuring (from the pipeline model)
- Builder order = nesting: the first `Use` is the outermost layer. The tool loop happens inside `FunctionInvokingChatClient`: it calls the layer below it, runs the tool, adds the result to the messages and calls the layer below it again.
- Redactor outside `UseFunctionInvocation`: runs once, on the request from the app, before any tool result exists. The tool result is created inside the function-invocation layer and goes straight down to the model: the IBAN leaks.
- Redactor inside: every model call passes through it, tool results included. The model only ever sees `[IBAN]`, so it can't tell the user their account: the data is protected, but the feature is broken.
- Runs per question: outside = once; inside = once per model call (2 for one tool call). The whole history is redacted again on every call, so the same user message is masked and logged more than once.

### Step 1: redactor and middleware tests
- 13 tests green with `FakeChatClient`: email, IBAN with and without spaces, EMBG, amounts and dates untouched, a 13-digit number that can't be a date untouched, a date-shaped reference number masked (known false positive), tool results masked, streaming path masked, caller's history unchanged, clean messages passed through as the same object.

### Step 2: redactor position, qwen3:4b-instruct, one tool call

| Setup | Requests to the model | What the model saw | Redactor runs | Answer to the user |
| --- | --- | --- | --- | --- |
| Redactor outside `UseFunctionInvocation` | 2 | request 1 clean, request 2 had the tool result with email, IBAN and EMBG | 1 | the real IBAN |
| Redactor inside | 2 | both clean | 2 | "IBAN: [IBAN]" |
| Redactor inside, streaming | 2 | both clean | 2 | "IBAN: [IBAN]" |

- All predictions held. Outside: the redactor only sees the app's request; the tool result is created inside the function-invocation layer and goes straight to the model. Position decides what is protected, not the redactor's code.
- Inside protects everything and breaks the feature: the model can't tell Ana her own account. The user is allowed to see her IBAN; the model provider isn't.
- The real fix isn't a smarter redactor, it's data minimisation: tools return only what the model needs to answer (e.g. a masked IBAN `MK07 **** 984`), and the app shows full values from its own data, never from the model's text. Redaction is the safety net, not the main control.
- The whole history is redacted again on every model call: the second run logged "2 EMBG, 2 EMAIL, 1 IBAN" for 1 + 1 + 1 values. Counts are per model call, not per user request: don't use them as an audit metric as they are.
- `response.Text` joins all assistant text from the loop, including the model's "I'll check your employee details..." before the tool call. A chat UI should show only the final message.
- The tool returned a JSON string, so the model got it serialised twice (escaped quotes). Tools should return objects and let `AIFunctionFactory` serialise them once.
- Streaming behaved exactly like non-streaming: the override of `GetStreamingResponseAsync` works.
