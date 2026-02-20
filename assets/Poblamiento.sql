-- Created by GitHub Copilot in SSMS - review carefully before executing

/*
    SCRIPT PURPOSE:
    Creates sample orders with random order lines for a specified customer.
    This script generates 5 orders dated in January 2026, each containing 1-5 random products.
    All operations are wrapped in a transaction to ensure data consistency.
    
    PARAMETERS:
    @CustomerId - The customer identifier for whom orders will be created
    
    PREREQUISITES:
    - Customer must exist in dbo.customer
    - Customer must have an associated account in dbo.customeraccount
    - Active products must exist in dbo.product
*/

-- ============================================================================
-- CONFIGURATION PARAMETERS
-- ============================================================================
DECLARE @CustomerId NVARCHAR(50) = 'CID-069';  -- Change this to the desired customer ID

-- ============================================================================
-- VARIABLE DECLARATIONS
-- ============================================================================
DECLARE @CustomerAccountId NVARCHAR(50);
DECLARE @OrderCount INT = 5;  -- Number of orders to create
DECLARE @MaxLinesPerOrder INT = 5;  -- Maximum order lines per order
DECLARE @ErrorMessage NVARCHAR(500);

-- Working variables for order line generation
DECLARE @CurrentOrderId NVARCHAR(50);
DECLARE @CurrentLineCount INT;
DECLARE @LineNumber INT;
DECLARE @RandomProductID INT;
DECLARE @RandomQuantity INT;
DECLARE @UnitPrice DECIMAL(18,2);
DECLARE @LineTotal DECIMAL(18,2);

-- Table variables
DECLARE @Products TABLE (ProductID INT, ProductName NVARCHAR(200), ListPrice DECIMAL(18,2));
DECLARE @OrdersToInsert TABLE (
    OrderId NVARCHAR(50),
    OrderNumber NVARCHAR(50),
    OrderDate DATE,
    LineCount INT
);

-- ============================================================================
-- START TRANSACTION
-- ============================================================================
BEGIN TRANSACTION;

