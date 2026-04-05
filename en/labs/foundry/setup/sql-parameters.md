# How to obtain the SQL Database parameters for our Agents?

If you are following all the workshop labs in order, these values come from the Fabric environment deployed in [Lab 1](../../fabric/lab01-data-setup.en.md). Otherwise, use the endpoint and identifier of your own database.

## Obtaining the parameters from Microsoft Fabric

### Step 1 — Navigate to the workspace and open the database

In the Fabric side menu, click **Workspaces** and select your workspace. Locate the item of type **SQL database** (e.g. `db_retail`) and open it.

<img src="../../../assets/sqlstep1.png" alt="Step 1 - Navigate to workspace and open db_retail" align="left" />

<br clear="left" />

---

### Step 2 — Open the database settings

Inside the database, click the **settings** (gear) icon in the top toolbar.

<img src="../../../assets/sqlstep2.png" alt="Step 2 - Open settings" align="left" />

<br clear="left" />

---

### Step 3 — Copy the connection string

In the settings panel, select **Connection strings** and find the **ADO.NET** string. From it extract the two values you need:

- `FabricWarehouseSqlEndpoint` = value of **`Data Source`** (highlighted in yellow), **without** the trailing `,1433`
- `FabricWarehouseDatabase` = value of **`Initial Catalog`** (highlighted in green)

```text
Data Source=xxxxx.database.fabric.microsoft.com,1433;Initial Catalog=retail_sqldatabase_xxx;...
```

<img src="../../../assets/sqlstep3.png" alt="Step 3 - Copy connection string" align="left" />

<br clear="left" />

---

### Resulting values example

- `FabricWarehouseSqlEndpoint`:
  - `kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com`

- `FabricWarehouseDatabase`:
  - `retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b`

> [!TIP]
> Only the Contoso Retail database is required to run this lab. If you are not following the full lab sequence, you do not need to have Microsoft Fabric deployed. For the queries in Lab 4 (Julie), you can point directly to a standalone SQL database (e.g. Azure SQL Database) using:
>
> - `FabricWarehouseSqlEndpoint` = SQL host of your standalone database
> - `FabricWarehouseDatabase` = name of your database
>
> If you do not provide these values, the deployment does not fail: it skips the DB configuration for Lab 4 and will show a notice to configure it manually afterwards.