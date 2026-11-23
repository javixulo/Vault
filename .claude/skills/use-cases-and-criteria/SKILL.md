---
name: use-cases-and-criteria
description: Write or extend the use cases and acceptance criteria of a functional spec. Each use case is its own section, with a very short description and one table of Given/When/Then criteria with stable IDs (UC-03.AC2). A capability usually gets a success use case and an error use case, plus corner-case use cases when the edges deserve their own rules. Asks the user for what is still missing before writing. Use this whenever the user wants to specify a feature, tool, endpoint, command or behaviour, add a use case, write acceptance criteria, or turn an idea into spec text, even if they never say "use case".
---

# Use cases and acceptance criteria

A use case says what a caller wants to achieve. Its acceptance criteria hold the behaviour, and they make it checkable, so a tester and a reviewer cannot disagree about whether it is done. Write them so that someone who has never seen the code can decide pass or fail.

This skill writes new use cases. It does not review existing ones.

## 1. Find what is missing and ask the user

Read what the user and the spec already say. Check these points and note which ones are not answered:

- Who or what triggers it, and what must be true beforehand.
- Which information is required and which is optional.
- What a success changes: data, state, side effects.
- What can go wrong, and what must be true after each failure (nothing changed? partial result allowed?).
- What a failure must not reveal.
- What happens if the same request is sent twice, or two arrive at once.

Ask the user about every gap before writing anything. These decisions are theirs, and a silent assumption hides one. Use the question tool if there is one, and offer a proposed default for each question so the user can just confirm. There is no limit on how many questions to ask, but do not ask what the user or the spec already answered. If the tool allows only a few questions per call, ask in rounds, the ones that change the behaviour most first.

Start writing once the answers are in. A gap the user chooses to leave open goes under open questions in the spec, and only then: do not turn your own guesses into open questions or into criteria. If the user tells you to go ahead without answering, say in your reply which assumptions you made.

## 2. Structure

Follow the numbering and layout the spec already has. If there is none, use the layout of the template below. Number the use cases in sequence across the document.

A capability usually has a **success** use case (`UC-NN · Verb the thing`) and an **error** use case (`UC-NN · Reject an invalid X`). Corner cases are common too: when behaviour at the edges needs rules of its own, give it a use case, or just criteria inside the use case it belongs to. None of these is mandatory and the list is not closed. Do not invent a use case to fill a slot, and do not squeeze a real one out.

Each use case is its own section (`#### UC-NN · Title`) with these parts, in this order:

1. **A description of one or two sentences, no more.** It only lets a human see at a glance what the use case is about. If the description is carrying behaviour, move that behaviour into criteria: the criteria are the specification, and a behaviour written in two places drifts apart.
2. **Preconditions**, optional: a few short lines under a `**Preconditions**` label, on what must hold before the use case can start. Leave it out when there are none. When a precondition can fail, the error use case covers what happens then.
3. **Acceptance criteria**: a sub-section under an `**Acceptance criteria**` label that holds every criterion of the use case in **one single table**, with the columns `ID | Name | Given | When | Then`. Never split the criteria into several tables, lists or paragraphs. The ID is the use case plus the criterion, `UC-03.AC2`: it survives reordering, so tests and other documents can point to it.

An error use case adds two things before its criteria: the rules that cause a rejection, as a flat numbered list (1., 2., 3.) with one testable rule per item and no other identifiers, so a human can point at a rule by its number (the criteria do not cite the number, because it shifts when rules are added; they name the error returned, when the spec defines errors), and one sentence that defines once what "rejected" means (what is unchanged, what is logged), so the criteria do not repeat it.

Do not write actors, goals, main flows, alternative flows, numbered steps or a section of assumptions inside a use case. A flow describes screens and repeats what the criteria already say. The output is only the use case sections, ready to paste into the functional spec, with no title or introduction of its own.

## 3. Writing the criteria

**Given** is the state of the world before the action, and only the part that matters for this criterion. Write it as facts ("a member who has reached the limit"), not as steps. What the use case's preconditions already guarantee does not need repeating. Leave it empty when nothing matters.

**When** is the one action or event that triggers the behaviour, from the caller's point of view. If it takes two actions, the criterion is probably two criteria, or the first action belongs in Given.

**Then** is what can be observed afterwards: the result returned, the state of each place that holds data, what is logged. A vague outcome cannot be tested. If the Then joins unrelated outcomes, split the criterion.

Criteria are abstract. Describe the condition, not a literal value: "a name that is empty", "one loan below the limit", "a date in the past". Concrete values tie the criterion to one test and hide the rule it exists to show. Literals belong in tests.

Other points:

- One behaviour per criterion.
- Avoid words like fast, secure, appropriate or friendly, unless a measurable threshold goes with them.
- Describe behaviour, not implementation. Class, file and table names belong in the technical spec.

Failures and edges hide most gaps, so go through these types and keep the ones that apply: required-only and with-optionals variants; one criterion per rejection rule; several rules broken at once (all reported together, or the first only: decide and state it); thing not found; duplicate or ambiguous match; the same request twice; values at a boundary; unexpected failure with nothing left half-done; what must not be revealed; concurrent requests.

If the user asks for EARS, write the rejection rules as `IF <condition>, THEN the system SHALL <response>`. Otherwise do not introduce it.

## 4. Before handing it over

- Every rejection rule has at least one criterion. Every criterion of an error use case belongs to a rule of its list: if you need a criterion for a case that no rule covers, add the rule to the list first. Every criterion of any other use case belongs to a behaviour that use case is about.
- Every use case has a short description and a single criteria table, and the behaviour is in the criteria.
- No criterion contains a literal value, and no two criteria contradict each other. The same thing has the same name everywhere.
- What the user left open is visible: it is in the open questions section of the spec, or, if it has none, in a short list after the use cases.

Put the text in the project's functional spec, in the section of the capability. Do not create a new file unless asked.

## Template

```markdown
#### UC-01 · Borrow a book

A member borrows an available book.

**Preconditions**

- The loan limit per member is configured.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-01.AC1 | Borrow an available book | A member and an available book | The member borrows the book | A loan exists for that member and book, dated today. The book is no longer available. The loan is returned |
| UC-01.AC2 | One loan below the limit | A member with one loan fewer than the limit | The member borrows an available book | The loan is created |

#### UC-02 · Reject an invalid loan

A loan is rejected if it breaks any of these rules:

1. The member exists.
2. The book exists and is available.
3. The member has not reached the loan limit.

Rejected means: no loan is created, nothing else changes, and a warning with the reason is logged. The caller is told every broken rule at once.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-02.AC1 | Book already on loan | A book that another member has borrowed | A member borrows it | Rejected, with `book_unavailable` |
| UC-02.AC2 | Two rules broken | A book that does not exist, and a member who has reached the limit | The member borrows that book | Rejected, with `book_not_found` and `limit_reached` in the same answer |
```
