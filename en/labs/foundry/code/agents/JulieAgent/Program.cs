using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenAI.Responses;
using JulieAgent;

#pragma warning disable AAIP001 // Azure.AI.Projects.Agents: Toolbox is a preview API
#pragma warning disable OPENAI001 // OpenAI preview API

// =====================================================================
//  Julie - Marketing Campaign Orchestrator Agent
//  (Microsoft Foundry - new experience)
//
//  Program.cs is ONLY responsible for:
//  1. Creating/verifying the 3 agents in Microsoft Foundry
//     (SqlAgent, MarketingAgent, Julie)
//  2. Opening an interactive chat with Julie
//
//  All orchestration is done by Julie internally:
//    SqlAgent (tool) → generates T-SQL
//    SqlExecutor (OpenAPI tool) → executes SQL against the DB
//    MarketingAgent (tool) → generates personalized messages
//    Julie → organizes the result as campaign JSON
// =====================================================================

// --- Load configuration ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("Missing FoundryProjectEndpoint in appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("Missing ModelDeploymentName in appsettings.json");

// Base URL of the Function App with the SQL query executor.
// Configured in appsettings.json once the function is deployed.
var functionAppBaseUrl = config["FunctionAppBaseUrl"];

// --- Load database structure ---
var dbStructurePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "db-structure.txt");
if (!File.Exists(dbStructurePath))
    dbStructurePath = Path.Combine(Directory.GetCurrentDirectory(), "db-structure.txt");
if (!File.Exists(dbStructurePath))
{
    throw new FileNotFoundException(
        "File db-structure.txt not found. " +
        "Make sure it exists in the root folder of the JulieAgent project.");
}
var dbStructure = File.ReadAllText(dbStructurePath);
Console.WriteLine($"[Config] DB structure loaded ({dbStructure.Length} chars)");

// --- (Optional) Download OpenAPI spec from the Function App ---
JsonElement? openApiSpecJson = null;

if (!string.IsNullOrEmpty(functionAppBaseUrl) && !functionAppBaseUrl.StartsWith("<"))
{
    Console.WriteLine("[OpenAPI] Downloading specification from the Function App...");
    var openApiUrl = $"{functionAppBaseUrl}/openapi/v3.json";
    var maxAttempts = 3;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            var openApiSpec = await httpClient.GetStringAsync(openApiUrl);
            openApiSpecJson = JsonSerializer.Deserialize<JsonElement>(openApiSpec);
            Console.WriteLine($"[OpenAPI] Specification downloaded ({openApiSpec.Length} bytes)");
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenAPI] Attempt {attempt}/{maxAttempts} failed: {ex.Message}");
            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                continue;
            }

            Console.WriteLine("[OpenAPI] Julie will be created without the OpenAPI tool.");
        }
    }
}
else
{
    Console.WriteLine("[Config] FunctionAppBaseUrl not configured.");
    Console.WriteLine("  → Julie will be created without the OpenAPI tool (SQL execution pending).");
    Console.WriteLine("  → Configure FunctionAppBaseUrl in appsettings.json once the Function App is deployed.");
}

// --- Foundry project client ---
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

// =====================================================================
//  PHASE 1: Create/verify the 3 agents in Microsoft Foundry
// =====================================================================

Console.WriteLine();
Console.WriteLine("========================================");
Console.WriteLine(" Julie - Campaign Orchestrator");
Console.WriteLine("========================================");
Console.WriteLine();

// --- Helper to create or reuse an agent (typed definition) ---
var agentsClient = projectClient.AgentAdministrationClient;

// Adding a version to a legacy agent object keeps the shared project
// identity, so an existing agent is deleted instead of versioned.
async Task EnsureAgent(string agentName, ProjectsAgentDefinition agentDefinition)
{
    Console.WriteLine($"[Foundry] Searching for agent '{agentName}'...");
    try
    {
        var existing = agentsClient.GetAgent(agentName);
        Console.WriteLine($"[Foundry] Agent '{agentName}' found");
        Console.Write($"[Foundry] Delete '{agentName}' and recreate it from scratch? (y/N): ");
        var answer = Console.ReadLine()?.Trim();
        var shouldRecreate = string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);

        if (!shouldRecreate)
        {
            Console.WriteLine($"[Foundry] Keeping existing '{agentName}'.");
            return;
        }

        agentsClient.DeleteAgent(agentName);
        Console.WriteLine($"[Foundry] Agent '{agentName}' deleted.");
    }
    catch (ClientResultException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"[Foundry] Agent '{agentName}' not found. A new one will be created.");
    }

    ProjectsAgentVersion created = await agentsClient.CreateAgentVersionAsync(
        agentName,
        new ProjectsAgentVersionCreationOptions(agentDefinition));

    Console.WriteLine($"[Foundry] Agent '{agentName}' created (v{created.Version})");

    // instance_identity is null on legacy agents and non-null on agents that
    // carry their own Entra identity, which is what Agent 365 syncs.
    using var agentJson = JsonDocument.Parse(agentsClient.GetAgent(agentName).GetRawResponse().Content.ToString());
    var hasIdentity = agentJson.RootElement.TryGetProperty("instance_identity", out var identity)
                      && identity.ValueKind != JsonValueKind.Null;
    Console.WriteLine(hasIdentity
        ? $"[Foundry] {agentName} instance_identity: {identity}"
        : $"[Foundry] {agentName} instance_identity: null/absent -> legacy agent, it will NOT sync to Agent 365.");
}


