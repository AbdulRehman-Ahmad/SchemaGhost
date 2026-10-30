# SchemaGhost

**Bring old apps back to life when the API they depend on changes.**

SchemaGhost helps you migrate a legacy application to a changed API. You give it two JSON samples:

1. the JSON your old app **expects**, and
2. the JSON the API **returns now**.

SchemaGhost compares them, suggests which new field replaces each old field, checks every suggestion with plain C# code, and (only after you approve) produces a reviewable `mapping.json` and a `CompatibilityAdapter.cs` file.

> Built for the **Hacktoberfest Open-Source AI Challenge, Week 4: Ghost in the Machine**.
> Demo video: [VIDEO_URL_TO_BE_ADDED]

---

## The problem

APIs change. Fields get renamed, moved into nested objects, or change type (`1024` becomes `"1024"`). An old application that still expects the old shape quietly breaks. Nobody remembers how it works, and nobody wants to touch it. That app is a ghost in the machine.

Fixing it by hand means reading both responses field by field and writing glue code. SchemaGhost does the boring comparison for you, uses a small open-weight AI model to help with the hard guesses, and keeps a human in charge of every decision.

## How it works

1. **Compare.** SchemaGhost flattens both JSON samples into paths (like `account.id`) and shows what was removed, added, or changed type.
2. **Suggest.** It proposes a mapping for each legacy field. Suggestions come from an offline heuristic (matching values and similar names), from Qwen3 running in Ollama, or both.
3. **Approve.** You review each suggestion and approve or reject it. Nothing is applied until you do.
4. **Verify.** SchemaGhost checks the approved mappings against your samples: does the path exist, does the type match, is the conversion safe?
5. **Generate.** It writes `mapping.json`, `CompatibilityAdapter.cs`, and `adapted.json`.

**The AI only proposes. It never decides.** The model returns structured JSON, not code. Deterministic C# code then checks every path and transform. Anything the model invents (a field that does not exist, a transform that is not allowed) is thrown away. No model-generated code is ever executed.

## Features

- Structural comparison of two JSON samples, with likely renames and moves flagged as guesses.
- Three suggestion modes: `heuristic` (offline, no AI), `hybrid` (heuristic first, Qwen3 as a second opinion), and `qwen3` (the model leads).
- Human approval for every mapping, in both the command line and the web app.
- Safety checks: refuses unsafe boolean guesses, flags number conversions that would lose decimals, and rejects paths that do not exist.
- Generates a C# `CompatibilityAdapter` class plus the mapping and sample output as JSON.
- Command line tool (`schemaghost`) and a web app with an ASP.NET Core API.
- Works fully offline with Ollama, or with any OpenAI-compatible endpoint if you configure one.

## Before and after

**Legacy JSON** (what the old app expects, `samples/old.json`):

```json
{
  "user_id": 1024,
  "name": "Ali",
  "email": "ali@example.com",
  "is_active": true,
  "balance": 250.75
}
```

**Modern JSON** (what the API returns now, `samples/new.json`):

```json
{
  "account": { "id": 1024, "status": "active" },
  "full_name": "Ali",
  "contact": { "email_address": "ali@example.com" },
  "isActive": "true",
  "wallet": { "balance": 250.75 }
}
```

Fields moved, got renamed, and `isActive` became the string `"true"`.

**Mappings you approve:**

| Legacy field | Modern source        | Transform |
| ------------ | -------------------- | --------- |
| `user_id`    | `account.id`         | none      |
| `name`       | `full_name`          | none      |
| `email`      | `contact.email_address` | none   |
| `is_active`  | `isActive`           | `toBool`  |
| `balance`    | `wallet.balance`     | none      |

**Adapted JSON** (the modern response rebuilt in the legacy shape, so the old app works again):

```json
{
  "user_id": 1024,
  "name": "Ali",
  "email": "ali@example.com",
  "is_active": true,
  "balance": 250.75
}
```

`account.status` is not in the output because the legacy app never expected it. Which suggestions are accepted automatically depends on your confidence threshold. Low-confidence ones, such as the boolean, are the ones you review by hand.

## Qwen3 and Ollama

