# Worked examples

Show only the SQL needed for the point being taught. These are excerpts, not runnable
scripts. Session settings, declarations and unrelated output columns stay out unless
they are the subject. The [rules](../SKILL.md) cover those separately.

## 1. Column lists

Leading commas, plain pass-through columns, and aligned `alias = expression` for computed columns.

```sql
select
      od.orderid
    , [gross] = od.unitprice * od.quantity
    , [net]   = od.unitprice * od.quantity * (1 - od.discount)
```

## 2. Join indentation and filter placement

`on` ends the join line. The joined-table filter stays in ON; the root-table filter stays in WHERE.
This illustrates authoring, not permission for a formatter to move predicates.

```sql
from
    [order details] od
    join products p on
        p.productid = od.productid
        and p.discontinued = 0
where
    od.orderid = @orderid
```

## 3. CTE spacing and chaining

Blank lines inside each CTE, with the next name on the closing line: `), ordrollup as (`.
The second CTE uses only the orders selected by `gd`. The final SELECT is omitted.

```sql
;with gd as (

    select
        orderid
    from
        orders
    where
        shippeddate is not null

), ordrollup as (

    -- line counts for shipped orders
    select
          gd.orderid
        , [linecount] = count(*)
    from
        gd
        join [order details] od on
            od.orderid = gd.orderid
    group by gd.orderid

)
```

## 4. Same table in an APPLY

Inner `oo` doubles outer `o`; `po` names the previous-order result. Here, previous means
the same customer's greatest order ID below the current one.

```sql
from
    orders o
    outer apply (
        select top 1
              [previous_orderid]   = oo.orderid
            , [previous_orderdate] = oo.orderdate
        from
            orders oo
        where
            oo.customerid = o.customerid
            and oo.orderid < o.orderid
        order by
            oo.orderid desc
    ) po
```
