# Vault

C# / .NET 10 MCP server: a **read-only** wrapper of the OpenMediaVault (OMV) JSON-RPC API, so an agent can ask about the state of the NAS without shell access. A capability of Sigil, in the same family as Tinker.

## Rules

- Everything is **C# on .NET 10**.
- The specs are in `docs/`. Read `docs/functional_spec.md` and `docs/technical_spec.md` before changing behaviour, and update them when a decision is made.
- The contract with the clients is the tool names, schemas and messages (technical spec), not the implementation. Do not change it without saying so.
- Messages in tool results are written for the AI that consumes them: what happened and what to do next. Never return a raw exception.
- Write documentation and code in English and communicate with the user in Spanish.
- **Never add an OMV RPC call to the exposed list (`OmvRpcCall.Exposed`) unless it is documented as read-only in the functional spec, section "RPC calls", first.** There is no passthrough: no tool takes an OMV service or method name, and `OmvRpcCall` cannot be built from outside. A test checks that the spec and the code list the same calls.
- Every tool is read-only (`ReadOnly = true`). Vault has no tool that changes the NAS; levels above read-only are out of scope until the user decides otherwise.
- The OMV credentials are a Docker secret (a file), never an environment variable, a log line, a message or the repository.

## Skills

Project skills are in `.claude/skills/` and are versioned with the code.

- `use-cases-and-criteria`: write the use cases and acceptance criteria of the functional spec.
- `open-questions`: keep the table of open questions of each spec.
- `mcp-tool-design`: design the tools (Vault's are all read-only).
- `writing-tests`, `ci-pipeline`, `containerize-app`, `container-delivery`, `app-telemetry`, `versioning-and-changelog`: as in the other MCPs.

The agent `spec-reviewer` (`.claude/agents/`) reviews the specs; run it after writing or changing them.

## Commands

```bash
dotnet build
dotnet test
dotnet run --project src/Vault.Mcp
```

`dotnet run` needs `Vault__OmvBaseUrl` and a credentials file (`Vault__CredentialsFile`, a JSON file with `username` and `password`); see the README.

## Git and secrets

- `.env` and everything in `secrets/` (except the example) are git-ignored. Never print them, commit them or put them in a URL.
- Work directly on `main`. Do not create branches unless asked.
- Commit messages follow Conventional Commits: `type(scope): subject`, with the types `feat`, `fix`, `docs`, `test`, `refactor`, `build`, `ci` and `chore`. The subject is in the imperative and has no final period. The scope is optional.
- Use the **GitHub MCP** (`mcp__github__*`) for everything that talks to GitHub: commits, pushes, branches, pull requests, issues. Do not shell out to `git` or `gh` for that. The repository is `javixulo/Vault`.
  - With the MCP, a commit and its push are the same call (`push_files` creates the commit on the remote branch). So when several changes are asked for in one go, make **one** `push_files` call with all the files, not one call per change.
  - The MCP does not touch the local `.git`: after it pushes, the local clone is behind `origin/main` even though the files are the same. Say so in the answer and give the user `git fetch origin` and `git reset --mixed origin/main` to catch up (the second keeps the working tree).
- When the user asks for "commit and push", and the work would make several commits, do all the commits first and push **once**, at the end, not after each commit.
- Never do any of these unless asked explicitly:
  - commit;
  - push (asking for a commit is not asking for a push);
  - rewrite history: force push, `reset --hard`, rebase or amend of published commits, `--no-verify`;
  - merge into `main`, close pull requests or delete branches.
