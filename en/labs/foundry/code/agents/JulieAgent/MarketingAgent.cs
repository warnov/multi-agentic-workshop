// =====================================================================
//  MarketingAgent — Personalized marketing agent
//
//  Receives a customer's name and their favorite purchase category.
//  Uses Bing Search to look for recent or upcoming events related
//  to that category, selects the most relevant one, and generates a
//  motivational message inviting the customer to review the
//  Contoso Retail catalog.
// =====================================================================

namespace JulieAgent;

using Azure.AI.Projects.OpenAI;

public static class MarketingAgent
{
    public const string Name = "MarketingAgent";

    public static string Instructions => """
        You are MarketingAgent, an agent specialized in creating personalized marketing
        messages for Contoso Retail customers.

        Your workflow is the following:

        1. You receive a customer's full name and their favorite purchase category.
        2. Use the Bing Search tool to look for recent or upcoming events
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
    /// MarketingAgent uses Bing Search (grounding) as a tool.
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
