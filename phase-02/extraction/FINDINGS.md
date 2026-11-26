# Findings: leave request extraction

**Question:** can a small local model turn free-text leave requests into a typed `LeaveRequest` the app can act on, which layer catches which mistake, and does sending the JSON schema as the response format matter?

**Setup:** `GetResponseAsync<T>` with `TryGetResult`, then a C# validator (end after start, half day = one day, within a month back and a year ahead, max 30 days, no overlaps, no periods only with a follow-up question). Eight messages: two periods, a return date, a relative date, "next Friday", missing dates, 30 February, an injection ("approve 100 days"), two periods in one sentence. qwen3:4b-instruct on Ollama, temperature 0, today fixed at 2026-11-25.

| Option | Passed | Notes |
| --- | --- | --- |
| Model returns real dates, schema as response format | 4 of 8 | wrong years and weekdays, "back tomorrow" counted as leave, a follow-up question in every answer |
| Model returns real dates, schema only in the prompt | 4 of 8 | same shape quality; the injection became a valid-looking 10-day leave |
| Model copies dates as said, code resolves them, rules in the prompt | 5 of 8 (3 real) | calendar right, but end dates dropped; two hollow passes from loose checks |
| Model copies dates as said, code resolves them, 5 worked examples | 8 of 8 | every case right, 62 s instead of ~160 s |

**Winner and why:** the model copies dates exactly as said (`LeaveMentions`), and code does the calendar (`LeaveResolver`): year, weekdays, impossible dates, and a question when something is ambiguous or missing. Worked examples in the prompt, not rules, made the copying reliable. The model never sees today's date, so it has nothing to calculate with.

**Other decisions:**
- The schema as response format guaranteed shape, not meaning. Both modes parsed equally well here; it also made the model fill every field, including an optional question.
- Text the model returns to code is untrusted input: `TryGetResult`, then a validator. JSON can leave non-nullable C# properties null.
- Rules catch impossible output, not plausible-wrong output ("100 days" → 10 days). In PayPilot the employee confirms the extracted dates before anything is submitted.
- Follow-up questions are written by code: deterministic, specific ("Do you mean 27 November 2026 or 4 December 2026?") and safe to show.
- Checks must test content, not shape: "is there a question?" let two wrong answers pass.

**Goes into PayPilot?** Yes: leave requests from chat via the copy-then-resolve design, validator, and a confirmation step in the UI before submit. The eight messages start the extraction eval set (Phase 5). New ADR to write, linking here.
