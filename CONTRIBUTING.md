# Contributing to SchemaGhost

Thank you for wanting to help! This guide is written for beginners. If anything is unclear, open an issue and ask. Questions are welcome.

By taking part, you agree to follow our [Code of Conduct](CODE_OF_CONDUCT.md).

## Ways to help

- Report a bug or suggest an idea (see below).
- Improve the documentation or fix a typo.
- Add a test case.
- Fix a bug or build a small feature.

If you are new, look for issues labeled `good first issue`, or ask in a new issue what would be a good start.

## What you need

- [Git](https://git-scm.com/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (for the CLI, API, and tests)
- [Node.js](https://nodejs.org/) and npm (only for the web app in `web/`)
- [Ollama](https://ollama.com) (optional, only if you want to try the Qwen3 features)

## Get the code running

1. Fork the repository on GitHub, then clone your fork:

   ```bash
   git clone https://github.com/<your-username>/SchemaGhost.git
   cd SchemaGhost
   ```

2. Run the tests:

   ```bash
   dotnet test tests/SchemaGhost.Tests
   dotnet test tests/SchemaGhost.Api.Tests
   ```

3. Try the CLI (no AI needed):

   ```bash
   dotnet run --project src/SchemaGhost -- map --legacy samples/old.json --modern samples/new.json --engine heuristic
   ```

4. Optional: run the web app. Start the API on port 5080, then the Angular app.

   ```bash
   # terminal 1 (macOS/Linux; on Windows PowerShell set $env:ASPNETCORE_ENVIRONMENT = "Development" first)
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/SchemaGhost.Api --urls http://localhost:5080

   # terminal 2
   cd web
   npm ci
   npm start
   ```

   Then open http://localhost:4200.

5. Optional: try Qwen3.

   ```bash
   ollama pull qwen3:0.6b
   ```

## Project layout

- `src/SchemaGhost/`: core logic and the command line tool
- `src/SchemaGhost.Api/`: the ASP.NET Core API
- `web/`: the Angular web app
- `tests/`: xUnit tests
- `samples/` and `benchmark/`: example JSON used for demos and the `bench` command

## Making a change

1. Create a branch from `main` with a short, clear name:

   ```bash
   git checkout -b fix/boolean-conversion-message
   ```

2. Make your change. Keep it small and focused. One pull request should do one thing.
3. Add or update tests when you change behavior. Tests live in `tests/`.
4. Run the checks:

   ```bash
   dotnet build SchemaGhost.sln
   dotnet test tests/SchemaGhost.Tests
   dotnet test tests/SchemaGhost.Api.Tests
   cd web && npm ci && npm run build
   ```

   Only run the `web` part if you changed the web app.
5. Commit with a clear message, for example `Fix error message for invalid boolean values`.
6. Push your branch to your fork.

## Project rules to keep in mind

- **The AI only proposes.** Model output must always be validated by deterministic code and approved by a human. Please do not add anything that runs model-generated code or skips approval.
- **Never invent data.** If there is no safe mapping, the field stays unmapped.
- **No secrets.** Never commit API keys, passwords, `.env` files, or private URLs. Use environment variables. Use `.env.example` to document settings.
- **No build output.** Do not commit `bin/`, `obj/`, `publish/`, `dist/`, `node_modules/`, or compiled files (`.dll`, `.exe`, `.pdb`, `.zip`). They are git-ignored.
- Follow the existing code style. The web app has `.editorconfig` and Prettier settings in `web/`.

## Reporting a bug

Open a new issue and choose **Bug report**. Please include:

- what you did, step by step,
- what you expected and what happened,
- your two JSON samples if you can share them (remove any private data first),
- your OS, .NET version, and whether you used Ollama or another model.

Do **not** report security problems in public issues. See [SECURITY.md](SECURITY.md).

## Suggesting a feature

Open an issue and choose **Feature request**. Explain the problem first, then your idea. SchemaGhost is intentionally small, so ideas that keep it simple and safe are easier to accept.

## Submitting a pull request

1. Push your branch and open a pull request against `main` on GitHub.
2. Fill in the pull request template. Link the issue it fixes, like `Fixes #12`.
3. Make sure the tests pass.
4. Be patient. A maintainer will review it and may ask for changes. That is normal and not a criticism.
5. If asked, push more commits to the same branch. The pull request updates automatically.

## Hacktoberfest

SchemaGhost is a Hacktoberfest 2026 DEV challenge submission. Contributions are welcome, but please note the challenge rules for the submission itself: commits made after the deadline are listed in the README.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
