using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using OpenAI.Responses;

#pragma warning disable OPENAI001 // OpenAI preview API

// =====================================================================
//  JulieBackup — Campaign orchestrator agent (prompt, not workflow)
//
//  Backup version of Julie that works as a NORMAL agent
//  (PromptAgentDefinition) with function tools. Avoids the workflow
//  type for greater stability in deployment and operation.
//
//  SqlAgent and MarketingAgent are exposed as function tools.
//  Program.cs intercepts function calls and redirects them
//  to the real agents in Foundry using independent conversations.
//
//  Usage: dotnet run
// =====================================================================

// --- Load configuration ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("Missing FoundryProjectEndpoint in appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("Missing ModelDeploymentName in appsettings.json");

// --- Foundry project client ---
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

Console.WriteLine();
Console.WriteLine("========================================================");
Console.WriteLine(" JulieBackup - Orchestrator agent (prompt + functions)");
Console.WriteLine("========================================================");
Console.WriteLine();

// =====================================================================
//  PHASE 1: Verify that SqlAgent and MarketingAgent exist in Foundry
// =====================================================================

const string sqlAgentName = "SqlAgent";
const string marketingAgentName = "MarketingAgent";
const string julieBackupAgentName = "JulieBackup";

Console.WriteLine("[Foundry] Verifying that dependent agents exist...");

foreach (var dependentAgent in new[] { sqlAgentName, marketingAgentName })
{
    try
    {
        projectClient.Agents.GetAgent(dependentAgent);
        Console.WriteLine($"  ✓ '{dependentAgent}' found");
    }
    catch (ClientResultException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"  ✗ '{dependentAgent}' NOT found in Foundry.");
        Console.WriteLine($"    Create the agent first and then run JulieBackup again.");
        Console.WriteLine();
        Console.WriteLine("[Aborted] JulieBackup cannot be created without its dependent agents.");
        return;
    }
}

Console.WriteLine();

// =====================================================================
//  PHASE 2: Create the JulieBackup agent (prompt + function tools)
// =====================================================================

var julieInstructions = """
    You are JulieBackup, the planning and orchestrator agent for Contoso Retail marketing campaigns.

    Your responsibility is to coordinate the creation of personalized marketing campaigns
    for specific customer segments.

    You have two tools:
    - query_customers: queries the database to retrieve customers from a specific segment
      (returns FirstName, LastName, PrimaryEmail, FavoriteCategory).
    - generate_marketing_message: generates a personalized marketing message
      for a customer given their name and favorite category.

    When you receive a campaign request, follow these steps:

    1. EXTRACTION: Analyze the user prompt and extract the customer segment description.

    2. CUSTOMER QUERY: Invoke query_customers with the segment description.
       You will receive customer data.

    3. PERSONALIZED MARKETING: For EACH customer returned, invoke
       generate_marketing_message with their full name and favorite category.

    4. FINAL ORGANIZATION: With all generated messages, organize the
       result as a campaign JSON:

    ```json
    {
      "campaign": "Descriptive campaign name",
      "generatedAt": "YYYY-MM-DDTHH:mm:ss",
      "totalEmails": N,
      "emails": [
        {
          "to": "email@example.com",
          "customerName": "First Last",
          "favoriteCategory": "Category",
          "subject": "Attractive email subject",
          "body": "Personalized marketing message"
        }
      ]
    }
    ```

    RULES:
    - Always respond in English.
    - If a customer has no email, omit them from the result.
    - Generate a descriptive campaign name based on the segment.
    """;

// Define function tools representing SqlAgent and MarketingAgent
var queryCustomersParams = BinaryData.FromObjectAsJson(new
{
    type = "object",
    properties = new
    {
        segment_description = new
        {
            type = "string",
            description = "Natural language description of the customer segment to query. Example: 'customers who purchased bicycles in the last year'"
        }
    },
    required = new[] { "segment_description" }
});

var generateMessageParams = BinaryData.FromObjectAsJson(new
{
    type = "object",
    properties = new
    {
        customer_name = new
        {
            type = "string",
            description = "Customer's full name (FirstName LastName)"
        },
        favorite_category = new
        {
            type = "string",
            description = "Customer's favorite product category (e.g.: Bikes, Clothing, Accessories, Components)"
        }
    },
    required = new[] { "customer_name", "favorite_category" }
});

var julieDefinition = new PromptAgentDefinition(modelDeployment)
{
    Instructions = julieInstructions,
    Tools =
    {
        ResponseTool.CreateFunctionTool(
            functionName: "query_customers",
            functionParameters: queryCustomersParams,
            strictModeEnabled: false,
            functionDescription: "Queries the Contoso Retail database for customers matching a given segment. Returns a list with FirstName, LastName, PrimaryEmail and FavoriteCategory."
        ).AsAgentTool(),
        ResponseTool.CreateFunctionTool(
            functionName: "generate_marketing_message",
            functionParameters: generateMessageParams,
            strictModeEnabled: false,
            functionDescription: "Generates a personalized marketing message for a customer, searching for relevant events on Bing based on their favorite category."
        ).AsAgentTool()
    }
};

// Check if JulieBackup already exists
Console.WriteLine($"[Foundry] Searching for agent '{julieBackupAgentName}'...");
AgentRecord? existingAgent = null;
bool shouldCreate = true;

