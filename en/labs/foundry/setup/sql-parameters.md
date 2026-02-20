# How to obtain the SQL Database parameters for our Agents?

If you are following all the workshop labs in order, these values come from the Fabric environment deployed in [Lab 1](../../fabric/lab01-data-setup.md). Otherwise, use the endpoint and identifier of your own database.

## Obtaining the parameters from Microsoft Fabric

- In your Fabric Workspace, open the database and copy the SQL **connection string**. You will see something like:

```text
Data Source=xxxxx.database.fabric.microsoft.com,1433;Initial Catalog=retail_sqldatabase_xxx;...
```

- Mapping:

  - `FabricWarehouseSqlEndpoint` = `Data Source` without `,1433`

  - `FabricWarehouseDatabase` = `Initial Catalog`

- Example:

  - `FabricWarehouseSqlEndpoint`:
    - `kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com`

  - `FabricWarehouseDatabase`:
    - `retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b`



> [!TIP]
> Only the Contoso Retail database is required to run this lab. If you are not following the full lab sequence, you do not need to have Microsoft Fabric deployed. For the queries in Lab 5 (Julie), you can point directly to a standalone SQL database (e.g. Azure SQL Database) using:
>
> - `FabricWarehouseSqlEndpoint` = SQL host of your standalone database
> - `FabricWarehouseDatabase` = name of your database
>
> If you do not provide these values, the deployment does not fail: it skips the DB configuration for Lab 5 and will show a notice to configure it manually afterwards.