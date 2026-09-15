---
name: roy-sql
description: "Write or reformat SQL in the roy-sql house style: lowercase keywords, leading commas, alias-first projections, expanded joins and CTEs, and comments that explain why. Use for roy-sql, match my SQL style, or format this query in this profile. Includes an optional local T-SQL formatter. Preserve existing names and values when reformatting; this is not a database execution or query optimization tool."
license: MIT
metadata:
  version: 2.27.0
  publishable: true
---

# roy-sql

My SQL house style. Principles are the why; mechanics are the how.
When they conflict, the principle wins. Read principles before mechanics.

New to the terms? [roy-sql, in plain language](references/style-explained.md) explains each
piece with a small example, and separates formatting from query-design choices.

For deterministic expanded T-SQL layout, the [formatter guide](references/formatter.md)
documents the local helper, its admitted syntax and safe refusals.
The compact authoring latitude below is not part of its v1 output.

## Two modes: authoring vs reformatting

- **Authoring:** all rules apply, including lowercase concatenated parameter names,
  lowercase identifiers and no header docblock, in queries, procs and DDL alike.
- **Reformatting:** restyle layout, casing and comments, but preserve load-bearing names:
  proc parameters, consumer-facing columns, and aliases/CTEs also emitted as data.
  Flag those names instead of renaming them. Preserve existing header docblocks too.
- Lowercase keywords and built-in functions in both modes. Casing a keyword is not
  renaming an identifier. The spelling of a load-bearing identifier stays byte-for-byte.
- A rule saying **flag, don't change** refers to reformatting mode.

Examples use Microsoft's [Northwind sample](https://github.com/microsoft/sql-server-samples/tree/master/samples/databases/northwind-pubs).
Its documented PascalCase identifiers appear here as I would type them, lowercase.
Substitute your own schema. The rules needed to use this skill are here and in the linked references.

# Principles

## Lowercase by hand

Hand-typed SQL is lowercase: keywords, tables and columns, even when schema docs use
other casing. Mixed case often comes from autocomplete or pasted code, not my typing.
This convention assumes the target's case-insensitive identifier resolution; the skill
does not establish a database's collation. See the literal/output exceptions below.

## Format on the outside

Keep SQL basic, lowercase and minimal at every layer, including procs, tables and DDL.
Presentation belongs at the outside edge: an ad-hoc query's output list is its UI.
Consumer-facing aliases may carry case or spaces; don't push that formatting inward.

## Terse and self-documenting

Write for SQL readers. Short aliases, pass-through columns and tight CTE names beat
ceremony. Comments supply context the code cannot; if every line needs one, simplify.

## Comment-friendly structure

Make individual development lines toggleable: leading commas on columns, leading
`and` on WHERE conditions, separate join conditions, and commented test-value alternates.
`where 1=1` makes even the first filter toggleable while developing, not when shipping.

## Stay plain — fancy features need a reason

Start with ordinary joins, aggregates and filters. Optimize from a plan/runtime, not taste.
Check these smells rather than banning useful constructs:

- Many-column GROUP BY may be a misplaced DISTINCT or the wrong aggregation.
- Window functions need a reason; a clearer CTE chain is equally valid.
- Recursive CTEs suit real hierarchies; prefer simpler joins when they suffice.
- Prefer separate INSERT/UPDATE/DELETE to MERGE unless MERGE is genuinely clearer.
- Verify where side-effecting OUTPUT clauses write.
- Restructure deeply nested subqueries as CTEs.

A per-result-set total on each row can justify `sum() over ()`. Put a conditional
`case` inside `sum` so NULLs drop, and use `coalesce(..., 0)` where needed.

## Filter-first, decorate-second

Scope the row set in the first CTE (`gd`, good/got data). Later rollups or apply blocks
decorate only that surviving set, e.g. `where orderid in (select orderid from gd)`.
Put `top (@topn)` INSIDE that scoping CTE, never after whole-database rollups.

# Language preferences

Prefer ANSI/standard SQL where the target supports the same behavior. These are authoring
choices, not layout rules; dialect-specific syntax still has a place when needed.

## coalesce over isnull

