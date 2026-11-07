/*
    ORDER INVENTORY MANAGEMENT — SQL Server reporting queries
    Status is stored as nvarchar(20) by OrderConfiguration.
*/

-- 1. Top 5 customers by spend in the last 30 days.
-- Only Confirmed and Shipped orders contribute to revenue.
SELECT TOP (5)
    u.Id AS CustomerId,
    u.FullName AS CustomerName,
    u.Email,
    SUM(o.Total) AS TotalSpend,
    COUNT_BIG(*) AS TotalOrders
FROM dbo.Orders AS o
INNER JOIN dbo.Users AS u ON u.Id = o.CustomerId
WHERE o.Status IN (N'Confirmed', N'Shipped')
  AND o.CreatedAt >= DATEADD(DAY, -30, SYSUTCDATETIME())
GROUP BY u.Id, u.FullName, u.Email
ORDER BY SUM(o.Total) DESC, u.Id;


-- 2. Products that have never appeared in an order.
SELECT p.Id, p.Sku, p.Name, p.Price, p.StockQuantity
FROM dbo.Products AS p
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.OrderItems AS oi
    WHERE oi.ProductId = p.Id
);


-- 3. Monthly revenue for the current UTC calendar year, including zero months,
-- with a year-to-date running total.
;WITH Months AS
(
    SELECT 1 AS RevenueMonth
    UNION ALL
    SELECT RevenueMonth + 1
    FROM Months
    WHERE RevenueMonth < 12
),
MonthlyRevenue AS
(
    SELECT
        MONTH(o.CreatedAt) AS RevenueMonth,
        SUM(o.Total) AS MonthlyRevenue
    FROM dbo.Orders AS o
    WHERE o.Status IN (N'Confirmed', N'Shipped')
      AND o.CreatedAt >= DATEFROMPARTS(YEAR(SYSUTCDATETIME()), 1, 1)
      AND o.CreatedAt < DATEFROMPARTS(YEAR(SYSUTCDATETIME()) + 1, 1, 1)
    GROUP BY MONTH(o.CreatedAt)
),
RevenueByMonth AS
(
    SELECT m.RevenueMonth,
           COALESCE(r.MonthlyRevenue, CONVERT(decimal(18, 2), 0)) AS MonthlyRevenue
    FROM Months AS m
    LEFT JOIN MonthlyRevenue AS r ON r.RevenueMonth = m.RevenueMonth
)
SELECT
    RevenueMonth,
    MonthlyRevenue,
    SUM(MonthlyRevenue) OVER
    (
        ORDER BY RevenueMonth
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS RunningTotal
FROM RevenueByMonth
ORDER BY RevenueMonth
OPTION (MAXRECURSION 12);


-- 4. Second-highest DISTINCT order total for each customer.
-- DENSE_RANK means ties at the highest amount do not count as second-highest.
;WITH RankedOrders AS
(
    SELECT
        o.CustomerId,
        o.Total,
        DENSE_RANK() OVER
        (
            PARTITION BY o.CustomerId
            ORDER BY o.Total DESC
        ) AS AmountRank
    FROM dbo.Orders AS o
)
SELECT
    u.Id AS CustomerId,
    u.FullName AS CustomerName,
    MAX(r.Total) AS SecondHighestOrderTotal
FROM RankedOrders AS r
INNER JOIN dbo.Users AS u ON u.Id = r.CustomerId
WHERE r.AmountRank = 2
GROUP BY u.Id, u.FullName
ORDER BY u.Id;


-- 5. Products whose current stock is below their average quantity
-- sold per order. First aggregate quantity by product and order so that
-- duplicate order-item rows (if introduced by future imports) count as one order.
;WITH QuantityPerOrder AS
(
    SELECT
        oi.ProductId,
        oi.OrderId,
        SUM(oi.Quantity) AS QuantitySold
    FROM dbo.OrderItems AS oi
    GROUP BY oi.ProductId, oi.OrderId
)
SELECT
    p.Id,
    p.Sku,
    p.Name,
    p.StockQuantity,
    AVG(CONVERT(decimal(18, 2), q.QuantitySold)) AS AverageQuantitySoldPerOrder
FROM dbo.Products AS p
INNER JOIN QuantityPerOrder AS q ON q.ProductId = p.Id
GROUP BY p.Id, p.Sku, p.Name, p.StockQuantity
HAVING p.StockQuantity <
    AVG(CONVERT(decimal(18, 2), q.QuantitySold));
