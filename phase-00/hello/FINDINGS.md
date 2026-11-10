# Findings: turning off qwen3 thinking

**Question:** can qwen3:4b answer without hidden reasoning, and how much time does that save?

**Setup:** qwen3:4b on Ollama, CPU only (Intel Iris Xe laptop). One question about a synthetic payslip, asking for a two-sentence answer.

| Option | Reasoning shown? | Output tokens | Time |
| --- | --- | --- | --- |
| No switch | yes | 420 | 40.8 s |
| `/no_think` in the prompt | yes | 451 | 50.7 s |
| `think=false` in ChatOptions | yes | — | — |

**Winner and why:** neither switch works. qwen3:4b always reasons through Ollama. The visible answer is ~60 tokens; the rest is reasoning the user never sees.

**Goes into PayPilot?** No. Instead: compare a non-thinking model in Phase 1.
