# Contoso Retail Database Documentation
## ER
This is the ER diagram of the Contoso Retail database. It contains the main tables with their relationship fields to better understand the system.

``` mermaid
---
config:
  look: neo
  theme: neo-dark
---
erDiagram
    CUSTOMER {
        nvarchar CustomerId
        nvarchar FirstName
        nvarchar LastName
    }
    ACCOUNT {
        nvarchar AccountId
        nvarchar CustomerId
        nvarchar AccountNumber
    }
    CUSTOMERACCOUNT {
        nvarchar CustomerAccountId
        nvarchar CustomerId
        nvarchar ParentAccountId
    }
    ORDERS {
        nvarchar OrderId
        nvarchar CustomerId
        nvarchar CustomerAccountId
    }
    ORDERLINE {
        nvarchar OrderId
        int OrderLineNumber
        nvarchar ProductId
    }
    INVOICE {
        nvarchar InvoiceId
        nvarchar CustomerId
        nvarchar OrderId
    }
    PAYMENT {
        nvarchar PaymentId
        nvarchar InvoiceId
        nvarchar OrderId
    }
    PRODUCT {
        int ProductID
        nvarchar ProductCategoryID
        nvarchar ProductName
    }
    PRODUCTCATEGORY {
        int CategoryID
        nvarchar ParentCategoryId
        nvarchar CategoryName
    }
    LOCATION {
        nvarchar LocationId
        nvarchar CustomerId
        nvarchar LocationName
    }
    CUSTOMERRELATIONSHIPTYPE {
        nvarchar CustomerRelationshipTypeId
        nvarchar CustomerRelationshipTypeName
    }
    CUSTOMERTRADENAME {
        nvarchar CustomerId
        nvarchar CustomerTypeId
        nvarchar TradeNameId
        nvarchar TradeName
    }

    CUSTOMER ||--o{ ACCOUNT : "has"
    CUSTOMER ||--o{ CUSTOMERACCOUNT : "owns"
    CUSTOMER ||--o{ ORDERS : "places"
    CUSTOMER ||--o{ INVOICE : "receives"
    CUSTOMER ||--o{ LOCATION : "located at"
    CUSTOMER ||--o{ CUSTOMERTRADENAME : "uses tradename"
    CUSTOMERRELATIONSHIPTYPE ||--o{ CUSTOMER : "defines relationship"
    ORDERS ||--o{ ORDERLINE : "contains"
    ORDERLINE }o--|| PRODUCT : "refers to"
    PRODUCTCATEGORY ||--o{ PRODUCT : "categorizes"
    ORDERS ||--o{ INVOICE : "generates"
    INVOICE ||--o{ PAYMENT : "paid by"
```
## Tables
### `account`

| Column            | Type      |
| ----------------- | --------- |
| AccountId         | nvarchar  |
| AccountNumber     | nvarchar  |
| CustomerId        | nvarchar  |
| AccountType       | nvarchar  |
| AccountStatus     | nvarchar  |
| CreatedDate       | date      |
| CreatedBy         | nvarchar  |
| GoldLoadTimestamp | datetime2 |

------

### `customer`

| Column                     | Type      |
| -------------------------- | --------- |
| CustomerId                 | nvarchar  |
| CustomerTypeId             | nvarchar  |
| CustomerRelationshipTypeId | nvarchar  |
| DateOfBirth                | date      |
| CustomerEstablishedDate    | date      |
| IsActive                   | bit       |
| FirstName                  | nvarchar  |
| LastName                   | nvarchar  |
| Gender                     | nvarchar  |
| PrimaryPhone               | nvarchar  |
| SecondaryPhone             | nvarchar  |
| PrimaryEmail               | nvarchar  |
| SecondaryEmail             | nvarchar  |
| CreatedBy                  | nvarchar  |
| UpdatedBy                  | nvarchar  |
| GoldLoadTimestamp          | datetime2 |

------

### `customeraccount`

| Column              | Type      |
| ------------------- | --------- |
| CustomerAccountId   | nvarchar  |
| ParentAccountId     | nvarchar  |
| CustomerAccountName | nvarchar  |
| CustomerId          | nvarchar  |
| IsoCurrencyCode     | nvarchar  |
| UpdatedBy           | nvarchar  |
| GoldLoadTimestamp   | datetime2 |

------

### `customerrelationshiptype`

| Column                              | Type      |
| ----------------------------------- | --------- |
| CustomerRelationshipTypeId          | nvarchar  |
| CustomerRelationshipTypeName        | nvarchar  |
| CustomerRelationshipTypeDescription | nvarchar  |
| GoldLoadTimestamp                   | datetime2 |

------

### `customertradename`

