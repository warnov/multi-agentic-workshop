# Como obtener los parámetros de SQL Database para nuestros Agentes?

Si estás siguiendo todos los laboratorios del workshop en orden, estos valores se obtienen del entorno de Fabric desplegado en el [Lab 1](../../fabric/lab01-data-setup.md). De lo contrario, usa el endpoint e identificación de tu base de datos propia.

## Obteniendo los parámetros desde Microsoft Fabric

- En tu Workspace de Fabric, abre la base de datos y copia el **connection string** SQL. Verás algo como: 

```text
Data Source=xxxxx.database.fabric.microsoft.com,1433;Initial Catalog=retail_sqldatabase_xxx;...
```

- Mapeo:

  - `FabricWarehouseSqlEndpoint` = `Data Source` sin `,1433`

  - `FabricWarehouseDatabase` = `Initial Catalog`

- Ejemplo:

  - `FabricWarehouseSqlEndpoint`:
    - `kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com`

  - ``FabricWarehouseDatabase`:
    - `retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b`



> [!TIP]
> Para la ejecución de este laboratorio solo es necesaria la base de datos de Contoso Retail. > Si no estás siguiendo toda la secuencia de laboratorios, no es necesario tener desplegado > Microsoft Fabric. Así que para las consultas del Lab 5 (Julie), puedes apuntar directamente > a una base SQL standalone (por ejemplo Azure SQL Database) usando:
>
> - `FabricWarehouseSqlEndpoint` = host SQL de tu base standalone
> - `FabricWarehouseDatabase` = nombre de tu base
>
> Si no proporcionas estos valores, el despliegue no falla: omite la configuración de DB para > Lab 5 y te mostrará un aviso para configurarla manualmente después.