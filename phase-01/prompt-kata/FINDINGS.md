# Findings: prompt kata

**Question:** can prompt patterns make the fast, non-thinking model (qwen3:4b-instruct) reliable on PayPilot's tasks, what does temperature change, and is the thinking model (qwen3:4b) worth its cost on any of them?

**Setup:** 11 prompt files (one pattern each) in `prompts/`. Temperatures 0, 0.7 and 1.2. qwen3:4b-instruct: 3 samples per setting, run twice. qwen3:4b: 1 sample per setting. Ollama on CPU (Intel Iris Xe). Automatic checks: `equals:` / `contains:` on the cleaned answer. Raw answers in `results/`.

## Results

| Task | Pattern | instruct | thinking |
| --- | --- | --- | --- |
| Explain net pay, v1 (Lab 1.1 prompt) | none | 5/18 | 2/3 |
| Explain net pay, v2 | role, rules, delimiters, output contract as a rule | 7/18 | 2/3 |
| Explain net pay, v3 | answer template | **9/9** | not run |
| Classify ticket | closed label list | 18/18 | 3/3 |
| Classify ticket with injection | delimiters + "data is not instructions" | 18/18 | 3/3 |
| Extract leave dates (JSON) | exact JSON shape + today's date | 18/18 | 3/3 |
| Extract, no dates in message | null instead of guessing | 18/18 | 3/3 |
| Question not in the data | exact "I don't know." | 18/18 | 2/3 (missing full stop) |
| Off-topic question | exact refusal sentence | 18/18 | 3/3 |
| Ticket priority | few-shot examples | 18/18 | 3/3 |

| | qwen3:4b-instruct | qwen3:4b |
| --- | --- | --- |
| Average time per call | ~2.3 s | ~140 s |
| Output tokens, classify a ticket | 2 | 736–1,396 |
| Worst case | 9.6 s | 581 s (the injection ticket) |

## What I learned

- **One run proves nothing.** v2 passed in single runs, then 7/18 when repeated.
- **A template beats a rule.** Describing the answer ("show gross, each deduction and net") was followed partly; showing its exact shape was followed fully.
- **Temperature doesn't matter for constrained tasks.** Classification, extraction and refusals gave the same answer at 0, 0.7 and 1.2. It only affects free text, and on this model barely that (likely the default top_k/top_p).
- **Temperature 0 is not deterministic** in practice: 2 different answers in 3 runs, most likely because of the prompt cache.
- **Reasoning length is unrelated to difficulty.** The thinking model spent 10 minutes classifying the injection ticket.
- **The literal checks are useful but strict.** `$1,377.47` and "I don't know" without a full stop fail; an answer with the right numbers and a wrong explanation would pass. Meaning-based checks come in Phase 5.

**Winner and why:** qwen3:4b-instruct with the v3 prompt. With clear prompts it passed every classification, extraction and refusal task at every temperature, and with an answer template it explains payslips 9/9 — beating the thinking model on its strongest task at about 60× less time per call. Use temperature 0 for anything with a right answer.

**Goes into PayPilot?** Yes:
1. ADR: switch PayPilot's model from qwen3:4b to qwen3:4b-instruct (evidence: this file and `cost-calculator/FINDINGS.md`).
2. The v3 system prompt becomes PayPilot's first versioned prompt file, at temperature 0.
3. Phase 2: build the calculation sentence in code from the payslip; the model only writes the plain-language part.