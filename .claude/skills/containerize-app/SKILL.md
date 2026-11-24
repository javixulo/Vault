---
name: containerize-app
description: Make an application run well inside a Docker container: the image (multi-stage, pinned, small, no secrets), running as a non-root user, configuration through environment variables, secrets as files (never environment variables), the compose file and its environment file, data kept outside the image, validation at startup with actionable messages, a health check, logs and clean shutdown, and a compose file that documents how to run it. Language-neutral. Use this whenever the user wants to put an app in a container, "dockerize" it, write or review a Dockerfile or compose file, or asks why a container fails to start, loses data or cannot write to a folder, even if they only say "make it run in Docker". Publishing the image is covered by container-delivery.
---

# Containerize an app

A container is a promise: the same image behaves the same on any host, and everything that changes between hosts (configuration, data, secrets, the user it runs as) comes from outside. Most container problems are that promise broken: something baked into the image that should have been outside, or something the app assumes about its environment that nobody checked. This skill covers making the app honour that promise, in any language. Building and publishing in CI is out of scope (see `ci-pipeline` and `container-delivery`).

## 0. Ask before writing

Do not write a Dockerfile from guesses. Ask the human, proposing a default for each answer:

- How the app starts and what it listens on (port, protocol), or whether it is a job that ends.
- What it writes, and what must survive a restart (databases, uploads, caches that can be rebuilt).
- What it reads from the host (input folders) and whether it only reads them.
- Its configuration and which parts are secret.
- Where it will run (a home server, a cloud host, an orchestrator), its CPU architecture, and who owns the data folder there.
- Whether it should exit or run degraded when something it needs is missing.
- How the user will run the compose file (a terminal, or a stack editor such as OMV's Compose plugin or Portainer that takes a compose file and an environment file), and whether the host can reach what the app talks to by a network address.

Record the answers that change behaviour in the spec, not only in the Dockerfile.

## 1. The image

- **Multi-stage.** Build in one stage with all the tooling; copy only the result into a small runtime stage. Name every stage. Copy the dependency manifests and install before copying the source, so a code change does not repeat the install.
- **Pin the base image** to a version, or a digest when you need exact repeatability. A moving tag makes the same Dockerfile produce different images.
- **Small and plain.** No compilers, package managers' caches, docs or debug tools in the runtime stage. Drop files the container will never use, such as native libraries for other platforms. Install an OS package only for a real need, such as an HTTP client for the health check, and clean its cache in the same layer.
- **Verify what you download** during the build: a pinned revision plus a checksum, so a changed file fails the build instead of silently changing the image. Put big, rarely changing downloads in their own stage before the code, so the cache keeps them.
- **No secrets in layers.** `ARG` and `ENV` values are visible in the image history. Use build secret mounts for credentials needed at build time, and never copy credential files into the build context.
- **A complete ignore file** (`.dockerignore`) keeps the version control folder, local environment files, keys and build outputs out of the context. It also speeds up the build. It is a second line of defence, not the first.
- **Exec form for the entrypoint,** so the app is the main process and receives stop signals (section 6).
- Add the standard source label so the registry can link the image to its repository.

## 2. Run as a non-root user

A compromised process in a container should not be root, and a root-owned file written to a mounted folder is painful for its owner.

- Run as a fixed, high numeric user id. Prefer letting the operator choose it when the container starts, so it can match the owner of the mounted folder, and make sure the app works with any id: it must not need to write inside its own install folder.
- Never fix permission problems with world-writable folders. The folder's owner and the container's user must match.
- Document how to find the right ids on the host.

## 3. Configuration and state

- **Configuration comes from environment variables,** with defaults baked into the image for paths and ports, so the common case needs few settings. Document every variable in one place.
- **Secrets never live in the image,** and a secret is never an environment variable of the container (section 3.1).
- **Nothing persistent lives in the image or in the container's own filesystem.** Declare the folders that hold data and mount them from the host. Mount input folders read-only: it states the intent and limits the damage of a bug.
- Some stores break on network file systems, because file locks are unreliable there (embedded databases are the usual case). Say so, and put the data folder on a local disk.
- Keep the process stateless where you can. Caches that can be rebuilt may stay in memory.

### 3.1 Secrets: the user is a variable, the password is a file

**Rule: a secret (a password, a key, a token) reaches the container as a file under `/run/secrets`, never as an environment variable of the container.** An environment variable shows in `docker inspect`, in the container view of stack managers such as Portainer, and in any dump of the environment that ends up in a log. A file does not. What a file does **not** protect from is someone who can run commands inside the container or reach the Docker socket: they can read it. So a secret is also kept unique to the app and minimal in what it can do.

- **Split the credential.** The user name is not secret: pass it as a normal variable. Only the password (or key) is a secret.
- **Plain text, not JSON.** The file holds the secret as is, and the app ignores one trailing line break. A JSON file breaks on passwords with quotes or backslashes, and it needs escaping nobody remembers. The app takes the path from a variable with a default (`<APP>__PasswordFile=/run/secrets/<name>`).
- **Read it when it is used,** not once at start, so a rotated secret needs no restart. Validate it at startup (section 4): the file can be read by the running user and is not empty. Never log its content, valid or not.
- **No file on the host.** Declare the secret in compose from a variable, so the user needs no extra file and the whole configuration is the compose file plus its variables (section 7):

  ```yaml
  services:
    app:
      secrets: [app_password]
  secrets:
    app_password:
      environment: APP_PASSWORD       # compose creates /run/secrets/app_password from this variable
  ```

  Notes checked in practice (Compose 5.x): the Compose Spec has **no `content:` key for `secrets`** (it exists only for `configs`), so `environment:` is the way; the value is taken literally (no `$` escaping); the file is mounted readable by every user (mode 0444), so any non-root `user:` can read it; a missing or empty variable stops compose with a message. It needs Compose 2.23 or later and is not valid for Swarm. A user who prefers a file replaces that line with `file: ./secrets/app_password`.
- State the limit honestly in the README: the password is plain text in the user's environment file or stack variables, so the account behind it must be dedicated to the app and with only the rights it needs.

## 4. Validate at startup

When the container starts, check everything the app needs: configuration present and valid, data folder exists and is **really writable** (try writing a file, do not just read permissions), input folders readable, the store can be opened and its version is the expected one, downloaded assets exist.

- Log each problem with what is wrong, the value found, and how to fix it ("the data folder is not writable by user 1000; make the owner of the mounted folder match the user of the container"). The operator reads this in the logs, with no one to ask.
- Report all the problems at once, not the first one.
- Decide, and write in the spec, whether a failed check exits or runs degraded. Exiting makes the problem obvious, but with an automatic restart policy it becomes a restart loop; say how the loop is noticed.

## 5. Health

- Expose a cheap, local endpoint that answers only once startup validation has passed. It checks that the process can serve, not the business logic, and calls no outside system.
- Use the container's health check with a start period long enough for a slow start, so a slow boot is not a failure.
- Probe with a tool that exists in the image, and probe the address the app listens on (the IPv4 loopback address, not the name `localhost`, which may resolve to IPv6 first).
- Keep it out of the app's real interface (for example, not a tool or an API route a client could call by mistake).

## 6. Logs, telemetry and shutdown

- Write logs to standard output or error, as one stream of events. The container platform collects them; the app does not manage log files.
- Optional telemetry uses the standard, vendor-neutral environment variables and is off when they are empty. Console logs stay on next to it, so the logs are readable when the telemetry backend is down.
- Record identifiers and parameters, never the content of user data, and never secrets.
- Handle the stop signal: finish the requests in flight and close the stores within the platform's stop timeout.

## 7. Exposure and the run file

- Publish a port on the loopback address unless another machine must reach it, and say who provides TLS and authentication (the app, or a proxy in front). An image listening on every interface with no authentication is a decision, not a default.
- **Host port.** The port inside the container is fixed (the image says it). The host side is a variable with a default that is **not a common one** (not 80, 3000, 5000 or 8080: they are always taken), for example `"${APP_PORT:-7842}:8080"`. Say in the README that the left number is the machine's and the right one is the container's.
- Provide a compose file as the working documentation: the image, the user, the published port, every mounted folder with its mode, the variables, the restart policy and the health check. Make host-specific values variables with sensible defaults.
- **Harden it and explain it.** `cap_drop: [ALL]` (add back only what the app needs) and `security_opt: ["no-new-privileges:true"]`. Put a one-line comment on each saying what it does, because the people who copy the file may not know.

### 7.1 The compose file, the environment file and the variables

Many users run the compose file in a stack editor (OMV's Compose plugin, Portainer, Dockge) that takes **two things**: the compose file and an environment file (the `.env`). Design for that.

- **Three layers, and keep them apart:** (1) the *environment file* holds the values the user fills in; (2) the compose file's `${NAME}` references read them; (3) the `environment:` block hands the app what it reads, in the app's own names (for example `Vault__OmvBaseUrl: ${VAULT_OMV_URL}`). The user edits only layer 1. Explain this once in the README, because the same word, "variable", names layers 1 and 3.
- **Name the user-facing variables with the app's prefix** (`VAULT_PORT`) and let the app's own names follow its framework's convention (the double underscore of ASP.NET Core is a section separator, not a collision guard).
- **Required values fail early and clearly:** `${NAME:?Say what to set}`. Optional ones have a default: `${NAME:-value}`. No variable without a use: if nobody would change it, hardcode it (a label that is always `production` is not a variable).
- **Pin the image version in a required variable** (`image: <registry>/<app>:${APP_VERSION:?...}`), so updating is a decision. Do not default to `latest`.
- **One compose file for users,** with no `build:` (leave it as a comment for those who build from source), so the same file works on a host that has never seen the repository. Include the telemetry variables (empty by default, section 6) and the self-signed-certificate switch if the app needs one, so they are found where people look.
- **The README shows both files:** the compose file, a table of variables (required or optional, example, meaning), and an example environment file with a value for **every** variable, required ones first. Test that the two blocks together make a valid file (`docker compose config` on a copy of them).
- **The environment file is private.** It is git-ignored, it may hold the password, and quotes are needed around values with `$` or `#`. Provide a `.env.template` with placeholders and say it.
- **Addresses the container reaches** (another service, the NAS) are the host's address on the network, never `localhost`: inside the container that is the container itself. Say it next to the variable.

## 8. Check it

Run it for real, on throwaway folders, as a non-root user:

- It builds from a clean checkout with the cache off, and the image size is what you expect.
- It starts, the health check turns healthy, and the first request works.
- Data survives a restart of the container.
- With a data folder that cannot be written, a missing input folder and a missing required variable, it fails with a message that says how to fix it.
- The image history and the layers contain no secrets, and the process is not root. `docker inspect` of the running container shows no secret in its environment, and the secret file is readable by the non-root user.
- The README's compose file and example environment file, copied to an empty folder, start the container (`docker compose config` first). A missing required variable stops with the message you wrote.
- A timeout or limit with a default was tried against the **real** system, including a cold first call: defaults chosen on a warm test can fail in production.
- A stop signal ends it cleanly within the timeout.
- The compose file runs on a host that is not the author's machine.