| Column                | Type      |
| --------------------- | --------- |
| CustomerId            | nvarchar  |
| CustomerTypeId        | nvarchar  |
| TradeNameId           | nvarchar  |
| TradeName             | nvarchar  |
| PeriodStartDate       | nvarchar  |
| PeriodEndDate         | nvarchar  |
| CustomerTradeNameNote | nvarchar  |
| UpdatedBy             | nvarchar  |
| GoldLoadTimestamp     | datetime2 |

------

### `invoice`

| Column            | Type      |
| ----------------- | --------- |
| InvoiceId         | nvarchar  |
| InvoiceNumber     | nvarchar  |
| CustomerId        | nvarchar  |
| OrderId           | nvarchar  |
| InvoiceDate       | date      |
| DueDate           | date      |
| SubTotal          | decimal   |
| TaxAmount         | decimal   |
| TotalAmount       | decimal   |
| InvoiceStatus     | nvarchar  |
| CreatedBy         | nvarchar  |
| GoldLoadTimestamp | datetime2 |

------

### `location`

| Column            | Type      |
| ----------------- | --------- |
| LocationId        | nvarchar  |
| CustomerId        | nvarchar  |
| LocationName      | nvarchar  |
| IsActive          | bit       |
| AddressLine1      | nvarchar  |
| AddressLine2      | nvarchar  |
| City              | nvarchar  |
| StateId           | nvarchar  |
| ZipCode           | int       |
| CountryId         | nvarchar  |
| SubdivisionName   | nvarchar  |
| Region            | nvarchar  |
| Latitude          | decimal   |
| Longitude         | decimal   |
| Note              | nvarchar  |
| UpdatedBy         | nvarchar  |
| GoldLoadTimestamp | datetime2 |

------

### `orderline`

| Column            | Type     |
| ----------------- | -------- |
| OrderId           | nvarchar |
| OrderLineNumber   | int      |
| ProductId         | nvarchar |
| Quantity          | decimal  |
| UnitPrice         | decimal  |
| LineTotal         | decimal  |
| DiscountAmount    | decimal  |
| TaxAmount         | decimal  |
| GoldLoadTimestamp | nvarchar |

------

### `orderpayment`

| Column            | Type      |
| ----------------- | --------- |
| OrderId           | nvarchar  |
| PaymentMethod     | nvarchar  |
| TransactionId     | nvarchar  |
| GoldLoadTimestamp | datetime2 |

------

### `orders`

| Column            | Type      |
| ----------------- | --------- |
| OrderId           | nvarchar  |
| SalesChannelId    | nvarchar  |
| OrderNumber       | nvarchar  |
| CustomerId        | nvarchar  |
| CustomerAccountId | nvarchar  |
| OrderDate         | date      |
| OrderStatus       | nvarchar  |
| SubTotal          | decimal   |
| TaxAmount         | decimal   |
| OrderTotal        | decimal   |
| PaymentMethod     | nvarchar  |
| IsoCurrencyCode   | nvarchar  |
| CreatedBy         | nvarchar  |
| GoldLoadTimestamp | datetime2 |

------

### `payment`

| Column            | Type      |
| ----------------- | --------- |
| PaymentId         | nvarchar  |
| PaymentNumber     | nvarchar  |
| InvoiceId         | nvarchar  |
| OrderId           | nvarchar  |
| PaymentDate       | date      |
| PaymentAmount     | decimal   |
| PaymentStatus     | nvarchar  |
| PaymentMethod     | nvarchar  |
| CreatedBy         | nvarchar  |
| GoldLoadTimestamp | datetime2 |

------

### `product`

| Column             | Type      |
| ------------------ | --------- |
| ProductID          | int       |
| ProductName        | nvarchar  |
| ProductDescription | nvarchar  |
| BrandName          | nvarchar  |
| ProductNumber      | nvarchar  |
| Color              | nvarchar  |
| ProductModel       | nvarchar  |
| ProductCategoryID  | nvarchar  |
| CategoryName       | nvarchar  |
| ListPrice          | decimal   |
| StandardCost       | decimal   |
| Weight             | nvarchar  |
| WeightUom          | nvarchar  |
| ProductStatus      | nvarchar  |
| CreatedDate        | nvarchar  |
| SellStartDate      | nvarchar  |
| SellEndDate        | nvarchar  |
| IsoCurrencyCode    | nvarchar  |
| UpdatedDate        | nvarchar  |
| CreatedBy          | nvarchar  |
| UpdatedBy          | nvarchar  |
| GoldLoadTimestamp  | datetime2 |

------

### `productcategory`

| Column              | Type      |
| ------------------- | --------- |
| CategoryID          | int       |
| ParentCategoryId    | nvarchar  |
| CategoryName        | nvarchar  |
| CategoryDescription | nvarchar  |
| BrandName           | nvarchar  |
| BrandLogoUrl        | nvarchar  |
| IsActive            | bit       |
| GoldLoadTimestamp   | datetime2 |

