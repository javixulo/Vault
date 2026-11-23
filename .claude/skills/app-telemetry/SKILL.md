---
name: app-telemetry
description: Standardize how an application emits logs and traces so every project sends them to the same shared backend through OpenTelemetry (OTLP), with the backend chosen only by environment variables and replaceable without touching code. Covers configuration, what to record and what never to, structured messages, trace correlation, spans, behaviour when the backend is down, and a check step. Language-neutral, app side only. Use this whenever the user adds or reviews logging, tracing, telemetry or observability in a project, wires an app to the shared log backend, or asks "where do the logs go", even if they only say "add logging".
---

# App telemetry

Every project sends its logs and traces to the same place, so there is one place to look when something breaks. For that to stay true, and for the backend to be replaceable one day, the app must know **nothing** about which backend it talks to: it speaks OpenTelemetry (OTLP, an open standard) and everything backend-specific lives in environment variables. This skill covers the app side. Running the backend is out of scope. Logs and traces are the standard signals; metrics are an optional extension, since not every backend accepts them.

## 0. Ask before writing

Ask the human, proposing a default for each answer:

- The questions someone will ask of this app in production ("why did this request fail?", "what is slow?"). Every record you add must help answer one.
- The entry points of the app (requests, jobs, commands, startup) and what a unit of work is.
- The data the app handles and which of it is personal, secret or large (section 3).
- Whether the shared backend is already running and its address (section 7).
- Which client or agent calls this app, whether it opens a trace per step and sends `traceparent`, and which other services are called as part of the same step (section 4).

## 1. Configuration: only standard variables

- The app reads the standard OpenTelemetry variables: `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_EXPORTER_OTLP_HEADERS` and `OTEL_SERVICE_NAME`. It does not invent its own names and never contains a backend address, name or API key in code.
- **Empty endpoint means no export.** The app starts and works with telemetry off, so a laptop or a test run needs no backend.
- **Set the protocol explicitly** in every deployment. SDK defaults differ (many default to gRPC, and some backends accept only HTTP), and a mismatch fails silently.
- Keys and tokens go in the headers variable, from a secret, never in the image or the repository.
- Set `OTEL_SERVICE_NAME` per application, short and stable; it is how the backend tells projects apart. Also set the version and the environment (production, test) as resource attributes, so a record says which build and where.
- Switching backend must be exactly this: change those variables. If it needs a code change, the code knows too much.

## 2. Logs

- **Console always on,** next to the export, so the logs are readable when the backend is down or unreachable. Write to standard error when standard output is a protocol channel.
- **Structured, with a constant message.** A message template plus named fields (`order {order_id} rejected: {reason}`), never a string built by concatenation. The backend then groups by message and filters by field; a message with the value inside is a new message every time.
- **Levels, used consistently:** error for an unexpected failure someone may need to act on; warning for something rejected or degraded but handled; information for a significant event that completed (created, changed, removed, started); debug for detail, off in production.
- Log an unexpected exception once, where it is handled, with its stack trace. Log expected rejections as warnings with their reason and the values that caused them, without a stack trace.
- **Never return the exception to the caller.** The caller gets a message and the trace id; the full detail stays in the log, found through that id.
- **What to log:** the outcome of each unit of work that changes something (created, changed, removed, rejected, failed), startup and its validation result, and calls to other systems that failed or were slow. Do not log every read, every step of a loop or the success of trivial operations; the span already says they happened.
- **Log once.** Do not log an error and then rethrow it for another layer to log again: the same failure would appear several times. The layer that handles it logs it.
- **Keep it quiet when nothing happens.** A repeated failure (a dependency that is down, a retry loop) must not write thousands of identical lines: log the first, then a count or a lower level.
- **Messages are written in English, in the same style in every project:** a short sentence about what happened, in the past tense, with no punctuation tricks, so the shared backend reads uniformly.

## 3. What never gets recorded

Telemetry pipelines are a classic way to leak data, and a log cannot be taken back.

- Record **identifiers and parameters, not content.** Never the body of documents, files, messages or other data the user entrusted to the app; never secrets, tokens, passwords or keys; never whole request bodies.
- Use opaque ids instead of emails, names or free text. When something that may be personal (a search text, a rejected form value) would help diagnosis, decide it with the human, write the decision in the spec, and apply it in one place, not call by call.
- Do not use unbounded values (ids, text, urls) as names of spans or metrics; put them in attributes.
- Check the real output, not the code: the leak is usually in a field nobody thought of.

## 4. Traces

