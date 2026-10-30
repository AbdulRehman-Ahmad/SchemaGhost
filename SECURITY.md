# Security Policy

## Reporting a vulnerability

Please **do not** report security problems in public issues, pull requests, or discussions.

Use GitHub's private vulnerability reporting instead:

1. Go to the repository: https://github.com/AbdulRehman-Ahmad/SchemaGhost
2. Open the **Security** tab.
3. Click **Report a vulnerability** and fill in the form.

Only you and the maintainer can see the report. Please include:

- what the problem is and where it is (file, endpoint, or feature),
- steps to reproduce it,
- what you think the impact is,
- any suggested fix (optional).

I will try to acknowledge reports within a few days and keep you updated. SchemaGhost is a small project maintained by one person, so please be patient.

## If you do not see "Report a vulnerability"

That means private vulnerability reporting is not turned on for the repository yet. Until it is, please do not post details publicly. Open a normal issue that says only "I would like to report a security problem privately" (no details), and the maintainer will arrange a private channel.

## For the maintainer: how to enable private reporting

1. Open the repository on GitHub and click **Settings**.
2. In the sidebar, click **Code security** (older layouts: **Security & analysis**).
3. Find **Private vulnerability reporting** and click **Enable**.

You need admin access to the repository. See GitHub's documentation, "Configuring private vulnerability reporting for a repository".

## Supported versions

Only the latest code on the `main` branch is supported. There are no separately maintained release branches.

## Things to know about SchemaGhost's security design

- The AI model only proposes field mappings as JSON. Its output is validated by deterministic code, and model-generated code is never executed.
- Mappings become part of generated C# source, so the API strictly checks paths, transforms, and namespaces before generating anything. Reports about ways around these checks are especially welcome.
- If you configure a hosted, OpenAI-compatible provider (`Ai__Provider=openai`), the JSON you submit is sent to that provider. With Ollama, it stays on your machine.
- Never commit API keys. Use environment variables, and use `.env.example` only as a template.
