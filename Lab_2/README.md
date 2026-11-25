# Zeya

Zeya is a C# console assistant for Lab 2 Track 2. It chats with a local Ollama
model, keeps recent exchanges in RAM, and uses the formatter and sink from Lab 1.
The personal-assistant version with permanent fact storage is on `saving-memory`.

## Run

From the repository root:

```powershell
dotnet build .\Zeya.slnx
dotnet run --project .\Lab_2\Zeya.csproj
```

Inside `Lab_2`, use `dotnet run`. Start Ollama first. The default model is
`qwen3.5:4b-q4_K_M`. Install it with `ollama pull qwen3.5:4b-q4_K_M` if needed.

Choose a memory tier and log level at startup. Press Enter to use tier 2 and
`info`. One exchange is a user message and the assistant's final reply.

| Tier | Complete exchanges kept |
| --- | ---: |
| 1 | 2 |
| 2 | 6 |
| 3 | 10 |

Try `My name is Ada.` followed by `What is my name?`. Once the selected tier
fills up, each completed turn evicts the oldest pair. `/clear` clears the
current conversation. `quit` or `/exit` closes the program.

Ask `What time is it?`, `What is today's date?`, or `What is 12 times 7?` to
exercise the three tools. The calculator accepts two numbers and one operator
(`+`, `-`, `*`, or `/`). The session allows three tool attempts per turn.

## Logging and tier comparison

- `trace` shows request sizes and new exchanges.
- `info` shows Ollama's prompt tokens, completion tokens, elapsed time, and
  completion tokens per second.
- `warn` reports evicted exchanges.
- `error` reports request and tool failures.

To compare tiers, start a fresh chat on tier 1 and again on tier 3. Send the
same sequence of at least four short prompts. Compare `prompt=`, `completion=`,
and `tokens/sec=` at the same turn. Use short expected replies to keep the
workload comparable.

## Checks

From the repository root:

```powershell
dotnet run --project .\Zeya.Checks\Zeya.Checks.csproj
dotnet run --project .\Zeya.Checks\Zeya.Checks.csproj -- --live
```

The default checks use a fake model and a stub HTTP handler. The live mode uses
Ollama to check recall during one chat and the calculator response.

## Session handoff

- `main` uses `ConversationHistory.Tier1()`, `Tier2()`, and `Tier3()`.
  History evicts complete exchanges and disappears when the app exits.
- `saving-memory` preserves the project with JSON fact storage, memory
  extraction, the tokenizer registry, and the persistence checks.
- The Lab 1 project and the Zeya solution build with zero warnings or errors.
  Windows Application Control blocked the runtime checks at launch.
- [ARCHITECTURE.md](ARCHITECTURE.md) shows where the console, session, memory,
  tools, and model client connect.
