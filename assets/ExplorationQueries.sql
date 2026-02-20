SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
ORDER BY TABLE_NAME;


select * from customerrelationshiptype

select top 10 * from hst_conversations

select top 10 * from customer


select * from customer where CustomerId = 'CID-009'


SELECT 
    OrderId,
    OrderNumber,
    OrderDate,
    OrderStatus,
    OrderTotal
FROM Orders
WHERE CustomerId = 'CID-009'
ORDER BY OrderDate DESC;



SELECT TOP 5
    CustomerId,
    COUNT(OrderId) AS NumOrders
FROM Orders
GROUP BY CustomerId
ORDER BY NumOrders DESC;


SELECT pc.CategoryName, SUM(ol.Quantity * ol.UnitPrice) AS TotalSpent FROM Orders o INNER JOIN OrderLine ol ON o.OrderId = ol.OrderId INNER JOIN Product p ON ol.ProductId = p.ProductID INNER JOIN ProductCategory pc ON p.ProductCategoryID = pc.CategoryID WHERE o.CustomerId = 'CID-069' GROUP BY pc.CategoryName ORDER BY TotalSpent DESC;



SELECT
    COALESCE(p.CategoryName, 'Sin categoría') AS CategoryName,
    SUM(
        COALESCE(ol.LineTotal, 0)
        - COALESCE(ol.DiscountAmount, 0)
        + COALESCE(ol.TaxAmount, 0)
    ) AS TotalSpent
FROM orders o
INNER JOIN orderline ol
    ON ol.OrderId = o.OrderId
INNER JOIN product p
    ON p.ProductID = ol.ProductId
WHERE o.CustomerId = 'CID-069'
GROUP BY COALESCE(p.CategoryName, 'Sin categoría')
ORDER BY TotalSpent DESC;


SELECT
    p.CategoryName AS CategoryName,
    SUM(COALESCE(ol.LineTotal, 0)) AS BaseTotalSpent
FROM orders o
INNER JOIN orderline ol
    ON ol.OrderId = o.OrderId
INNER JOIN product p
    ON p.ProductID = ol.ProductId
WHERE o.CustomerId = 'CID-069'
GROUP BY p.CategoryName
ORDER BY BaseTotalSpent DESC;



SELECT
    o.OrderId,
    o.OrderNumber,
    ol.OrderLineNumber,
    ol.ProductId,
    ol.Quantity,
    ol.UnitPrice,
    ol.LineTotal,
    (ol.Quantity * ol.UnitPrice) AS CalcBase,
    (COALESCE(ol.LineTotal, 0) - COALESCE(ol.Quantity, 0) * COALESCE(ol.UnitPrice, 0)) AS Diff,
    p.ProductCategoryID,
    pc.CategoryID,
    pc.CategoryName
FROM Orders o
INNER JOIN OrderLine ol
    ON o.OrderId = ol.OrderId
INNER JOIN Product p
    ON ol.ProductId = p.ProductID
LEFT JOIN ProductCategory pc
    ON p.ProductCategoryID = pc.CategoryID
WHERE o.CustomerId = 'CID-069'
  AND (
        ol.LineTotal IS NULL
        OR ol.Quantity IS NULL
        OR ol.UnitPrice IS NULL
        OR ABS(COALESCE(ol.LineTotal, 0) - (COALESCE(ol.Quantity, 0) * COALESCE(ol.UnitPrice, 0))) > 0.0001
        OR pc.CategoryID IS NULL
      )
ORDER BY ABS(COALESCE(ol.LineTotal, 0) - (COALESCE(ol.Quantity, 0) * COALESCE(ol.UnitPrice, 0))) DESC;




-- Fechas mínima y máxima + información de la orden y su CustomerId

WITH MinMax AS (
    SELECT 
        MIN(OrderDate) AS MinOrderDate,
        MAX(OrderDate) AS MaxOrderDate
    FROM orders
)

SELECT 
    'MIN_DATE' AS RecordType,
    o.OrderId,
    o.OrderNumber,
    o.CustomerId,
    o.OrderDate
FROM orders o
JOIN MinMax mm ON o.OrderDate = mm.MinOrderDate

UNION ALL

SELECT 
    'MAX_DATE' AS RecordType,
    o.OrderId,
    o.OrderNumber,
    o.CustomerId,
    o.OrderDate
FROM orders o
JOIN MinMax mm ON o.OrderDate = mm.MaxOrderDate;



-- Created by GitHub Copilot in SSMS - review carefully before executing