try
{
    existingAgent = projectClient.Agents.GetAgent(julieBackupAgentName);
    Console.WriteLine($"[Foundry] Agent '{julieBackupAgentName}' found");
    Console.Write($"[Foundry] Do you want to overwrite it with a new version? (y/N): ");
    var answer = Console.ReadLine();
    shouldCreate = answer?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true
                || answer?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true;

    if (!shouldCreate)
    {
        Console.WriteLine($"[Foundry] Keeping existing '{julieBackupAgentName}'.");
        return;
    }
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] Agent '{julieBackupAgentName}' not found. A new one will be created.");
}

// Create/update JulieBackup
try
{
    Console.WriteLine($"[Foundry] Creating/updating agent '{julieBackupAgentName}'...");

    var result = await projectClient.Agents.CreateAgentVersionAsync(
        julieBackupAgentName,
        new AgentVersionCreationOptions(julieDefinition));

    var responseJson = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
    var version = responseJson.RootElement.TryGetProperty("version", out var vProp) ? vProp.GetString() : "?";
    Console.WriteLine($"[Foundry] Agent '{julieBackupAgentName}' created successfully (v{version})");
}
catch (ClientResultException ex) when (ex.Status == 400 && existingAgent is not null)
{
    Console.WriteLine($"[Foundry] Could not create new version: {ex.Message}");
    Console.WriteLine($"[Foundry] Reusing the existing version.");
}

Console.WriteLine();
Console.WriteLine("[Foundry] Agent ready. Starting interactive chat...");

// =====================================================================
//  PHASE 3: Interactive chat with function call handling
//
//  When JulieBackup invokes query_customers → redirect to SqlAgent
//  When JulieBackup invokes generate_marketing_message → redirect to MarketingAgent
// =====================================================================

ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
Console.WriteLine($"[Foundry] Conversation created: {conversation.Id}");

ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: julieBackupAgentName,
    defaultConversationId: conversation.Id);

Console.WriteLine();
Console.WriteLine("=== Chat with JulieBackup (type 'exit' to quit) ===");
Console.WriteLine("Example: 'Create a campaign for customers who have purchased bicycles'");
Console.WriteLine();

// --- Helper: send a message to a sub-agent and get the response ---
async Task<string> InvokeSubAgent(string agentName, string message)
{
    Console.WriteLine($"  [→ {agentName}] {(message.Length > 100 ? message[..100] + "..." : message)}");
    try
    {
        ProjectConversation subConv = projectClient.OpenAI.Conversations.CreateProjectConversation();
        var subClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
            defaultAgent: agentName,
            defaultConversationId: subConv.Id);

        var subResponse = await subClient.CreateResponseAsync(message);
        var result = subResponse.Value.GetOutputText();
        Console.WriteLine($"  [← {agentName}] {(result.Length > 120 ? result[..120] + "..." : result)}");
        return result;
    }
    catch (Exception ex)
    {
        var error = $"Error invoking {agentName}: {ex.Message}";
        Console.WriteLine($"  [✗ {agentName}] {error}");
        return error;
    }
}

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    try
    {
        // Send message to JulieBackup
        ResponseResult response = responseClient.CreateResponse(input);

        // Function call loop: JulieBackup may request N function calls
        while (true)
        {
            // Collect all pending function calls
            var functionCalls = response.OutputItems.OfType<FunctionCallResponseItem>().ToList();

            if (functionCalls.Count == 0)
                break; // No more function calls, exit the loop

            Console.WriteLine($"  [JulieBackup] Invoking {functionCalls.Count} tool(s)...");

            var functionOutputs = new List<ResponseItem>();

            foreach (var funcCall in functionCalls)
            {
                var funcArgs = funcCall.FunctionArguments?.ToString() ?? "{}";
                var argsJson = JsonDocument.Parse(funcArgs).RootElement;

                string result;
                switch (funcCall.FunctionName)
                {
                    case "query_customers":
                        var segment = argsJson.TryGetProperty("segment_description", out var seg)
                            ? seg.GetString() ?? ""
                            : funcArgs;
                        result = await InvokeSubAgent(sqlAgentName, segment);
                        break;

                    case "generate_marketing_message":
                        var customerName = argsJson.TryGetProperty("customer_name", out var n)
                            ? n.GetString() ?? ""
                            : "";
                        var category = argsJson.TryGetProperty("favorite_category", out var c)
                            ? c.GetString() ?? ""
                            : "";
                        var prompt = $"Generate a personalized marketing message for customer {customerName} whose favorite category is {category}.";
                        result = await InvokeSubAgent(marketingAgentName, prompt);
                        break;

                    default:
                        result = $"Unknown function: {funcCall.FunctionName}";
                        break;
                }

                functionOutputs.Add(
                    ResponseItem.CreateFunctionCallOutputItem(funcCall.CallId, result));
            }

            // Send function results back to JulieBackup
            // No previousResponseId needed — ProjectResponsesClient already injects conversationId
            response = responseClient.CreateResponse(functionOutputs, previousResponseId: null);
        }

        // Show JulieBackup's final response
        var outputText = response.GetOutputText();
        if (!string.IsNullOrEmpty(outputText))
        {
            Console.WriteLine();
            Console.WriteLine($"JulieBackup: {outputText}");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("[JulieBackup] No output text.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[Error] {ex.Message}");
        if (ex.InnerException != null)
            Console.WriteLine($"  [Inner] {ex.InnerException.Message}");
    }

    Console.WriteLine();
}

Console.WriteLine("[Foundry] Chat ended.");
Console.WriteLine("[Foundry] Agent JulieBackup remains available in Microsoft Foundry.");
