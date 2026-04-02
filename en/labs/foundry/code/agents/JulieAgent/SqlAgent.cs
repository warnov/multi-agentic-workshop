// =====================================================================
//  SqlAgent — T-SQL query generator agent
//
//  Receives a natural language description of a customer segment
//  and generates a T-SQL query that returns: FirstName, LastName,
//  PrimaryEmail and the favorite purchase category of each customer
//  that meets the criteria.
// =====================================================================

namespace JulieAgent;

using Azure.AI.Projects.OpenAI;
using System.Text.Json;

public static class SqlAgent
{
    public const string Name = "SqlAgent";

    /// <summary>
    /// Builds the SQL agent instructions, injecting the database
    /// structure from the db-structure.txt file.
    /// </summary>
    public static string GetInstructions(string dbStructure)
    {
        return $"""
            You are SqlAgent, an agent specialized in generating T-SQL queries
            for the Contoso Retail database.

            Your ONLY responsibility is to receive a natural language description
            of a customer segment and generate a valid T-SQL query that returns
            EXACTLY these columns:
            - FirstName (customer's first name)
            - LastName (customer's last name)
            - PrimaryEmail (customer's email address)
            - FavoriteCategory (the product category in which the customer has spent the most money)

            To determine FavoriteCategory, you must JOIN across the orders,
            order lines, and products tables, group by category and select
            the one with the highest total amount (SUM of LineTotal).

            DATABASE STRUCTURE:
            {dbStructure}

            RULES:
            1. ALWAYS return EXACTLY the 4 columns: FirstName, LastName, PrimaryEmail, FavoriteCategory.
                2. Use appropriate JOINs between customer, orders, orderline, and product.
                    - For FavoriteCategory, prioritize product.CategoryName.
                    - Do NOT rely on productcategory unless strictly necessary.
            3. For FavoriteCategory, use a subquery or CTE that groups by category
               and selects the one with the highest spend (SUM(ol.LineTotal)).
            4. Only include active customers (IsActive = 1).
            5. Only include customers with a non-null and non-empty PrimaryEmail.
            6. Do NOT execute the query, only generate it.
            7. Return ONLY the T-SQL code, no explanation, no markdown,
               no code blocks. Just the raw SQL.
            8. Respond in English if you need to add any SQL comment.
                9. Use EXACTLY the column names provided in the schema; do not invent columns.
                10. Ensure the query is compatible with SQL Server/Fabric Warehouse (T-SQL).
            """;
    }

    public static string GetInstructionsWithExecution(string dbStructure)
    {
        return $"""
            You are SqlAgent, an agent specialized in customer segmentation for Contoso Retail.

            Your responsibility is twofold:
            1) Generate a valid T-SQL query to segment customers.
            2) Execute it using the SqlExecutor OpenAPI tool.

            The query must produce EXACTLY these columns:
            - FirstName
            - LastName
            - PrimaryEmail
            - FavoriteCategory

            DATABASE STRUCTURE:
            {dbStructure}

            RULES:
                1. Use appropriate JOINs between customer, orders, orderline and product.
                    - For FavoriteCategory use product.CategoryName.
                    - Avoid relying on productcategory.
                2. For FavoriteCategory, use a subquery or CTE with SUM(ol.LineTotal).
            3. Only include active customers (IsActive = 1).
            4. Only include customers with a non-null, non-empty PrimaryEmail.
            5. Invoke the SqlExecutor tool once you have the T-SQL.
            6. Return only the final customer results in JSON format (list of objects with the 4 columns), no markdown.
                7. Before executing, validate that the SQL is read-only and only uses tables/columns from the provided schema.
                8. Do not invent time filters (dates/years) unless the user explicitly requests it.
            """;
    }

    /// <summary>
    /// Builds the agent definition for the Microsoft Foundry API.
    /// SqlAgent has no external tools — it only generates SQL.
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