-- Obtener la orden más antigua y más reciente con sus CustomerIds
SELECT 
    'Orden más antigua' AS Tipo,
    MIN(OrderDate) AS Fecha,
    (SELECT TOP 1 CustomerId 
     FROM dbo.orders 
     WHERE OrderDate = (SELECT MIN(OrderDate) FROM dbo.orders)
     ORDER BY OrderId) AS CustomerId,
    (SELECT TOP 1 OrderId 
     FROM dbo.orders 
     WHERE OrderDate = (SELECT MIN(OrderDate) FROM dbo.orders)
     ORDER BY OrderId) AS OrderId

UNION ALL

SELECT 
    'Orden más reciente' AS Tipo,
    MAX(OrderDate) AS Fecha,
    (SELECT TOP 1 CustomerId 
     FROM dbo.orders 
     WHERE OrderDate = (SELECT MAX(OrderDate) FROM dbo.orders)
     ORDER BY OrderId DESC) AS CustomerId,
    (SELECT TOP 1 OrderId 
     FROM dbo.orders 
     WHERE OrderDate = (SELECT MAX(OrderDate) FROM dbo.orders)
     ORDER BY OrderId DESC) AS OrderId
FROM dbo.orders;


--Customers with more orders
-----------------------------------------------------------
SELECT
    CustomerId,
    COUNT(*) AS TotalOrders
FROM orders
GROUP BY CustomerId
ORDER BY TotalOrders DESC;

/*
CID-069
CID-281
CID-287
CID-335
CID-356
CID-436
CID-439
CID-503
*/



--Rango de fechas de ordenes para un CustomerId específico
-----------------------------------------------------------
DECLARE @CustomerId NVARCHAR(50) = 'CID-069';

SELECT  
    MIN(OrderDate) AS MinOrderDate,
    MAX(OrderDate) AS MaxOrderDate
FROM orders
WHERE CustomerId = @CustomerId;
------------------------------------------------------------

