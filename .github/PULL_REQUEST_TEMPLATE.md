## What does this pull request do?
<!-- A short summary of the change and why it is needed. -->

Fixes #<!-- issue number, if any -->

## Type of change
- [ ] Bug fix
- [ ] New feature
- [ ] Documentation
- [ ] Tests
- [ ] Other:

## How did you test it?
<!-- Commands you ran and what you saw. -->

## Checklist
- [ ] I read [CONTRIBUTING.md](../CONTRIBUTING.md) and agree to the [Code of Conduct](../CODE_OF_CONDUCT.md)
- [ ] `dotnet test tests/SchemaGhost.Tests` passes
- [ ] `dotnet test tests/SchemaGhost.Api.Tests` passes
- [ ] `npm run build` passes in `web/` (if I changed the web app)
- [ ] I added or updated tests for behavior changes
- [ ] The AI still only proposes: model output is validated, and a human approves
- [ ] I did not commit secrets, `.env` files, or build output (`bin/`, `obj/`, `publish/`, `dist/`, `node_modules/`)
- [ ] I updated the documentation if needed