// --- Ensure the Web Search Toolbox used by MarketingAgent exists ---
// Creating a Toolbox version is a data-plane call (SDK), just like creating
// an agent: it requires no ARM resource and no key-based connection.
const string marketingToolboxName = "marketing-websearch-toolbox";
var toolboxesClient = agentsClient.GetAgentToolboxes();

Console.WriteLine($"[Foundry] Looking for toolbox '{marketingToolboxName}'...");
try
{
    toolboxesClient.Get(marketingToolboxName);
    Console.WriteLine($"[Foundry] Toolbox '{marketingToolboxName}' already exists, reusing it.");
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] Toolbox '{marketingToolboxName}' not found. Creating it.");
    await toolboxesClient.CreateVersionAsync(
        marketingToolboxName,
        tools: new List<ToolboxTool> { new WebSearchToolboxTool() },
        description: "Web Search for MarketingAgent (replaces Grounding with Bing Search)");
    Console.WriteLine($"[Foundry] Toolbox '{marketingToolboxName}' created.");
}

// "Consumer" endpoint: always serves the default_version, so promoting a new
// toolbox version never requires touching or recompiling MarketingAgent.
var marketingToolboxEndpoint = new Uri($"{foundryEndpoint}/toolboxes/{marketingToolboxName}/mcp?api-version=v1");

// Create the two prompt sub-agents that Julie orchestrates
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, marketingToolboxEndpoint));

// =====================================================================
//  Julie: hosted agent deployed from source
// =====================================================================

const string julieAgentName = "Julie";

// Foundry builds the uploaded source remotely, so no Docker or registry is needed locally.
var hostedSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "JulieHosted"));

if (!Directory.Exists(hostedSourcePath))
    throw new DirectoryNotFoundException($"Hosted agent source not found at {hostedSourcePath}");

// An agent's kind is immutable, so a Julie left over from the workflow model
// must be deleted before it can be recreated as a hosted agent.
try
{
    var existingJulie = agentsClient.GetAgent(julieAgentName);
    using var julieJson = JsonDocument.Parse(existingJulie.GetRawResponse().Content.ToString());
    var existingKind = julieJson.RootElement
        .GetProperty("versions").GetProperty("latest")
        .GetProperty("definition").GetProperty("kind").GetString();

    if (existingKind == "hosted")
    {
        Console.WriteLine($"[Foundry] Agent '{julieAgentName}' is already hosted. A new version will be added.");
    }
    else
    {
        Console.WriteLine($"[Foundry] Agent '{julieAgentName}' exists with kind '{existingKind}', which cannot be changed in place.");
        Console.Write($"[Foundry] Delete '{julieAgentName}' and redeploy it as a hosted agent? (y/N): ");
        var julieAnswer = Console.ReadLine()?.Trim();
        var recreateJulie = string.Equals(julieAnswer, "y", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(julieAnswer, "yes", StringComparison.OrdinalIgnoreCase);

        if (!recreateJulie)
            throw new InvalidOperationException(
                $"'{julieAgentName}' must be deleted before it can be deployed as a hosted agent.");

        agentsClient.DeleteAgent(julieAgentName);
        Console.WriteLine($"[Foundry] Agent '{julieAgentName}' deleted.");
    }
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] Agent '{julieAgentName}' not found. A new one will be created.");
}

Console.WriteLine($"[Foundry] Uploading Julie hosted agent source from {hostedSourcePath}...");