/* =========================================================
   Seed de 5 órdenes para el cliente CUD069
   - 1 a 5 líneas aleatorias por orden
   - Productos y cantidades aleatorias
   - Sin impuestos ni descuentos
   - Fechas aleatorias en enero de 2026
   - Calcula SubTotal y OrderTotal (= SubTotal)
   ========================================================= */

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    ---------------------------------------------------------
    -- Parámetros
    ---------------------------------------------------------
    DECLARE @CustomerId NVARCHAR(100) = N'CUD069';
    DECLARE @IsoCurrencyCode NVARCHAR(10) = N'USD';
    DECLARE @CreatedBy NVARCHAR(100) = N'seed-script';
    DECLARE @Now DATETIME2 = SYSDATETIME();

    ---------------------------------------------------------
    -- Validación: pool de productos disponible
    ---------------------------------------------------------
    IF NOT EXISTS (SELECT 1 FROM dbo.product)
    BEGIN
        RAISERROR(N'No hay productos en la tabla [product]. Carga productos antes de ejecutar el seed.', 16, 1);
        ROLLBACK TRAN;
        RETURN;
    END

    ---------------------------------------------------------
    -- Opcional: si no existiera el cliente, puedes crear un placeholder
    -- (descomenta si tu esquema exige FK estricto).
    ---------------------------------------------------------
    /*
    IF NOT EXISTS (SELECT 1 FROM dbo.customer WHERE CustomerId = @CustomerId)
    BEGIN
        INSERT INTO dbo.customer
        (CustomerId, CustomerTypeId, CustomerRelationshipTypeId, DateOfBirth, CustomerEstablishedDate,
         IsActive, FirstName, LastName, Gender, PrimaryPhone, SecondaryPhone,
         PrimaryEmail, SecondaryEmail, CreatedBy, UpdatedBy, GoldLoadTimestamp)
        VALUES
        (@CustomerId, N'RETAIL', N'END', NULL, NULL,
         1, N'CUD', N'069', N'N/A', NULL, NULL,
         N'cud069@example.com', NULL, @CreatedBy, NULL, @Now);
    END
    */

    ---------------------------------------------------------
    -- Preparamos 5 fechas aleatorias dentro de Enero de 2026
    -- y 5 OrderIds/OrderNumbers únicos.
    ---------------------------------------------------------
    DECLARE @Orders TABLE
    (
        OrderId           NVARCHAR(100) PRIMARY KEY,
        OrderNumber       NVARCHAR(100),
        OrderDate         DATE
    );

    WITH n AS
    (
        SELECT 1 AS i UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5
    )
    INSERT INTO @Orders (OrderId, OrderNumber, OrderDate)
    SELECT
        CONCAT('ORD-', FORMAT(@Now, 'yyyyMMddHHmmss'), '-', i)                                      AS OrderId,
        CONCAT('SO-', FORMAT(@Now, 'yyyyMM'), '-', RIGHT(CONVERT(VARCHAR(12), ABS(CHECKSUM(NEWID()))), 4)) AS OrderNumber,
        DATEFROMPARTS(2026, 1, 1 + (ABS(CHECKSUM(NEWID())) % 31))                                   AS OrderDate
    FROM n
    OPTION (MAXRECURSION 0);

    ---------------------------------------------------------
    -- Insertamos las 5 órdenes (headers) en dbo.orders
    -- (campos clave del esquema: ver Base de Datos.loop)
    ---------------------------------------------------------
    INSERT INTO dbo.orders
    (
        OrderId,
        SalesChannelId,
        OrderNumber,
        CustomerId,
        CustomerAccountId,
        OrderDate,
        OrderStatus,
        SubTotal,
        TaxAmount,
        OrderTotal,
        PaymentMethod,
        IsoCurrencyCode,
        CreatedBy,
        GoldLoadTimestamp
    )
    SELECT
        o.OrderId,
        NULL AS SalesChannelId,
        o.OrderNumber,
        @CustomerId AS CustomerId,
        NULL AS CustomerAccountId,
        o.OrderDate,
        N'Created' AS OrderStatus,
        0.00 AS SubTotal,
        0.00 AS TaxAmount,
        0.00 AS OrderTotal,
        N'N/A' AS PaymentMethod,
        @IsoCurrencyCode AS IsoCurrencyCode,
        @CreatedBy AS CreatedBy,
        @Now AS GoldLoadTimestamp
    FROM @Orders o;
    -- (orders: OrderId, OrderNumber, CustomerId, OrderDate, OrderStatus, SubTotal, TaxAmount, OrderTotal, IsoCurrencyCode, CreatedBy...) [1](https://loop.cloud.microsoft/p/eyJ1IjoiaHR0cHM6Ly9taWNyb3NvZnQuc2hhcmVwb2ludC5jb20vY29udGVudHN0b3JhZ2UvQ1NQX2FmZjZhMWY2LTcwNTYtNDU1OC1hNDEwLTA5YmNhOTFjOGFkYT9uYXY9Y3owbE1rWmpiMjUwWlc1MGMzUnZjbUZuWlNVeVJrTlRVQ1UxUm1GbVpqWmhNV1kySlRKRU56QTFOaVV5UkRRMU5UZ2xNa1JoTkRFd0pUSkVNRGxpWTJFNU1XTTRZV1JoSm1ROVlpVXlNVGx4U0RKeU1WcDNWMFZYYTBWQmJUaHhVbmxMTW0xUU5Hd3djM0JVVlRsQmNFRkpRM1pKUTBGblpVcHBVamczU25ZMGEyVlVObGxuU2tGbFNURk5RakFtWmowd01VSTNSRTVMVEUxRlEwNWFWbGRLU2taUFRrWkpURU5CUmxsS1ZVcFlVVkUxSm1NOUpUSkcifQ%3D%3D)

    ---------------------------------------------------------
    -- Para cada orden, creamos entre 1 y 5 líneas con:
    --  - productos aleatorios (distintos por orden)
    --  - cantidad aleatoria (1..5)
    --  - UnitPrice desde product.ListPrice
    ---------------------------------------------------------
    ;WITH ProductPool AS
    (
        SELECT TOP (2000)  -- limita el pool si hay muchísimos productos
               p.ProductID,
               p.ProductName,
               p.ListPrice
        FROM dbo.product p
        WHERE p.ListPrice IS NOT NULL
        ORDER BY p.ProductID
    )
    INSERT INTO dbo.orderline
    (
        OrderId,
        OrderLineNumber,
        ProductId,      -- nvarchar en orderline (convertimos el int de product)
        Quantity,
        UnitPrice,
        LineTotal,
        DiscountAmount,
        TaxAmount,
        GoldLoadTimestamp
    )
    SELECT
        o.OrderId,
        lines.OrderLineNumber,
        CONVERT(NVARCHAR(50), lines.ProductID) AS ProductId,
        lines.Qty AS Quantity,
        lines.UnitPrice,
        CAST(lines.Qty * lines.UnitPrice AS DECIMAL(18,2)) AS LineTotal,
        0.00 AS DiscountAmount,
        0.00 AS TaxAmount,
        CONVERT(NVARCHAR(30), @Now, 126) AS GoldLoadTimestamp
    FROM @Orders o
    CROSS APPLY
    (
        -- Elegimos cuántas líneas tendrá esta orden (1..5)
        SELECT 1 + ABS(CHECKSUM(NEWID())) % 5 AS LineCount
    ) lc
    CROSS APPLY
    (
        -- Tomamos "LineCount" productos aleatorios, numerados 1..N
        SELECT TOP (lc.LineCount)
               ROW_NUMBER() OVER (ORDER BY NEWID())                 AS OrderLineNumber,
               pp.ProductID,
               pp.ListPrice                                         AS UnitPrice,
               (1 + ABS(CHECKSUM(NEWID())) % 5)                      AS Qty
        FROM ProductPool pp
        ORDER BY NEWID()
    ) lines;
    -- (orderline: OrderId, OrderLineNumber, ProductId[nvarchar], Quantity, UnitPrice, LineTotal, DiscountAmount, TaxAmount, GoldLoadTimestamp) [1](https://loop.cloud.microsoft/p/eyJ1IjoiaHR0cHM6Ly9taWNyb3NvZnQuc2hhcmVwb2ludC5jb20vY29udGVudHN0b3JhZ2UvQ1NQX2FmZjZhMWY2LTcwNTYtNDU1OC1hNDEwLTA5YmNhOTFjOGFkYT9uYXY9Y3owbE1rWmpiMjUwWlc1MGMzUnZjbUZuWlNVeVJrTlRVQ1UxUm1GbVpqWmhNV1kySlRKRU56QTFOaVV5UkRRMU5UZ2xNa1JoTkRFd0pUSkVNRGxpWTJFNU1XTTRZV1JoSm1ROVlpVXlNVGx4U0RKeU1WcDNWMFZYYTBWQmJUaHhVbmxMTW0xUU5Hd3djM0JVVlRsQmNFRkpRM1pKUTBGblpVcHBVamczU25ZMGEyVlVObGxuU2tGbFNURk5RakFtWmowd01VSTNSRTVMVEUxRlEwNWFWbGRLU2taUFRrWkpURU5CUmxsS1ZVcFlVVkUxSm1NOUpUSkcifQ%3D%3D)

    ---------------------------------------------------------
    -- Recalcular totales del header:
    -- SubTotal = SUM(LineTotal); TaxAmount = 0; OrderTotal = SubTotal
    ---------------------------------------------------------
    ;WITH S AS
    (
        SELECT ol.OrderId, SUM(ol.LineTotal) AS SubTotal
        FROM dbo.orderline ol
        WHERE ol.OrderId IN (SELECT OrderId FROM @Orders)
        GROUP BY ol.OrderId
    )
    UPDATE o
    SET o.SubTotal   = s.SubTotal,
        o.TaxAmount  = 0.00,
        o.OrderTotal = s.SubTotal
    FROM dbo.orders o
    JOIN S s ON s.OrderId = o.OrderId;

    ---------------------------------------------------------
    -- Resultado de verificación
    ---------------------------------------------------------
    SELECT o.OrderId, o.OrderNumber, o.CustomerId, o.OrderDate, o.OrderStatus,
           o.SubTotal, o.TaxAmount, o.OrderTotal, o.IsoCurrencyCode
    FROM dbo.orders o
    WHERE o.OrderId IN (SELECT OrderId FROM @Orders)
    ORDER BY o.OrderDate, o.OrderId;

    SELECT ol.OrderId, ol.OrderLineNumber, ol.ProductId, ol.Quantity, ol.UnitPrice, ol.LineTotal
    FROM dbo.orderline ol
    WHERE ol.OrderId IN (SELECT OrderId FROM @Orders)
    ORDER BY ol.OrderId, ol.OrderLineNumber;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    DECLARE @Msg NVARCHAR(4000) = ERROR_MESSAGE();
    RAISERROR(@Msg, 16, 1);
END CATCH;




/* ============================================================
   CONSULTA: Órdenes y fechas de creación para un CustomerId
   MODELO USADO: Tabla 'orders' del Lakehouse
   CAMPOS REFERENCIADOS:
      - OrderId
      - OrderNumber
      - CustomerId
      - OrderDate
      - OrderStatus
      - OrderTotal
      - IsoCurrencyCode
   ============================================================ */

-- Declaración del parámetro del cliente (CustomerId)
DECLARE @CustomerId NVARCHAR(50) = 'CID-069';

-- Consulta principal: lista todas las órdenes del cliente
SELECT
    o.OrderId,             -- Identificador único de la orden
    o.OrderNumber,         -- Número de orden visible para negocio
    o.OrderDate,           -- Fecha de creación / registro de la orden
    o.OrderStatus,         -- Estado actual de la orden
    o.OrderTotal,          -- Monto total de la orden
    o.IsoCurrencyCode      -- Moneda asociada a la orden
FROM orders o
WHERE o.CustomerId = @CustomerId   -- Filtra únicamente órdenes del cliente solicitado
ORDER BY 
    o.OrderDate ASC,               -- Orden cronológico: más antigua → más reciente
    o.OrderId ASC;                 -- Desempate estable si hay fechas iguales



-- Parámetros
-- @CustomerId   NVARCHAR(100)
-- @DateStart    DATE
-- @DateEnd      DATE


DECLARE @CustomerId NVARCHAR(50) = 'CUST-009';
DECLARE @DateStart  DATE = '2026-01-01';
DECLARE @DateEnd    DATE = '2026-01-31';


WITH orders_sel AS (
    SELECT 
        o.OrderId,
        o.OrderNumber,
        o.OrderDate,
        o.OrderStatus,
        o.OrderTotal,
        o.IsoCurrencyCode,
        i.InvoiceId,
        i.InvoiceNumber,
        i.InvoiceDate,
        i.TotalAmount AS InvoiceTotal,
        i.InvoiceStatus
    FROM orders o
    LEFT JOIN invoice i 
        ON i.OrderId = o.OrderId
    WHERE 
        o.CustomerId = @CustomerId
        AND o.OrderDate >= @DateStart
        AND o.OrderDate <= @DateEnd
),

lines AS (
    SELECT
        ol.OrderId,
        ol.OrderLineNumber,
        ol.ProductId,
        p.ProductName,
        ol.UnitPrice,
        ol.Quantity,
        (ol.UnitPrice * ol.Quantity) AS LineTotal
    FROM orderline ol
    LEFT JOIN product p
        ON p.ProductID = ol.ProductId
    WHERE 
        ol.OrderId IN (SELECT OrderId FROM orders_sel)
),

payments AS (
    SELECT
        pmt.InvoiceId,
        pmt.PaymentNumber,
        pmt.PaymentDate,
        pmt.PaymentAmount,
        pmt.PaymentStatus
    FROM payment pmt
    WHERE 
        pmt.InvoiceId IN (SELECT InvoiceId FROM orders_sel)
)

SELECT 
    os.OrderId,
    os.OrderNumber,
    os.OrderDate,
    os.OrderStatus,
    os.OrderTotal,
    os.IsoCurrencyCode,

    -- Factura
    os.InvoiceId,
    os.InvoiceNumber,
    os.InvoiceDate,
    os.InvoiceTotal,
    os.InvoiceStatus,

    -- Líneas
    ln.OrderLineNumber,
    ln.ProductId,
    ln.ProductName,
    ln.UnitPrice,
    ln.Quantity,
    ln.LineTotal,

    -- Pagos
    pmt.PaymentNumber,
    pmt.PaymentDate,
    pmt.PaymentAmount,
    pmt.PaymentStatus

FROM orders_sel os
LEFT JOIN lines ln
    ON ln.OrderId = os.OrderId
LEFT JOIN payments pmt
    ON pmt.InvoiceId = os.InvoiceId

ORDER BY 
    os.OrderDate DESC,
    os.OrderId DESC,
    ln.OrderLineNumber ASC;




    -- Created by GitHub Copilot in SSMS - review carefully before executing

-- Obtener la orden más antigua y más reciente con sus CustomerIds
WITH MinMaxDates AS (
    SELECT 
        MIN(OrderDate) AS MinDate,
        MAX(OrderDate) AS MaxDate
    FROM dbo.orders
),
OldestOrder AS (
    SELECT TOP 1
        'Orden más antigua' AS Tipo,
        OrderDate AS Fecha,
        CustomerId,
        OrderId
    FROM dbo.orders
    CROSS JOIN MinMaxDates
    WHERE OrderDate = MinMaxDates.MinDate
    ORDER BY OrderId
),
NewestOrder AS (
    SELECT TOP 1
        'Orden más reciente' AS Tipo,
        OrderDate AS Fecha,
        CustomerId,
        OrderId
    FROM dbo.orders
    CROSS JOIN MinMaxDates
    WHERE OrderDate = MinMaxDates.MaxDate
    ORDER BY OrderId DESC
)
SELECT * FROM OldestOrder
UNION ALL
SELECT * FROM NewestOrder;