BEGIN TRY
    -- ========================================================================
    -- VALIDATE CUSTOMER AND RETRIEVE ACCOUNT ID
    -- ========================================================================
    
    -- Retrieve the CustomerAccountId for the specified CustomerId
    SELECT @CustomerAccountId = ca.CustomerAccountId
    FROM dbo.customeraccount ca
    WHERE ca.CustomerId = @CustomerId;
    
    -- Validate that customer account was found
    IF @CustomerAccountId IS NULL
    BEGIN
        SET @ErrorMessage = 'Customer account not found for CustomerId: ' + @CustomerId;
        THROW 50001, @ErrorMessage, 1;
    END;
    
    -- ========================================================================
    -- LOAD AVAILABLE PRODUCTS
    -- ========================================================================
    
    -- Load top 20 active products for random selection
    INSERT INTO @Products (ProductID, ProductName, ListPrice)
    SELECT TOP 20 ProductID, ProductName, ListPrice
    FROM dbo.product
    WHERE ProductStatus = 'Active'
    ORDER BY ProductID;
    
    -- Validate that products were found
    IF NOT EXISTS (SELECT 1 FROM @Products)
    BEGIN
        THROW 50002, 'No active products found in the database', 1;
    END;
    
    -- ========================================================================
    -- GENERATE ORDER HEADERS
    -- ========================================================================
    
    -- Generate 5 orders with random dates in January 2026
    INSERT INTO @OrdersToInsert (OrderId, OrderNumber, OrderDate, LineCount)
    VALUES 
        (NEWID(), 'ORD-' + @CustomerId + '-001', DATEADD(DAY, ABS(CHECKSUM(NEWID())) % 31, '2026-01-01'), ABS(CHECKSUM(NEWID())) % @MaxLinesPerOrder + 1),
        (NEWID(), 'ORD-' + @CustomerId + '-002', DATEADD(DAY, ABS(CHECKSUM(NEWID())) % 31, '2026-01-01'), ABS(CHECKSUM(NEWID())) % @MaxLinesPerOrder + 1),
        (NEWID(), 'ORD-' + @CustomerId + '-003', DATEADD(DAY, ABS(CHECKSUM(NEWID())) % 31, '2026-01-01'), ABS(CHECKSUM(NEWID())) % @MaxLinesPerOrder + 1),
        (NEWID(), 'ORD-' + @CustomerId + '-004', DATEADD(DAY, ABS(CHECKSUM(NEWID())) % 31, '2026-01-01'), ABS(CHECKSUM(NEWID())) % @MaxLinesPerOrder + 1),
        (NEWID(), 'ORD-' + @CustomerId + '-005', DATEADD(DAY, ABS(CHECKSUM(NEWID())) % 31, '2026-01-01'), ABS(CHECKSUM(NEWID())) % @MaxLinesPerOrder + 1);
    
    -- Insert order headers with initial zero totals (will be updated after lines are added)
    INSERT INTO dbo.orders (
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
        CreatedBy
    )
    SELECT 
        OrderId,
        'Fabric' AS SalesChannelId,
        OrderNumber,
        @CustomerId AS CustomerId,
        @CustomerAccountId AS CustomerAccountId,
        OrderDate,
        'Completed' AS OrderStatus,
        0.00 AS SubTotal,
        0.00 AS TaxAmount,
        0.00 AS OrderTotal,
        'MC' AS PaymentMethod,  -- MasterCard
        'USD' AS IsoCurrencyCode,
        'SYSTEM' AS CreatedBy
    FROM @OrdersToInsert;
    
    -- ========================================================================
    -- GENERATE ORDER LINES
    -- ========================================================================
    
    -- Cursor to iterate through each order and create random lines
    DECLARE order_cursor CURSOR FOR 
    SELECT OrderId, LineCount FROM @OrdersToInsert;
    
    OPEN order_cursor;
    FETCH NEXT FROM order_cursor INTO @CurrentOrderId, @CurrentLineCount;
    
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @LineNumber = 1;
        
        -- Create specified number of lines for current order
        WHILE @LineNumber <= @CurrentLineCount
        BEGIN
            -- Select random product
            SELECT TOP 1 
                @RandomProductID = ProductID,
                @UnitPrice = ListPrice
            FROM @Products
            ORDER BY NEWID();
            
            -- Generate random quantity between 1 and 10
            SET @RandomQuantity = ABS(CHECKSUM(NEWID())) % 10 + 1;
            SET @LineTotal = @UnitPrice * @RandomQuantity;
            
            -- Insert order line
            INSERT INTO dbo.orderline (
                OrderId,
                OrderLineNumber,
                ProductId,
                Quantity,
                UnitPrice,
                LineTotal,
                DiscountAmount,
                TaxAmount,
                GoldLoadTimestamp
            )
            VALUES (
                @CurrentOrderId,
                @LineNumber,
                CAST(@RandomProductID AS NVARCHAR(50)),
                @RandomQuantity,
                @UnitPrice,
                @LineTotal,
                0.00,
                0.00,
                NULL
            );
            
            SET @LineNumber = @LineNumber + 1;
        END;
        
        FETCH NEXT FROM order_cursor INTO @CurrentOrderId, @CurrentLineCount;
    END;
    
    CLOSE order_cursor;
    DEALLOCATE order_cursor;
    
    -- ========================================================================
    -- UPDATE ORDER TOTALS
    -- ========================================================================
    
    -- Calculate and update order totals based on order lines
    UPDATE o
    SET 
        SubTotal = ISNULL(line_totals.TotalAmount, 0),
        OrderTotal = ISNULL(line_totals.TotalAmount, 0)
    FROM dbo.orders o
    INNER JOIN (
        SELECT 
            OrderId,
            SUM(LineTotal) AS TotalAmount
        FROM dbo.orderline
        WHERE OrderId IN (SELECT OrderId FROM @OrdersToInsert)
        GROUP BY OrderId
    ) line_totals ON o.OrderId = line_totals.OrderId
    WHERE o.OrderId IN (SELECT OrderId FROM @OrdersToInsert);
    
    -- ========================================================================
    -- DISPLAY RESULTS
    -- ========================================================================
    
    -- Show summary of created orders
    SELECT 
        o.OrderNumber,
        o.OrderDate,
        o.OrderStatus,
        COUNT(ol.OrderLineNumber) AS LineCount,
        o.SubTotal,
        o.OrderTotal
    FROM dbo.orders o
    LEFT JOIN dbo.orderline ol ON o.OrderId = ol.OrderId
    WHERE o.OrderId IN (SELECT OrderId FROM @OrdersToInsert)
    GROUP BY 
        o.OrderNumber,
        o.OrderDate,
        o.OrderStatus,
        o.SubTotal,
        o.OrderTotal
    ORDER BY o.OrderDate;
    
    -- ========================================================================
    -- COMMIT TRANSACTION
    -- ========================================================================
    COMMIT TRANSACTION;
    
    PRINT 'SUCCESS: ' + CAST(@OrderCount AS VARCHAR(10)) + ' orders created successfully for customer ' + @CustomerId;

