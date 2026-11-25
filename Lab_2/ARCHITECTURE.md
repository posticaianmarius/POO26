# Zeya architecture

`Program.cs` reads the startup options, creates the chosen history tier,
connects the Ollama client to the assistant, and starts the console loop.

| Component | Responsibility |
| --- | --- |
| `ConsoleChat` | Read messages and commands, print replies, and report expected model errors. |
| `AssistantSession` | Assemble chat requests and coordinate tool calls. |
| `ConversationHistory` | Store the newest complete user/reply pairs for the selected tier. |
| `Toolbox` | Run the clock, date, and calculator tools. |
| `IChatModel` and `OllamaChatModel` | Send messages to Ollama and record the returned usage metrics. |
| `AssistantSettings` | Provide the default model name, Ollama address, and request timeout. |

## One turn

1. The session puts the system message first, followed by history and the
   user's new message.
2. The model sends a plain-text answer or a JSON tool request. Tool requests
   and results go into the current request. The session asks the model again
   after each tool result and stops after three tool attempts.
3. Once the final answer arrives, history adds the user message and answer as
   a pair. If history is full, it removes the oldest pair first.

`Program.cs` creates and supplies the history and model to `AssistantSession`.
That is aggregation. The session constructs its own `Toolbox`, which is
composition. The application also compiles the existing Lab 1 `Logger`,
`Formatter`, and `ConsoleSink` directly from their source files.
