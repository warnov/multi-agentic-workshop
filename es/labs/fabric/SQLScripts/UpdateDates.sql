-- Step 1: Calculate days difference
DECLARE @today DATE = CAST(GETDATE() AS DATE);
DECLARE @max_start_time DATE;
DECLARE @days_difference INT;

SELECT @max_start_time = MAX(OrderDate)
FROM dbo.orders;

SET @days_difference = 
    CASE 
        WHEN @max_start_time IS NOT NULL 
        THEN DATEDIFF(DAY, @max_start_time, @today) - 1
        ELSE 0
    END;

-- Step 2: Update tables with adjusted dates
UPDATE dbo.orders
SET OrderDate = CAST(DATEADD(DAY, @days_difference, OrderDate) AS DATE);

UPDATE dbo.invoice
SET InvoiceDate = CAST(DATEADD(DAY, @days_difference, InvoiceDate) AS DATE),
    DueDate     = CAST(DATEADD(DAY, @days_difference, DueDate) AS DATE);

UPDATE dbo.payment
SET PaymentDate = CAST(DATEADD(DAY, @days_difference, PaymentDate) AS DATE);

UPDATE dbo.customer
SET CustomerEstablishedDate = CAST(DATEADD(DAY, @days_difference, CustomerEstablishedDate) AS DATE);

UPDATE dbo.account
SET CreatedDate = CAST(DATEADD(DAY, @days_difference, CreatedDate) AS DATE);