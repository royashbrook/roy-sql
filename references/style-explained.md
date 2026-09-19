# roy-sql, in plain language

A **house style** is a consistent set of choices about how code looks. A **formatter profile**
is the set of choices a formatting tool applies. `roy-sql` names this particular combination,
not a separate SQL language or an industry standard.

This page explains the vocabulary. The [skill's rules](../SKILL.md) remain the source of truth;
the [worked examples](worked-examples.md) isolate each point in a short SQL excerpt.

## the visible pieces

| piece | plain meaning | what it looks like here |
|---|---|---|
| lowercase keywords | write SQL's instruction words in lowercase | `select`, `from`, `where`, not `SELECT`, `FROM`, `WHERE` |
| leading commas, or comma-first | put the separator at the start of the next list item, not the end of the previous one | a column line starts with `, o.customerid` |
| alias-first, or equals-style aliases | give an output column its label before its calculation | `[total] = quantity * price`; this labels a result, it does not update a stored column |
| selective brackets | use SQL Server's square-bracket quoting where a name needs it, and for renamed/computed output labels in this style | `[Customer Name]` or `[total]`, but a plain reference stays `o.orderid` |
| standalone clause keywords | put a section's opening words on their own line, with its contents underneath | `select` above even one column, `from` above the table, `where` above the conditions; small-query exceptions are in the rules |
| block indentation | move inward to show that something belongs inside something else | a CTE's query sits one level inside its parentheses |
| construct-relative indentation | calculate that inward step from the surrounding SQL structure, not the file's left edge | table and joins aligned under `from`, conditions one level deeper; normally four spaces per level |
| vertical alignment | add spaces so related items line up in columns | all output aliases in a SELECT list have their `=` signs in the same column, sized to the longest alias |
| leading boolean operators | start each additional condition with its joining word | `and o.shippeddate is not null`; preserve parentheses and the order of `and`/`or` |
| CTE chain | give intermediate queries names so a longer query can be read in stages | `;with gd as (...)` continues on its closing line as `), totals as (`; CTE means common table expression, not a saved table |
| comment-friendly layout | give columns and conditions their own lines so they are easier to inspect or temporarily comment out | leading commas and `and` help with later items; the first item still needs care |
| why-comments | explain a reason the SQL alone cannot tell the reader | explain why a cutoff exists, not that `where` filters rows |

Leading/trailing commas, alignment and indentation are established formatter vocabulary.
[SQLFluff's layout guide](https://docs.sqlfluff.com/en/stable/configuration/layout.html) uses
these concepts. Alias-first is a descriptive label for the documented T-SQL syntax
[`column_alias = expression`](https://learn.microsoft.com/en-us/sql/t-sql/queries/select-clause-transact-sql?view=sql-server-ver17).
Some of these choices are T-SQL-specific; the combination is not a promise that every SQL dialect supports them.

A single projection gets the ordinary four-space indent. Only a multi-column list pads
its first item by two more spaces to match later items' `, `. Nested SELECTs count their
own columns; an outer multi-column query does not force that padding inside a single-column lookup.

## names are not all the same thing

- **identifier:** the name of a table, column, variable or other SQL object, such as `orders`.
- **alias:** a name used for a table/query in this statement, or a label given to an output
  column. `orders o` is a table alias; `[Customer Name]` is an output label.
- **literal:** a value written into the SQL, such as `42` or `'MM/dd/yyyy'`. It is data, not a keyword.
- **load-bearing name:** a name something else relies on. Changing a report column or a stored
  procedure's parameter can break its caller even if the rewritten SQL still parses.

That is why "lowercase SQL" does not mean blindly lowercasing the whole file. When restyling
existing code, preserve names and values that carry meaning. Naming styles such as `camelCase`,
`PascalCase` and `snake_case` describe how a name is spelled, not the layout of the SQL around it.

## design and language choices, not just formatting

**Standard SQL first** is a language preference: use portable constructs such as `coalesce`
where they fit. **Unambiguous date/time literals** avoid local reading conventions such as
month-first versus day-first. The [language preferences](../SKILL.md#language-preferences)
give the forms to use when authoring; a formatter must not silently rewrite existing values.

**Filter-first, decorate-second** means selecting the relevant rows before adding the rest of
the details and calculations. **Short, meaningful aliases** keep references compact. These
are authoring preferences, not permission for a formatter to reorder joins, relocate filters
or rename things. A CTE also does not guarantee a particular physical execution order.

The **three-line read guard** is a session setting choice, not decoration: it permits dirty
reads, sets deadlock priority low and suppresses row-count messages. It does not make a query
safe or read-only. Likewise, choosing `coalesce` instead of `isnull` can change result types.
Those choices need context and are not automatic formatting fixes.

## the tool words

A **parser** reads SQL into its structure. An **AST** (abstract syntax tree) is that structure
as data, such as which expression belongs to which query. **Tokens** are the source pieces:
words, punctuation, values and, in ScriptDOM's stream, whitespace and comments.

A **formatter**, or pretty-printer, lays that code out. **Idempotent** means formatting the
result again makes no further changes. A **style check** asks whether layout matches the
profile, not whether the query returns the right results. Unsupported syntax means the tool
cannot handle that input yet, not necessarily that the SQL is wrong.
