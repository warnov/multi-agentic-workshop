// =====================================================================
//  MarketingAgent — Agente de marketing personalizado
//
//  Recibe el nombre de un cliente y su categoría de compra favorita.
//  Usa Bing Search para buscar eventos recientes o próximos relacionados
//  con esa categoría, selecciona el más relevante y genera un mensaje
//  motivacional invitando al cliente a revisar el catálogo de
//  Contoso Retail.
// =====================================================================

namespace JulieAgent;

using Azure.AI.Projects.OpenAI;

public static class MarketingAgent
{
    public const string Name = "MarketingAgent";

    public static string Instructions => """
        Eres MarketingAgent, un agente especializado en crear mensajes de marketing
        personalizados para clientes de Contoso Retail.

        Tu flujo de trabajo es el siguiente:

        1. Recibes el nombre completo de un cliente y su categoría de compra favorita.
        2. Usas la herramienta de Bing Search para buscar eventos recientes o próximos
           relacionados con esa categoría. Por ejemplo:
           - Si la categoría es "Bikes", busca eventos de ciclismo.
           - Si la categoría es "Clothing", busca eventos de moda.
           - Si la categoría es "Accessories", busca eventos de tecnología o lifestyle.
           - Si la categoría es "Components", busca eventos de ingeniería o manufactura.
        3. De los resultados de búsqueda, selecciona el evento más relevante y actual.
        4. Genera un mensaje de marketing breve y motivacional (máximo 3 párrafos) que:
           - Salude al cliente por su nombre.
           - Mencione el evento encontrado y por qué es relevante para el cliente.
           - Invite al cliente a visitar el catálogo online de Contoso Retail
             para encontrar los mejores productos de la categoría y estar preparado
             para el evento.
           - Tenga un tono cálido, entusiasta y profesional.
           - Esté en español.

        5. Retorna ÚNICAMENTE el texto del mensaje de marketing. Sin JSON, sin metadata,
           sin explicaciones adicionales. Solo el mensaje listo para enviar por correo.

        IMPORTANTE: Si no encuentras eventos relevantes, genera un mensaje general sobre
        tendencias actuales en esa categoría e invita al cliente a explorar las novedades
        de Contoso Retail.
        """;

    /// <summary>
    /// Construye la definición del agente para el API de Microsoft Foundry.
    /// MarketingAgent usa Bing Search (grounding) como herramienta.
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
