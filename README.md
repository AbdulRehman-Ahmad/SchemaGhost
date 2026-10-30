# SchemaGhost

AI-assisted API migration for forgotten and legacy applications. Give it the JSON your old app expects and the JSON the API returns now. It proposes field mappings with a local open-weight model, checks them deterministically, and emits a reviewable `mapping.json` plus a `CompatibilityAdapter.cs`.

The model only proposes. Deterministic C# validates paths, types and transforms. No model-generated code is ever executed.

## Run
```bash
# needs .NET 10 SDK; Ollama optional
dotnet run --project src/SchemaGhost -- map --legacy samples/old.json --modern samples/new.json --engine heuristic
ollama pull qwen3:0.6b     # or qwen3:1.7b / qwen3:4b for better accuracy
dotnet run --project src/SchemaGhost -- map --legacy samples/old.json --modern samples/new.json --model qwen3:0.6b
dotnet run --project src/SchemaGhost -- bench --cases benchmark/cases.json --with-ollama
dotnet test tests/SchemaGhost.Tests
dotnet pack src/SchemaGhost -c Release   # then: dotnet tool install -g --add-source src/SchemaGhost/nupkg SchemaGhost
```

## Limits (MVP)
Arrays are copied as whole values; no mapping inside arrays of objects. Keys containing dots are unsupported. Missing data is never invented.

## Visual Studio
Open `SchemaGhost.sln`, set `SchemaGhost` as startup project, pick a profile from the run dropdown (next to the green button) and press F5. Profiles use `--pause` so the console stays open.
