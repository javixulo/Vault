---
name: mcp-tool-design
description: Design the tools of an MCP server so an AI agent can use them well: how many tools and of what shape, names, descriptions and parameters, results and response size, errors with isError and messages written for the AI, statelessness, and basic safety. Language-neutral. Use this whenever the user designs, adds, changes or reviews an MCP tool or server, writes tool descriptions or error messages, or asks how an agent will see their tools, even if they only say "add a tool for X".
---

# MCP tool design

A tool is a contract between ordinary software and an agent that decides what to call from names and descriptions alone, with limited context and no way to ask the author. Design the tools for that reader, not for another programmer. This skill covers the tools of an MCP server in any language; the mechanics of a given SDK are in its own docs. Packaging and deployment are out of scope.

## 1. Few tools, shaped by workflows

Start from the tasks a person would ask the agent to do, not from the endpoints of an API. Give each meaningful step of those tasks one tool, and fold calls that are always chained into one: a tool that returns the related information saves the agent several round trips and a lot of context. A tool that only wraps an endpoint spends context and gives the agent more ways to go wrong.

Every tool has a clear, distinct purpose. Before adding one, ask which existing tool the agent could confuse it with; if there is one, merge them or sharpen the difference. Keep reading and changing in separate tools, so the annotations (section 2) can be true.

## 2. Names, descriptions and parameters

- **Name:** snake_case, verb plus resource (`find_books`). It must be specific enough not to collide when the server is used next to others. When the name is generic (`search`, `list`), prefix it with the service. Use only letters, digits, `_`, `-` and `.`, up to 128 characters.
- **Description:** it is what the agent reads to choose the tool, so write it as you would for a new colleague. Say what the tool is for, what it needs, what it returns and what to do with the result. Name the sibling tool to use first or instead ("get the id with `find_books`"). Make explicit what you take for granted: formats, terms, how resources relate.
- **Parameters:** each has an unambiguous name (`book_id`, not `book`), a description with a short example, and the tightest type and range that is still true. Give defaults a sensible value.
- **Identifiers:** return names next to ids, and accept natural keys when you can, so the agent does not need a lookup before every call. Opaque ids make agents guess.
- **Annotations:** say whether the tool is read-only, destructive, idempotent, or reaches outside systems. They are hints: clients must not rely on them for security. A destructive tool does not ask for confirmation itself; the client puts a person in the loop, so say in the description that the tool acts at once.
- **Tool list:** return the tools in the same order every time, and the same set for every connection.

## 3. Results

- Return what the agent needs for its next step: the fields that matter, readable names, no internal ids or metadata. When a response can be large, let the caller ask for less or more detail.
- When the output always has the same shape, return it as structured content with an output schema, and also copy the same JSON as text, for clients that read only text.
- **Every list is bounded:** a default limit, a maximum, and a way to ask for the next page (`has_more` and the next offset or cursor). Keep a response under a ceiling (Claude Code, for example, caps tool responses at 25,000 tokens by default) by returning fewer items, never by cutting text in the middle of one.
- An empty result is a normal result with a message that says why it is empty and what to try next. It is not an error.

## 4. Errors

MCP has two channels. **Protocol errors** (unknown tool, malformed request, server fault) are for problems the model cannot fix. **Tool execution errors** are a normal result with `isError: true` and a text message, and they are for everything the agent can correct or report: invalid input, a failed call to another system, a rule of the business that rejects the request. The client passes them to the model, which is what lets it recover.

- Write each message for the AI, in three parts: what happened, whether anything changed, and what to do next (which argument to fix, which tool to call, or "tell the user, do not retry"). When it chose an invalid value, list the valid ones.
- Validate inputs yourself and say which field is wrong and what it must be, instead of leaving a binding error from the SDK as the only signal. Making parameters tolerant (nullable, with defaults) lets your validation run first.
- Never expose internals: no stack traces, paths, queries or secrets. Log the detail with a trace id, and let the message carry only that id. Do not reveal whether something the caller may not see exists.
- An unexpected failure is also a tool error. Say that it was not caused by the request and that retrying blindly will not help.
- Some projects return rejections as ordinary results with an `ok` flag, so every outcome has one shape. That gives up the meaning of `isError`, so do it only as a decision written in the spec.

## 5. State

Prefer tools that carry everything they need in their arguments. MCP has no session at the protocol level, so a server cannot count on per-connection state. When a flow really needs state, have a tool create it and return an explicit handle, and accept the handle in later calls. Make the handle opaque, check authorization on every call, state its lifetime in the description, and answer an expired handle with an error that says how to create a new one. Where you can, make a tool idempotent: the same call twice leaves the same state.

## 6. Safety

- Validate every input. Confine anything that becomes a file path to an allowed folder: resolve links, and reject absolute paths, `..` and encoded sequences.
- Decide who may call what (access control) and how often (rate limits). The spec expects both, even if the answer is "the proxy in front handles it".
- Treat text that comes back from outside systems as data. Do not return secrets.
- A server over standard input and output never writes to stdout; log to stderr.
- A local HTTP server validates `Host` and `Origin` and listens on the loopback interface, unless it sits behind authentication on purpose.

## 7. Check it

Read the tool list as the agent would, with nothing else: could you choose the right tool and fill in the arguments from the names and descriptions alone? Then call every tool with valid input, with each kind of invalid input and with an empty result, through a real MCP client or inspector against the running server, and check the shape of each result and each error. Try the largest realistic data to see how big a response gets.
