# Vault

C# / .NET 11 MCP server: a **read-only** wrapper of the OpenMediaVault (OMV) JSON-RPC API, so an agent can ask about the state of the NAS without shell access. A capability of Sigil, in the same family as Tinker.

## Rules

- Everything is **C# on .NET 11**.
- The specs are in `docs/`. Read `docs/functional_spec.md` and `docs/technical_spec.md` before changing behaviour, and update them when a decision is made.
- The contract with the clients is the tool names, schemas and messages (technical spec), not the implementation. Do not change it without saying so.
- Messages in tool results are written for the AI that consumes them: what happened and what to do next. Never return a raw exception.
- Write documentation and code in English and communicate with the user in Spanish.
- **Never add an OMV RPC call to the exposed list (`OmvRpcCall.Exposed`) unless it is documented as read-only in the functional spec, section "RPC calls", first.** There is no passthrough: no tool takes an OMV service or method name, and `OmvRpcCall` cannot be built from outside. A test checks that the spec and the code list the same calls.
- Every tool is read-only (`ReadOnly = true`). Vault has no tool that changes the NAS; levels above read-only are out of scope until the user decides otherwise.
- The OMV password is a Docker secret (a file), never an environment variable of the container, a log line, a message or the repository.

## Skills

The reusable skills and the agent come from the **`grimoire` plugin** ([github.com/javixulo/grimoire](https://github.com/javixulo/grimoire)); they are not copied into this repository. Install it once per machine: `claude plugin marketplace add javixulo/grimoire`, then `claude plugin install grimoire@grimoire`. Improve them in that repository, not here. A skill particular to Vault would go in `.claude/skills/` of this repository.

- `grimoire:use-cases-and-criteria`: write the use cases and acceptance criteria of the functional spec.
- `grimoire:open-questions`: keep the table of open questions of each spec.
- `grimoire:mcp-tool-design`: design the tools (Vault's are all read-only).
- `grimoire:writing-tests`, `grimoire:ci-pipeline`, `grimoire:containerize-app`, `grimoire:container-delivery`, `grimoire:app-telemetry`, `grimoire:versioning-and-changelog`: as in the other MCPs.

The agent `grimoire:spec-reviewer` reviews the specs; run it after writing or changing them.

## Commands

```bash
dotnet build
dotnet test
dotnet run --project src/Vault.Mcp
```

`dotnet run` needs `Vault__OmvBaseUrl`, `Vault__OmvUsername` and a password file (`Vault__OmvPasswordFile`, plain text); see the README. The container is configured only through its compose file (technical spec, section 4). Releases are published by `.github/workflows/release.yml` when a `v*.*.*` tag is pushed (technical spec, section 9): never publish an image by hand.

## Git and secrets

- `.env` and everything in `secrets/` (except the example) are git-ignored. Never print them, commit them or put them in a URL.
- Work directly on `main`. Do not create branches unless asked.
- Commit messages follow Conventional Commits: `type(scope): subject`, with the types `feat`, `fix`, `docs`, `test`, `refactor`, `build`, `ci` and `chore`. The subject is in the imperative and has no final period. The scope is optional.
- Use **`git`** for commits and pushes: it is Git for Windows (`C:\Program Files\Git\cmd`, in the machine PATH), and the work happens in the local clone. The repository is `javixulo/Vault`. A shell that was already open when `git` was installed or the PATH changed does not see it: reload the PATH from the registry in that shell (`$env:Path = [Environment]::GetEnvironmentVariable('Path','User') + ';' + [Environment]::GetEnvironmentVariable('Path','Machine')`) before giving up on it.
  - If `git` still fails or cannot be run (not found, authentication, any other problem), use the **GitHub MCP** (`mcp__github__*`) instead, and say so.
  - With the MCP, a commit and its push are the same call (`push_files` creates the commit on the remote branch), so make **one** `push_files` call with all the files, not one call per change. The MCP does not touch the local `.git`: afterwards the local clone is behind `origin/main` even though the files are the same, so run `git fetch origin` and `git reset --mixed origin/main` (the second keeps the working tree) as soon as `git` works, or give them to the user.
- When the user asks for "commit and push", and the work would make several commits, do all the commits first and push **once**, at the end, not after each commit.
- Never do any of these unless asked explicitly:
  - commit;
  - push (asking for a commit is not asking for a push);
  - rewrite history: force push, `reset --hard`, rebase or amend of published commits, `--no-verify`;
  - merge into `main`, close pull requests or delete branches.
