# Mechanical formatter

The helper gives recognized **T-SQL SELECTs** expanded roy-sql layout, and makes a
best-effort layout pass over other SQL-like text. It makes no model call, connects to no
database and never writes the input file. It is not a whole-language parser or translator.

## Run

PowerShell 7 and ScriptDOM **180.18.1**, `lib/netstandard2.0/Microsoft.SqlServer.TransactSql.ScriptDom.dll`.
Install the dependency once (an explicit download from Microsoft's NuGet package), then run:

```powershell
pwsh -NoProfile -File scripts/install-scriptdom.ps1
pwsh -NoProfile -File scripts/format-sql.ps1 -Path query.sql
pwsh -NoProfile -File scripts/format-sql.ps1 -Path query.sql -Strict
```

Or supply an existing pinned assembly explicitly:

```powershell
pwsh -NoProfile -File scripts/format-sql.ps1 -AssemblyPath /path/to/ScriptDom.dll -Path query.sql
pwsh -NoProfile -File scripts/format-sql.ps1 -AssemblyPath /path/to/ScriptDom.dll -Path query.sql -Check
```

Omit `-Path` to read stdin. SQL goes to stdout, diagnostics to stderr. Exit **0** is successful
formatting or an already-formatted check, **1** is check-mode drift, **2** is refusal/setup failure.
Default mode produces output even when the syntax cannot be parsed: a token-layout fallback
reports its reason on stderr. If tokenization cannot retain the text, input is returned
unchanged with a warning. Missing/unreadable files, missing or mismatched dependencies,
compilation and output errors remain failures. `-Strict` opts into the original refusals.
Check mode emits no replacement SQL. Save output to a **different** path if wanted; shell
redirection to the input path can truncate it before this script runs.

The default assembly location is `scripts/lib/Microsoft.SqlServer.TransactSql.ScriptDom.dll`.
No network download happens at invocation. The dependency hash is checked before compilation.
The implementation is readable PowerShell plus [C# source](../scripts/RoySql.cs), compiled
in-process by PowerShell, not an opaque implementation DLL. No separate .NET SDK is required.
The [dependency source](https://github.com/microsoft/SqlScriptDOM) is MIT-licensed. The
package and assembly are independently hash-pinned. Cold execution on all three OSes is
tested by CI; check its actual result before calling a particular build cross-platform proven.

## What it does

- Standalone clauses, leading projection commas, local alias/declaration alignment and
  four-space construct-relative indentation. JOIN/APPLY align with the primary table,
  with conditions one level deeper. CTE interior blank lines, adjacent `), next as (` chains.
- Expanded CASE blocks. Short-case compaction remains an authoring choice, not an invented
  character cutoff. Plain `join` replaces `inner join`; integer `top (50)` becomes `top 50`.
  Parenthesized variable/calculated TOP expressions remain unchanged.
- Converts a named projection to `[Alias] = expression`, retaining the alias's exact value/case.
  Adds optional `as` in variable declarations. These are syntax forms, not query-design edits.
- Lowercases lexer keywords, native types and recognized unqualified built-ins. Preserves
  identifiers, variables, literals, date masks, comments and schema-qualified function names.
  Common unqualified DATE_TRUNC/ARRAY_AGG are recognized for casing, not execution validity.
  In default mode, bare TRUE/FALSE comparison operands are treated as SQL-like boolean words
  and lowercased. `-Strict` preserves them as T-SQL identifiers. Quoted/qualified names stay intact.
- Preserves LF/CRLF choice. The canonical output ends with a newline.
- Reparses output, compares a canonical ScriptDOM-generated representation, and checks exact
  comment content/order. The generator is used for comparison, not for printing source.

No `isnull` replacement, comparison/BETWEEN rewrite, guard insertion, predicate movement, join reordering or
compact-layout heuristic. Canonical comparison is a regression guard, **not proof of database
execution equivalence**. No SQL executes during formatting or the test suite.

## Best effort versus strict

Default mode first tries structural formatting. When that is unavailable, its fallback
organizes recognizable clause boundaries, projection/definition lists and parenthesized
query blocks. A multi-column SELECT pads its first item to align with the names after
leading commas; single-column lists do not. Nested SELECTs count independently.
Unknown words and operators retain their order. Inline spacing is conservative
because a T-SQL token boundary may split another dialect's operator. Comments and quoted
text are preserved; unclosed literals can cause the entire input to remain unchanged.
This is not full support for PostgreSQL, MySQL or arbitrary pseudocode, and layout may be
less polished than the T-SQL path. Warnings are deliberately outside the SQL output.

`-Strict` requires the admitted structural slice below and retains canonical-representation
and comment checks. It is a grammar/coverage check, not a schema, type or execution validator.
For example, a function name can parse without identifying a real SQL Server function.

## Strict coverage and refusals

The admitted slice is SELECT, normal named/function/derived table sources, joins/APPLY,
CTEs, TOP, WHERE/GROUP BY/HAVING/ORDER BY, CASE/window expressions, scalar/EXISTS/IN subqueries,
variable declarations, and the existing isolation/deadlock/nocount guard. It does not invent a guard.

USE, DML/DDL/procedural statements, dynamic SQL, SELECT INTO, UNION, FOR XML/JSON, optimizer hints,
OFFSET, named WINDOW clauses and exotic table sources refuse. An expression with a comment
between it and a trailing alias refuses rather than moving that comment across the expression.
A comment trapped after a projection/CTE comma also refuses rather than moving it across that
separator. Comments before a leading comma remain supported. String-valued projection aliases
also refuse. Existing table/join hints and ordinary OVER expressions are preserved, not inserted.
Other unsupported parser shapes may refuse. SQLCMD commands/repeat counts are outside this slice.

A strict refusal emits **no partial SQL**. Unsupported valid SQL is a coverage gap, not a claim
that the original query is wrong. Parser diagnostics include line, column and the parser's
message. Omit `-Strict` for best-effort output instead.

## Test

```powershell
pwsh -NoProfile -File tests/formatter.ps1 -AssemblyPath /path/to/ScriptDom.dll
pwsh -NoProfile -File tests/feedback.ps1 -AssemblyPath /path/to/ScriptDom.dll
pwsh -NoProfile -File tests/cli.ps1 -AssemblyPath /path/to/ScriptDom.dll
```

Exact layout fixtures, idempotence and safe-refusal checks use synthetic SQL. CI installs
the pinned dependency and runs all three suites cold on Windows, macOS and Linux.
`-Check` checks for changes this formatter would make, not every prose rule in the skill.
A stable formatting bug can pass `-Check`; expected-output regression tests catch those mistakes.

[Generated before/after examples](formatter-examples.md) show actual output.
