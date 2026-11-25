# Project instructions

- This repository contains C# laboratory work.
- For Lab 2 work, read `Lab_2/README.md` first, including the session handoff, and inspect the current code before making changes.
- Lab 2 is a fresh personal AI assistant implementation. Do not inspect or reuse the separate Zeya AI project unless the user asks.
- The assistant's identity is Zeya: use the `Zeya` project/assembly and responsibility-based `Zeya.*` namespaces. `Lab_2` remains the assignment directory. The solution is `Zeya.slnx`, and checks live in `Zeya.Checks`.
- Keep responsibilities separate: console input/output in `ConsoleUI`, conversation orchestration in `Assistant`, model access in `Chat`, memory processing and storage in `Memory`, and startup settings in `Configuration`. See `Lab_2/ARCHITECTURE.md` before changing those boundaries.
- For Lab 2, prioritize readability over compactness: use clear names, focused methods, braces, and multiline calls or declarations when helpful. Keep terminal messages short. Update the Lab 2 handoff when its state or next steps materially change.
- Use offensive programming for internal contracts: invalid arguments and impossible state must throw visibly. Handle expected file, network, user-input, and model-data failures at their boundaries; do not catch programming exceptions as recoverable errors.
