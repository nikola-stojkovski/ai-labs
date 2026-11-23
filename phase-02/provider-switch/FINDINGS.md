# Findings: provider switch

**Question:** can PayPilot's business code run unchanged on different providers chosen by config, does the same model behave the same through different clients, and how do we stop provider types leaking into business code?

**Setup:** `PayslipExplainer` in its own project that references only `Microsoft.Extensions.AI.Abstractions`. Three registrations in `Labs.Common`, chosen by `Ai:Provider`: OllamaSharp (Ollama's own API), the OpenAI SDK against Ollama's OpenAI-compatible `/v1`, and `FakeChatClient`. Microsoft.Extensions.AI 10.11.1, qwen3:4b-instruct (and qwen3:4b by accident), CPU only, temperature 0, warm-up then 3 runs.

| Option | Result | Notes |
| --- | --- | --- |
| Ollama (OllamaSharp) | ~5 s, 193 in / 57 out | 9.8 s on the first run: prompt not yet cached by Ollama |
| OpenAI SDK → Ollama `/v1` | ~5 s, 193 in / 57 out | same answer word for word; `MaxOutputTokens` silently ignored (sent as `max_completion_tokens`, `/v1` only supports `max_tokens`) |
| Fake | 0 s, 0 tokens | business code runs with no model |
| `Provider=Gemini` | fails at startup | config binding rejects the enum value |
| Missing `appsettings.json` (bug) | ~300 s, 2,100–2,500 out | silently fell back to the default thinking model; content root was the repo root |

**Winner and why:** no winner between the two real clients: same server and model gave identical answers, tokens and times. What matters is the boundary: business code sees only `IChatClient`, one composition root picks the provider, and a test fails if business code uses a provider type. The OpenAI client is the more useful default for production code because the same client reaches OpenAI, Azure OpenAI/Foundry and other compatible providers; OllamaSharp stays for Ollama-only settings.

**Other decisions:**
- Every option that matters is verified per provider; the abstraction doesn't guarantee a provider honours it.
- Missing configuration must fail at startup, like a wrong value does. Defaults in options classes must be safe ones.
- Log the client metadata (`GetService<ChatClientMetadata>()`) at startup: provider, endpoint, model.
- Each client has its own timeout setting; set it explicitly for slow local models.

**Goes into PayPilot?** Yes: provider chosen by config in Infrastructure.Ai, an architecture test that Application and Domain reference no provider assembly, startup validation of the AI options and a startup log of the client metadata. New ADR to write, linking here.
