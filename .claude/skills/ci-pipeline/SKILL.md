---
name: ci-pipeline
description: Design or review the continuous integration pipeline of an application on GitHub Actions: one build whose package every test job reuses, one independent job per test level, what runs on pull requests and what only on the main branch, a hardened workflow, diagnosable failures, a single final check, and grouped, infrequent dependency updates that never propose versions the project cannot use. Language-neutral. Use this whenever the user creates, changes, standardizes or reviews a CI workflow, asks why CI is slow, flaky or insecure, or says "add CI to this project", even if they do not mention GitHub Actions.
---

# CI pipeline

CI answers one question on every change: does this exact build still work? It is only trustworthy if the thing tested is the thing built, if each kind of failure shows up on its own, and if the workflow cannot be turned against the repository. This skill standardizes that shape for any language. Publishing and deploying are out of scope; so is anything specific to container images.

## 0. Ask before writing

Do not write a workflow from guesses. Ask the human, proposing a default for each answer:

- What the build produces and what is needed to run the tests from it (code under test, dependencies, test programs, fixtures).
- The test levels that exist (unit, integration, contract, end to end) and how long each takes.
- Which levels run on pull requests and which only after merge.
- Anything downloaded or provided from outside (data, models, services, secrets) and how it is verified.
- Which checks the branch protection must require.
- Which versions the project cannot move to (the runtime or language version it requires, a platform it supports, a framework it is pinned to) and how often updates should arrive (see section 7).

If the answer is not known yet, say so in the report instead of inventing it.

## 1. Build once, test the package

The build job runs once per change and produces a **package**: everything a test job needs to run, and nothing it must fetch or compile again (code under test, its dependencies, the tests ready to execute, the fixtures). Test jobs download the package and run it. They do not check out, restore dependencies or compile.

Why: testing a package that a later step rebuilds proves nothing about the rebuilt one, and every repeated compile is time and a chance for the two to differ. How the package looks is up to the language (a compiled output folder, an installed environment, a bundle); define it by its content, not by a tool.

- Pack it as one archive. The artifact service drops file permissions, so executables stop being executable otherwise.
- Remove what the test machine will not use (other platforms' native files, caches, sources) before packing. Smaller packages upload and download faster.
- Give it a short retention (a day is usually enough). It is a hand-off between jobs, not a deliverable.
- A job that really needs part of the source tree (for example, tests that locate a project folder) checks out only that part.

## 2. One independent job per test level

Each level (unit, integration, contract, end to end) is its own job, started when the build finishes, running in parallel with the others.

Why: a failure points at a level, a slow level does not delay the fast feedback of the rest, and one failure does not hide the result of the others. If you use a matrix, set `fail-fast: false` for the same reason. Split a long level into shards only when its time justifies it.

Order the levels so the cheapest answers arrive first, but never serialize them just for tidiness: add `needs` only where a job truly consumes another's output.

## 3. What runs when

- Every pull request and every push to the main branch runs the fast and medium levels.
- The slowest or most expensive levels may run only on pushes to the main branch. That is a trade-off: say it in the spec, and make sure something else covers the gap before merge (a required manual run, a label, a scheduled run).
- Use `concurrency` per branch with cancel-in-progress for pull requests, so a new push stops the obsolete run. Do not cancel runs on the main branch.
- A scheduled run catches what the code does not: expired downloads, moved dependencies, a service that changed. Add one when the pipeline depends on anything external.

## 4. Harden the workflow

The workflow runs code from outside the repository (actions) and, on pull requests, code from contributors. Treat it as an attack surface.

- **Least privilege:** set `permissions: contents: read` at the top, and widen per job only where a job writes. Never give write permissions to a job that runs contributed code.
- **Pin third-party actions to a full commit SHA**, with the version as a comment. Tags can be moved after you trusted them. Let a dependency bot propose updates for them (section 7).
- **Use `pull_request`, not `pull_request_target`,** for anything that builds or tests contributed code. The second one runs with secrets and write access.
- **No untrusted text inside `run:`.** Values such as titles, branch names and messages go through an `env:` variable, never straight into the script.
- **Secrets only where needed**, passed through `env:`, never as command arguments, never printed. A test pipeline rarely needs any.
- **Verify what you download:** pin a revision and check a checksum, then cache by that revision. A changed file fails the job instead of silently changing the tests.
- **Set `timeout-minutes` on every job.** A hung job otherwise burns the default six hours.
- **One source of truth for repeated values** (versions, revisions, checksums). When the same value is copied in two files, one of them will be forgotten.

### 4.1 Repository settings that complete the hardening

The workflow file is half of the security; the repository settings are the other half. They are set by hand on the repository's web page (the code host's API tools usually cannot change them), so write them down in the spec and check them at the end.

