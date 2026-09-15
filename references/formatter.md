# Mechanical formatter

The helper formats existing **T-SQL SELECTs** into the expanded roy-sql layout. It makes no
model call, connects to no database and never writes the input file. It is not a
whole-language formatter.

## Run

PowerShell 7 and ScriptDOM **180.18.1**, `lib/netstandard2.0/Microsoft.SqlServer.TransactSql.ScriptDom.dll`.
Install the dependency once (an explicit download from Microsoft's NuGet package), then run:

```powershell
pwsh -NoProfile -File scripts/install-scriptdom.ps1
pwsh -NoProfile -File scripts/format-sql.ps1 -Path query.sql
```

Or supply an existing pinned assembly explicitly:

```powershell
pwsh -NoProfile -File scripts/format-sql.ps1 -AssemblyPath /path/to/ScriptDom.dll -Path query.sql
pwsh -NoProfile -File scripts/format-sql.ps1 -AssemblyPath /path/to/ScriptDom.dll -Path query.sql -Check
```

Omit `-Path` to read stdin. SQL goes to stdout, diagnostics to stderr. Exit **0** is successful
formatting or an already-formatted check, **1** is check-mode drift, **2** is refusal/setup failure.
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
  four-space construct-relative indentation. CTE interior blank lines, expanded nested queries.
- Converts a named projection to `[Alias] = expression`, retaining the alias's exact value/case.
  Adds optional `as` in variable declarations. These are syntax forms, not query-design edits.
- Lowercases lexer keywords, native types and recognized unqualified built-ins. Preserves
  identifiers, variables, literals, date masks, comments and schema-qualified function names.
- Preserves LF/CRLF choice. The canonical output ends with a newline.
- Reparses output, compares a canonical ScriptDOM-generated representation, and checks exact
  comment content/order. The generator is used for comparison, not for printing source.

No `isnull` replacement, comparison/BETWEEN rewrite, guard insertion, name change, predicate movement, join reordering or
compact-layout heuristic. Canonical comparison is a regression guard, **not proof of database
execution equivalence**. No SQL executes during formatting or the test suite.

## Coverage and refusals

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

A refusal emits **no partial SQL**. Unsupported valid SQL is a coverage gap, not a claim
that the original query is wrong. Read the diagnostic and format it with the skill's authoring
guidance, or add a reviewed fixture before expanding the helper's scope.

## Test

```powershell
pwsh -NoProfile -File tests/formatter.ps1 -AssemblyPath /path/to/ScriptDom.dll
pwsh -NoProfile -File tests/cli.ps1 -AssemblyPath /path/to/ScriptDom.dll
```

Exact layout fixtures, idempotence and safe-refusal checks use synthetic SQL. CI installs
the pinned dependency and runs both suites cold on Windows, macOS and Linux.
`-Check` checks for changes this formatter would make, not every prose rule in the skill.
A stable formatting bug can pass `-Check`; expected-output regression tests catch those mistakes.

[Generated before/after examples](formatter-examples.md) show actual output.
