<!-- long-tail section of the roy-sql skill; principles, mechanics and commenting rules live in ../SKILL.md and still apply here -->

## Procedural and DDL

The query rules carry over — procedural code (`if`/`else`, `declare`, `insert`/`update`, `set`) and DDL (`create`/`alter`/`drop`, table definitions) get the same lowercase, leading-comma, basic treatment as a query body. Nothing is off-limits; it's all just *Format on the outside* (SKILL.md) applied to the database layer. Keep tables and procs as plain and lowercase as the queries — formatting lives at the UI, not in the schema. A few idioms:

- **DDL drops use an `if exists` guard**, not `if (select … )`:

  ```sql
  if exists (select 1 from sys.objects where name = 'foo' and type = 'p')
      drop procedure foo
  ```

- **`if` / `else`**: condition on its line, the governed statement indented on the next, a blank line before `else`. Keep it flat — deep nesting is a smell that the proc is doing too much (*Stay plain*, SKILL.md).
- **`insert … select`** takes an explicit, leading-comma column list like any select — don't lean on positional `insert … select *`.
- **Table / proc definitions**: lowercase identifiers, leading-comma column lists, minimal. Don't decorate the schema; see *Format on the outside* (SKILL.md).
- **No version / change-history docblock** on a proc or DDL script (*No header block* in SKILL.md) — source control owns that.
- **No ad-hoc read guard** on a proc / trigger / DDL object — the three-line guard is for ad-hoc read queries, not object definitions (see *Locking* in SKILL.md). An existing `set nocount on` can stay, but isn't required; the isolation / deadlock lines never belong on an object definition.

Triggers especially are rare and usually a smell: logic hidden in the persistence tier instead of a concrete business-logic layer. This style covers *reformatting* existing triggers, not introducing new ones without a reason.

The skill is still query-*first* (most SQL anyone writes is an ad-hoc query, and the output column list is its UI). But the same voice extends down to DDL and procs cleanly — there's no separate style for them.