Author with ANSI, variadic `coalesce`, not `isnull`. `isnull` returns the first argument's
type and can truncate strings; that difference matters when reviewing existing SQL.
**Reformatting never auto-swaps them:** type, nullability and evaluation behavior can differ.
Keep existing `isnull` unless the replacement has been confirmed safe.
See [Microsoft's comparison](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/coalesce-transact-sql?view=sql-server-ver17).

## Comparisons and ranges

Author not-equal comparisons with `!=`, not `<>`. This is an explicit house-style exception
to the standard-SQL preference: Microsoft lists `!=` as valid T-SQL but not ISO standard,
while `<>` is standard. Neither spelling is a speed optimization.
See [Microsoft's comparison operators](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/comparison-operators-transact-sql).

Prefer `between` when both endpoints should be inclusive. `a between x and y` means
`a >= x and a <= y`, not `a > x and a <= y`. Use explicit comparisons when an endpoint
must be exclusive, e.g. `a >= @start and a < @nextday` for a timestamp interval.
For date/time periods, prefer that half-open form: December 2000 is
`a >= '20001201' and a < '20010101'`. It covers the month without guessing the last
representable time on December 31. The quoted values are ISO date strings, not integers.
Do not choose one spelling on an assumed performance advantage: BETWEEN can seek an index
too. Measure plans/reads for the actual query before claiming a gain.
See [BETWEEN semantics](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/between-transact-sql)
and [SQL Server seek predicates](https://techcommunity.microsoft.com/t5/sql-server/seek-predicates/ba-p/383124).

The mechanical formatter preserves existing comparison operators and range forms. It does
not rewrite BETWEEN into two comparisons, change inclusivity, or normalize `<>` to `!=`.

## Date and time literals

Use unambiguous forms rather than locale-dependent dates or 12-hour clock strings:

- T-SQL date: `'20220506'`, not `'5/6/2022'`.
- Time: `'14:30:00'`, with fractional seconds when needed.
- Date and time: `'2022-05-06T14:30:00'`. Preserve required precision and timezone information.

Microsoft calls `yyyymmdd` **ISO** (style 112), and the `T`-separated timestamp **ISO 8601**
(style 126), rather than ANSI. See [the date/time styles](https://learn.microsoft.com/en-us/sql/t-sql/functions/cast-and-convert-transact-sql?view=sql-server-ver17).
Use the target dialect's supported literal syntax; these examples are T-SQL strings.
On reformat, preserve literal bytes and format masks. Flag ambiguous dates, don't guess
their intended meaning or change a consumer's required output format.

# Mechanics

## Locking — the three-line guard

Open ad-hoc read queries with:

```sql
-- issue/honor no locks and be the deadlock victim if needed
set transaction isolation level read uncommitted
set deadlock_priority -10
set nocount on
```

Use numeric `-10`, not `low`. No per-table `with (nolock)`, it repeats the isolation
posture. The comment explains dirty reads/deadlock priority, not the file's purpose;
`nocount` suppresses rows-affected messages.
**Not for procs, triggers or DDL.** Triggers run in the firing write transaction.
Keep an existing `set nocount on` in an object definition, but don't force-add it or
either read-guard setting. This guard is an ad-hoc reading posture, not an object preamble.

## Brackets, literals, and output names

Bracket only renamed/computed columns (`[line_total] = od.unitprice * od.quantity`)
or identifiers needing escaping (`[order details]`, reserved words, special characters).
Raw references such as `o.orderid` stay unbracketed.

**Literals:** lowercase when authoring, with these exceptions:

- Preserve case copied from an external source (issue, vendor doc, screenshot).
- Output literals are consumer data. Fresh ones default lowercase, but a consumer can
  require case. On reformat, flag SELECT-side constants, don't silently lowercase them.
- Under case-insensitive equality, filter case can be harmless. Recognizable domain/status
  codes (`PENDING`, `SHIPPED`, `CANCELLED`) often retain source-system case.
  Fresh authoring is lowercase; reformatting flags them instead of mass-lowercasing.
- **Never lowercase format/style strings or date-format arguments.** Preserve masks such
  as `'MM/dd/yyyy'`, convert style arguments and datename/datepart parts byte-for-byte.
  Formatting semantics are not governed by string-equality collation.

Don't rename output columns unless needed. Pass through a good raw name; alias a computed
value or a source name unsuitable for the consumer. Default aliases are lowercase, terse,
without spaces. For human-facing CSVs/reports/dashboards, deliberately use PascalCase or
Title Case With Spaces if useful. Acronyms stay uppercase (`REV`, not `Rev`).

```sql
select
      [OrderNo]             = o.orderid
    , [Customer Name]       = c.companyname
    , [REV linehaul charge] = ord_rev.rev_linehaul
```

## Declarations and parameters

Use `declare @name as <type> = <value>`, aligned within the block. Fresh parameter names
are lowercase and concatenated: `@startdate`, not `@start_date`, including proc signatures.
On reformat, preserve existing proc parameter identifiers byte-for-byte and flag them;
lowercase keywords and leading-comma-align the signature without breaking its callers.

```sql
declare @customerid as varchar(10) = 'alfki'
declare @startdate  as date        = '20240401'
declare @enddate    as date        = '20250501'
declare @topn       as int         = 10000
```

For string inputs targeting indexed numeric columns, `try_cast` once at the declaration,
not on the indexed column in WHERE. Invalid input becomes NULL and matches no rows in
the ordinary equality filter. Keep commented scenario alternates above the active value:

```sql
declare @orderid int = try_cast(
--'10248' -- single line order, smoke test
--'10250' -- multi-line order with beverages
--'10260' -- multi-line across multiple categories
--'10256' -- order with a discount applied
    '10271' -- order containing a discontinued product
    as int)
```

## Function calls

Default to `left(name, 2)`: no space before `(` or just inside either parenthesis.
Hand-formatted SQL may use `left( name, 2 )`, or tier long nested calls across lines with
their closing parentheses on separate lines. This is readability latitude, not a fixed-width
rule. The mechanical v1 formatter uses the tight default; automatic nested-call wrapping is deferred.

## Column lists

In a normal SELECT block, put columns below `select`, even a single column, including
inside CTEs. Small nested queries have the [compact-layout exception](#compact-nested-queries).
Leading commas sit one indent unit in. **Only for a multi-column list**, give the first
identifier two extra spaces so it aligns with identifiers after `, `. A single projection
(column, expression, alias or `*`) gets one ordinary indent unit, no comma padding.
Apply that count separately in each nested SELECT. Pass-through columns are unbracketed; computed
or renamed columns use bracketed **`alias = expression`**, not `expression as alias`.
Align every projection-alias `=` within a SELECT list to the longest left-hand alias,
regardless of which position it occupies. Nested lists align independently. Bare pass-through
columns stay bare; do not invent aliases just to add an equals sign.

```sql
select
      o.orderid
    , o.customerid
    , [line_total]  = od.unitprice * od.quantity * (1 - od.discount)
    , [order_total] = coalesce(sum(od.unitprice * od.quantity * (1 - od.discount)) over (), 0)
```

## Aliases

Apply these to tables, subqueries/apply blocks and CTEs:

- Single character by default (`orders o`, `products p`, `categories c`, `employees e`).
  Add characters for a clash (`cust` alongside categories `c`) or a useful convention.
- For prefix-keyed schemas, the recognizable column prefix is the minimal alias:
  `ivh.ivh_invoicenumber`, `ivd`, `cht`. Don't force clashing one-letter aliases.
- Child-of-parent: parent's alias plus a child hint (`orders o`, `[order details] od`).
- Same table in another scope: double the letter (`o` outside, `oo` inside).
  An apply's OUTER alias can name its role (`po` previous order, `lo` latest order);
  its INNER table alias still doubles. Multi-char bases may extend (`ivd` → `ivdd`).
- Meaning earns length (`gd`, `po`, `lo`); `customer` for `customers` doesn't.
  With a bare-CTE outer or no single letter to double, choose a readable non-clashing alias.
- Names also consumed as data are fixed. If a CTE/alias name is echoed in a string literal,
  don't restyle it and change output. Flag it under the reformatting boundary.

## FROM and joins

`from` stands alone. The first table is one unit in, join targets another unit in,
and each ON condition another unit in. `on` ENDS the join line. Put the new table on
the LEFT of `=` and established/parent table on the RIGHT (`od.orderid = o.orderid`).
Order joins down the domain hierarchy: orders → details → products → categories.

```sql
from
    orders o
        join [order details] od on
            od.orderid = o.orderid
        join products p on
            p.productid = od.productid
            and p.discontinued = 0
where
    o.orderid = @orderid
    and o.shippeddate is not null
```

Joined-table filters belong in that join's ON clause. WHERE holds root-table filters
and cross-table conditions that don't logically attach to a single join.
Small nested queries can inline FROM under the compact-layout exception; the outer FROM stays standalone.
A single-condition join can inline ON in a space-constrained email/chat snippet.
Expand by default, collapse only when the destination or small subquery warrants it.

## Compact nested queries

Simple scalar, EXISTS/IN and APPLY lookups have more latitude than the main query or a CTE.
Compact can mean one line per clause, not necessarily one line for the whole query. For example,
inside an APPLY, with `o` supplied by the outer query:

```sql
select top 1 oo.orderdate
from orders oo
where oo.customerid = o.customerid
order by oo.orderid desc
```

A small check can stay entirely inline: `exists (select 1 from orders oo where oo.customerid = o.customerid)`.
Judge readability at its actual indentation. Expand when long expressions, multiple outputs,
joins, branching predicates or comments make the compact form harder to scan. No fixed 60- or
100-character threshold is established. Preserve clear existing compact forms; don't automatically
collapse expanded SQL just because it fits. When uncertain, expand.

## CTE chains

```sql
;with gd as (

    select top (@topn)
        orderid
    from
        orders
    where
        shippeddate is not null

)

-- orders containing at least one beverages line
, withbev as (

    select
        gd.orderid
    from
        gd
            join [order details] od on
                od.orderid = gd.orderid
            join products p on
                p.productid = od.productid
            join categories c on
                c.categoryid = p.categoryid
                and c.categoryname = 'beverages'

)
```

Use a new-line `;with` to defend against a prior batch without `go`. Keep one blank
line after each opening `(` and before its closing `)`. Subsequent CTEs have a leading
comma and a short `--` comment above naming what they find/exclude.
CTE names are pure lowercase without underscores by default, snake_case when ambiguity
or meaningful prefixes warrant it (`ord_rev`, `ord_pay`), PascalCase last resort.
The scoping CTE is conventionally `gd` (good/got data).

## WHERE

Standalone `where`, conditions one unit in, each `and`/`or` on its own aligned line.
This is also the CTE default. Inline only in subselects or deliberately concise spots.
Align nearby `=` signs when condition lengths are similar.
Put the least-toggleable, defining condition first; optional/test filters follow.
For a specific-order lookup that is the ID; for shipped orders in a date range, shipment
status may define the query and precede the dates.

```sql
where
    o.orderid = @orderid
    and o.shippeddate is not null
```

During development, `where 1=1` makes every real condition independently toggleable:

```sql
where 1=1
    and o.shippeddate is not null
    and o.orderdate > getdate() - 365
    --and o.customerid = 'alfki'
    --and o.employeeid = 5
```

Before shipping, remove `1=1` and the first real condition's `and`.
Keep that condition BELOW standalone `where`, not on the keyword line.

## ORDER BY

`order by` stands alone by default; columns go one unit in on the next line.
ORDER BY column numbers are fine when the SELECT list is stable and the intent clear.
Inline ORDER BY suits space-constrained email/chat.

```sql
order by
    o.orderid desc, od.productid
```

## GROUP BY

`group by` stands alone by default, columns one unit in on the next line.
A single grouping column can be inline. Many columns remain a design smell.

## Apply patterns

`cross apply` requires the inner row; `outer apply` allows it to be absent.
Don't comment merely to restate that distinction. Use `top 1` + ORDER BY to pick a
canonical row: descending for latest, ascending for first, according to the real need.
Projection brackets and alias rules apply inside subqueries too.

## Indentation

One **tab-over unit per construct**, not a single indent per file. Clause keywords sit
at their block level, contents one unit in, joins one further unit from FROM's contents,
ON conditions one further unit, and CTE bodies one unit from their opening line.
Use the same width throughout a query. Mechanical output is **4 spaces, no tab bytes**.
I type tabs converted by my editor, usually to 4 spaces; where tabs can't be typed,
2 spaces is the minimum visible unit. The rule wins over a conflicting example.

## Procedural and DDL

The same lowercase, leading-comma, minimal style applies to procedural code and objects.
Read [references/procedural-and-ddl.md](references/procedural-and-ddl.md) for those tasks.

# Commenting

Explain **why**, not what the keyword says. Short inline `--` is the default, `/* */`
only for a reason such as a tool stripping single-line comments. Lowercase plain voice.
No redundant `-- inclusive` on `>=`, `-- might be null` on OUTER JOIN, or `--may not exist`
on OUTER APPLY. If comments overwhelm the SQL, improve the code's readability first.

No header/purpose/ownership/changelog/dispatch-command/tracking-issue docblock on newly
authored queries, procs or DDL. Source control owns history and commits name the issue.
The guard's one-line comment explains those settings, not the file. Truly unavoidable
top-of-file context is rare. On reformat, keep existing vendor/parameter docblocks and flag.

Heavy adjacent explanation is right for bit meanings, magic date offsets, hidden subquery
side effects and schema workarounds. Keep the code tight; don't remove necessary context.
Inline notes can explain intent, gotchas, why-this-not-that, and test-value scenarios.
No emoji, pleasantries, all-caps tags (`NOTE:`, `TODO:`, `FIXME:`), or long author/date credits.

# Worked examples

Read [references/worked-examples.md](references/worked-examples.md) for column lists,
joins, CTE chains and same-table APPLY. Examples teach one point at a time: use a labeled
excerpt when a full query would distract. Omit session settings, declarations and unrelated
columns unless they illustrate that point. This does not change the full-query authoring rules.

# What this skill is NOT

- Domain/schema choice, indexes and correctness need separate knowledge.
- Apply judgment: long queries expand, email/chat can squish. Not a rigid reformatter.
- Query-first, not query-only: procedural and DDL tasks use the linked reference too.
- Leave dead/commented-out code as-is; reformatting it adds churn and toggle risk.
- For unspecified edges, follow the examples and principles: lowercase, less ceremony,
  leading commas, brackets only where they earn their keep.
