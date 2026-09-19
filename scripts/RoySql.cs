using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

// Layout is a set of whitespace decisions over retained ScriptDOM tokens, not a SQL grammar.
public static class RoySql
{
    sealed class Walk : TSqlFragmentVisitor
    {
        readonly Action<TSqlFragment> action;
        public Walk(Action<TSqlFragment> action) { this.action = action; }
        public override void Visit(TSqlFragment node) { action(node); }
    }

    static TSqlFragment Parse(string sql)
    {
        using (var reader = new StringReader(sql))
        {
            var tree = new TSql160Parser(true).Parse(reader, out var errors);
            if (errors.Count > 0)
                throw new FormatException($"T-SQL parse at {errors[0].Line}:{errors[0].Column}: {errors[0].Message} ({errors[0].Number})");
            if (string.Concat(tree.ScriptTokenStream.Select(t => t.Text)) != sql)
                throw new FormatException("token retention failed");
            return tree;
        }
    }

    static bool Comment(TSqlParserToken t) => t.TokenType == TSqlTokenType.SingleLineComment || t.TokenType == TSqlTokenType.MultilineComment;
    static bool Space(TSqlParserToken t) => t.TokenType == TSqlTokenType.WhiteSpace || t.TokenType == TSqlTokenType.EndOfFile;
    static bool IdentifierToken(TSqlParserToken t) => t.TokenType == TSqlTokenType.Identifier || t.TokenType == TSqlTokenType.QuotedIdentifier;
    static readonly HashSet<string> Builtins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "count", "sum", "min", "max", "avg", "coalesce", "isnull", "isnumeric", "nullif", "getdate", "getutcdate", "sysdatetime", "sysutcdatetime", "dateadd", "datediff", "datediff_big", "datename", "datepart", "eomonth", "year", "month", "day", "concat", "concat_ws", "left", "right", "substring", "len", "datalength", "ltrim", "rtrim", "trim", "replace", "reverse", "upper", "lower", "quotename", "round", "abs", "ceiling", "floor", "power", "square", "sqrt", "row_number", "rank", "dense_rank", "ntile", "lag", "lead", "first_value", "last_value", "string_agg", "format", "try_cast", "try_convert", "date_trunc", "array_agg" };

    static HashSet<int> FunctionNames(TSqlFragment tree)
    {
        var result = new HashSet<int>();
        var protectedNames = new HashSet<int>();
        tree.Accept(new Walk(n => {
            if (n is Identifier || n is Literal)
                for (int i = n.FirstTokenIndex; i <= n.LastTokenIndex; i++) protectedNames.Add(i);
            if (n is FunctionCall f && f.CallTarget == null && f.FunctionName.QuoteType == QuoteType.NotQuoted && Builtins.Contains(f.FunctionName.Value))
                result.Add(f.FunctionName.FirstTokenIndex);
            if (n is SqlDataTypeReference) result.Add(n.FirstTokenIndex);
        }));
        // Contextual keywords (APPLY, ISOLATION, etc.) lex as identifiers but are not AST names.
        for (int i = 0; i < tree.ScriptTokenStream.Count; i++)
            if (tree.ScriptTokenStream[i].TokenType == TSqlTokenType.Identifier && !protectedNames.Contains(i)) result.Add(i);
        return result;
    }

    static string TokenText(TSqlParserToken t, bool function = false)
    {
        if (function) return t.Text.ToLowerInvariant();
        // Identifier/literal/variable spellings are data. Only lexer-recognized keywords change case.
        return t.IsKeyword() || t.TokenType == TSqlTokenType.Go ? t.Text.ToLowerInvariant() : t.Text;
    }

    static string SemanticForm(TSqlFragment tree)
    {
        tree.Accept(new Walk(n => {
            if (n is TopRowFilter top && top.Expression is ParenthesisExpression p && p.Expression is IntegerLiteral)
                top.Expression = p.Expression;
        }));
        var generator = new Sql160ScriptGenerator(new SqlScriptGeneratorOptions { KeywordCasing = KeywordCasing.Lowercase });
        generator.GenerateScript(tree, out var sql);
        var canonical = Parse(sql);
        var functions = FunctionNames(canonical);
        var names = new HashSet<int>();
        canonical.Accept(new Walk(n => { if (n is Identifier) names.Add(n.FirstTokenIndex); }));
        var result = new StringBuilder();
        for (int i = 0; i < canonical.ScriptTokenStream.Count; i++)
        {
            var t = canonical.ScriptTokenStream[i];
            if (Space(t) || Comment(t)) continue;
            var value = TokenText(t, functions.Contains(i));
            var kind = t.TokenType.ToString();
            if (names.Contains(i)) { value = Identifier.DecodeIdentifier(value, out _); kind = "name"; }
            result.Append(kind).Append(':').Append(value.Length).Append(':').Append(value).Append('|');
        }
        return result.ToString();
    }

    static void Gate(TSqlFragment tree)
    {
        tree.Accept(new Walk(n => {
            if (n is TSqlStatement && !(n is SelectStatement || n is DeclareVariableStatement || n is PredicateSetStatement || n is SetTransactionIsolationLevelStatement || n is SetCommandStatement))
                throw new FormatException("unsupported statement: " + n.GetType().Name);
            if (n is SelectStatement s && (s.Into != null || s.On != null || s.ComputeClauses.Count > 0 || s.OptimizerHints.Count > 0))
                throw new FormatException("unsupported SELECT option");
            if (n is BinaryQueryExpression || n is QueryParenthesisExpression || n is SelectSetVariable || n is ForClause || n is OffsetClause || n is WindowClause)
                throw new FormatException("unsupported construct: " + n.GetType().Name);
            if (n is WithCtesAndXmlNamespaces w && (w.XmlNamespaces != null || w.ChangeTrackingContext != null))
                throw new FormatException("unsupported WITH option");
            if (n is PredicateSetStatement p && p.Options != SetOptions.NoCount)
                throw new FormatException("unsupported SET option");
            if (n is SetCommandStatement c && c.Commands.Any(x => !(x is GeneralSetCommand g) || g.CommandType != GeneralSetCommandType.DeadlockPriority))
                throw new FormatException("unsupported SET command");
        }));
    }

    sealed class Edit
    {
        public int Start, Length;
        public string Text;
        public Edit(int start, int length, string text) { Start = start; Length = length; Text = text; }
    }

    static string NormalizeSyntax(string sql, TSqlFragment tree)
    {
        var edits = new List<Edit>();
        tree.Accept(new Walk(n => {
            if (n is QualifiedJoin join && join.QualifiedJoinType == QualifiedJoinType.Inner)
                for (int i = join.FirstTableReference.LastTokenIndex + 1; i < join.SecondTableReference.FirstTokenIndex; i++)
                    if (tree.ScriptTokenStream[i].TokenType == TSqlTokenType.Inner)
                        edits.Add(new Edit(tree.ScriptTokenStream[i].Offset, tree.ScriptTokenStream[i].Text.Length, ""));
            if (n is TopRowFilter top && top.Expression is ParenthesisExpression parens && parens.Expression is IntegerLiteral
                && !tree.ScriptTokenStream.Skip(parens.FirstTokenIndex).Take(parens.LastTokenIndex - parens.FirstTokenIndex + 1).Any(Comment))
            {
                edits.Add(new Edit(parens.StartOffset, 1, " "));
                edits.Add(new Edit(parens.StartOffset + parens.FragmentLength - 1, 1, " "));
            }
            if (n is SelectStatement select && select.WithCtesAndXmlNamespaces != null)
            {
                var with = select.WithCtesAndXmlNamespaces;
                int previous = with.FirstTokenIndex - 1;
                while (previous >= 0 && (Space(tree.ScriptTokenStream[previous]) || Comment(tree.ScriptTokenStream[previous]))) previous--;
                if (previous < 0 || tree.ScriptTokenStream[previous].Text != ";") edits.Add(new Edit(with.StartOffset, 0, ";"));
            }
            if (n is SelectElement || n is CommonTableExpression)
            {
                int previous = n.FirstTokenIndex - 1;
                bool comment = false;
                while (previous >= 0 && (Space(tree.ScriptTokenStream[previous]) || Comment(tree.ScriptTokenStream[previous])))
                { comment |= Comment(tree.ScriptTokenStream[previous]); previous--; }
                if (comment && previous >= 0 && tree.ScriptTokenStream[previous].Text == ",")
                    throw new FormatException("comment after list separator requires review");
            }
            if (n is DeclareVariableElement d)
            {
                bool hasAs = tree.ScriptTokenStream.Skip(d.VariableName.LastTokenIndex + 1).Take(d.DataType.FirstTokenIndex - d.VariableName.LastTokenIndex - 1)
                    .Any(t => t.TokenType == TSqlTokenType.As);
                if (!hasAs) edits.Add(new Edit(d.DataType.StartOffset, 0, "as "));
            }
            if (n is SelectScalarExpression s && s.ColumnName != null)
            {
                var name = s.ColumnName.Identifier;
                if (name == null) throw new FormatException("string-valued alias is outside v1");
                var quoted = Identifier.EncodeIdentifier(name.Value);
                if (s.ColumnName.StartOffset < s.Expression.StartOffset)
                    edits.Add(new Edit(name.StartOffset, name.FragmentLength, quoted));
                else
                {
                    int end = s.Expression.StartOffset + s.Expression.FragmentLength;
                    if (tree.ScriptTokenStream.Skip(s.Expression.LastTokenIndex + 1).Take(s.LastTokenIndex - s.Expression.LastTokenIndex).Any(Comment))
                        throw new FormatException("comment between expression and alias requires review");
                    edits.Add(new Edit(s.Expression.StartOffset, 0, quoted + " = "));
                    edits.Add(new Edit(end, s.StartOffset + s.FragmentLength - end, ""));
                }
            }
        }));
        var output = new StringBuilder(sql);
        int boundary = sql.Length;
        foreach (var e in edits.OrderByDescending(e => e.Start))
        {
            if (e.Start + e.Length > boundary) throw new FormatException("overlapping alias edit");
            output.Remove(e.Start, e.Length).Insert(e.Start, e.Text);
            boundary = e.Start;
        }
        return output.ToString();
    }

    sealed class Layout
    {
        readonly IList<TSqlParserToken> tokens;
        readonly Dictionary<int, string> before = new Dictionary<int, string>();
        readonly Dictionary<int, int> indentation = new Dictionary<int, int>();
        readonly HashSet<int> functions;
        readonly HashSet<int> visitedQueries = new HashSet<int>();
        readonly HashSet<int> visitedCases = new HashSet<int>();
        readonly HashSet<int> unary = new HashSet<int>();
        readonly HashSet<int> tightOpen = new HashSet<int>();
        readonly string newline;

        public Layout(TSqlFragment tree, string newline)
        {
            tokens = tree.ScriptTokenStream;
            functions = FunctionNames(tree);
            this.newline = newline;
            tree.Accept(new Walk(n => {
                if (n is UnaryExpression) unary.Add(n.FirstTokenIndex);
                if (n is FunctionCall f) tightOpen.Add(Next(f.FunctionName.LastTokenIndex));
                else if (n is SqlDataTypeReference || n is CastCall || n is TryCastCall || n is ConvertCall || n is TryConvertCall || n is CoalesceExpression || n is NullIfExpression || n is IIfCall || n is LeftFunctionCall || n is RightFunctionCall || n is FullTextPredicate)
                    for (int i = n.FirstTokenIndex; i <= n.LastTokenIndex; i++)
                        if (tokens[i].Text == "(") { tightOpen.Add(i); break; }
            }));
        }

        void Span(TSqlFragment n, int indent)
        {
            for (int i = n.FirstTokenIndex; i <= n.LastTokenIndex; i++) indentation[i] = indent;
        }
        int Prev(int i) { do { i--; } while (i >= 0 && (Space(tokens[i]) || Comment(tokens[i]))); return i; }
        int Next(int i) { do { i++; } while (i < tokens.Count && (Space(tokens[i]) || Comment(tokens[i]))); return i; }
        void Line(int i, int indent, bool blank = false)
        {
            before[i] = newline + (blank ? newline : "") + new string(' ', indent);
            indentation[i] = indent;
        }
        int Find(int first, int last, string word)
        {
            for (int i = first; i <= last; i++) if (!Comment(tokens[i]) && tokens[i].Text.Equals(word, StringComparison.OrdinalIgnoreCase)) return i;
            throw new FormatException("layout boundary not found: " + word);
        }

        string Gap(int prior, int i)
        {
            var p = tokens[prior].Text;
            var t = tokens[i].Text;
            if (p == "-" && t == "-") return " "; // Never turn adjacent unary minuses into a comment.
            if (t == ";" || t == "," || t == "." || t == ")" || p == "." || p == "(" || unary.Contains(prior) || tightOpen.Contains(i)) return "";
            if ((p == ">" || p == "<" || p == "!") && (t == "=" || t == ">" || t == "<")) return "";
            return " ";
        }

        int InlineLength(TSqlFragment n)
        {
            int size = 0, prior = -1;
            for (int i = n.FirstTokenIndex; i <= n.LastTokenIndex; i++)
            {
                if (Space(tokens[i]) || Comment(tokens[i])) continue;
                size += tokens[i].Text.Length + (prior < 0 ? 0 : Gap(prior, i).Length);
                prior = i;
            }
            return size;
        }

        void Declarations(List<DeclareVariableElement> block)
        {
            if (block.Count == 0) return;
            int names = block.Max(d => InlineLength(d.VariableName));
            int types = block.Max(d => InlineLength(d.DataType));
            foreach (var d in block)
            {
                before[Find(d.VariableName.LastTokenIndex + 1, d.DataType.FirstTokenIndex - 1, "as")] = new string(' ', names - InlineLength(d.VariableName) + 1);
                if (d.Value != null) before[Find(d.DataType.LastTokenIndex + 1, d.Value.FirstTokenIndex - 1, "=")] = new string(' ', types - InlineLength(d.DataType) + 1);
            }
            block.Clear();
        }

        void Expression(TSqlFragment n, int indent)
        {
            n.Accept(new Walk(part => {
                if (part is ScalarSubquery sub && !visitedQueries.Contains(sub.QueryExpression.FirstTokenIndex))
                {
                    Query(sub.QueryExpression, indent + 4);
                    Line(sub.LastTokenIndex, indent);
                }
                if (part is CaseExpression c && visitedCases.Add(c.FirstTokenIndex))
                {
                    Span(c, indent);
                    var clauses = c is SearchedCaseExpression searched
                        ? searched.WhenClauses.Cast<TSqlFragment>()
                        : ((SimpleCaseExpression)c).WhenClauses.Cast<TSqlFragment>();
                    foreach (var clause in clauses)
                    {
                        Span(clause, indent + 4);
                        Line(clause.FirstTokenIndex, indent + 4);
                        Expression(clause, indent + 4);
                    }
                    if (c.ElseExpression != null)
                    {
                        Line(Prev(c.ElseExpression.FirstTokenIndex), indent + 4);
                        Span(c.ElseExpression, indent + 4);
                        Expression(c.ElseExpression, indent + 4);
                    }
                    Line(c.LastTokenIndex, indent);
                }
            }));
        }

        void Predicate(BooleanExpression n, int indent, int first)
        {
            Span(n, indent);
            // ExistsPredicate's span can start at '(' rather than EXISTS. The clause owns its boundary.
            Line(first, indent);
            n.Accept(new Walk(part => {
                if (part is BooleanBinaryExpression b)
                    Line(Find(b.FirstExpression.LastTokenIndex + 1, b.SecondExpression.FirstTokenIndex - 1, b.BinaryExpressionType == BooleanBinaryExpressionType.And ? "and" : "or"), indent);
            }));
            Expression(n, indent);
        }

        void Table(TableReference n, int indent)
        {
            if (n is QualifiedJoin join)
            {
                Table(join.FirstTableReference, indent);
                Span(join.SecondTableReference, indent);
                Line(Next(join.FirstTableReference.LastTokenIndex), indent);
                TableBody(join.SecondTableReference, indent);
                Predicate(join.SearchCondition, indent + 4, Next(Find(join.SecondTableReference.LastTokenIndex + 1, join.SearchCondition.FirstTokenIndex, "on")));
            }
            else if (n is UnqualifiedJoin unqualified)
            {
                Table(unqualified.FirstTableReference, indent);
                Span(unqualified.SecondTableReference, indent);
                Line(Next(unqualified.FirstTableReference.LastTokenIndex), indent);
                TableBody(unqualified.SecondTableReference, indent);
            }
            else { Span(n, indent); Line(n.FirstTokenIndex, indent); TableBody(n, indent); }
        }

        void TableBody(TableReference n, int indent)
        {
            if (n is QueryDerivedTable d)
            {
                Query(d.QueryExpression, indent + 4);
                Line(Next(d.QueryExpression.LastTokenIndex), indent);
            }
            else if (n is NamedTableReference || n is SchemaObjectFunctionTableReference) Expression(n, indent);
            else throw new FormatException("unsupported table source: " + n.GetType().Name);
        }

        void Query(QueryExpression expression, int indent)
        {
            if (!(expression is QuerySpecification q)) throw new FormatException("unsupported query expression");
            if (!visitedQueries.Add(q.FirstTokenIndex)) return;
            Span(q, indent);
            Line(q.FirstTokenIndex, indent);
            int width = q.SelectElements.OfType<SelectScalarExpression>().Where(s => s.ColumnName != null).Select(s => tokens[s.ColumnName.FirstTokenIndex].Text.Length).DefaultIfEmpty(0).Max();
            for (int e = 0; e < q.SelectElements.Count; e++)
            {
                var item = q.SelectElements[e];
                Span(item, indent + 4);
                Line(e == 0 ? item.FirstTokenIndex : Prev(item.FirstTokenIndex), indent + (e == 0 && q.SelectElements.Count > 1 ? 6 : 4));
                if (item is SelectScalarExpression s && s.ColumnName != null)
                    before[Next(s.ColumnName.LastTokenIndex)] = new string(' ', width - tokens[s.ColumnName.FirstTokenIndex].Text.Length + 1);
                Expression(item, indent + 4);
            }
            if (q.FromClause != null)
            {
                Line(q.FromClause.FirstTokenIndex, indent);
                foreach (var table in q.FromClause.TableReferences)
                {
                    Table(table, indent + 4);
                    if (table != q.FromClause.TableReferences[0]) { before.Remove(table.FirstTokenIndex); Line(Prev(table.FirstTokenIndex), indent + 4); }
                }
            }
            if (q.WhereClause != null) { Line(q.WhereClause.FirstTokenIndex, indent); Predicate(q.WhereClause.SearchCondition, indent + 4, Next(q.WhereClause.FirstTokenIndex)); }
            if (q.GroupByClause != null)
            {
                Line(q.GroupByClause.FirstTokenIndex, indent);
                Line(q.GroupByClause.GroupingSpecifications[0].FirstTokenIndex, indent + 4);
                Expression(q.GroupByClause, indent + 4);
            }
            if (q.HavingClause != null) { Line(q.HavingClause.FirstTokenIndex, indent); Predicate(q.HavingClause.SearchCondition, indent + 4, Next(q.HavingClause.FirstTokenIndex)); }
            if (q.OrderByClause != null)
            {
                Line(q.OrderByClause.FirstTokenIndex, indent);
                Line(q.OrderByClause.OrderByElements[0].FirstTokenIndex, indent + 4);
                Expression(q.OrderByClause, indent + 4);
            }
        }

        public string Print(TSqlFragment tree)
        {
            foreach (var batch in ((TSqlScript)tree).Batches)
            {
            var declarations = new List<DeclareVariableElement>();
            TSqlStatement preceding = null;
            foreach (var statement in batch.Statements)
            {
                Span(statement, 0);
                Line(statement.FirstTokenIndex, 0, statement is SelectStatement || (statement is DeclareVariableStatement && !(preceding is DeclareVariableStatement)));
                if (statement is SelectStatement s)
                {
                    if (s.WithCtesAndXmlNamespaces != null)
                    {
                        var with = s.WithCtesAndXmlNamespaces;
                        Line(with.FirstTokenIndex, 0, true);
                        int previous = Prev(with.FirstTokenIndex);
                        if (previous >= 0 && tokens[previous].Text == ";") { Line(previous, 0, true); before[with.FirstTokenIndex] = ""; }
                        foreach (var cte in with.CommonTableExpressions)
                        {
                            if (cte != with.CommonTableExpressions[0])
                            {
                                before[Prev(cte.FirstTokenIndex)] = "";
                                before[cte.FirstTokenIndex] = " ";
                            }
                            Query(cte.QueryExpression, 4);
                            Line(cte.QueryExpression.FirstTokenIndex, 4, true);
                            Line(Next(cte.QueryExpression.LastTokenIndex), 0, true);
                        }
                    }
                    Query(s.QueryExpression, 0);
                    Line(s.QueryExpression.FirstTokenIndex, 0, true);
                }
                else Expression(statement, 0);
                if (statement is DeclareVariableStatement d) declarations.AddRange(d.Declarations);
                else Declarations(declarations);
                preceding = statement;
            }
            Declarations(declarations);
            }
            // A full-line comment belongs with the following clause, not above its blank line.
            foreach (var entry in before.ToArray())
            {
                if (!entry.Value.StartsWith(newline)) continue;
                int i = entry.Key - 1, comment = -1;
                while (i >= 0 && (Space(tokens[i]) || Comment(tokens[i])))
                {
                    if (Comment(tokens[i]))
                    {
                        int p = Prev(i);
                        if (p >= 0 && tokens[p].Line == tokens[i].Line) break;
                        comment = i;
                    }
                    i--;
                }
                if (comment >= 0)
                {
                    for (int c = comment; c < entry.Key; c++)
                        if (Comment(tokens[c])) indentation[c] = indentation[entry.Key];
                    before[comment] = entry.Value;
                    before[entry.Key] = newline + new string(' ', indentation[entry.Key]);
                }
            }
            var output = new StringBuilder();
            int prior = -1;
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (Space(t)) continue;
                string gap = "";
                if (prior >= 0)
                {
                    var p = tokens[prior];
                    bool hadNewline = tokens.Skip(prior + 1).Take(i - prior - 1).Any(x => x.Text != null && (x.Text.Contains("\n") || x.Text.Contains("\r")));
                    if (before.TryGetValue(i, out var planned)) gap = planned;
                    else if (Comment(t)) gap = hadNewline ? newline + new string(' ', indentation.TryGetValue(i, out var ci) ? ci : 0) : " ";
                    else gap = Gap(prior, i);
                    if (p.TokenType == TSqlTokenType.SingleLineComment && !gap.Contains(newline))
                        gap = newline + new string(' ', indentation.TryGetValue(i, out var li) ? li : 0);
                    if (t.TokenType == TSqlTokenType.Go || p.TokenType == TSqlTokenType.Go) gap = newline;
                }
                output.Append(gap).Append(TokenText(t, functions.Contains(i)));
                prior = i;
            }
            return output.ToString().TrimEnd('\r', '\n') + newline;
        }
    }

    static IList<TSqlParserToken> Lex(string sql)
    {
        using (var reader = new StringReader(sql))
        {
            var tokens = new TSql160Parser(true).GetTokenStream(reader, out var errors);
            if (errors.Count > 0)
                throw new FormatException($"tokenization at {errors[0].Line}:{errors[0].Column}: {errors[0].Message}");
            if (string.Concat(tokens.Select(t => t.Text)) != sql) throw new FormatException("token retention failed");
            return tokens.Where(t => !Space(t)).ToList();
        }
    }

    static string LooseText(IList<TSqlParserToken> tokens, int i)
    {
        var t = tokens[i];
        var prior = i > 0 ? tokens[i - 1].Text.ToLowerInvariant() : "";
        var next = i + 1 < tokens.Count ? tokens[i + 1].Text : "";
        if (t.TokenType == TSqlTokenType.Identifier && prior != "." && next != ".")
        {
            if (Builtins.Contains(t.Text) && next == "(") return t.Text.ToLowerInvariant();
            if ((t.Text.Equals("false", StringComparison.OrdinalIgnoreCase) || t.Text.Equals("true", StringComparison.OrdinalIgnoreCase))
                && (prior == "=" || prior == "<>" || prior == "!=" || prior == "is" || prior == "not")) return t.Text.ToLowerInvariant();
            if (((t.Text.Equals("limit", StringComparison.OrdinalIgnoreCase) || t.Text.Equals("offset", StringComparison.OrdinalIgnoreCase))
                    && i + 1 < tokens.Count && tokens[i + 1].TokenType == TSqlTokenType.Integer)
                || (t.Text.Equals("filter", StringComparison.OrdinalIgnoreCase) && next == "(")) return t.Text.ToLowerInvariant();
        }
        return TokenText(t);
    }

    sealed class LooseScope
    {
        public int Indent, ProjectionStart = -1;
        public bool Block, Columns, Between, FirstProjection;
        public string Clause = "";
    }

    // ponytail: this fallback lays out tokens, not another SQL grammar. Unknown syntax stays in order.
    static string Loose(string sql)
    {
        var tokens = Lex(sql);
        if (tokens.Count == 0) return sql;
        var newline = sql.Contains("\r\n") ? "\r\n" : "\n";
        var output = new StringBuilder();
        var scopes = new Stack<LooseScope>();
        scopes.Push(new LooseScope { Block = true });
        bool tableDefinition = false;
        int prior = -1;
        string planned = "";
        Func<int, string> line = indent => newline + new string(' ', indent);
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var word = token.Text.ToLowerInvariant();
            var p = prior >= 0 ? tokens[prior].Text.ToLowerInvariant() : "";
            var next = i + 1 < tokens.Count ? tokens[i + 1].Text.ToLowerInvariant() : "";
            var scope = scopes.Peek();
            bool firstProjection = scope.FirstProjection && !Comment(token);
            var indent = scope.Indent + (scope.Clause.Length > 0 ? 4 : 0);
            // Keep inline spacing: a T-SQL token boundary need not be a boundary in another dialect.
            string gap = prior < 0 ? "" : sql.Substring(tokens[prior].Offset + tokens[prior].Text.Length,
                token.Offset - tokens[prior].Offset - tokens[prior].Text.Length);
            if (gap.Contains("\n") || gap.Contains("\r")) gap = " ";
            if (planned.Length > 0) { gap = planned; planned = ""; }

            if (Comment(token))
            {
                if (prior >= 0 && token.Line > tokens[prior].Line && !gap.StartsWith(newline)) gap = line(indent);
            }
            else if (word == "(")
            {
                bool query = next == "select" || next == "with";
                bool columns = tableDefinition && scope.Block;
                bool block = query || columns;
                scopes.Push(new LooseScope { Indent = indent, Block = block, Columns = columns });
                if (block) planned = line(indent + 4);
                tableDefinition = false;
            }
            else if (word == ")" && scopes.Count > 1)
            {
                var closed = scopes.Pop();
                if (closed.Block) gap = line(closed.Indent);
            }
            else if (scope.Block)
            {
                if (word == "table" && p == "create") tableDefinition = true;
                bool name = (token.TokenType == TSqlTokenType.Identifier || token.IsKeyword()) && p != "." && next != ".";
                bool pair = (word == "group" || word == "order") && next == "by";
                bool clause = name && (new[] { "select", "from", "where", "having", "limit", "offset", "returning", "union" }.Contains(word) || pair);
                bool join = word == "join" || ((word == "left" || word == "right" || word == "full" || word == "inner" || word == "cross" || word == "outer") && (next == "join" || next == "outer" || next == "apply"));
                if (clause && !scope.Columns)
                {
                    gap = line(scope.Indent + (scopes.Count > 1 ? 4 : 0));
                    scope.Clause = word;
                    scope.Between = false;
                    scope.FirstProjection = word == "select";
                    scope.ProjectionStart = -1;
                    if (!pair) planned = line(scope.Indent + (scopes.Count > 1 ? 8 : 4));
                }
                else if (word == "by" && (p == "order" || p == "group")) planned = line(scope.Indent + (scopes.Count > 1 ? 8 : 4));
                else if (name && join && !new[] { "left", "right", "full", "inner", "cross", "outer" }.Contains(p))
                {
                    gap = line(scope.Indent + 4);
                    scope.Clause = "join";
                }
                else if (word == "on" && scope.Clause == "join") planned = line(scope.Indent + 8);
                else if (word == "between") scope.Between = true;
                else if (word == "and" && scope.Between) scope.Between = false;
                else if ((word == "and" || word == "or") && (scope.Clause == "where" || scope.Clause == "having" || scope.Clause == "join"))
                    gap = line(scope.Indent + (scope.Clause == "join" ? 8 : 4));
                else if (word == "," && (scope.Clause == "select" || scope.Columns))
                {
                    // Only a comma in this SELECT's scope proves that its first item needs padding.
                    if (scope.Clause == "select" && scope.ProjectionStart >= 0)
                    {
                        output.Insert(scope.ProjectionStart, "  ");
                        scope.ProjectionStart = -1;
                    }
                    gap = line(scope.Indent + (scope.Clause == "select" && scopes.Count > 1 ? 8 : 4));
                    planned = " ";
                }
                else if (word == ";") { scope.Clause = ""; planned = newline + newline; }
            }
            if (prior >= 0 && tokens[prior].TokenType == TSqlTokenType.SingleLineComment) gap = line(indent);
            if (prior < 0) gap = "";
            if (firstProjection)
            {
                scope.ProjectionStart = output.Length + gap.Length;
                scope.FirstProjection = false;
            }
            output.Append(gap).Append(LooseText(tokens, i));
            prior = i;
        }
        var result = output.ToString().TrimEnd('\r', '\n') + newline;
        var after = Lex(result);
        if (tokens.Count != after.Count || !tokens.Select((t, i) => LooseText(tokens, i)).SequenceEqual(after.Select((t, i) => LooseText(after, i))))
            throw new FormatException("token boundaries changed during best-effort layout");
        return result;
    }

    public static string Format(string sql, bool strict, out string warning)
    {
        warning = null;
        if (strict) return Format(sql);
        try
        {
            var formatted = Format(sql);
            var tokens = Lex(formatted);
            // FALSE/TRUE in SQL-like comparisons are identifiers to the T-SQL parser, not literals.
            var output = new StringBuilder(formatted);
            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                var text = LooseText(tokens, i);
                if (text != tokens[i].Text) output.Remove(tokens[i].Offset, tokens[i].Text.Length).Insert(tokens[i].Offset, text);
            }
            return output.ToString();
        }
        catch (FormatException error)
        {
            warning = error.Message + "; using best-effort layout (not validated).";
            try { return Loose(sql); }
            catch (FormatException fallback)
            {
                warning += " " + fallback.Message + "; input preserved unchanged.";
                return sql;
            }
        }
    }

    public static string Format(string sql)
    {
        var original = Parse(sql);
        Gate(original);
        var normalized = Parse(NormalizeSyntax(sql, original));
        string newline = sql.Contains("\r\n") ? "\r\n" : "\n";
        var output = new Layout(normalized, newline).Print(normalized);
        var result = Parse(output);
        if (SemanticForm(original) != SemanticForm(result)) throw new FormatException("semantic representation changed; no output");
        if (!original.ScriptTokenStream.Where(Comment).Select(t => t.Text).SequenceEqual(result.ScriptTokenStream.Where(Comment).Select(t => t.Text)))
            throw new FormatException("comment content/order changed; no output");
        return output;
    }
}
