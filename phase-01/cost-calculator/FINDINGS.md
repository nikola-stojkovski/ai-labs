# Findings: cost calculator

**Question:** what does one PayPilot request cost on three cloud models, how much do language, data format and conversation length change that, and how much does a thinking model cost in time and tokens compared with a non-thinking one?

**Setup:** o200k_base tokenizer (Microsoft.ML.Tokenizers 3.0.0) for estimates. GPT-5.6 Luna, Terra and Sol prices checked 2026-11-10. qwen3:4b and qwen3:4b-instruct on Ollama, CPU only (Intel Iris Xe). One synthetic payslip, seed 42. Live numbers are from cold starts.

## Tokens

| Text | Tokens | Note |
| --- | --- | --- |
| English sentence | 39 | |
| Same sentence in Macedonian | 60 | ~1.5× |
| Payslip as JSON | 72 | includes a GUID and extra fields |
| Payslip as compact text | 22 | 4 amounts only |

- o200k estimate vs Ollama's real count for the same request: 148 vs 194 (~31% low: chat template + qwen's tokenizer).

## Cost per request (148 input, 150 answer tokens)

| Model | Per request | 100,000/month | + 400 reasoning tokens / month |
| --- | --- | --- | --- |
| gpt-5.6-luna | $0.00021 | $21 | $69 |
| gpt-5.6-terra | $0.00210 | $210 | $690 |
| gpt-5.6-sol | $0.00524 | $524 | $1,724 |

- Measured reasoning was 366–1,139 tokens, not 400, so real reasoning cost is higher than the last column.

## 40-turn chat

- Total input 96,580 tokens vs 5,320 for the same 40 questions asked separately (18×). Input is ~91% of the cost.
- Passes Ollama's 4,096-token context at turn 35 (o200k), around turn 27 with qwen's real counts.

## Thinking vs non-thinking (cold start, streaming)

| Measurement | qwen3:4b | qwen3:4b-instruct |
| --- | --- | --- |
| First answer token | 34.2 s (115.4 s on another run) | 4.5 s |
| Total | 39.9 s | 6.9 s |
| Output tokens | 366 (1,139 on another run) | 27 |
| Answer | correct, with gross, deductions and net | correct, but no amounts |

**Winner and why:** a non-thinking model for PayPilot's payslip questions. It's 6–8× faster to the first answer and uses 14–42× fewer output tokens, and the reasoning model's extra minute bought no extra correctness on a simple lookup. Its weakness is vague answers, which looks like a prompt problem, not a model problem. Lab 1.2 tests that.

**Other decisions:**
- Prices live in config with a checked-on date, never in code.
- Budgets and context limits use the provider's own token count, not an estimate from another tokenizer.
- Long chats need history trimming or summarizing, and data should be fetched with tools instead of resent every turn (Phase 2).

**Goes into PayPilot?** Not yet. If Lab 1.2 shows a better prompt gets specific answers from qwen3:4b-instruct, then an ADR to switch PayPilot's local model from qwen3:4b to qwen3:4b-instruct, linking here.