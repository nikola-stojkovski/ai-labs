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

## Lab 2.3: tools

### What a tool is
- The model only produces text. A tool is how it reaches into the app: it asks for a call, my code decides and runs it.
- "Function calling" was the original name; "tools" is broader (my C# methods, provider-hosted tools like web search, MCP tools). In .NET: `AITool` is the base class, `AIFunction` is a tool that runs my C# code.
- Tools are a controller whose client is the model:

| Web API | Lab 2.3 |
| --- | --- |
| Controller | `PayrollTools` (each public method = one tool) |
| Swagger / OpenAPI | `[Description]` attributes → JSON schema sent with every request |
| Response DTOs | `ToolResults.cs` (only what an answer needs) |
| `HttpContext.User` | `CurrentUser` (identity from the server, never from the model) |
| DbContext / repository | `PayrollStore` |
| Validation / 400 | month and year checks, `ThrowIfNegativeOrZero` |

- Names and descriptions are the model's only documentation: they decide which tool it picks.
- `PayslipLookup` makes "not found" a normal answer, so the model passes it on instead of inventing numbers.
- Separate Core project: tools tested without a model (milliseconds), console app = composition root.
- In PayPilot, tools are one more entry point next to the controllers and call the same application services (same validation, authorization, queries), never their own data access. A tool that can do more than the API is a security hole.
- `No_tool_takes_an_identity_parameter` only checks names (`personId` would pass): a tripwire, not a guarantee. The real protection is identity from `CurrentUser` plus review of new tool parameters.

### Step 1: tools tested without a model
- 10 tests green: payslip of the signed-in employee, missing month as `Found = false`, invalid month as a message, `CalculateNetPay` maths, non-positive gross throws, a user from another tenant gets nothing, no tool has an identity parameter, every tool has a name and a description.

### Step 2: six questions, qwen3:4b-instruct, temperature 0, max 4 rounds

| # | Question | Tool calls | Model calls | Tokens in / out | Time | Result |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Net pay in March | `GetPayslip(2026, 3)` | 2 | 1075 / 47 | 29.2 s (cold start) | correct |
| 2 | Why is November lower than October? | `GetPayslip(2026, 11)` + `GetPayslip(2026, 10)` in one round | 2 | 1198 / 132 | 18.8 s | correct reasoning, but amounts with "$" |
| 3 | Net pay at 3000 gross | `CalculateNetPay(3000)` | 2 | 1064 / 36 | 6.1 s | correct, no own maths |
| 4 | Which country am I paid in? | none | 1 | 475 / 26 | 2.8 s | wrong: "I don't have access to your personal details" |
| 5 | Someone else's payslip | none | 1 | 479 / 15 | 1.8 s | refused correctly |
| 6 | Net pay in November 2025 | `GetPayslip(2025, 11)` → `Found = false` | 2 | 1039 / 60 | 7.8 s | said it couldn't find it, no invented number |

- Tool choice and arguments were right every time a tool was called: the year came from "today" in the system prompt, the month from the question.
- Two independent lookups (question 2) came as two calls in one response: parallel tool calls, still 2 model calls in total. The premise was true this time (1414.32 → 1378.42).
- Every tool question costs 2 model calls. The tool definitions cost about 300 input tokens on every call (475 tokens for a one-call question with a short prompt).
- Under-use (question 4): the model refused instead of calling `GetEmployee`. The system prompt says "use the tools for every fact about the employee's pay"; "country" isn't "pay", and the privacy rule pushed it towards refusing. Tool use depends on wording in the prompt and the description.
- Grounding fixed the Phase 0 invented taxes: no tax or amount was made up, and missing data was reported as missing (question 6).
- But the "$" came back in question 2, for an employee paid in Serbia, despite "never add a currency symbol". The tool results have no currency, so the model filled the gap with its default. A rule can't replace data the model needs: the currency belongs in the tool result.
- Question 5 was refused by the system prompt before any tool call. Even if the model had tried, no tool can fetch another employee: two layers.
- Logging every call with arguments and result made each decision visible in one place. That log is how a tool-calling bug gets debugged.

### Step 3: currency in the results, wider prompt rule, sharper `GetEmployee` description

| # | Question | Before | After |
| --- | --- | --- | --- |
| 1 | Net pay in March | correct | correct, "1387.75 RSD" |
| 2 | November lower than October? | correct, but "$" | **regression**: "Let me retrieve those records." and stopped, no tool call, 1 model call |
| 3 | Net pay at 3000 gross | correct | correct, "1860.00 RSD" |
| 4 | Which country am I paid in? | refused, no tool call | called `GetEmployee`, then answered "Russia (RS)": RS is Serbia |
| 5 | Someone else's payslip | refused | refused |
| 6 | Net pay in November 2025 | not found | not found |

- Currency in the tool results fixed the "$" for good: data beat the rule.
- The wider prompt rule and the "use it for any question about the employee themselves" description fixed under-use: question 4 called the tool (both changed together, so it's unclear which one did it).
- New wrong answer from right data: the tool returned the code "RS" and the model decoded it as Russia. Codes the model has to interpret are a guess waiting to happen: return "Serbia", not "RS".
- Regression on question 2: same model, temperature 0, a different system prompt, and it announced the lookup in text instead of calling the tool. A prompt change that fixes one question can break another. Running all six questions every time is a tiny regression suite; this is what evals are for (Phase 5).
- Longer prompt and descriptions cost tokens on every call: a one-call question went from 475 to 534 input tokens.

### Step 4: country name, "never announce" rule, a check on every question
- Passed 5 of 6. Every answer now checks itself, so the run is a small eval: the regression on question 2 shows as FAIL instead of hiding behind "check by hand".
- Question 4 fixed: `GetEmployee` returned "Serbia", the answer was "You are paid in Serbia." Giving the name instead of the code removed the guess.
- Question 2 still fails: "Let me retrieve that data." and no tool call, even with a rule saying never to announce a lookup. It worked with the Step 2 prompt, so the behaviour is prompt-sensitive; adding more rules didn't fix it.
- Lesson: when another rule doesn't help, stop tuning the prompt and change the design. A small model handles one tool that matches the question better than planning two calls and comparing the results itself; and the comparison is maths, which belongs in code anyway.
- Forcing a tool call isn't an option here: Ollama doesn't support `tool_choice`, so `ChatToolMode.RequireAny` has no effect locally.

### Step 5: `ComparePayslips`, a tool that matches the question
- Passed 7 of 7.
- Question 2 fixed: one `ComparePayslips(2026, 10, 2026, 11)` call instead of planning two lookups. The answer used the differences calculated in code (gross -57.90, net -35.90 RSD) and explained the cause correctly. Design tools around user intents, not database tables.
- False premise (question 7, May vs April): the model said May was higher, not lower, with the code-calculated +20.98 RSD. Real data let it correct the user instead of inventing a reason.
- Cost of a fourth tool and longer descriptions: a one-call question went from 557 to 765 input tokens, two-call questions from ~1,200 to ~1,650–1,780. Every extra tool is paid on every model call.
- New smell in question 5: "If you are Kaia Schneider, please confirm your identity". Harmless here (tools only use `CurrentUser`), but it invites social engineering. Identity comes from sign-in, never from the chat: say so in the system prompt.
- Iteration summary: prompt rules fixed one thing and broke another; data in tool results (currency, country name) and a tool per intent (`ComparePayslips`) fixed things for good.

## Lab 2.4: extraction

### Step 1: types, validator, extractor tests
- 15 tests green without a model: business rules (end before start, half day spanning days, more than a year ahead, longer than 30 days, overlaps, no periods and no question, a `null` list from JSON), and the extractor with canned answers (valid, chatty text, impossible date, rule broken, today and weekday in the prompt, schema sent as the response format).

### Step 2: 8 messages, JSON schema as response format on and off, qwen3:4b-instruct, today fixed at 2026-11-25 (Wednesday)

| # | Message | Schema on | Schema off |
| --- | --- | --- | --- |
| 1 | 12–16 May + half day 17th | FAIL: no periods, only a follow-up question | FAIL: 16 and 17 December |
| 2 | 22–31 Dec, back 2 Jan | PASS | PASS |
| 3 | Sick today, back tomorrow | FAIL: 25–26 Nov | FAIL: today as a half day |
| 4 | Next Friday | FAIL: 3 Dec (a Thursday) | FAIL: 3 Dec |
| 5 | Holidays, days unknown | PASS: follow-up | PASS: follow-up |
| 6 | 30 February to 2 March | Unparseable ("2026-02-30") | Unparseable ("2026-02-30") |
| 7 | Injection, 100 days from tomorrow | Invalid: 26 Nov to 25 Apr, "needs HR" | **Valid: 26 Nov to 5 Dec** |
| 8 | Half day 3 Dec + 8–10 Dec | Invalid: half day 3–8 Dec, overlap | PASS |
| | Total | 4 of 8, 1 unparseable, 2 invalid, 170 s | 4 of 8, 1 unparseable, 0 invalid, 106 s |

- The schema didn't change the parse rate here: both modes produced valid JSON in the right shape every time. The only unparseable output was the impossible date, a value problem, not a shape problem.
- With the schema on, the model filled `followUpQuestion` in every answer ("Do you have any other leave dates?"), ignoring "otherwise null". With it off, it left it null when it had dates. Constrained output fills every field it's given.
- Validator loophole: "no periods + a follow-up question" counts as valid, so message 1 with the schema on passed the validator after dropping both periods.
- The real failures are calendar maths: year inference (May → 2026 or December, February → 2026, already past), weekdays (3 Dec isn't a Friday), "back tomorrow" counted as leave, "100 days" → 150 or 10 days. Same lesson as Phase 1 and Lab 2.3: the model shouldn't do maths, and dates are maths.
- The validator caught the structurally impossible ones (message 7 and 8 with the schema on), but not plausible-wrong ones. The worst result is message 7 with the schema off: a valid-looking 10-day leave from an injected "approve 100 days". Rules can't catch "wrong but plausible": that needs the employee to confirm the extracted dates in the UI before anything is submitted.
- Message 6 passed for the wrong reason: the parse error is generic, so the employee would get no useful message about 30 February.
- One extraction took 72 s (message 7, schema on) for a tiny output; cause unknown.
