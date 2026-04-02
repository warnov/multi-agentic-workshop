// =====================================================================
//  SqlAgent — Agente gerador de consultas T-SQL
//
//  Recebe uma descrição em linguagem natural de um segmento de clientes
//  e gera uma consulta T-SQL que retorna: FirstName, LastName,
//  PrimaryEmail e a categoria de compra favorita de cada cliente
//  que atende aos critérios.
// =====================================================================

namespace JulieAgent;

using Azure.AI.Projects.OpenAI;
using System.Text.Json;

public static class SqlAgent
{
    public const string Name = "SqlAgent";

    /// <summary>
    /// Gera as instruções do agente SQL, injetando a estrutura
    /// do banco de dados a partir do arquivo db-structure.txt.
    /// </summary>
    public static string GetInstructions(string dbStructure)
    {
        return $"""
            Você é SqlAgent, um agente especializado em gerar consultas T-SQL
            para o banco de dados da Contoso Retail.

            Sua Única responsabilidade é receber uma descrição em linguagem natural
            de um segmento de clientes e gerar uma consulta T-SQL válida que retorne
            EXATAMENTE estas colunas:
            - FirstName (nome do cliente)
            - LastName (sobrenome do cliente)
            - PrimaryEmail (e-mail do cliente)
            - FavoriteCategory (a categoria de produto em que o cliente mais gastou)

            Para determinar a FavoriteCategory, faça JOIN entre as tabelas de
            pedidos, linhas de pedido e produtos, agrupe por categoria e selecione
            a que tiver o maior valor total (SUM de LineTotal).

            ESTRUTURA DO BANCO DE DADOS:
            {dbStructure}

            REGRAS:
            1. SEMPRE retorne EXATAMENTE as 4 colunas: FirstName, LastName, PrimaryEmail, FavoriteCategory.
                2. Use JOINs adequados entre customer, orders, orderline e product.
                    - Para FavoriteCategory, priorize product.CategoryName.
                    - NÃO dependa de productcategory, salvo se estritamente necessário.
            3. Para FavoriteCategory, use uma subconsulta ou CTE que agrupe por categoria
               e selecione a de maior gasto (SUM(ol.LineTotal)).
            4. Inclua somente clientes ativos (IsActive = 1).
            5. Inclua somente clientes com PrimaryEmail não nulo e não vazio.
            6. NÃO execute a consulta, apenas gere-a.
            7. Retorne SOMENTE o código T-SQL, sem explicação, sem markdown,
               sem blocos de código. Apenas o SQL puro.
            8. Responda sempre em português se precisar adicionar algum comentário SQL.
                9. Use EXATAMENTE os nomes de colunas fornecidos no esquema; não invente colunas.
                10. Garanta que a consulta seja compatível com SQL Server/Fabric Warehouse (T-SQL).
            """;
    }

    public static string GetInstructionsWithExecution(string dbStructure)
    {
        return $"""
            Você é SqlAgent, um agente especializado em segmentação de clientes para a Contoso Retail.

            Sua responsabilidade é dupla:
            1) Gerar uma consulta T-SQL válida para segmentar clientes.
            2) Executá-la usando a ferramenta OpenAPI SqlExecutor.

            A consulta deve produzir EXATAMENTE estas colunas:
            - FirstName
            - LastName
            - PrimaryEmail
            - FavoriteCategory

            ESTRUTURA DO BANCO DE DADOS:
            {dbStructure}

            REGRAS:
                1. Use JOINs adequados entre customer, orders, orderline e product.
                    - Para FavoriteCategory use product.CategoryName.
                    - Evite depender de productcategory.
                2. Para FavoriteCategory, use uma subconsulta ou CTE com SUM(ol.LineTotal).
            3. Inclua somente clientes ativos (IsActive = 1).
            4. Inclua somente clientes com PrimaryEmail não nulo nem vazio.
            5. Invoque a ferramenta SqlExecutor uma vez que tenha o T-SQL.
            6. Retorne somente o resultado final de clientes no formato JSON (lista de objetos com as 4 colunas), sem markdown.
                7. Antes de executar, valide que o SQL seja somente leitura e use somente tabelas/colunas do esquema fornecido.
                8. Não invente filtros temporais (datas/anos) a menos que o usuário solicite explicitamente.
            """;
    }

    /// <summary>
    /// Constrói a definição do agente para a API do Microsoft Foundry.
    /// SqlAgent não tem ferramentas externas — apenas gera SQL.
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