- One span per unit of work that enters the app (a request, a job, a command), named for what it does, not for the data it handles. Add child spans only around meaningful steps (a store, a call to another system, a long computation), with the outcome as an attribute: success, or the error kind.
- Use the framework's automatic instrumentation for the incoming and outgoing protocols; add manual spans for the app's own steps.
- **Correlation:** the log records written inside a span carry its trace id and span id. Use the SDK's logging bridge so this happens by itself; do not copy ids by hand.
- Sampling: keep everything at small scale. If volume ever demands sampling, sample the same way in every project.

**Several services, one flow.** When a client (an agent, a gateway, another service) calls several services as parts of one step, that step should be one trace, so one filter in the backend shows all their logs together. Use the trace id for this, not a correlation id of your own.

- The **client** opens one root span per step and sends the W3C `traceparent` header on every call it makes during it. This is the weak link: the services can only join a trace the client starts.
- Every **service** accepts `traceparent` on each call received and continues that trace; if none arrives, it starts its own. Make sure the framework's automatic instrumentation does this, and test it (section 8).
- Do not add a correlation id as a parameter of the tools or endpoints. A model or a caller would have to remember to pass it, and it pollutes the contract.
- A trace is one step. To follow something that spans many steps and days (a project, an order), record its domain id as an attribute on every span and log of that flow (section 5) and filter by it.
- If the client does not send `traceparent`, each service starts its own trace and the steps cannot be tied together by trace. Say so in the spec and check the real client early; correlate by the domain id and the time meanwhile.

## 5. Names every project shares

A shared backend is only useful across projects if the same thing has the same name everywhere. Otherwise a filter that finds one project misses the next.

- **Resource attributes on every record,** set from configuration: `service.name`, `service.version` and `deployment.environment.name`.
- **One name per concept, for all projects:** lowercase, dot-separated, from the OpenTelemetry semantic conventions when one exists (`http.request.method`, `error.type`, `session.id`), and from a shared list otherwise. Never `orderId` in one project and `order_id` in another.
- **Domain ids** (the customer, the order, the booking) are named `<entity>.id` and written exactly the same by every service that handles that entity. When a flow crosses services, they are what ties it together beyond one trace.
- **Each project lists the attributes it adds** in its spec (name, meaning, whether it can hold personal data). Before inventing a name, look for it in the other projects' lists and in the conventions.
- A name is part of the contract with whoever reads the logs: renaming one is a change to say out loud.

## 6. Telemetry must never hurt the app

- A backend that is down, slow or full must not slow, block or fail the app. Export in the background with bounded queues; drop records rather than wait.
- A failure of the exporter is not an app error and does not change the response to the caller.
- Do not log inside hot loops, and keep the cost of a debug line you will not emit near zero.

## 7. Backend profile: the only part to edit when the backend changes

This table is the one place that names the backend. The rest of the skill does not depend on it. When the shared backend changes, edit only this table (and the values in each deployment).

| Item | Current value |
|---|---|
| Backend | Seq |
| Protocol (`OTEL_EXPORTER_OTLP_PROTOCOL`) | `http/protobuf` (it does not accept gRPC) |
| Endpoint (`OTEL_EXPORTER_OTLP_ENDPOINT`) | `http://<backend-host>:5341/ingest/otlp`; the SDK adds `/v1/logs` and `/v1/traces` itself |
| Signals accepted | logs and traces; no metrics |
| Authentication | none, trusted local network only; if enabled later, send the key in `OTEL_EXPORTER_OTLP_HEADERS` |

Replace `<backend-host>` with the address the human gives for the shared backend (a container name on a shared network, or the name of the host). If the backend stops being reachable by every app directly, put an OpenTelemetry collector in front: apps keep sending OTLP to the collector, and only the collector knows the new backend.

## 8. Check it

Run the app and look at the backend, not at the code:

- With the endpoint empty, the app starts, works and logs to the console.
- With it set, trigger one successful and one failing operation: both appear, with the right service name, version and environment.
- Find the failing one by its trace id, and see the log lines and the spans together.
- Read the stored records and confirm there is no user content, secret or full body in them.
- Stop the backend: the app still answers at normal speed and the console still shows the logs. Start it again: new records arrive.
- Call it from another instrumented service, or from the real client, with a `traceparent`: the records of both share one trace id. Call it with none: it starts its own trace and still works.
- Compare attribute names with another project's: the same concept has the same name.
- Change the endpoint and protocol variables to point to a different backend and see that no code change was needed.
