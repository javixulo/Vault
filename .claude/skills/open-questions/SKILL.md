---
name: open-questions
description: Create and maintain the open questions and pending decisions of a spec as one table with number, title, description, status, priority and comments. Use this whenever the user raises something unresolved, a doubt, a pending decision, a TBD or a "decide later", leaves a question open while writing a spec, or asks to add, resolve, drop or review the open questions of a document, even if they never say "open questions".
---

# Open questions

A spec is easier to trust when everything still undecided sits in one place, and not scattered through the prose as "TBD" or "to be decided". This skill creates and maintains that place: one table per spec. It records questions. It never answers them for the user.

Only what is still unresolved is in the table. A question that gets resolved leaves it.

## The table

Put it in a section called `Open questions`, at the end of the spec. The table replaces any list of open questions written as prose. Columns, in this order:

| Column | Content |
|---|---|
| `#` | Position in the table, starting at 1. |
| `Title` | A few words that name the question. |
| `Description` | What is undecided and why it matters, with the options if there are any. Write it so that someone who did not follow the conversation understands it. |
| `Status` | `Open` (nothing settled yet), `Half-way` (part of it is settled, some doubts remain) or `Blocked` (it cannot move until something else happens). |
| `Priority` | `High` (blocks other work or a core behaviour), `Medium` (to settle before implementing) or `Low` (can wait). Always set. |
| `Comments` | Free text: who decides, by when, context, links. For `Blocked`, it must say what blocks it. |

Keep the rows in the order they arrived. The Priority column is what tells them apart, so do not sort.

For `Blocked`, name the cause in Comments: another question (by its title), an answer someone outside has to give, a piece of work that has to land first. A `Blocked` row that does not say what blocks it cannot be unblocked. For `Half-way`, say in Comments which doubts remain, so the next person knows what is left to settle.

## Adding a question

Add a row, with status `Open`, when the user leaves something unresolved or when your work uncovers a real gap. Use the user's words for the title and the description. If you do not know the priority, ask: it is a judgement about their project, not yours.

Do not add your own guesses as rows. A row is a question the user has seen and agreed is open. If you notice a gap the user has not seen, mention it and ask whether it should go in the table.

A question belongs to one spec only. If it touches two documents, keep the row in the one that owns the decision, and in the other say "see the open question *Title*" where it matters. Refer by title, not by number.

## Resolving and dropping

When the user decides, write the decision and its reason in the part of the spec where it belongs, and remove the row in the same edit. The reason matters as much as the decision, because it is what lets someone judge later whether it still holds. Do not remove a row before the body carries both: the reason would be lost.

When the user says a question no longer matters, remove the row. If the reason for dropping it is worth keeping, write it in the body.

After removing rows, renumber so the table runs 1 to n with no gaps. The number is a position to point at in a conversation. Do not cite it in the spec text, in other documents or in commit messages: it changes, and the reference would point at another question.

Set a status only when what the user said supports it. `Half-way` needs something that is settled, and `Blocked` needs a cause you can name.

When the table has no rows, keep the section with the sentence "No open questions."

If the questions come from writing use cases, the ones the user chose to leave open go in this table.

## Template

```markdown
## Open questions

| # | Title | Description | Status | Priority | Comments |
|---|---|---|---|---|---|
| 1 | Late return fee | Whether a late return is charged per day, as a flat amount, or not at all. It changes what the return use case records. | Open | High | Needs an answer from the library board. |
| 2 | Renewal limit | How many times a loan can be renewed before it must be returned. | Half-way | Medium | The limit itself is settled. Still open: whether staff can override it. |
| 3 | Loan history | Whether returned loans are kept forever or removed after a period. | Blocked | Low | Blocked by the question *Late return fee*: what is kept depends on what is charged. |
```