// The SDK uploads the folder as-is and the remote build fails on local build
// artifacts, so bin/ and obj/ are removed before packaging.
foreach (var stale in new[] { "bin", "obj" })
{
    var staleDir = Path.Combine(hostedSourcePath, stale);
    if (Directory.Exists(staleDir))
    {
        Directory.Delete(staleDir, recursive: true);
        Console.WriteLine($"[Foundry] Removed local build output '{stale}' before upload.");
    }
}

HostedAgentDefinition julieDefinition = new(cpu: "0.5", memory: "1Gi")
{
    Versions = { new ProtocolVersionRecord(ProjectsAgentProtocol.Responses, "2.0.0") },
    CodeConfiguration = new(
        runtime: "dotnet_10",
        entryPoint: ["dotnet", "julie-hosted.dll"],
        dependencyResolution: CodeDependencyResolution.RemoteBuild)
};
julieDefinition.EnvironmentVariables.Add("FOUNDRY_PROJECT_ENDPOINT", foundryEndpoint);
julieDefinition.EnvironmentVariables.Add("AZURE_AI_MODEL_DEPLOYMENT_NAME", modelDeployment);
// auto lets Julie fall back to clearly marked demo customers when Fabric SQL is missing.
julieDefinition.EnvironmentVariables.Add("JULIE_DATA_MODE", config["JulieDataMode"] ?? "auto");

ProjectsAgentVersion julieVersion = await agentsClient.CreateAgentVersionFromCodeAsync(
    agentName: julieAgentName,
    filePath: hostedSourcePath,
    metadata: new AgentVersionFromCodeMetadata(julieDefinition));

Console.WriteLine($"[Foundry] Julie version {julieVersion.Version} created. Waiting for provisioning...");

for (var attempt = 1; attempt <= 60; attempt++)
{
    await Task.Delay(TimeSpan.FromSeconds(10));
    julieVersion = await agentsClient.GetAgentVersionAsync(julieAgentName, julieVersion.Version);
    Console.WriteLine($"[Foundry] Provisioning status: {julieVersion.Status} ({attempt}/60)");

    if (julieVersion.Status == AgentVersionStatus.Active) break;
    if (julieVersion.Status == AgentVersionStatus.Failed)
        throw new InvalidOperationException("Julie hosted agent provisioning failed.");
}

if (julieVersion.Status != AgentVersionStatus.Active)
    throw new TimeoutException("Timed out waiting for Julie to become active.");

await agentsClient.PatchAgentAsync(julieAgentName, new PatchAgentOptions
{
    AgentEndpoint = new AgentEndpointConfiguration
    {
        VersionSelector = new([new FixedRatioVersionSelectionRule(julieVersion.Version, 100)]),
        ProtocolConfiguration = new() { Responses = new ResponsesProtocolConfiguration() }
    }
});
Console.WriteLine($"[Foundry] Julie endpoint routed to version {julieVersion.Version}");

// =====================================================================
//  Grant the hosted agent identity access to the project
//
//  A hosted agent runs under its own Entra identity, which is recreated
//  together with the agent object, so the role is re-assigned on every run
//  instead of being a manual one-off step.
//  'Foundry User' is required: 'Foundry Agent Consumer' only grants
//  endpoints/interact and Julie gets 403 on agents/read when it looks up
//  SqlAgent and MarketingAgent. The assignment must target the project
//  scope; the account scope alone was not honoured.
// =====================================================================

const string foundryUserRoleId = "53ca6127-db72-4b80-b1b0-d745d6d5456d";

string? juliePrincipalId = null;
using (var julieIdentityJson = JsonDocument.Parse(agentsClient.GetAgent(julieAgentName).GetRawResponse().Content.ToString()))
{
    if (julieIdentityJson.RootElement.TryGetProperty("instance_identity", out var julieIdentity)
        && julieIdentity.ValueKind == JsonValueKind.Object
        && julieIdentity.TryGetProperty("principal_id", out var juliePrincipal))
    {
        juliePrincipalId = juliePrincipal.GetString();
    }
}

// The ARM scope of the project (for Julie's RBAC) can no longer be derived
// opportunistically from the Bing connection (removed). It is built
// explicitly from the configured subscription/resource group plus the
// account/project name, which are already embedded in FoundryProjectEndpoint.
var subscriptionId = config["SubscriptionId"];
var resourceGroupName = config["ResourceGroupName"];