END TRY
BEGIN CATCH
    -- ========================================================================
    -- ROLLBACK ON ERROR
    -- ========================================================================
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    
    -- Display error information
    DECLARE @ErrorNumber INT = ERROR_NUMBER();
    DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
    DECLARE @ErrorState INT = ERROR_STATE();
    DECLARE @ErrorProcedure NVARCHAR(128) = ISNULL(ERROR_PROCEDURE(), 'N/A');
    DECLARE @ErrorLine INT = ERROR_LINE();
    DECLARE @ErrorMsg NVARCHAR(4000) = ERROR_MESSAGE();
    
    PRINT 'ERROR: Transaction rolled back';
    PRINT 'Error Number: ' + CAST(@ErrorNumber AS VARCHAR(10));
    PRINT 'Error Message: ' + @ErrorMsg;
    PRINT 'Error Procedure: ' + @ErrorProcedure;
    PRINT 'Error Line: ' + CAST(@ErrorLine AS VARCHAR(10));
    
    -- Re-throw the error
    THROW;
END CATCH;








-- Created by GitHub Copilot in SSMS - review carefully before executing

/*
    SCRIPT PURPOSE:
    Retrieves detailed order line information for a specific customer within a date range.
    Shows order details, product information, quantities, and pricing.
*/

-- ============================================================================
-- PARAMETERS
-- ============================================================================
DECLARE @CustomerId NVARCHAR(50) = 'CID-069';  -- Customer identifier
DECLARE @StartDate DATE = '2026-01-01';        -- Start date of search range
DECLARE @EndDate DATE = '2026-01-31';          -- End date of search range

-- ============================================================================
-- QUERY: ORDER LINE DETAILS
-- ============================================================================

-- Retrieve all order lines with product details for the specified customer and date range
SELECT 
    o.OrderNumber,
    o.OrderDate,    
    ol.OrderLineNumber,
    p.ProductName,
    p.BrandName,
    p.CategoryName,
    ol.Quantity,
    ol.UnitPrice,
    ol.LineTotal
FROM dbo.orders o
INNER JOIN dbo.orderline ol ON o.OrderId = ol.OrderId
INNER JOIN dbo.product p ON ol.ProductId = CAST(p.ProductID AS NVARCHAR(50))
WHERE o.CustomerId = @CustomerId
  AND o.OrderDate >= @StartDate
  AND o.OrderDate <= @EndDate
ORDER BY 
    o.OrderDate,
    o.OrderNumber,
    ol.OrderLineNumber;


    select * from orders where orders.OrderNumber='F100401'
    select top 10 * from orders

    select orders.OrderNumber from orders where orders.CustomerId='CID-069'