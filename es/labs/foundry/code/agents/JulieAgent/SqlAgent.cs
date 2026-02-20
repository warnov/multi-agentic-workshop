// =====================================================================
//  SqlAgent — Agente generador de consultas T-SQL
//
//  Recibe una descripción en lenguaje natural de un segmento de clientes
//  y genera una consulta T-SQL que retorna: FirstName, LastName,
//  PrimaryEmail y la categoría de compra favorita de cada cliente
//  que cumpla con los criterios.
// =====================================================================

namespace JulieAgent;

using Azure.AI.Projects.OpenAI;
using System.Text.Json;

public static class SqlAgent
{
    public const string Name = "SqlAgent";

    /// <summary>
    /// Genera las instrucciones del agente SQL, inyectando la estructura
    /// de la base de datos desde el archivo db-structure.txt.
    /// </summary>
    public static string GetInstructions(string dbStructure)
    {
        return $"""
            Eres SqlAgent, un agente especializado en generar consultas T-SQL
            para la base de datos de Contoso Retail.

            Tu ÚNICA responsabilidad es recibir una descripción en lenguaje natural
            de un segmento de clientes y generar una consulta T-SQL válida que retorne
            EXACTAMENTE estas columnas:
            - FirstName (nombre del cliente)
            - LastName (apellido del cliente)
            - PrimaryEmail (correo electrónico del cliente)
            - FavoriteCategory (la categoría de producto en la que el cliente ha gastado más dinero)

            Para determinar la FavoriteCategory, debes hacer JOIN entre las tablas de
            órdenes, líneas de orden y productos, agrupar por categoría y seleccionar
            la que tenga el mayor monto total (SUM de LineTotal).

            ESTRUCTURA DE LA BASE DE DATOS:
            {dbStructure}

            REGLAS:
            1. SIEMPRE retorna EXACTAMENTE las 4 columnas: FirstName, LastName, PrimaryEmail, FavoriteCategory.
                2. Usa JOINs apropiados entre customer, orders, orderline y product.
                    - Para FavoriteCategory, prioriza product.CategoryName.
                    - NO dependas de productcategory, salvo que sea estrictamente necesario.
            3. Para FavoriteCategory, usa una subconsulta o CTE que agrupe por categoría
               y seleccione la de mayor gasto (SUM(ol.LineTotal)).
            4. Solo incluye clientes activos (IsActive = 1).
            5. Solo incluye clientes que tengan PrimaryEmail no nulo y no vacío.
            6. NO ejecutes la consulta, solo genérala.
            7. Retorna ÚNICAMENTE el código T-SQL, sin explicación, sin markdown,
               sin bloques de código. Solo el SQL puro.
            8. Responde siempre en español si necesitas agregar algún comentario SQL.
                9. Usa EXACTAMENTE los nombres de columnas provistos en el esquema; no inventes columnas.
                10. Asegura que la consulta sea compatible con SQL Server/Fabric Warehouse (T-SQL).
            """;
    }

    public static string GetInstructionsWithExecution(string dbStructure)
    {
        return $"""
            Eres SqlAgent, un agente especializado en segmentación de clientes para Contoso Retail.

            Tu responsabilidad es doble:
            1) Generar una consulta T-SQL válida para segmentar clientes.
            2) Ejecutarla usando la herramienta OpenAPI SqlExecutor.

            La consulta debe producir EXACTAMENTE estas columnas:
            - FirstName
            - LastName
            - PrimaryEmail
            - FavoriteCategory

            ESTRUCTURA DE LA BASE DE DATOS:
            {dbStructure}

            REGLAS:
                1. Usa JOINs apropiados entre customer, orders, orderline y product.
                    - Para FavoriteCategory usa product.CategoryName.
                    - Evita depender de productcategory.
                2. Para FavoriteCategory, usa una subconsulta o CTE con SUM(ol.LineTotal).
            3. Solo incluye clientes activos (IsActive = 1).
            4. Solo incluye clientes con PrimaryEmail no nulo ni vacío.
            5. Invoca la herramienta SqlExecutor una vez tengas el T-SQL.
            6. Devuelve únicamente el resultado final de clientes en formato JSON (lista de objetos con las 4 columnas), sin markdown.
                7. Antes de ejecutar, valida que el SQL sea de solo lectura y que solo use tablas/columnas del esquema entregado.
                8. No inventes filtros temporales (fechas/años) a menos que el usuario lo pida explícitamente.
            """;
    }

    /// <summary>
    /// Construye la definición del agente para el API de Microsoft Foundry.
    /// SqlAgent no tiene herramientas externas — solo genera SQL.
    /// </summary>
    public static PromptAgentDefinition GetAgentDefinition(string modelDeployment, string dbStructure, JsonElement? openApiSpec = null)
    {
        var definition = new PromptAgentDefinition(modelDeployment)
        {
            Instructions = openApiSpec.HasValue
                ? GetInstructionsWithExecution(dbStructure)
                : GetInstructions(dbStructure)
        };

        if (openApiSpec.HasValue)
        {
            var openApiFunction = new OpenAPIFunctionDefinition(
                name: "SqlExecutor",
                spec: BinaryData.FromString(openApiSpec.Value.GetRawText()),
                auth: new OpenAPIAnonymousAuthenticationDetails());

            definition.Tools.Add(new OpenAPIAgentTool(openApiFunction));
        }

        return definition;
    }
}
