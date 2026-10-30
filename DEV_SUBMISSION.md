---
title: SchemaGhost: bringing legacy apps back to life with Qwen3 and a human in charge
published: false
tags: devchallenge, hf26challenge
---

*This is a submission for the [Hacktoberfest Open-Source AI Challenge Week 4: Ghost in the Machine](https://dev.to/challenges/hacktoberfest-week4-2026-10-26)*

<!--
Before publishing:
1. Replace [VIDEO_URL_TO_BE_ADDED] below with your video link.
2. Change `published: false` to `published: true`.
3. Add a live demo link in the Demo section if you have one.
Delete this comment block when you are done.
-->

## What I Built

SchemaGhost is a tool that helps an old application keep working after the API it depends on has changed.

Here is the problem. A company has an old app. It expects JSON like this:

```json
{ "user_id": 1024, "name": "Ali", "email": "ali@example.com", "is_active": true, "balance": 250.75 }
```

Then the API gets updated and now returns this:

```json
{
  "account": { "id": 1024, "status": "active" },
  "full_name": "Ali",
  "contact": { "email_address": "ali@example.com" },
  "isActive": "true",
  "wallet": { "balance": 250.75 }
}
```

Fields were renamed, moved into nested objects, and `isActive` turned into a string. The old app breaks, and often nobody remembers how it works. That is a ghost in the machine: software that is still running, but that nobody dares to touch.

You give SchemaGhost the JSON the old app expects and the JSON the API returns now. It compares them, suggests which new field replaces each old one, checks every suggestion, and asks you to approve. After that, it writes a `mapping.json` and a `CompatibilityAdapter.cs` file that turns the new response back into the old shape.

**How I read the theme.** I took "bring a forgotten corner back to life" literally. Legacy apps are the forgotten corners of many companies. SchemaGhost does not rewrite them. It gives them a translator so they can keep working.

## Demo

🎥 **Video demo:** [VIDEO_URL_TO_BE_ADDED]

{% embed [VIDEO_URL_TO_BE_ADDED] %}

<!-- Optional: add a live demo link here if you have one. -->

In the demo, you can see the web app compare the two samples, show suggestions from the heuristic and from Qwen3, let me approve each mapping, verify them, and download the generated files.

## Code

{% embed https://github.com/AbdulRehman-Ahmad/SchemaGhost %}

The repository is MIT licensed. It contains a command line tool, an ASP.NET Core API, an Angular web app, and tests. The README has the exact setup commands.

## How I Built It

### The pieces

- **Core library and CLI (C#, .NET 10):** flattens JSON into paths like `account.id`, compares two shapes, runs the matching, validates, and generates the adapter. The CLI uses Spectre.Console for readable tables.
- **API (ASP.NET Core):** exposes the same logic as endpoints for comparing, suggesting, validating, and generating.
- **Web app (Angular and Tailwind CSS):** a simple workspace where you paste JSON, review suggestions, and download the result.
- **Open-source AI:** Qwen3, served locally by Ollama.

### How Qwen3 is used

Qwen3 is an open-weight language model family from Alibaba's Qwen team. "Open-weight" means the trained model files are published, so anyone can download and run them. I use the small `qwen3:0.6b` by default, and larger sizes work too.

A language model reads text and predicts what text should come next. If you describe two JSON shapes to it in a prompt, it can often work out that `user_id` and `account.id` mean the same thing, even when the names do not look alike. That is the kind of "meaning" guess that plain string matching struggles with.

Here is how SchemaGhost asks:

1. It sends both JSON samples to Qwen3 through Ollama.
2. It asks for a JSON answer in a fixed shape: for each legacy field, a modern path (or `null`), a transform, a confidence, and a short reason.
3. It sets the temperature to 0 and turns off the model's "thinking" so answers stay short and repeatable.
4. If the model still adds `<think>` tags or extra text, the code strips them out before reading the JSON.

### The AI only proposes

A small model can be wrong, so I did not let it make decisions. Everything it says passes through plain C# code:

- A proposed path that does not exist in the new JSON is thrown away.
- A transform that is not on the short allowed list (`none`, `toString`, `toInt`, `toDouble`, `toBool`) is replaced with `none`.
- The model's confidence is capped, and its suggestions are always marked "needs review".
- In hybrid mode, a strong match from the offline heuristic wins. If Qwen3 disagrees, the web app shows its alternative but does not apply it.
- Unsafe conversions are blocked. Only the strings `"true"` and `"false"` become booleans, and turning `3.7` into an integer is refused because it would silently lose data.
- Code is only generated after verification passes. No code written by a model is ever run.

Finally, **a human approves every mapping**. That is the core idea: the AI saves you time, and you stay responsible for the result.

### Challenges

These are the real difficulties that shaped the design:

- **Models make things up.** A model can suggest a field that does not exist. The answer was a strict sanitizer that checks every path against the real JSON, plus tests for hallucinated paths.
- **Model output is messy.** Small models sometimes wrap JSON in extra text. Asking Ollama for schema-shaped output and cleaning the response made it reliable, and the app falls back to the heuristic if the answer is unusable.
- **Confidence is not truth.** A model can sound sure and still be wrong. So I capped its confidence and kept approval in human hands.
- **Mappings end up inside generated source code.** That makes them untrusted input. The API checks paths, transforms, and namespaces strictly, and enforces limits on body size, field counts, and request rate. During the build I also fixed the rate limiter and updated a vulnerable dependency.
- **Privacy.** Production data can be sensitive. The app can run with a local model or with no AI at all, and it warns you if your JSON would go to a third-party provider.

### Lessons learned

- A small model plus good guardrails beats a big model with blind trust.
- Put the AI in a narrow role (suggest, never decide) and the project becomes much easier to test.
- A cheap offline baseline is valuable. It gives the model something to be compared against, and it works when the model is unavailable.
- Safe "no" answers matter. Leaving a field unmapped is better than inventing a value.

### Limitations and future improvements

SchemaGhost is an early version. Today it does not map inside arrays of objects, does not support keys that contain dots, and only supports five simple transforms. Verification uses your samples only. The benchmark cases are synthetic seed cases, so I do not claim real-world accuracy numbers.

Ideas for next steps:

- Mapping inside arrays of objects.
- More transforms (for example dates and enums), each with safety checks.
- Real-world benchmark cases from public API changelogs, and a comparison of different Qwen3 sizes.
- Generating adapters for more languages than C#.
- Frontend tests.

## Why Does Open Innovation Matter?

Legacy systems often handle important and private data, such as customer records and financial details. Open-weight models changed what I could build:

- **Privacy.** With Qwen3 running in Ollama on my own machine, the JSON never has to leave it. With a closed API, I would have to send possibly sensitive samples to someone else's server.
- **Works without the internet.** The whole flow, including the AI part, can run offline.
- **No lock-in.** The model is a setting (`Ai__Model`). I can switch between Qwen3 sizes, or point to any OpenAI-compatible endpoint, without rewriting the app. A tool for rescuing forgotten software should not itself depend on a service that might disappear.
- **Cost.** There is no per-request fee. Small experiments cost nothing but electricity.
- **Learning and trust.** Because the model and the code are open, you can see how it works, test it, and swap it.

There was a trade-off, too. The small local model is less accurate than a large hosted one. That is exactly why SchemaGhost is built so the model never has the last word.

## The Spirit of It

The best software ghosts are not scary. They are quiet apps that someone depends on and no one is looking after. SchemaGhost is my way of honoring them: instead of replacing them, it helps them keep working, with a small open model as a guide and a human making the final call.

<!--
Prize Categories: no partner technology is used in this project, so this section is left out.
My Agent Session: optional and left out. Add it here if you saved a DevRelay session.
-->

*Built for Hacktoberfest 2026. Source code: https://github.com/AbdulRehman-Ahmad/SchemaGhost*
