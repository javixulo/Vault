---
name: containerize-app
description: Make an application run well inside a Docker container: the image (multi-stage, pinned, small, no secrets), running as a non-root user, configuration through environment variables, data kept outside the image, validation at startup with actionable messages, a health check, logs and clean shutdown, and a compose file that documents how to run it. Language-neutral. Use this whenever the user wants to put an app in a container, "dockerize" it, write or review a Dockerfile or compose file, or asks why a container fails to start, loses data or cannot write to a folder, even if they only say "make it run in Docker". Publishing the image is covered by container-delivery.
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
- **Secrets never live in the image.** Pass them at run time from an environment file or the platform's secret mechanism, and keep that file out of version control.
- **Nothing persistent lives in the image or in the container's own filesystem.** Declare the folders that hold data and mount them from the host. Mount input folders read-only: it states the intent and limits the damage of a bug.
- Some stores break on network file systems, because file locks are unreliable there (embedded databases are the usual case). Say so, and put the data folder on a local disk.
- Keep the process stateless where you can. Caches that can be rebuilt may stay in memory.

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
- Provide a compose file as the working documentation: the image, the user, the published port, every mounted folder with its mode, the variables, the restart policy and the health check. Make host-specific values variables with sensible defaults.

## 8. Check it

Run it for real, on throwaway folders, as a non-root user:

- It builds from a clean checkout with the cache off, and the image size is what you expect.
- It starts, the health check turns healthy, and the first request works.
- Data survives a restart of the container.
- With a data folder that cannot be written, a missing input folder and a missing required variable, it fails with a message that says how to fix it.
- The image history and the layers contain no secrets, and the process is not root.
- A stop signal ends it cleanly within the timeout.
- The compose file runs on a host that is not the author's machine.
