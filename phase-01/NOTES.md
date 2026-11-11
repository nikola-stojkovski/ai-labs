# Phase 1 notes

## Concepts
- The model writes one token at a time. For each next token it has a list of candidates with probabilities.
- Temperature = how the next token is picked from that list. Low (0–0.3) = almost always the top choice, predictable; high = the list flattens, unlikely words get picked more, more variety and more mistakes. Payslip answers: 0–0.3. Drafting emails: 0.7–1.0. (Lab 1.2)
- The name comes from physics: hot particles move randomly, cold ones settle.
- Temperature 0 = always the top choice, but still not guaranteed identical: tiny rounding differences in the hardware can tip a close call.
- Top-p = cut off the unlikely tail instead of reshaping the list (TopP 0.9 = pick only from the words that add up to 90%). Tune one of the two, not both.
- In code: `new ChatOptions { Temperature = 0.2f }`.
- The API is stateless: the model remembers nothing, the app resends the whole conversation every turn.
- Hallucination fixes: grounding (tools, RAG), constraints ("say I don't know"), verification (evals). (Phases 2, 4, 5)
- Choosing a model: start with the cheapest that works, move up only where evals prove it's needed. (Phase 5)

## Lab 1.1 — Step 2: tokens
- Tokenizer: `TiktokenTokenizer.CreateForEncoding("o200k_base")` (OpenAI's recent models). Two packages: the engine (Microsoft.ML.Tokenizers) and the vocabulary data (…Data.O200kBase), so apps ship only the vocabularies they use. Using 3.0.0.
- `CreateForEncoding` instead of `CreateForModel`: new model names come out faster than library releases; the encoding name doesn't change.
- Every model family has its own tokenizer. o200k counts are only an estimate for qwen3.

| Text | Chars | Tokens | Chars/token |
| --- | --- | --- | --- |
| English sentence | 120 | 39 | 3.1 |
| Macedonian sentence | 147 | 60 | 2.5 |
| Payslip as JSON | 170 | 72 | 2.4 |
| Payslip as compact text | 50 | 22 | 2.3 |

- Macedonian = ~1.5× the tokens of English for the same meaning. "Your gross pay" = 3 tokens; "Вашата бруто плата" = 7 (2 + 3 + 2). The tokenizer saw far less Macedonian, so words get split into pieces. MK tenants cost ~50% more and get less room in the context window.
- Numbers are expensive: `2,450.00` is several tokens. Payroll text is number-heavy, so it sits below the "4 chars per token" rule of thumb (that rule is from English prose).
- JSON 72 vs compact 22 tokens, but JSON also carried EmployeeId (a GUID, ~20+ tokens), Period and HasBonus. What you send matters more than the format: don't send fields the model doesn't need (context engineering). Format matters too: JSON repeats field names as text. (Phases 2, 4)
- `???` in the console = Windows console code page, not the tokenizer. Fix: `Console.OutputEncoding = System.Text.Encoding.UTF8;`. A piece can still show as `�` when a token ends in the middle of a Cyrillic letter (the tokenizer works on bytes).

## Lab 1.1 — Step 3: cost of one request
- Prices go in appsettings.json, not code, with a CheckedOn date. They change often: GPT-5.6 Luna was cut 80% on 30 July 2026.
- Prices checked 2026-11-10 (USD per 1M tokens, input / output): gpt-5.6-luna 0.20 / 1.20, gpt-5.6-terra 2.00 / 12.00, gpt-5.6-sol 5.00 / 30.00.
- `decimal` for money: `double` can't represent 0.20 exactly, and errors add up over 100,000 requests.
- Request = system prompt + payslip JSON + question = 148 input tokens. Assumed 150 answer tokens, +400 reasoning tokens (from Phase 0).

| Model | Per request | + reasoning | 100,000/month | + reasoning |
| --- | --- | --- | --- | --- |
| gpt-5.6-luna | 0.000210 | 0.000690 | 20.96 | 68.96 |
| gpt-5.6-terra | 0.002096 | 0.006896 | 209.60 | 689.60 |
| gpt-5.6-sol | 0.005240 | 0.017240 | 524.00 | 1,724.00 |

- Output = ~86% of a single request's cost on all three models (output is priced at exactly 6× input on each). With reasoning ~96%. For one question, cut output first: shorter answers, no unnecessary reasoning.
- Reasoning multiplies the bill by 3.3× for the same answer. For "why is my net lower than my gross" it adds cost and waiting, not quality.
- Model choice is a 25× decision (Luna $21 vs Sol $524 a month).
- $21/month for 100,000 answers is nothing next to a developer's day. Cost becomes a real problem with an expensive model + reasoning, or input that grows every call.
- The system prompt is sent with every request: a long system prompt is a cost on every call.
- "Output dominates" holds for short requests only. With RAG (Phase 4) a few thousand tokens of documents make input dominate.

## Lab 1.1 — Step 4: a 40-turn chat
- Simulated with the tokenizer only. Each turn asks about another month and attaches that payslip. `history = turnInput + turnOutput` is the stateless API in one line.

| Turn | Input tokens | Total input | Total cost (Luna) |
| --- | --- | --- | --- |
| 1 | 133 | 133 | 0.00007 |
| 10 | 1,186 | 6,595 | 0.00179 |
| 20 | 2,356 | 24,890 | 0.00591 |
| 30 | 3,526 | 54,885 | 0.01238 |
| 40 | 4,696 | 96,580 | 0.02119 |

- Each turn adds ~117 tokens, so a turn's input grows in a straight line; the total grows with the square of the turns.
- 40 separate questions = 5,320 input tokens. One 40-turn chat = 96,580: 18× more for the same questions. Turn 40 alone costs ~13× turn 1.
- In a long chat input flips to ~91% of the cost (single request: output was 86%).
- 2 cents per conversation on Luna, ~50 cents on Sol.
- Over Ollama's 4,096-token context from turn 35 (o200k count; qwen3 counts more, so earlier on my machine). Ollama then drops the oldest messages, system prompt first: PayPilot would silently lose "only payroll" and "say you don't know". No error, just slowly worse answers. That's why chatbots "forget". (Checkpoint question)
- Cutting the cost of a long chat (checkpoint question):
  1. Trim the history: keep the last N turns, always keep the system prompt.
  2. Summarize old turns into a short summary.
  3. Don't resend data: ~72 of the ~117 tokens per turn are payslip JSON. Keep data out of the history, fetch it with a tool when needed. (Phase 2)
  4. Prompt caching: a repeated prefix is billed at a fraction of the price on cloud models.
- Microsoft.Extensions.AI has chat reducers for trimming and summarizing. (Phase 2)

## Lab 1.1 — Step 5: live timing
- Measures model load (cold start), time to first token, time to first answer token, streaming vs non-streaming, and Ollama's real token count vs the o200k estimate.
- Run with qwen3:4b (thinks) and qwen3:4b-instruct (same size, no thinking): any difference comes from reasoning alone. Follow-up from phase-00/hello FINDINGS.
- `Get<T>()` returns null for a missing config section (the header showed "?"). Always fall back to defaults: `?? new LabAiOptions()`, the same way Labs.Common does.

### qwen3:4b (thinking)

| | First token | First answer | Total | Output tokens |
| --- | --- | --- | --- | --- |
| Streaming | 0.1 s | 115.4 s | 118.8 s | 1,139 |
| Non-streaming | 73.7 s | 73.7 s | 73.7 s | 737 |

- Model load (warm-up): 2.4 s, because the model was already in memory. Ollama keeps a model loaded ~5 minutes. A real cold start from disk is longer (`ollama stop qwen3:4b` before a run to see it).
- Input tokens: o200k estimate 148, Ollama actual 194, so the estimate is ~31% low. Two reasons: the chat template (role markers around every message are tokens too) and qwen's own tokenizer. Fine for a ballpark; for budgets and context limits use the provider's own count.
- That moves step 4: with ~31% more tokens per turn, qwen3 passes 4,096 around turn 27, not turn 35.
- Streaming barely helped: the first token came at 0.1 s, but it was reasoning. The first answer word came at 115 s, 3 s before the end. With hidden reasoning the user stares at a spinner for ~2 minutes. Streaming only helps when the first useful token comes early.
- ~97% of the output was hidden reasoning: 1,139 tokens for a ~25-token answer (Phase 0: ~85%).
- Time follows output tokens: 1,139 tokens / 118.8 s and 737 / 73.7 s are both ~9.6 tokens/s. On this laptop every output token is ~0.1 s, so predicting latency = predicting output length.
- The same question reasoned for 1,139 tokens once and 737 the next: reasoning length, and so time and cost, varies from run to run. One measurement isn't enough to budget on.
- Step 3 assumed 400 reasoning tokens; measured 700–1,100. With that, Luna + reasoning is ~$0.0014 per request, ~$141/month: ~7× the plain answer, not 3.3×. Measured numbers beat assumed ones.
- Answer: "Net pay is gross pay minus Income Tax (222.17) and Social Contributions (622.08)." Correct (10% and 28% of 2,221.71), grounded in the data, short, nothing invented. Also much shorter than the 150 answer tokens assumed.
- So ~2 minutes of reasoning bought nothing for a simple lookup: same right answer, far slower and more expensive. That's when a reasoning model makes things worse. (Checkpoint question)

### qwen3:4b (thinking), cold start (`ollama stop qwen3:4b` first)

| | First token | First answer | Total | Output tokens |
| --- | --- | --- | --- | --- |
| Streaming | 4.6 s | 34.2 s | 39.9 s | 366 |
| Non-streaming | 42.3 s | 42.3 s | 42.3 s | 428 |

- Model load: 7.2 s cold vs 2.4 s warm, so a cold start costs ~5 s (reading ~2.5 GB from disk). The first user after a quiet period waits longer.
- First token went from 0.1 s (warm) to 4.6 s (cold). Before generating, the model processes every input token (prompt processing): 194 tokens at ~40 tokens/s on CPU ≈ 4.5 s. On the warm run Ollama most likely still had that exact prompt cached and skipped the work. The non-streaming call right after didn't pay it again, since the prompt was cached again by then.
- That's prompt caching on my own laptop: a repeated prefix is cheap. Cloud providers bill cached input at a fraction of the price. Put fixed content (system prompt) first, so only the new part at the end needs processing.
- Reasoning length swings ~3× between runs of the same question: 366/428 tokens here vs 1,139/737 before. Speed stayed ~10 tokens/s, so time swung with it (first answer 34 s vs 115 s). For budgets: many runs, plan for the high end.
- Answer this time: "…subtracting income tax (222.17) and social contributions (622.08) from gross pay (2221.72), resulting in 1377.47." Correct, and more complete than the first run. Wording and detail change between runs. (Lab 1.2, Phase 5 evals)

### qwen3:4b-instruct (non-thinking), cold start

| | First token | First answer | Total | Output tokens |
| --- | --- | --- | --- | --- |
| Streaming | 4.5 s | 4.5 s | 6.9 s | 27 |
| Non-streaming | 2.4 s | 2.4 s | 2.4 s | 27 |

- Model load 6.2 s cold. Input 192 tokens (194 for the thinking model).
- Without reasoning, first answer = prompt processing (~4.5 s for 192 tokens on CPU), then 27 tokens in ~2.4 s.
- Non-streaming looked faster (2.4 s vs 6.9 s) only because the streaming call ran first and paid prompt processing; the prompt was cached for the second call. Measuring lesson: order matters. Warm up with the same prompt, alternate the order, repeat runs.
- Answer: "Your net pay is less than your gross pay because deductions like income tax and social contributions have been subtracted from your gross pay." Correct but generic: no amounts, although the payslip was in the prompt.

### Thinking vs non-thinking (cold starts)

| | qwen3:4b (thinking) | qwen3:4b-instruct | Difference |
| --- | --- | --- | --- |
| First answer token | 34.2 s | 4.5 s | 7.6× faster |
| Total (streaming) | 39.9 s | 6.9 s | 5.8× faster |
| Output tokens | 366 (up to 1,139) | 27 | 14–42× fewer |
| Luna, 100k requests/month | ~$47–141 | ~$7 | 7–20× cheaper |

- With short answers input matters again: at 192 in / 27 out, input is about half of Luna's cost. "Output dominates" only held because answers were long.
- The trade-off is answer quality: the fast model was right but vague; the thinking model (best run) gave gross, deductions and net.
- Question for Lab 1.2: can a better prompt ("explain using the exact amounts: gross, each deduction, net") get the specific answer from the fast model? Fix it with the prompt before paying for reasoning.
