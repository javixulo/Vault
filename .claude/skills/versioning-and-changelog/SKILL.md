---
name: versioning-and-changelog
description: Version an application and keep its changelog: what counts as a breaking change (the public surface), semantic version numbers including the 0.x phase, git tags as the single source of the version and how the build stamps it into the app and image, choosing the next number from commit types with a person confirming, and writing a changelog that users can read. Language-neutral. Use this whenever the user prepares a release, asks what version number comes next, creates or fixes a changelog or release notes, asks what counts as a breaking change, or wonders where the version should live, even if they only say "let's release".
---

# Versioning and changelog

A version number is a promise to whoever uses the app: "if only the last number changed, nothing you rely on broke". The changelog is where you explain what changed in terms they care about. Both are worthless if the number does not mean something fixed, if the version printed by the running app is not the one released, or if the changelog is a dump of commit messages. This skill covers the numbers, where the version lives, and the changelog. Publishing the image is in `container-delivery`.

## 0. Ask before writing

Ask the human, proposing a default for each answer:

- **The public surface:** everything other people or programs depend on (section 1). The default is to list it with them, not to guess.
- Whether the project is still 0.x, and if it is already stable, since when.
- Where releases are announced and who reads them (users, other projects, only the author).
- Whether versions already exist, and in which form.

Write the answers in the spec. They are what every later version decision is judged against.

## 1. The public surface decides what is breaking

A change is **breaking** when something that worked for a user stops working after upgrading without them changing anything. That only has meaning relative to what users rely on, so name it once, in the spec. Typical contents:

- For a service or library: its calls, their inputs and outputs, error behaviour.
- For a server whose clients are programs or agents: names, schemas and messages of what it exposes (see `mcp-tool-design`). An agent reads these, so a renamed tool or a reworded error is a change to the contract.
- For a container: environment variables, mounted folders, the user it runs as, the port, the data format on disk and its migrations.
- For a command line: commands, options, exit codes and output that scripts parse.

Anything outside that list is internal and may change in any release. Add things to the list on purpose, because each one is a promise.

## 2. Version numbers

- Use **MAJOR.MINOR.PATCH**. PATCH for fixes that do not change the public surface, MINOR for backward-compatible additions, MAJOR for a breaking change.
- A **pre-release** adds a suffix (`1.2.0-beta.1`). It sorts before the release it precedes, and nothing should treat it as stable (it never moves a "latest" pointer).
- **While the version is 0.x,** anything may break, and a breaking change raises MINOR, not MAJOR. The step to 1.0.0 is a decision by the human, written in the spec ("the surface is stable and we support it"), never an automatic result.
- A version is **never reused or rewritten.** If a release is wrong, publish the next one and say so in the changelog.
- Deprecate before removing: announce in one release, remove in a later one, so users get warning.

## 3. One source: the git tag

The version lives in exactly one place, the tag on the commit that was released (`v1.2.3`). Everything else is derived from it by the build, never typed.

- The build reads the tag and **stamps it into** the application's metadata, the image label, and whatever the app reports about itself at run time: its logs and telemetry (see `app-telemetry`), the handshake of a protocol that carries a server version, a version command or endpoint. The version someone sees on a running app must be the released one.
- Builds that are not a release carry a clearly different version (the last tag plus the commit, or a development marker), so nobody mistakes them for one.
- Check that the stamping works: a version that is the framework's default (such as 1.0.0) on every build means the tag never reached the build.
- Do not keep a hand-edited version in a file next to the tag: two sources drift. If a tool insists on a file, generate it in the build.

## 4. Choosing the next number

- Propose it from the commit types since the last tag, using Conventional Commits: a `fix` gives PATCH, a `feat` gives MINOR, and a breaking marker (`!` or a breaking-change note) gives MAJOR (MINOR while 0.x). Take the highest.
- **A person confirms the number,** after reading the proposed list against the public surface. Commit types are written by people in a hurry: a `refactor` that renamed a public name is breaking, and a `feat` that only touched internals is not.
- If any commit does not follow the convention, read it; do not skip it.

## 5. The changelog

A file in the repository (`CHANGELOG.md`), newest version first, so it travels with the code and with every copy of it. Each version has its number and date, and an `Unreleased` section at the top collects what is pending.

- **Draft from the commits, then edit by hand.** Generate a draft grouped by kind of change (added, changed, deprecated, removed, fixed, security). Then rewrite it for the reader: what they can now do, what they must change, in plain words. A raw list of commit subjects is not a changelog. Leave out changes with no effect on a user (tests, build tweaks, refactors), unless they changed the surface.
- **Breaking changes go first,** marked clearly, each with how to migrate: the old way, the new way, and whether data needs converting. This is the part users read.
- Mention what changed in configuration, mounted folders and the user a container runs as, even if small. Those break installations that "did nothing".
- Link each entry to the change (a commit, an issue or a pull request) when it helps.
- **The release notes are the changelog entry of that version,** copied, not a second text to write.
- Do not edit the entries of published versions. A correction is a new line in the next version.

## 6. A release, in order

1. Confirm the public surface list is up to date.
2. Propose the number from the commits and have the human confirm it.
3. Draft and edit the changelog entry. Commit it.
4. Tag that commit with the version. The release workflow takes it from there.
5. Check that the published artifacts and the running app report that version.

## 7. Check it

- Is the public surface written down, and was every change since the last tag judged against it?
- Does the number match the highest kind of change, with the 0.x rule applied?
- Is the tag the only place the version is typed, and does the running app report it? Is a non-release build recognizable?
- Does the changelog entry say what changed for a user, with migration steps for each breaking change, and is it in English and plain words?
- Did anything get published or rewritten under an existing version? (It must not.)
