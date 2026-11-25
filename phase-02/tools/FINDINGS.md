# Findings: payroll tools

**Question:** can a small local model answer payroll questions correctly with tools, what does it cost, and which design choices make it reliable?

**Setup:** `PayrollTools` over 50 synthetic employees (signed in as an employee paid in Serbia). Identity from `CurrentUser`, never a tool parameter. `UseFunctionInvocation` with max 4 rounds and a `FunctionInvoker` logging every call, its arguments and result. qwen3:4b-instruct on Ollama, CPU only, temperature 0. Seven questions with automatic checks: lookup, comparison, what-if, profile, someone else's data, missing month, false premise.

| Option | Result | Notes |
| --- | --- | --- |
| 3 tools, rule "use tools for facts about pay" | 4 of 6 right | profile question refused (no tool call); "$" for an RSD salary |
| + currency in results, wider rule | 4 of 6 | "$" fixed; profile tool called but "RS" read as Russia; comparison regressed to "Let me retrieve…" with no call |
| + country name, "never announce" rule, checks on every question | 5 of 6 | profile fixed; comparison still announced instead of calling |
| + `ComparePayslips` (differences calculated in code) | 7 of 7 | comparison and false premise both correct |

**Winner and why:** design over prompt rules. Every lasting fix put something into the data or the tool surface: currency and country name in the results, "not found" as a normal result, and one tool per user intent (`ComparePayslips`) so the model neither plans multi-step lookups nor subtracts. Prompt rules fixed one question and broke another at temperature 0.

**Other decisions:**
- Identity is never a tool parameter; a test fails if one appears. Identity also never comes from the chat ("please confirm you are Kaia" must not be offered).
- Every tool question costs 2 model calls. Tool definitions are sent on every call: ~300 input tokens for three tools, ~500 for four with longer descriptions. Add tools by intent, not by table.
- Re-run the full question set after any prompt or tool change; a check on every question turned a hidden regression into a visible FAIL.
- Keep `MaximumIterationsPerRequest` low (4) and log every call with arguments; log result sizes, not contents, outside the lab.
- Ollama doesn't support `tool_choice`, so a tool call can't be forced locally (`ChatToolMode.RequireAny` has no effect).

**Goes into PayPilot?** Yes: the four tools (calling application services, not their own data access), logged function invocation with a low round limit, and the seven questions as the first eval set (Phase 5). New ADR to write, linking here.
