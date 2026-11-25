# Zeya - Personal AI assistant

Zeya is a personal AI assistant built from scratch in C# with .NET 10.
It chats through local Ollama and keeps useful facts in one local JSON file.
It needs no database or API key. .NET restores the tokenizer package at build time.

The project belongs to Lab 2, so its directory remains `Lab_2`.
The application, assembly, and namespaces use the name `Zeya`.

## Run

From the repository root:

```powershell
dotnet build .\Zeya.slnx
dotnet run --project .\Lab_2\Zeya.csproj
```

Inside `Lab_2`, use `dotnet run`. Ollama must be running.
The model `qwen3.5:4b-q4_K_M` is already installed on this laptop.
On another computer, download it once:

```powershell
ollama pull qwen3.5:4b-q4_K_M
```

## Try it

```text
You: Remember that my favorite color is blue.
You: /memory
You: /clear
You: What is my favorite color?
You: Correction: my favorite color is green now.
You: /memory
```

- `/memory` shows saved facts, their IDs and scopes, and the memory file path.
- `/forget <id>` removes one fact and clears recent conversation context.
- `/clear` clears recent conversation context and keeps permanent memories.
- `/exit` exits the program.

Start Zeya with `dotnet run` in a terminal to choose the memory tier and log
level before chatting. Press Enter at either prompt to use its default.
Tier 1 uses 50% of the tokens available for prior exchanges, tier 2 uses 75%,
and tier 3 uses 100%. The default is tier 2. An exchange is a user message and
its reply. Zeya includes the newest complete exchanges that fit after reserving
room for the instructions, saved facts, current turn, chat formatting, and reply.

Ask Zeya “What time is it?”, “What is today's date?”, or “What is 12 times 7?”
to try its local tools. Qwen can request `get_time`, `get_date`, or
`calculator` with a JSON object such as `{"tool":"calculator","input":"12 * 7"}`.
The calculator accepts two numbers and one operator (`+`, `-`, `*`, or
`/`). Time and date use the computer's local clock.

Choose `trace` for request and conversation history events.
The default is `info`. Use `warn` for history trims and errors, or `error`
for errors only. Launches with redirected input or output use both defaults.

Ollama metrics are logged per successful request. Chat requests, including
tool follow-ups, are labeled `Chat`. Structured memory requests are labeled
`Memory`. Tokens per second means completion tokens divided by the HTTP
request time measured on the client. A missing token count is displayed as
`n/a`.

