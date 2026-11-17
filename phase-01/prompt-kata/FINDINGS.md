# Findings: prompt kata

**Question:** can prompt patterns make the fast, non-thinking model (qwen3:4b-instruct) reliable on PayPilot's tasks, what does temperature change, and is the thinking model (qwen3:4b) worth its cost on any of them?

**Setup:** 16 prompt files (one pattern or version each) in `prompts/`. Temperatures 0, 0.7 and 1.2. qwen3:4b-instruct: 3 samples per setting, 4 runs (prompts 11–16 added in later runs). qwen3:4b: 1 sample per setting, 1 run. Ollama on CPU (Intel Iris Xe). Automatic checks: `equals:` / `contains:` on the cleaned answer. Raw answers in `results/`.

## Results

| Task | Pattern | instruct | thinking |
| --- | --- | --- | --- |
| Explain net pay, v1 (Lab 1.1 prompt) | none | 8/36 | 2/3 |
| Explain net pay, v2 | role, rules, delimiters, output contract as a rule | 14/36 | 2/3 |
| Explain net pay, v3, with payslip | answer template in the system prompt | 27/27 | not run |
| Explain net pay, v3, **no payslip** | same | **0/18** (invented a payslip every time) | not run |
| Explain net pay, v4, with / without payslip | template "only when a payslip is present" | 18/18 / **0/18** | not run |
| Explain net pay, v5, with / without payslip | rules only; template added with the data | 9/9 / **9/9** | not run |
| Classify ticket | closed label list | 36/36 | 3/3 |
| Classify ticket with injection | delimiters + "data is not instructions" | 36/36 | 3/3 |
| Extract leave dates (JSON) | exact JSON shape + today's date | 36/36 | 3/3 |
| Extract, no dates in message | null instead of guessing | 36/36 | 3/3 |
| Question not in the data | exact "I don't know." | 36/36 | 2/3 (missing full stop) |
| Off-topic question | exact refusal sentence | 36/36 | 3/3 |
| Ticket priority | few-shot examples | 36/36 | 3/3 |

| | qwen3:4b-instruct | qwen3:4b |
| --- | --- | --- |
| Average time per call | ~2.3 s | ~140 s |
| Output tokens, classify a ticket | 2 | 736–1,396 |
| Worst case | ~11 s | 581 s (the injection ticket) |

## What I learned

- **One run proves nothing.** v2 passed in single runs, then 14/36 when repeated.
- **A template beats a rule.** Describing the answer ("show gross, each deduction and net") was followed partly; showing its exact shape was followed fully.
- **A template the model can see gets used, whatever the rules say.** With the template in the system prompt and no payslip, the model invented a complete payslip in every run, even when told "only when a payslip is present". At temperature 0 it repeated the same invented numbers, so the wrong answer looked consistent and trustworthy. Moving the template next to the data fixed it, with the rules unchanged.
- **Decisions about the data belong in code.** The system prompt holds what's true in every conversation; anything that depends on the data is added by code, together with the data.
- **Test every data-using prompt without its data.** v3's 9/9 only covered the case with a payslip, and it broke the first time PayPilot used it without one.
- **Temperature doesn't matter for constrained tasks.** Classification, extraction and refusals gave the same answer at 0, 0.7 and 1.2. It only affects free text, and on this model barely that (likely the default top_k/top_p).
- **Temperature 0 is not deterministic** in practice: 2 different answers in 3 runs, most likely because of the prompt cache.
- **Reasoning length is unrelated to difficulty.** The thinking model spent 10 minutes classifying the injection ticket.
- **The literal checks are useful but strict.** `$1,377.47` and "I don't know" without a full stop fail; an answer with the right numbers and a wrong explanation would pass. Meaning-based checks come in Phase 5.

**Winner and why:** qwen3:4b-instruct with the v5 system prompt, and the answer template added by code only when a payslip is attached. It passed every classification, extraction and refusal task at every temperature, explained payslips 9/9 with data, and said "I don't know." 9/9 without data — where v3 and v4 invented a payslip every time. It beats the thinking model on its strongest task at about 60× less time per call. Use temperature 0 for anything with a right answer.

**Goes into PayPilot?** Yes:
1. ADR 0002: switch PayPilot's model from qwen3:4b to qwen3:4b-instruct (evidence: this file and `cost-calculator/FINDINGS.md`).
2. ADR 0003: the v5 system prompt becomes PayPilot's first versioned prompt file, at temperature 0. The answer template is not in it; it comes back in Phase 2, added by code together with the payslip.
3. Phase 2: build the calculation sentence in code from the payslip; the model only writes the plain-language part.