# Como obter os parâmetros do SQL Database para nossos Agentes?

Se você está seguindo todos os laboratórios do workshop em ordem, esses valores são obtidos do ambiente do Fabric implantado no [Lab 1](../../fabric/lab01-data-setup.md). Caso contrário, use o endpoint e a identificação do seu próprio banco de dados.

## Obtendo os parâmetros do Microsoft Fabric

- No seu Workspace do Fabric, abra o banco de dados e copie a **connection string** SQL. Você verá algo como:

```text
Data Source=xxxxx.database.fabric.microsoft.com,1433;Initial Catalog=retail_sqldatabase_xxx;...
```

- Mapeamento:

  - `FabricWarehouseSqlEndpoint` = `Data Source` sem `,1433`

  - `FabricWarehouseDatabase` = `Initial Catalog`

- Exemplo:

  - `FabricWarehouseSqlEndpoint`:
    - `kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com`

  - `FabricWarehouseDatabase`:
    - `retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b`



> [!TIP]
> Para a execução deste laboratório, apenas o banco de dados do Contoso Retail é necessário.
> Se você não está seguindo toda a sequência de laboratórios, não é necessário ter o
> Microsoft Fabric implantado. Assim, para as consultas do Lab 4 (Julie), você pode apontar diretamente
> para um banco SQL standalone (por exemplo, Azure SQL Database) usando:
>
> - `FabricWarehouseSqlEndpoint` = host SQL do seu banco standalone
> - `FabricWarehouseDatabase` = nome do seu banco
>
> Se você não fornecer esses valores, a implantação não falha: ela omite a configuração do banco de dados
> para o Lab 4 e exibirá um aviso para configurá-la manualmente depois.
