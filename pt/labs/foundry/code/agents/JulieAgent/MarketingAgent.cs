// =====================================================================
//  MarketingAgent — Agente de marketing personalizado
//
//  Recebe o nome de um cliente e sua categoria de compra favorita.
//  Usa o Bing Search para buscar eventos recentes ou próximos relacionados
//  com essa categoria, seleciona o mais relevante e gera uma mensagem
//  motivacional convidando o cliente a revisar o catálogo da
//  Contoso Retail.
// =====================================================================

namespace JulieAgent;

using Azure.AI.Projects.OpenAI;

public static class MarketingAgent
{
    public const string Name = "MarketingAgent";

    public static string Instructions => """
        Você é MarketingAgent, um agente especializado em criar mensagens de marketing
        personalizadas para clientes da Contoso Retail.

        Seu fluxo de trabalho é o seguinte:

        1. Você recebe o nome completo de um cliente e sua categoria de compra favorita.
        2. Use a ferramenta Bing Search para buscar eventos recentes ou próximos
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
    /// MarketingAgent usa Bing Search (grounding) como ferramenta.
    /// </summary>
    public static PromptAgentDefinition GetAgentDefinition(string modelDeployment, string bingConnectionId)
    {
        var bingGroundingAgentTool = new BingGroundingAgentTool(new BingGroundingSearchToolOptions(
            searchConfigurations: [new BingGroundingSearchConfiguration(projectConnectionId: bingConnectionId)]));

        return new PromptAgentDefinition(modelDeployment)
        {
            Instructions = Instructions,
            Tools = { bingGroundingAgentTool }
        };
    }
}