- **Order matters.** A required status check can only be chosen after the workflow has run once. So: push, let the CI run, and only then create the rules.
- **A ruleset for the main branch** (Settings → Rules → Rulesets): require a pull request with one approval, require the final check of section 6, block force pushes and deletion. The sole owner cannot approve their own pull request, so add **the owner as a bypass actor** (repository admin); pull requests from anyone else still need the owner's review.
- **A ruleset for version tags** (`v*`) that restricts who can create them: the tag starts the release (`container-delivery`).
- **Actions → General:** require approval to run workflows of pull requests from forks, for all external contributors, so a fork cannot run code in your pipeline before you read it; keep the default workflow permission read-only. The option is called "Require approval for all external contributors" in the repository settings (the enterprise policy page says "outside collaborators").
- **Before making a repository public,** read its whole history for secrets and for personal data that the history carries (author e-mail addresses are in every commit). Removing a secret from a later commit does not remove it from the history.

## 5. Make failures diagnosable

A red job should say what broke without a rerun.

- Upload test results and logs as an artifact with `if: failure()` (or `always()` when results are useful even on success), with a short retention.
- Write a short summary of the run to the job summary: which level, how many tests, what failed.
- Name jobs and steps for what they check, so the list of checks reads like the list of levels.
- Do not hide flakiness with automatic retries of whole jobs. Retry only the step that talks to the network, and fix a flaky test or quarantine it with an owner and a date.

## 6. One final check

Add a last job that depends on all the others (with `if: always()`) and fails unless every one of them succeeded. Require only this job in the branch protection.

Why: the protection then does not change when jobs are added or renamed, and a required job that was skipped does not count as a failure, which would let a broken change through.

## 7. Keep updates under control

Pinning (section 4) only helps if the pins are updated. Configure the dependency bot (Dependabot: `.github/dependabot.yml`) so that its pull requests are few, rare and never ask for something the project cannot do. A bot proposes the newest version; it does not know your requirements.

- **Group the updates.** One pull request per ecosystem (`groups` with `patterns: ["*"]`), not one per dependency. A group cannot span ecosystems, so expect one pull request per ecosystem (actions, base images, packages).
- **Set a slow rhythm.** Every 15 days is a good default for a project that is not under active dependency churn. Dependabot has no "15 days" interval, so use `interval: cron` with `cronjob: "0 5 1,16 * *"` (the 1st and the 16th of each month) and say that it is approximate. Ask the human if they prefer another rhythm; security updates are not delayed by this schedule.
- **Write the ignores from the requirements, not from the first noisy pull request.** Before writing the file, list what the project cannot move to: the runtime or SDK version it targets (`global.json`, `.nvmrc`, `pyproject.toml`, the base image tag, the spec), a platform or framework it is pinned to. For each one, add an `ignore` entry for that dependency with `update-types: ["version-update:semver-major"]` (or the finer range that is really blocked) and a comment that says why and where the requirement is written. Do this for every ecosystem that carries it: the same runtime usually appears in base images, in the package manager and in the setup action.
- **Ignore major versions of actions.** A new major version of an action can change inputs, the format of artifacts or the runtime it needs. Ignore `semver-major` for actions and update them by hand after reading the release notes and running the workflow. Say in the report that this leaves deprecation warnings (for example an old Node runtime) to be handled by hand.
- **Ignore means "decide later", not "never".** Write in the spec which major versions are held back and what would end the hold (for example, the day the project moves to the next runtime), so the ignores are removed on purpose.
- **Check the result.** After pushing, look at the pull requests the bot opens. Each one should be something you would merge after the CI passes. Every pull request you would reject is a missing `ignore`: add it, and write down the requirement behind it.

## 8. Check it

Read the workflow top to bottom and answer, with no help from the author:

- Is anything compiled or installed in more than one job? Does a test job use a package other than the build's?
- Does each test level fail on its own, and does the failure say which?
- Which jobs would still pass if a test level were deleted by mistake? (A job that finds no tests must fail.)
- Is every action pinned, every permission minimal, every download verified, every job limited in time?
- Can a pull request from a fork reach a secret or a write permission?
- After a red run, can you tell what failed from the artifacts alone?
- Does the final check cover every job, and is it the only one the protection requires?
- Is each workflow linted (actionlint, which also runs shellcheck on the scripts) and does a pull request from a fork wait for approval? Do the rulesets of section 4.1 exist, with the final check as the required one?
- Does the bot configuration group its updates, run at the agreed rhythm and ignore every version the project's requirements rule out, each with a comment that names the requirement?

Run it for real: a green run, a run with a deliberately failing test in each level, and a run with a cold cache.
