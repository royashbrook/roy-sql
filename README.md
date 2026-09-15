# roy-sql

My SQL house style, as an agent skill and a local T-SQL formatter.
Lowercase keywords, leading commas, alias-first projections, expanded joins and CTEs.
Plain SQL, comments that explain why, no query redesign disguised as formatting.

- [See the style](references/worked-examples.md).
- [See actual formatter output](references/formatter-examples.md).
- [Read the terms in plain language](references/style-explained.md).
- [Read the full skill](SKILL.md).

## Use the skill

Place this repository's contents in a `roy-sql` directory inside your agent's skill
directory, or give an agent [SKILL.md](SKILL.md) and its linked references directly.
The instructions are agent-independent. They need no runtime or database connection.
Ask: "use roy-sql to format this query, preserving its identifiers and values."

## Run the formatter

Requires [PowerShell 7](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell).
From the repository directory:

```powershell
pwsh -NoProfile -File scripts/install-scriptdom.ps1
pwsh -NoProfile -File scripts/format-sql.ps1 -Path query.sql
pwsh -NoProfile -File scripts/format-sql.ps1 -Path query.sql -Check
```

The first command downloads a hash-pinned Microsoft ScriptDOM dependency. The formatter
then runs locally, with no model, database, network call or input-file edit. Its implementation
is visible PowerShell and C# source; no separate .NET SDK is needed.

SQL goes to stdout. Diagnostics go to stderr. Exit 0 means success, 1 means check-mode
drift, 2 means refusal or setup failure. Save stdout to a different file, never the input path.

The mechanical helper covers **T-SQL SELECTs**, not all of the skill's authoring guidance.
Unsupported syntax, including USE, DML, DDL and UNION, refuses with no partial SQL.
Preservation checks are not proof that a query returns the same results on a database.
See [coverage, checks and limitations](references/formatter.md).

## License

[MIT](LICENSE). Microsoft ScriptDOM is a separate MIT-licensed dependency, downloaded
explicitly rather than bundled. Its notice is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
