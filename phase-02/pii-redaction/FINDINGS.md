# Findings: PII redaction middleware

**Question:** can one middleware stop personal data (EMBG, IBAN, email) from reaching the model, where does it have to sit in the pipeline, and what does it cost the feature?

**Setup:** `PiiRedactingChatClient` (`DelegatingChatClient`, regex rules, logs kinds and counts only). A tool returning synthetic contact and bank details. A `RecordingChatClient` as the innermost layer to capture exactly what reached the model. qwen3:4b-instruct on Ollama, temperature 0, question with an email and an EMBG asking for the salary account.

| Option | Result | Notes |
| --- | --- | --- |
| Redactor outside `UseFunctionInvocation` | leaks: tool result with email, IBAN, EMBG reached the model | redactor runs once, before any tool result exists |
| Redactor inside `UseFunctionInvocation` | nothing reached the model | user got "IBAN: [IBAN]": protected but useless |
| Inside, streaming | same as inside | streaming override works |

**Winner and why:** inside `UseFunctionInvocation`, because it is the only position that sees every model call, tool results included. But the experiment shows the redactor can't be the main control: masked data also can't be used in answers. The main control is data minimisation in tool design: tools return only what the model needs (a masked IBAN, totals instead of raw records), and the UI shows full values from the app's own data, not from the model's text. The redactor stays as the safety net for what users type and what slips through.

**Other decisions:**
- Log kinds and counts, never values. Counts are per model call (history is re-redacted every call), so they overcount per user request.
- Regex catches only fixed-format identifiers (EMBG, IBAN, email); names and salaries pass through. Accept false positives (date-shaped reference numbers) over false negatives.
- Tools return objects, not pre-serialised JSON strings.
- Show only the final assistant message in the chat UI; `response.Text` includes the text written before tool calls.

**Goes into PayPilot?** Yes: redaction middleware inside `UseFunctionInvocation` in Infrastructure.Ai, and a tool-design rule (minimum data, masked identifiers) for Lab 2.3's tools. New ADR to write, linking here.
