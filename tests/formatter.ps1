#requires -Version 7.0
param([Parameter(Mandatory)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath $AssemblyPath
Add-Type -Path "$PSScriptRoot/../scripts/RoySql.cs" -ReferencedAssemblies (@([IO.Directory]::GetFiles("$PSHOME/ref", '*.dll')) + (Resolve-Path -LiteralPath $AssemblyPath).Path)
$passed = 0
function Test-Format($name, $sql, $expected) {
    try { $actual = [RoySql]::Format($sql) } catch { throw "$name : $($_.Exception.InnerException.Message)" }
    if ($expected -and $actual -cne $expected.Replace("`r`n", "`n")) { throw "$name layout differs:`n$actual" }
    if ([RoySql]::Format($actual) -cne $actual) { throw "$name is not idempotent:`n$actual`nSECOND:`n$([RoySql]::Format($actual))" }
    $script:passed++
}
function Test-Refusal($name, $sql) {
    try { $null = [RoySql]::Format($sql) }
    catch { $script:passed++; return }
    throw "$name was accepted"
}
Test-Format 'projection aliases and join' 'SELECT o.OrderID, sum(od.Price*od.Quantity) AS Total FROM Orders o JOIN Details od ON od.OrderID=o.OrderID WHERE o.OrderID=@OrderID AND o.ShippedDate IS NOT NULL GROUP BY o.OrderID ORDER BY o.OrderID DESC' @'
select
      o.OrderID
    , [Total] = sum(od.Price * od.Quantity)
from
    Orders o
        join Details od on
            od.OrderID = o.OrderID
where
    o.OrderID = @OrderID
    and o.ShippedDate is not null
group by
    o.OrderID
order by
    o.OrderID desc

'@
Test-Format 'literal and comment bytes' "SELECT N'MM/dd/yyyy' AS [Mask], 'Bob''s' AS [Owner], 0xABCD AS [Bytes] -- Keep THIS`nFROM [Order Details] /* Mixed Case */ WHERE [Code]='XFR'"
Test-Format 'comparison operators' 'SELECT a FROM t WHERE a>=2 AND b<=3 AND c<>4 AND d!=5 AND e!<6 AND f!>7'
Test-Format 'boolean grouping' 'select a from t where (a=1 or b=2) and c between 3 and 4'
Test-Format 'window and case' 'select sum(case when a is null then 0 else a end) over (partition by b order by c) as Total from t'
Test-Format 'nested scalar' 'select (select top 1 x.a from t x where x.b=o.b order by x.a desc) as Latest from t o'
Test-Format 'exists' 'select o.a from t o where exists (select 1 from t x where x.a=o.a and x.b=2) and o.c=3'
Test-Format 'CTE' ';with gd as (select a from t where b=1), totals as (select a, count(*) as Qty from gd group by a) select a from totals'
Test-Format 'APPLY' 'select o.a, p.b from t o outer apply (select top 1 x.b from t x where x.a=o.a order by x.b desc) p' @'
select
      o.a
    , p.b
from
    t o
        outer apply (
            select top 1
                x.b
            from
                t x
            where
                x.a = o.a
            order by
                x.b desc
        ) p

'@
Test-Format 'preamble' "-- keep guard`nset transaction isolation level read uncommitted`nset deadlock_priority -10`nset nocount on`ndeclare @orderid int = try_cast('123' as int)`nselect @orderid"
Test-Format 'escaped identifiers' 'select a AS [a]]b], b AS "Spaced Name" from [Table]]Name]'
Test-Format 'comments around boundaries' "select a -- first`n, b as B /* second */ from t -- source`nwhere a=1 -- root`nand b=2"
Test-Format 'commented-out code' "--SELECT Broken {{{`nselect a`n--, b AS X`nfrom t`n-- where ???"
Test-Format 'GO' "select 1`nGO`nselect 2"
Test-Format 'CRLF' "SELECT 1`r`n"
Test-Format 'declarations' "DECLARE @id INT=1`nDECLARE @start DATE='20220506'`nSELECT @id" @'
declare @id    as int  = 1
declare @start as date = '20220506'

select
    @id

'@
Test-Format 'CTE exact' ';WITH gd AS (SELECT orderid FROM orders) SELECT orderid FROM gd' @'
;with gd as (

    select
        orderid
    from
        orders

)

select
    orderid
from
    gd

'@
Test-Format 'no guard insertion' 'SELECT ISNULL(a,0) AS x FROM t' @'
select
    [x] = isnull(a, 0)
from
    t

'@
Test-Format 'multi-character operators exact' 'select a from t where a>=2 and b<>3' @'
select
    a
from
    t
where
    a >= 2
    and b <> 3

'@
Test-Format 'multi-line literal bytes' "select N'first`r`nsecond' as Value"
Test-Format 'multi-line comment bytes' "select a /* first`r`n  second */ from t"
Test-Format 'no schema function lowercasing' 'SELECT dbo.MyFunction(a) AS Value FROM t'
Test-Format 'unary operators' 'select -1 as a, - -2 as b, ~3 as c'
Test-Format 'comma sources' 'select a.id from t a, t b where a.id=b.id'
Test-Format 'bare WITH' 'WITH gd AS (SELECT 1 AS n) SELECT n FROM gd'
Test-Format 'guard casing' "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED`nSET DEADLOCK_PRIORITY -10`nSET NOCOUNT ON`nSELECT 1" @'
set transaction isolation level read uncommitted
set deadlock_priority -10
set nocount on

select
    1

'@
Test-Format 'uppercase APPLY' 'SELECT o.a FROM t o OUTER APPLY (SELECT 1 AS a) p'
Test-Format 'inclusive BETWEEN untouched' 'select a from t where a between 1 and 4 and b != 2' @'
select
    a
from
    t
where
    a between 1 and 4
    and b != 2

'@
Test-Format 'EXISTS clause boundary' 'select a from t where exists (select 1 from u where u.a=t.a)' @'
select
    a
from
    t
where
    exists (
        select
            1
        from
            u
        where
            u.a = t.a
    )

'@
Test-Format 'single column has no comma padding' 'select OrderID from Orders' @'
select
    OrderID
from
    Orders

'@
Test-Format 'single star has no comma padding' 'select * from t' @'
select
    *
from
    t

'@
Test-Format 'single expression has no comma padding' 'select 1 + 2' @'
select
    1 + 2

'@
Test-Format 'multiple plain columns retain padding' 'select a, b, c from t' @'
select
      a
    , b
    , c
from
    t

'@
foreach ($projections in @('1 as short, 2 as much_longer_name, 3 as x', '1 as much_longer_name, 2 as x, 3 as short', '1 as x, a, 2 as short, 3 as much_longer_name')) {
    $sql = "select $projections from t"
    Test-Format 'alias alignment is list-wide and position independent' $sql
    $lines = ([RoySql]::Format($sql) -split "`n") | Where-Object { $_ -match '^\s+(, )?\[' }
    $columns = @($lines | ForEach-Object { $_.IndexOf('=') } | Select-Object -Unique)
    if ($columns.Count -ne 1 -or $columns[0] -ne 25) { throw 'projection equals signs do not align to the longest alias' }
}
Test-Format 'LEFT and RIGHT call spacing' 'SELECT LEFT( name, 2 ), RIGHT( name, 2 ) FROM things' @'
select
      left(name, 2)
    , right(name, 2)
from
    things

'@
Test-Format 'nested LEFT and RIGHT' 'SELECT LEFT(RIGHT(name, 4), 2) AS Part FROM things' @'
select
    [Part] = left(right(name, 4), 2)
from
    things

'@
foreach ($nl in @("`n", "`r`n")) {
    $sql = @('select a.id', 'from things a', 'outer apply (', '    -- first explanation', '    -- second explanation', '    select top 1 b.id', '    from things b', '    where b.id = a.id', ') found') -join $nl
    Test-Format 'nested full-line comment block' $sql
    $comments = @([RoySql]::Format($sql) -split '\r?\n' | Where-Object { $_ -match '^\s*--' })
    if ($comments.Count -ne 2 -or @($comments | Where-Object { $_ -match '^ {12}--' }).Count -ne 2) { throw 'comment block indentation differs' }
}
Test-Format 'predicate comment block' "select a from t where`n-- first`n/* second */`na=1" @'
select
    a
from
    t
where
    -- first
    /* second */
    a = 1

'@
Test-Format 'full-text predicate spacing' "SELECT Name FROM Things WHERE CONTAINS(Name, 'sample') OR FREETEXT(Name, 'example')" @'
select
    Name
from
    Things
where
    contains(Name, 'sample')
    or freetext(Name, 'example')

'@
Test-Format 'builtin casing preserves qualified function and argument names' 'SELECT ISNUMERIC(Value), dbo.ISNUMERIC(Value), dbo.MyFunction(Value) FROM Things' @'
select
      isnumeric(Value)
    , dbo.ISNUMERIC(Value)
    , dbo.MyFunction(Value)
from
    Things

'@
Test-Refusal 'USE outside v1' 'use ExampleDatabase; select 1'
Test-Refusal 'malformed' 'select from'
Test-Refusal 'DML' 'delete from t'
Test-Refusal 'DDL' 'create table t (a int)'
Test-Refusal 'dynamic SQL' "exec('select 1')"
Test-Refusal 'SELECT INTO' 'select a into newtable from t'
Test-Refusal 'union outside v1' 'select a from t union select b from u'
Test-Refusal 'alias-comment move' 'select a /* do not move */ as B from t'
Test-Refusal 'quoted identifier mode switch' 'set quoted_identifier off select "not an identifier"'
Test-Refusal 'FOR JSON' 'select a from t for json auto'
Test-Refusal 'FOR XML' 'select a from t for xml path'
Test-Refusal 'table-valued declarations' 'declare @t table (a int) select a from @t'
Test-Refusal 'comment after column comma' "select a, -- next`nb from t"
Test-Refusal 'comment after CTE comma' ";with a as (select 1 as n), -- next`nb as (select n from a) select n from b"
"$passed checks passed"
