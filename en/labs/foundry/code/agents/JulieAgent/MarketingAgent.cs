// =====================================================================
//  MarketingAgent — Personalized marketing agent
//
//  Receives a customer's name and their favorite purchase category.
//  Uses Web Search (via a Foundry Toolbox exposed as an MCP server) to
//  look for recent or upcoming events related to that category, selects
//  the most relevant one, and generates a motivational message inviting
//  the customer to review the Contoso Retail catalog.
// =====================================================================

namespace JulieAgent;

using Azure.AI.Projects.Agents;
using OpenAI.Responses;

#pragma warning disable AAIP001 // Azure.AI.Projects.Agents: Toolbox is a preview API
#pragma warning disable OPENAI001 // OpenAI.Responses: McpTool is a preview API

public static class MarketingAgent
{
    public const string Name = "MarketingAgent";

    public static string Instructions => """
        You are MarketingAgent, an agent specialized in creating personalized marketing
        messages for Contoso Retail customers.

        Your workflow is the following:

        1. You receive a customer's full name and their favorite purchase category.
        2. Use the Web Search tool to look for recent or upcoming events
           related to that category. For example:
           - If the category is "Bikes", search for cycling events.
           - If the category is "Clothing", search for fashion events.
           - If the category is "Accessories", search for technology or lifestyle events.
           - If the category is "Components", search for engineering or manufacturing events.
        3. From the search results, select the most relevant and current event.
        4. Generate a brief and motivational marketing message (maximum 3 paragraphs) that:
           - Greets the customer by name.
           - Mentions the event found and why it is relevant to the customer.
           - Invites the customer to visit the Contoso Retail online catalog
             to find the best products in the category and be ready for the event.
           - Has a warm, enthusiastic and professional tone.
           - Is written in English.

        5. Return ONLY the text of the marketing message. No JSON, no metadata,
           no additional explanations. Just the message ready to send by email.

        IMPORTANT: If no relevant events are found, generate a general message about
        current trends in that category and invite the customer to explore the latest
        products from Contoso Retail.
        """;

    /// <summary>
    /// Builds the agent definition for the Microsoft Foundry API.
    /// MarketingAgent uses Web Search as a tool, served through a Foundry
    /// Toolbox exposed as a remote MCP server. From the agent's point of
    /// view it's just another MCP server: the Toolbox only centralizes its
    /// management and versioning in the project.
    /// </summary>
    public static DeclarativeAgentDefinition GetAgentDefinition(string modelDeployment, Uri toolboxMcpEndpoint)
    {
        McpTool mcpTool = ResponseTool.CreateMcpTool(
            serverLabel: "marketing-websearch",
            serverUri: toolboxMcpEndpoint,
            serverDescription: "Foundry Toolbox with the Web Search tool",
            toolCallApprovalPolicy: GlobalMcpToolCallApprovalPolicy.NeverRequireApproval);
        ProjectsAgentTool webSearchTool = ProjectsAgentTool.AsProjectTool(mcpTool);

        return new DeclarativeAgentDefinition(modelDeployment)
        {
            Instructions = Instructions,
            Tools = { webSearchTool }
        };
    }
}