Before sending a chat request, Zeya logs `Chat: input tokens=...` at `info`
level for the latest user message. It shows `n/a` when the exact count is
unavailable. Ollama's `prompt=...` count also includes conversation history,
instructions, saved facts, and chat formatting. `ModelTokenCounter` uses the
bundled [Qwen3.5-4B tokenizer](https://huggingface.co/Qwen/Qwen3.5-4B/tree/851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a)
locally through `Tokenizers.HuggingFace`, version `3.23.1`.

To register another model, place its tokenizer JSON file under `Chat/` and
add its model name and relative path to `Chat/TokenizerRegistry.cs`.

## Project structure

```text
POO26/
|-- Zeya.slnx
|-- Lab_2/
|   |-- Zeya.csproj
|   |-- Program.cs
|   |-- Assistant/         Conversation flow, history, and personality instructions
|   |-- Chat/              Chat messages and the IChatModel interface
|   |   `-- Ollama/        The Ollama provider and its response format
|   |-- Memory/            Memory extraction, facts, updates, and IMemoryStore
|   |   `-- Storage/       Validated JSON persistence
|   |-- ConsoleUI/         Terminal input, output, and commands
|   `-- Configuration/     Model, endpoint, timeout, and memory location
`-- Zeya.Checks/            Deterministic checks and optional live checks
```

Start reading at `Program.cs`. It connects the settings, model, memory store,
assistant session, and console. [ARCHITECTURE.md](ARCHITECTURE.md) explains the
responsibilities, dependencies, and the message-to-memory flow.

Readability takes priority over compactness. Use descriptive names, focused
methods, braces, and multiline formatting for long declarations and calls.
Namespaces follow the feature folders, such as `Zeya.Assistant`,
`Zeya.Memory`, and `Zeya.Chat.Ollama`.
The complete source-by-source offensive-programming review is recorded in
[OFFENSIVE_REVIEW.md](OFFENSIVE_REVIEW.md).
At the user's request, `AssistantSettings` is in the global namespace and
can be used without a configuration namespace import.

## How memory works

1. `ConsoleChat` passes your message to `AssistantSession`.
2. The session builds a chat request using recent conversation context, the
   assistant instructions, and saved facts, then calls `IChatModel`.
3. The console prints the reply. A separate memory step calls
   `MemoryService.ExtractAndSaveAsync`.
4. The same local model extracts useful facts from the new user message,
   compares them with saved facts, and returns proposed actions under a JSON schema.
5. `JsonMemoryStore` validates IDs, lengths, scopes and exact supporting quotes,
   skips exact duplicates, and stages the entire batch before saving.
6. A temporary JSON file replaces `memory.json`. Only then does the in-process
   memory change. Failed writes preserve the previous state.

If a proposal fails JSON or memory validation, `MemoryService` makes one repair
request with the validation error. The repaired proposal must pass the same
checks. A second rejection is reported without saving; network and file errors
do not trigger this repair step.

The default file is `%LOCALAPPDATA%\Zeya\memory.json`, normally
`C:\Users\posti\AppData\Local\Zeya\memory.json` on this laptop.
It is created when the first fact is saved and survives restarts, rebuilds,
and changes to the terminal's working directory.
Zeya always uses this default location; there is no environment-variable override.

A `MemoryFact` contains an ID, text, scope, supporting user quote, and UTC
update time. These still serialize to `id`, `text`, `scope`,
`evidence`, and `updatedAt`. Existing memory files need no migration.

Corrections update facts in the same scope. A project exception creates a
separate fact and preserves a global preference. Natural-language deletion is
reserved for explicit forgetting requests. `/forget <id>` removes a specific
fact directly. Successful deletions also clear the session's recent history.

Same-scope updates must retain a meaningful word from the existing fact's
subject; generic words such as "user" and "prefers" do not qualify. Obvious
subject mismatches are rejected so repair can propose an addition instead.
This is a conservative lexical safeguard, not semantic proof: valid rewordings
without a shared subject word can also be rejected.

The extraction prompt filters filler, generic questions, temporary requests,
unsupported guesses and secrets. Common English instructions such as
"for this conversation only" and "do not save" also skip the entire memory
step in C#. Semantic filtering and paraphrase detection remain model decisions:
a copied quote identifies the source but does not prove a correct interpretation.

## Error handling

Internal code uses offensive programming: bad arguments fail at method entry,
and impossible session state or broken model-provider contracts throw visibly.
Programming exceptions are not converted into network errors or memory-repair
requests. Expected user, file, network, and model-data errors stay handled at
their boundaries. A missing memory file is normal on first startup; a corrupt
or unreadable file stops startup with exit code 1.
Session operations, memory requests, store operations, and console run loops
reject overlapping use. Memory saving accepts only the current session's latest
unsaved reply; clearing the session or starting another turn expires it.
Internal replies and change counts validate themselves at construction, while
wire/JSON records are checked at their external-data boundaries. Shared JSON
options are read-only. Passing arguments to Zeya reports exit code 2.

## Current limits

- Up to 50 facts, with 240 characters per fact and 60 per scope.
- Up to eight proposed memory changes per user message.
- Up to three tool-call attempts per question. Invalid requests return an error
  to the model and count toward the same limit.
- Saved fact summaries are all included in the model context; there is no
  embedding search or automatic expiration yet.
- Chat budgets use Qwen's message token counts plus allowances for formatting.
  When the tokenizer is unavailable, Zeya estimates text tokens from UTF-8 byte
  counts. Console input is limited to 4,000 characters.
- Memory extraction has a separate 512-token budget for earlier messages,
  including an allowance for their JSON formatting. It skips messages that
  exceed the remaining budget. The current user message is included separately.
- Ollama requests use an 8,192-token context, thinking disabled, and a maximum
  of 1,024 generated tokens. The first response can take a minute or more.
- Run one assistant instance per memory file to avoid concurrent writes.

Wait for the next `You:` prompt before entering another message: memory
processing finishes after the chat reply. Invalid existing memory files stop
startup without being overwritten.

## Verification

From the repository root:

```powershell
dotnet build .\Zeya.slnx
dotnet run --project .\Zeya.Checks\Zeya.Checks.csproj
```

The deterministic checks use temporary files, a stub HTTP handler, and a fake
`IChatModel`. They check persistence, validation, duplicate handling, scopes,
failed saves, session rollback, forgetting, and bounded conversation context.
They run without an Ollama server.

For model-level checks with Ollama running:

```powershell
dotnet run --project .\Zeya.Checks\Zeya.Checks.csproj -- --live
```

For terminal component checks with Python 3 and Ollama:

```powershell
python .\Zeya.Checks\live_checks.py
```

The Python runner itself has six checks that need neither Ollama nor .NET:

```powershell
python .\Zeya.Checks\test_live_checks.py
python -O .\Zeya.Checks\test_live_checks.py
```

Live checks use an isolated temporary memory file and leave the default file
untouched. The Python runner uses the check project's `--console <path>` mode,
which constructs the real console and assistant with a temporary store; it does
not launch the application's default-path startup. Current results:

- Solution build: zero warnings and errors.
- All 42 deterministic C# checks passed before the token-budget change.
  The updated checks build, but Windows Application Control blocked execution
  of the new app and check assemblies on this laptop.
- An isolated live Qwen chat returned 84 for 12 × 7, today's date, and a
  time that matched the local clock after explicit tool examples were added
  to the system prompt.
- Interactive startup accepts defaults or explicit selections and retries
  invalid entries. Piped startup uses defaults and command-line arguments
  report exit code 2.
- All six Python runner checks pass normally and with optimization enabled.
- Main application startup/exit passes. Corrupt startup reports exit 1 without
  changing the file; invalid app/check-runner arguments report exit 2.
- Live Qwen terminal checks pass saving, paraphrase deduplication, color
  correction, and transient-information filtering. The full run still fails
  because Qwen sometimes omits the independent global explanation preference.
  An earlier run overwrote the color; the subject guard now preserves it in
  the observed case. This remaining model-dependent behavior is not a passed
  end-to-end result. Failed live assertions print the actual saved facts.

## Earlier tier comparison

This run used the previous tiers, which capped history at 2, 6, or 10 exchanges.
One live run used Qwen 3.5 4B after one warm-up request. Each tier got the same
eight prompts in a fresh session with an empty temporary memory store. Each
prompt asked for one exact sentence and included “for this conversation only”
to skip memory extraction. No tool calls were involved.

Each prompt started with
`For this conversation only, reply with exactly this sentence:` followed by
one of these sentences in order:

1. The lantern glowed softly beside the window.
2. The gardener planted tulips near the gate.
3. A brown sparrow landed on the wooden fence.
4. The baker placed fresh bread on the counter.
5. A small boat crossed the lake at sunrise.
6. The library opened early on a quiet morning.
7. The wooden bridge stretched over the stream.
8. The evening breeze carried the scent of pine.

| Tier | Prompt tokens, turn 1 → 8 | Completion tokens, turn 8 | Request time, turn 8 | Tokens/sec, turn 8 | History trims |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 282 → 358 | 10 | 0.271 s | 36.84 | 6 |
| 3 | 282 → 557 | 10 | 0.249 s | 40.20 | 0 |

Tier 3 sent 199 more prompt tokens on turn 8. The timings are observations
from one warmed run and do not establish which tier is generally faster.

## Session handoff

- Assignment Part A: the Lab 1 logger now delegates formatting and console output to `Formatter` and `ConsoleSink`. Both logger constructors were exercised, the parentheses examples retained their results, and `%n` inside a message stayed literal.
- Assignment Track 2 memory tiers: `ConversationHistory.Tier1()`, `Tier2()`, and `Tier3()` use 50%, 75%, and 100% of the remaining history token budget. Memory extraction uses its own 512-token budget for previous messages. Choose the tier and log level at the terminal prompts. Enter accepts tier 2 and info.
- Assignment Track 2 tools: `Toolbox` provides `get_time`, `get_date`, and `calculator`. `AssistantSession` reads JSON tool requests, feeds results and errors back to Qwen, and stops after three attempts per question. Only the final answer enters conversation history.
- Assignment Track 2 logging: Zeya compiles the logger sources directly from `Lab_1/Logging/`. `TokenizerRegistry` owns the tokenizer paths. `ModelTokenCounter` loads the registered file at construction and uses UTF-8 byte estimates for budgeting when it is unavailable. Ollama returns prompt/completion counts and elapsed time as metadata that is excluded from JSON sent to the model. The shared log level defaults to info. The eight-turn tier 1 vs. tier 3 measurements are recorded above.
- Goal: build Zeya as a personal AI assistant in C#, with user-created tools and permanent memory. Lab 2 is its assignment location, not its application identity.
- Identity and layout: solution `Zeya.slnx`, application `Lab_2/Zeya.csproj`, checks `Zeya.Checks/Zeya.Checks.csproj`, assembly `Zeya`, and feature-based `Zeya.*` namespaces. Read `ARCHITECTURE.md` before changing boundaries.
- Responsibilities: `Program.cs` wires components; `ConsoleChat` handles terminal interaction; `AssistantSession` and `ConversationHistory` manage a conversation; `MemoryService` extracts memories; `JsonMemoryStore` validates and persists them. `IChatModel` and `IMemoryStore` separate those services from their implementations.
- Settings: `Configuration/AssistantSettings.cs` is a static class in the global namespace. Zeya and the check console share its model name, Ollama address, and request timeout. The default model is `qwen3.5:4b-q4_K_M` (approximately 3.3 GB).
- Laptop: Ryzen 9 7940HS, 16 GB RAM, RTX 4070 Laptop GPU with 8 GB VRAM.
- Implemented: interactive local chat, bounded history, persistent JSON facts, filtering/deduplication, corrections, scopes, explicit forgetting, and `/memory`, `/forget <id>`, `/clear`, `/exit`.
- Storage: `JsonMemoryStore()` resolves the default path `%LOCALAPPDATA%\Zeya\memory.json`. Tests pass temporary paths to the same store. The Python runner launches the check project's `--console <path>` mode to keep real memories separate.
- Latest verification: the static settings and tokenizer registry changes build with zero warnings/errors. MSBuild includes the Qwen tokenizer and license in build and publish output. Windows Application Control blocked the updated app at launch. Before these changes, a live Qwen chat logged `input tokens=10` before returning `OK`.
- Live verification: the terminal runner passes its first four checks, including restart persistence, deduplication, correction, and transient filtering, but the full run still fails to retain an independent global explanation preference reliably. The live run initially exposed replacement of an unrelated color fact; the new subject guard rejects that mismatch and the observed later run preserved the color. Remaining recall/deletion/full-flow cases were not reached. Do not claim the full AI-dependent suite passes.
- Important safeguards: common English session-only/no-save requests skip extraction in C#; cross-scope updates become separate facts; every proposed change needs an exact quote from the current user message. Semantic judgments remain model decisions.
- Error-handling style: offensive programming for internal contracts. Constructors reject missing dependencies; public APIs reject invalid arguments; pending or invalid conversation transitions and broken `IChatModel` replies throw visibly. Expected external data/IO failures remain handled; programming exceptions bypass model repair and console recovery. Failed startup reports exit code 1. Tests now include contract propagation, invalid arguments with no side effects, and overlapping operations. The capacity fixture uses the documented 50-fact limit because the store's constant is private.
- Full offensive-programming audit: all 23 source/check scripts were reviewed (20 app C# files, one C# runner, two Python scripts); see `OFFENSIVE_REVIEW.md` for every file's outcome. Added single-use/current-session reply ownership, instance-level operation gates, immutable validated internal results, read-only serialization options, direct file reads that expose access failures, preserved cleanup failures, strict CLI handling, and Python checks that remain enabled under optimization. Transport records and constant prompts remain intentionally validated at their owning boundaries. The subject-word update guard is conservative and does not prove semantic identity. No cross-process file locking was added.
- Preferences: readability over compactness, intuitive names, focused methods, explicit constructors, braces, multiline long calls, and logical spacing. Retain `if(...)`/`while(...)`, spaces around operators, and short terminal messages.
- Not implemented: user-defined tools, expiration, historical versions, scalable retrieval, or coordination between concurrent instances.
- The separate Zeya AI project remains excluded; do not inspect or reuse it unless requested.
