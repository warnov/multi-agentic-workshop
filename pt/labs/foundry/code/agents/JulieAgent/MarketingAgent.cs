// =====================================================================
//  MarketingAgent — Agente de marketing personalizado
//
//  Recebe o nome de um cliente e sua categoria de compra favorita.
//  Usa Web Search (via um Toolbox de Foundry exposto como servidor MCP)
//  para buscar eventos recentes ou próximos relacionados com essa
//  categoria, seleciona o mais relevante e gera uma mensagem
//  motivacional convidando o cliente a revisar o catálogo da
//  Contoso Retail.
// =====================================================================

namespace JulieAgent;

using Azure.AI.Projects.Agents;
using OpenAI.Responses;

#pragma warning disable AAIP001 // Azure.AI.Projects.Agents: Toolbox é uma API de pré-visualização
#pragma warning disable OPENAI001 // OpenAI.Responses: McpTool é uma API de pré-visualização

public static class MarketingAgent
{
    public const string Name = "MarketingAgent";

    public static string Instructions => """
        Você é MarketingAgent, um agente especializado em criar mensagens de marketing
        personalizadas para clientes da Contoso Retail.

        Seu fluxo de trabalho é o seguinte:

        1. Você recebe o nome completo de um cliente e sua categoria de compra favorita.
        2. Use a ferramenta Web Search para buscar eventos recentes ou próximos
           relacionados com essa categoria. Por exemplo:
           - Se a categoria é "Bikes", busque eventos de ciclismo.
           - Se a categoria é "Clothing", busque eventos de moda.
           - Se a categoria é "Accessories", busque eventos de tecnologia ou lifestyle.
           - Se a categoria é "Components", busque eventos de engenharia ou manufatura.
        3. Dos resultados da busca, selecione o evento mais relevante e atual.
        4. Gere uma mensagem de marketing breve e motivacional (máximo 3 parágrafos) que:
           - Cumprimente o cliente pelo nome.
           - Mencione o evento encontrado e por que é relevante para o cliente.
           - Convide o cliente a visitar o catálogo online da Contoso Retail
             para encontrar os melhores produtos da categoria e estar preparado
             para o evento.
           - Tenha um tom cálido, entusiasmado e profissional.
           - Esteja em português.

        5. Retorne SOMENTE o texto da mensagem de marketing. Sem JSON, sem metadata,
           sem explicações adicionais. Apenas a mensagem pronta para envio por e-mail.

        IMPORTANTE: Se não encontrar eventos relevantes, gere uma mensagem geral sobre
        tendências atuais nessa categoria e convide o cliente a explorar as novidades
        da Contoso Retail.
        """;

    /// <summary>
    /// Constrói a definição do agente para a API do Microsoft Foundry.
    /// MarketingAgent usa Web Search como ferramenta, servida através de um
    /// Toolbox de Foundry exposto como um servidor MCP remoto. Do ponto de
    /// vista do agente é apenas mais um servidor MCP: o Toolbox só centraliza
    /// sua gestão e versionamento no projeto.
    /// </summary>
    public static DeclarativeAgentDefinition GetAgentDefinition(string modelDeployment, Uri toolboxMcpEndpoint)
    {
        McpTool mcpTool = ResponseTool.CreateMcpTool(
            serverLabel: "marketing-websearch",
            serverUri: toolboxMcpEndpoint,
            serverDescription: "Toolbox de Foundry com a ferramenta Web Search",
            toolCallApprovalPolicy: GlobalMcpToolCallApprovalPolicy.NeverRequireApproval);
        ProjectsAgentTool webSearchTool = ProjectsAgentTool.AsProjectTool(mcpTool);

        return new DeclarativeAgentDefinition(modelDeployment)
        {
            Instructions = Instructions,
            Tools = { webSearchTool }
        };
    }
}