string? projectScope = null;
if (!string.IsNullOrWhiteSpace(subscriptionId) && !string.IsNullOrWhiteSpace(resourceGroupName)
    && !subscriptionId.StartsWith('<') && !resourceGroupName.StartsWith('<'))
{
    var foundryUri = new Uri(foundryEndpoint);
    var accountName = foundryUri.Host.Split('.')[0];
    var projectName = foundryUri.AbsolutePath.TrimEnd('/').Split('/')[^1];
    projectScope = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}"
                 + $"/providers/Microsoft.CognitiveServices/accounts/{accountName}/projects/{projectName}";
}

if (juliePrincipalId is null || projectScope is null)
{
    Console.WriteLine("[RBAC] Could not resolve Julie's identity or the project scope.");
    Console.WriteLine("[RBAC] Assign the 'Foundry User' role to Julie manually before chatting.");
}
else
{
    await AssignFoundryUserAsync(projectScope, juliePrincipalId);
}

async Task AssignFoundryUserAsync(string scope, string principalId)
{
    Console.WriteLine($"[RBAC] Granting 'Foundry User' to Julie's identity {principalId}...");
    var manualCommand = $"az role assignment create --role \"Foundry User\" "
                      + $"--assignee-object-id {principalId} --assignee-principal-type ServicePrincipal "
                      + $"--scope \"{scope}\"";
    try
    {
        var subscriptionId = scope.Split('/')[2];
        var armToken = await new DefaultAzureCredential().GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]), default);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", armToken.Token);

        var payload = JsonSerializer.Serialize(new
        {
            properties = new
            {
                roleDefinitionId = $"/subscriptions/{subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{foundryUserRoleId}",
                principalId,
                principalType = "ServicePrincipal"
            }
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var armResponse = await http.PutAsync(
            $"https://management.azure.com{scope}/providers/Microsoft.Authorization/roleAssignments/{Guid.NewGuid()}?api-version=2022-04-01",
            content);

        if (armResponse.IsSuccessStatusCode)
            Console.WriteLine("[RBAC] Role assigned. It may take about a minute to take effect.");
        else if (armResponse.StatusCode == HttpStatusCode.Conflict)
            Console.WriteLine("[RBAC] Julie already had the role.");
        else
        {
            Console.WriteLine($"[RBAC] Assignment failed ({(int)armResponse.StatusCode}): {await armResponse.Content.ReadAsStringAsync()}");
            Console.WriteLine($"[RBAC] Run it manually: {manualCommand}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[RBAC] Assignment failed: {ex.Message}");
        Console.WriteLine($"[RBAC] Run it manually: {manualCommand}");
    }
}

Console.WriteLine();
Console.WriteLine("[Foundry] All agents are ready.");

// =====================================================================
//  PHASE 2: Interactive chat with Julie
// =====================================================================

ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForAgentEndpoint(julieAgentName);

Console.WriteLine();
Console.WriteLine("=== Chat with Julie (type 'exit' to quit) ===");
Console.WriteLine("Example: 'Create a campaign for customers who have purchased bicycles'");
Console.WriteLine();

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    Console.Write("Julie: ");
    try
    {
        ResponseResult response = responseClient.CreateResponse(input);

        // --- DEBUG ---
        Console.WriteLine();
        Console.WriteLine($"  [DEBUG] Status: {response.Status}");

        // Serialize full response to JSON to inspect structure
        try
        {
            var jsonOpts = new JsonSerializerOptions { WriteIndented = true, MaxDepth = 10 };
            var responseJson = JsonSerializer.Serialize(response, jsonOpts);
            Console.WriteLine($"  [DEBUG] Response JSON ({responseJson.Length} chars):");
            Console.WriteLine(responseJson.Length > 3000 ? responseJson[..3000] + "\n  ... (truncated)" : responseJson);
        }
        catch (Exception serEx)
        {
            Console.WriteLine($"  [DEBUG] Could not serialize response: {serEx.Message}");
            // Fallback: dump properties via reflection
            foreach (var prop in response.GetType().GetProperties())
            {
                try
                {
                    var val = prop.GetValue(response);
                    var valStr = val?.ToString() ?? "(null)";
                    Console.WriteLine($"  [DEBUG] {prop.Name} ({prop.PropertyType.Name}): {(valStr.Length > 200 ? valStr[..200] + "..." : valStr)}");
                }
                catch { Console.WriteLine($"  [DEBUG] {prop.Name}: <error reading>"); }
            }
        }

        var outputText = response.GetOutputText();
        if (!string.IsNullOrEmpty(outputText))
        {
            Console.WriteLine();
            Console.WriteLine(outputText);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("[No output text returned by the agent]");
        }
        // --- FIN DEBUG ---
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
Console.WriteLine("[Foundry] Agents remain available in Microsoft Foundry.");