SchemaGhost uses [Qwen3](https://github.com/QwenLM/Qwen3), an open-weight model family, served locally by [Ollama](https://ollama.com).

- The default model is `qwen3:0.6b`, which is small and fast. Larger ones such as `qwen3:1.7b` or `qwen3:4b` can be more accurate.
- The request asks Ollama for output that matches a JSON schema, with `temperature` 0 and thinking turned off.
- If a model still returns `<think>` tags or extra text, SchemaGhost strips them before parsing. In the web app and API, if the output is not valid, SchemaGhost falls back to the heuristic results and tells you.
- In the API, model confidence is capped at 0.85 and every model suggestion is marked "needs review".
- In `hybrid` mode, a strong heuristic match (confidence 0.7 or higher) wins. If Qwen3 disagrees, its alternative is shown to you but not applied.

Because the model runs on your machine, your JSON does not have to leave it. If you instead configure `Ai__Provider=openai` (any OpenAI-compatible endpoint), your JSON is sent to that provider. The web app shows a privacy notice in that case.

## Human approval

Approval is built into every step:

- **Command line:** you are asked `Approve <legacy> ← <modern>?` for each mapping. Using `--yes` auto-approves only mappings at or above the confidence threshold (default 0.7).
- **Web app:** every row has an Approve checkbox. "Approve all ≥ 70%" and "Reject all" are shortcuts. Code generation is blocked if verification finds problems.

## Technology stack

| Part     | Tools |
| -------- | ----- |
| CLI      | .NET 10, C#, [Spectre.Console](https://spectreconsole.net/), packaged as a .NET tool |
| API      | ASP.NET Core minimal API (.NET 10), OpenAPI, built-in rate limiting |
| Web app  | Angular 22, TypeScript, Tailwind CSS 4 |
| AI       | Qwen3 through Ollama (or any OpenAI-compatible endpoint) |
| Tests    | xUnit (core logic and API) |
| Packaging | Dockerfile (builds the Angular app and serves it from the API) |

## Architecture

```
src/
  SchemaGhost/        Core library + CLI: JSON flattening, heuristic matcher,
                      Ollama client, sanitizer, validator, adapter generator
  SchemaGhost.Api/    ASP.NET Core API: /api/compare, /suggest, /validate,
                      /generate, /download, /config, /health
web/                  Angular app that talks to the API
tests/                xUnit tests for the core and for the API
samples/              Example legacy and modern JSON
benchmark/            Small set of mapping cases for the `bench` command
```

```
Legacy JSON + Modern JSON
        │
        ▼
  Compare → Suggest (heuristic and/or Qwen3) → Sanitize (drop invalid paths/transforms)
        │
        ▼
  You approve → Verify (types, paths, safe conversions) → Generate
        │
        ▼
 mapping.json · CompatibilityAdapter.cs · adapted.json
```

The API project references the core project, so the CLI and the web app share the same logic.

## Local setup

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) and npm (only for the web app; the Dockerfile uses Node 24)
- [Ollama](https://ollama.com) (optional, only for the Qwen3 features)

### 1. Clone and test

```bash
git clone https://github.com/AbdulRehman-Ahmad/SchemaGhost.git
cd SchemaGhost
dotnet test tests/SchemaGhost.Tests
dotnet test tests/SchemaGhost.Api.Tests
```

### 2. Try the CLI without any AI

```bash
dotnet run --project src/SchemaGhost -- map --legacy samples/old.json --modern samples/new.json --engine heuristic
```

### 3. Try the CLI with Qwen3

```bash
ollama pull qwen3:0.6b
dotnet run --project src/SchemaGhost -- map --legacy samples/old.json --modern samples/new.json --model qwen3:0.6b
```

Results are written to `ghost-out/` (this folder is git-ignored).

### 4. Run the web app

Start the API on port 5080 (the Angular dev server proxies `/api` to this port).

PowerShell (Windows):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/SchemaGhost.Api --urls http://localhost:5080
```

Bash (macOS/Linux):

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/SchemaGhost.Api --urls http://localhost:5080
```

In a second terminal:

```bash
cd web
npm ci
npm start
```

Open http://localhost:4200.

Development mode uses Ollama with `qwen3:0.6b` at `http://localhost:11434`. Without `ASPNETCORE_ENVIRONMENT=Development`, the API starts with AI turned off (`Ai:Provider` is `none`) and only the heuristic works.

### Configuration

Settings live in `src/SchemaGhost.Api/appsettings.json` and can be overridden with environment variables. See `.env.example`. **Never commit real keys.**

| Setting | Meaning |
| ------- | ------- |
| `Ai__Provider` | `none`, `ollama`, or `openai` (any OpenAI-compatible endpoint) |
| `Ai__BaseUrl` | Where the model server is |
| `Ai__Model` | Model name, for example `qwen3:0.6b` |
| `Ai__ApiKey` | Only if your provider needs one |
| `Ai__TimeoutSeconds` | How long to wait for the model |
| `Cors__Origins__0` | Allowed web origin, if the frontend is hosted elsewhere |

### Docker

```bash
docker build -t schemaghost .
docker run -p 8080:8080 schemaghost
```

This builds the Angular app and serves it from the API on port 8080. The AI is off unless you pass the `Ai__...` environment variables.

## Usage

CLI commands:

| Command | What it does |
| ------- | ------------ |
| `map` | Compare, suggest, review, verify, and write output files |
| `verify` | Check an existing `mapping.json` against two samples |
| `apply` | Apply a `mapping.json` to a modern JSON file and print the legacy-shaped result |
| `bench` | Score the heuristic (and optionally a local model) on `benchmark/cases.json` |

Useful options for `map`: `--legacy`, `--modern`, `--engine heuristic|ollama`, `--model`, `--threshold`, `--out`, `--yes`, `--ollama-url`.

```bash
dotnet run --project src/SchemaGhost -- verify --mapping ghost-out/mapping.json --legacy samples/old.json --modern samples/new.json
dotnet run --project src/SchemaGhost -- apply --mapping ghost-out/mapping.json --input samples/new.json
dotnet run --project src/SchemaGhost -- bench --cases benchmark/cases.json --with-ollama
```

To install the CLI as a tool:

```bash
dotnet pack src/SchemaGhost -c Release
dotnet tool install -g --add-source src/SchemaGhost/nupkg SchemaGhost
schemaghost help
```

Web app: paste (or load an example of) your legacy and modern JSON, compare, choose a suggestion engine, approve mappings, verify, then download the generated files.

## Limitations

- Arrays are copied as whole values. There is no mapping inside arrays of objects.
- Keys that contain dots are not supported.
- Only these transforms exist: `none`, `toString`, `toInt`, `toDouble`, `toBool`. Splitting, merging, or computing fields is not supported.
- Only `"true"` and `"false"` strings are converted to booleans. Other values are rejected on purpose.
- Missing data is never invented. A legacy field with no approved mapping is simply missing from the output.
- Verification uses your sample JSON only. Different data in production may behave differently, so review the generated adapter.
- Small models such as `qwen3:0.6b` make mistakes. That is why the model's suggestions always need review.
- Input limits in the API: 1 MB request body, 2,000 fields per document, 500 mappings. AI suggestions are rate limited to 30 requests per minute per client.
- The cases in `benchmark/cases.json` are synthetic seed cases, not real-world changelogs, so they are not a measure of real-world accuracy.
- The frontend currently has no unit tests.

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and our [Code of Conduct](CODE_OF_CONDUCT.md). To report a security problem, see [SECURITY.md](SECURITY.md).

## Hacktoberfest 2026

SchemaGhost is a submission to the **Hacktoberfest Open-Source AI Challenge Week 4: Ghost in the Machine** on DEV. The project was built during the challenge window (October 26 to 31, 2026). The write-up is in [DEV_SUBMISSION.md](DEV_SUBMISSION.md).

Commits made after the submission deadline (October 31, 2026, 11:59 PM PDT) will be listed here:

- None so far.

## License

[MIT](LICENSE) © 2026 Abdul Rehman Ahmad.

SchemaGhost uses third-party packages (for example Spectre.Console, Angular, and Tailwind CSS) under their own licenses. Qwen3 models are downloaded separately through Ollama and are not part of this repository; check the license of the model you choose.
