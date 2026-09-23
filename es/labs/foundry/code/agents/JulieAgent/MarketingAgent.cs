// =====================================================================
//  MarketingAgent — Agente de marketing personalizado
//
//  Recibe el nombre de un cliente y su categoría de compra favorita.
//  Usa Web Search (vía un Toolbox de Foundry expuesto como servidor MCP)
//  para buscar eventos recientes o próximos relacionados con esa
//  categoría, selecciona el más relevante y genera un mensaje
//  motivacional invitando al cliente a revisar el catálogo de
//  Contoso Retail.
// =====================================================================

namespace JulieAgent;

using Azure.AI.Projects.Agents;
using OpenAI.Responses;

#pragma warning disable AAIP001 // Azure.AI.Projects.Agents: Toolbox/AsProjectTool son API de vista previa
#pragma warning disable OPENAI001 // OpenAI.Responses: McpTool es API de vista previa

public static class MarketingAgent
{
    public const string Name = "MarketingAgent";

    public static string Instructions => """
        Eres MarketingAgent, un agente especializado en crear mensajes de marketing
        personalizados para clientes de Contoso Retail.

        Tu flujo de trabajo es el siguiente:

        1. Recibes el nombre completo de un cliente y su categoría de compra favorita.
        2. Usas la herramienta de Web Search para buscar eventos recientes o próximos
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
    /// MarketingAgent usa Web Search como herramienta, servida a través de un
    /// Toolbox de Foundry expuesto como un servidor MCP remoto. Desde el punto
    /// de vista del agente es un servidor MCP más: el Toolbox solo centraliza
    /// su gestión y versionado en el proyecto.
    /// </summary>
    public static DeclarativeAgentDefinition GetAgentDefinition(string modelDeployment, Uri toolboxMcpEndpoint)
    {
        McpTool mcpTool = ResponseTool.CreateMcpTool(
            serverLabel: "marketing-websearch",
            serverUri: toolboxMcpEndpoint,
            serverDescription: "Toolbox de Foundry con la herramienta Web Search",
            toolCallApprovalPolicy: GlobalMcpToolCallApprovalPolicy.NeverRequireApproval);
        ProjectsAgentTool webSearchTool = ProjectsAgentTool.AsProjectTool(mcpTool);

        return new DeclarativeAgentDefinition(modelDeployment)
        {
            Instructions = Instructions,
            Tools = { webSearchTool }
        };
    }
}
