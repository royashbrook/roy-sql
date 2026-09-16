#requires -Version 7.0
param([Parameter(Mandatory)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath $AssemblyPath
Add-Type -Path "$PSScriptRoot/../scripts/RoySql.cs" -ReferencedAssemblies (@([IO.Directory]::GetFiles("$PSHOME/ref", '*.dll')) + (Resolve-Path -LiteralPath $AssemblyPath).Path)
$samples = [ordered]@{
    aggregation = @'
SELECT p.category_id, c.category_name,
COUNT(DISTINCT p.product_id) AS total_products,
SUM(oi.quantity * oi.unit_price) AS total_revenue,
ROUND(AVG(oi.unit_price), 2) AS avg_item_price,
CASE WHEN SUM(oi.quantity * oi.unit_price) > 100000 THEN 'High Performing'
WHEN SUM(oi.quantity * oi.unit_price) BETWEEN 50000 AND 100000 THEN 'Mid Performing'
ELSE 'Low Performing' END AS performance_tier
FROM products p LEFT JOIN categories c ON p.category_id = c.id
INNER JOIN order_items oi ON p.product_id = oi.product_id
INNER JOIN orders o ON oi.order_id = o.id
WHERE o.order_status IN ('SHIPPED', 'DELIVERED') AND o.created_at >= '2025-01-01'
GROUP BY p.category_id, c.category_name HAVING COUNT(DISTINCT p.product_id) >= 5
ORDER BY total_revenue DESC;
'@
    windows = @'
WITH MonthlySales AS (
SELECT DATE_TRUNC('month', order_date) AS sales_month, customer_id,
SUM(net_amount) AS monthly_spend,
ROW_NUMBER() OVER (PARTITION BY DATE_TRUNC('month', order_date) ORDER BY SUM(net_amount) DESC) AS spender_rank
FROM customer_orders WHERE is_cancelled = FALSE GROUP BY 1, 2
), TopCustomers AS (
SELECT sales_month, customer_id, monthly_spend FROM MonthlySales WHERE spender_rank <= 10
)
SELECT tc.sales_month, tc.customer_id, tc.monthly_spend,
LAG(tc.monthly_spend, 1) OVER (PARTITION BY tc.customer_id ORDER BY tc.sales_month) AS prev_month_spend,
LEAD(tc.monthly_spend, 1) OVER (PARTITION BY tc.customer_id ORDER BY tc.sales_month) AS next_month_spend
FROM TopCustomers tc ORDER BY tc.sales_month DESC, tc.monthly_spend DESC;
'@
    ddl = @'
CREATE TABLE inventory_audit_log (
log_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
product_sku VARCHAR(50) NOT NULL,
warehouse_id INT NOT NULL,
old_quantity INT NOT NULL CHECK (old_quantity >= 0),
new_quantity INT NOT NULL CHECK (new_quantity >= 0),
change_reason VARCHAR(255) DEFAULT 'ROUTINE_ADJUSTMENT',
adjusted_by VARCHAR(100) NOT NULL,
created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
CONSTRAINT fk_inventory_warehouse FOREIGN KEY (warehouse_id) REFERENCES warehouses (id)
ON DELETE RESTRICT ON UPDATE CASCADE
);
CREATE INDEX idx_inventory_audit_sku_date ON inventory_audit_log (product_sku, created_at DESC);
'@
    operators = @'
-- Test case for inline comments and mixed casing
SeLeCt -- Trailing column comment
tbl.id AS [User ID], /* Block comment test */
tbl.details->>'email' AS email_address,
ARRAY_AGG(tbl.tags) FILTER (WHERE tbl.tags IS NOT NULL) AS tag_list,
COALESCE(tbl.updated_at, tbl.created_at, '1970-01-01 00:00:00+00') AS effective_date
FROM "Audit"."UserLogs" tbl
WHERE tbl.payload LIKE '%"status": "error"%'
AND tbl.created_at BETWEEN TIMESTAMP '2026-01-01 00:00:00' AND CURRENT_TIMESTAMP
GROUP BY tbl.id, tbl.details->>'email', tbl.updated_at, tbl.created_at;
'@
    limit = "select u.id, u.first_name from users u where u.status='COMPLETED' order by u.id limit 50;"
    incomplete = 'select from'
    unclosed = "select 'unterminated"
    identifiers = 'select Limit, Zone, False, dbo.DATE_TRUNC(d), [FALSE] from t'
    comments = "-- keep SELECT FROM`r`nselect a, -- next`r`nb from t limit 2; /* END */"
    punctuation = "select details->>'x', payload#>>'{a}', name::text, - -2, 'a`nb' from t limit 4;"
    pseudocode = 'select something from wherever MAGIC THING happens limit 1'
}
$passed = 0
foreach ($sample in $samples.GetEnumerator()) {
    $warning = $null
    $actual = [RoySql]::Format($sample.Value, $false, [ref]$warning)
    $secondWarning = $null
    $second = [RoySql]::Format($actual, $false, [ref]$secondWarning)
    if ($second -cne $actual) { throw "$($sample.Key) not idempotent:`n$actual`nSECOND:`n$second" }
    switch ($sample.Key) {
        aggregation {
            if ($actual -notmatch 'case\r?\n {8}when' -or $actual -match 'inner join' -or $actual -notmatch '\r?\n {4}left join' -or $warning) { throw 'aggregation layout failed' }
        }
        windows {
            if ($actual -cnotmatch 'date_trunc\(' -or $actual -cnotmatch 'is_cancelled = false' -or $actual -notmatch '\), TopCustomers as \(') { throw 'window/CTE style failed' }
        }
        ddl { if (!$warning -or !$actual.Contains('ROUTINE_ADJUSTMENT') -or $actual -notmatch '\r?\n {4}, product_sku') { throw 'DDL fallback failed' } }
        operators { if (!$warning -or $actual -notmatch "->>\s*'email'" -or !$actual.Contains('"Audit"."UserLogs"') -or !$actual.Contains('/* Block comment test */')) { throw "operator/comment retention failed: $warning`n$actual" } }
        limit { if (!$warning -or $actual -notmatch 'limit\r?\n {4}50' -or $actual -match 'top') { throw 'LIMIT was lost or translated' } }
        unclosed { if ($actual -cne $sample.Value -or $warning -notmatch 'preserved unchanged') { throw 'unclosed literal was damaged' } }
        identifiers { if (!$actual.Contains('Limit') -or !$actual.Contains('Zone') -or !$actual.Contains('False') -or !$actual.Contains('dbo.DATE_TRUNC')) { throw 'identifiers changed' } }
        punctuation { if ($actual -notmatch "#>>\s*'\{a\}'" -or $actual -notmatch '::\s*text' -or $actual -notmatch '- -\s*2') { throw "punctuation changed:`n$actual" } }
    }
    $passed++
}
"$passed feedback checks passed"
