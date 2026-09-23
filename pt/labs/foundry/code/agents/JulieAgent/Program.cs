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

#pragma warning disable AAIP001 // Azure.AI.Projects.Agents: Toolbox é uma API de pré-visualização
#pragma warning disable OPENAI001 // API de pré-visualização da OpenAI

// =====================================================================
//  Julie - Agente Orquestradora de Campanhas de Marketing
//  (Microsoft Foundry - nova experiência)
//
//  O Program.cs cuida SOMENTE de:
//  1. Criar/verificar os agentes de prompt no Microsoft Foundry
//     (SqlAgent e MarketingAgent)
//  2. Implantar Julie como agente hospedado a partir do código-fonte
//     do projeto JulieHosted
//  3. Abrir um chat interativo com Julie
//
//  Toda a orquestração é feita pela Julie internamente, já como código do
//  Agent Framework:
//    SqlAgent (tool) → gera T-SQL
//    SqlExecutor (OpenAPI tool) → executa SQL contra o banco de dados
//    MarketingAgent (tool) → gera mensagens personalizadas
//    Julie → organiza o resultado como JSON de campanha
// =====================================================================

// --- Carregar configuração ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("Falta FoundryProjectEndpoint em appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("Falta ModelDeploymentName em appsettings.json");

// URL base da Function App com o executor de consultas SQL.
// É configurada em appsettings.json quando a função estiver implantada.
var functionAppBaseUrl = config["FunctionAppBaseUrl"];

// --- Carregar estrutura do banco de dados ---
var dbStructurePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "db-structure.txt");
if (!File.Exists(dbStructurePath))
    dbStructurePath = Path.Combine(Directory.GetCurrentDirectory(), "db-structure.txt");
if (!File.Exists(dbStructurePath))
{
    throw new FileNotFoundException(
        "O arquivo db-structure.txt não foi encontrado. " +
        "Verifique se ele existe na pasta raiz do projeto JulieAgent.");
}
var dbStructure = File.ReadAllText(dbStructurePath);
Console.WriteLine($"[Config] Estrutura do banco carregada ({dbStructure.Length} caracteres)");

// --- (Opcional) Baixar a spec OpenAPI da Function App ---
JsonElement? openApiSpecJson = null;

if (!string.IsNullOrEmpty(functionAppBaseUrl) && !functionAppBaseUrl.StartsWith("<"))
{
    Console.WriteLine("[OpenAPI] Baixando a especificação da Function App...");
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
            Console.WriteLine($"[OpenAPI] Especificação baixada ({openApiSpec.Length} bytes)");
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenAPI] Tentativa {attempt}/{maxAttempts} falhou: {ex.Message}");
            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                continue;
            }

            Console.WriteLine("[OpenAPI] Julie será criada sem a ferramenta OpenAPI.");
        }
    }
}
else
{
    Console.WriteLine("[Config] FunctionAppBaseUrl não configurada.");
    Console.WriteLine("  → Julie será criada sem a ferramenta OpenAPI (execução SQL pendente).");
    Console.WriteLine("  → Configure FunctionAppBaseUrl em appsettings.json quando a Function App estiver implantada.");
}

// --- Cliente do projeto do Foundry ---
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

// =====================================================================
//  FASE 1: Criar/verificar os agentes no Microsoft Foundry
// =====================================================================

Console.WriteLine();
Console.WriteLine("========================================");
Console.WriteLine(" Julie - Orquestradora de Campanhas");
Console.WriteLine("========================================");
Console.WriteLine();

// --- Helper para criar ou reutilizar um agente (definição tipada) ---
var agentsClient = projectClient.AgentAdministrationClient;

// Versionar um agente legado preserva a identidade compartilhada do
// projeto, por isso o existente é apagado em vez de versionado.
async Task EnsureAgent(string agentName, ProjectsAgentDefinition agentDefinition)
{
    Console.WriteLine($"[Foundry] Procurando o agente '{agentName}'...");
    try
    {
        var existing = agentsClient.GetAgent(agentName);
        Console.WriteLine($"[Foundry] Agente '{agentName}' encontrado");
        Console.Write($"[Foundry] Apagar '{agentName}' e recriá-lo do zero? (s/N): ");
        var answer = Console.ReadLine()?.Trim();
        var shouldRecreate = string.Equals(answer, "s", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(answer, "sim", StringComparison.OrdinalIgnoreCase);

        if (!shouldRecreate)
        {
            Console.WriteLine($"[Foundry] O '{agentName}' existente será mantido.");
            return;
        }

        agentsClient.DeleteAgent(agentName);
        Console.WriteLine($"[Foundry] Agente '{agentName}' apagado.");
    }
    catch (ClientResultException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"[Foundry] Agente '{agentName}' não encontrado. Um novo será criado.");
    }

    ProjectsAgentVersion created = await agentsClient.CreateAgentVersionAsync(
        agentName,
        new ProjectsAgentVersionCreationOptions(agentDefinition));

    Console.WriteLine($"[Foundry] Agente '{agentName}' criado (v{created.Version})");

    // instance_identity é null em agentes legados e não nulo em agentes com
    // identidade própria do Entra, que é o que o Agent 365 sincroniza.
    using var agentJson = JsonDocument.Parse(agentsClient.GetAgent(agentName).GetRawResponse().Content.ToString());
    var hasIdentity = agentJson.RootElement.TryGetProperty("instance_identity", out var identity)
                      && identity.ValueKind != JsonValueKind.Null;
    Console.WriteLine(hasIdentity
        ? $"[Foundry] {agentName} instance_identity: {identity}"
        : $"[Foundry] {agentName} instance_identity: null/ausente -> agente legado, NÃO será sincronizado com o Agent 365.");
}


// --- Garantir que existe o Toolbox de Web Search usado pelo MarketingAgent ---
// Criar uma versão de Toolbox é uma chamada de data-plane (SDK), assim como
// criar um agente: não requer nenhum recurso ARM nem conexão com chave.
const string marketingToolboxName = "marketing-websearch-toolbox";
var toolboxesClient = agentsClient.GetAgentToolboxes();

Console.WriteLine($"[Foundry] Procurando o toolbox '{marketingToolboxName}'...");
try
{
    toolboxesClient.Get(marketingToolboxName);
    Console.WriteLine($"[Foundry] Toolbox '{marketingToolboxName}' já existe, será reutilizado.");
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] Toolbox '{marketingToolboxName}' não encontrado. Será criado.");
    await toolboxesClient.CreateVersionAsync(
        marketingToolboxName,
        tools: new List<ToolboxTool> { new WebSearchToolboxTool() },
        description: "Web Search para o MarketingAgent (substitui o Grounding with Bing Search)");
    Console.WriteLine($"[Foundry] Toolbox '{marketingToolboxName}' criado.");
}

// Endpoint "consumer": sempre serve a default_version, então promover uma
// versão nova do toolbox nunca exige tocar ou recompilar o MarketingAgent.
var marketingToolboxEndpoint = new Uri($"{foundryEndpoint}/toolboxes/{marketingToolboxName}/mcp?api-version=v1");

// Criar os dois subagentes de prompt que Julie orquestra
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, marketingToolboxEndpoint));

// =====================================================================
//  Julie: agente hospedado implantado a partir do código-fonte
// =====================================================================

const string julieAgentName = "Julie";

// O Foundry compila o código enviado remotamente, então localmente não são
// necessários nem Docker nem um Container Registry.
var hostedSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "JulieHosted"));

if (!Directory.Exists(hostedSourcePath))
    throw new DirectoryNotFoundException($"Código do agente hospedado não encontrado em {hostedSourcePath}");

// O kind de um agente é imutável: uma Julie herdada do modelo de workflow
// precisa ser apagada antes de poder ser recriada como agente hospedado.
try
{
    var existingJulie = agentsClient.GetAgent(julieAgentName);
    using var julieJson = JsonDocument.Parse(existingJulie.GetRawResponse().Content.ToString());
    var existingKind = julieJson.RootElement
        .GetProperty("versions").GetProperty("latest")
        .GetProperty("definition").GetProperty("kind").GetString();

    if (existingKind == "hosted")
    {
        Console.WriteLine($"[Foundry] O agente '{julieAgentName}' já é hospedado. Uma nova versão será adicionada.");
    }
    else
    {
        Console.WriteLine($"[Foundry] O agente '{julieAgentName}' existe com kind '{existingKind}', que não pode ser alterado no lugar.");
        Console.Write($"[Foundry] Apagar '{julieAgentName}' e reimplantá-lo como agente hospedado? (s/N): ");
        var julieAnswer = Console.ReadLine()?.Trim();
        var recreateJulie = string.Equals(julieAnswer, "s", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(julieAnswer, "sim", StringComparison.OrdinalIgnoreCase);

        if (!recreateJulie)
            throw new InvalidOperationException(
                $"É preciso apagar '{julieAgentName}' antes de implantá-lo como agente hospedado.");

        agentsClient.DeleteAgent(julieAgentName);
        Console.WriteLine($"[Foundry] Agente '{julieAgentName}' apagado.");
    }
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] Agente '{julieAgentName}' não encontrado. Um novo será criado.");
}

Console.WriteLine($"[Foundry] Enviando o código do agente hospedado Julie a partir de {hostedSourcePath}...");

// O SDK envia a pasta como está e a compilação remota falha com os artefatos
// de build locais, por isso bin/ e obj/ são removidos antes do empacotamento.
foreach (var stale in new[] { "bin", "obj" })
{
    var staleDir = Path.Combine(hostedSourcePath, stale);
    if (Directory.Exists(staleDir))
    {
        Directory.Delete(staleDir, recursive: true);
        Console.WriteLine($"[Foundry] Saída de build local '{stale}' removida antes do envio.");
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
// auto permite à Julie cair para clientes de demonstração claramente marcados quando falta o Fabric SQL.
julieDefinition.EnvironmentVariables.Add("JULIE_DATA_MODE", config["JulieDataMode"] ?? "auto");

ProjectsAgentVersion julieVersion = await agentsClient.CreateAgentVersionFromCodeAsync(
    agentName: julieAgentName,
    filePath: hostedSourcePath,
    metadata: new AgentVersionFromCodeMetadata(julieDefinition));

Console.WriteLine($"[Foundry] Versão {julieVersion.Version} da Julie criada. Aguardando o provisionamento...");

for (var attempt = 1; attempt <= 60; attempt++)
{
    await Task.Delay(TimeSpan.FromSeconds(10));
    julieVersion = await agentsClient.GetAgentVersionAsync(julieAgentName, julieVersion.Version);
    Console.WriteLine($"[Foundry] Status do provisionamento: {julieVersion.Status} ({attempt}/60)");

    if (julieVersion.Status == AgentVersionStatus.Active) break;
    if (julieVersion.Status == AgentVersionStatus.Failed)
        throw new InvalidOperationException("O provisionamento do agente hospedado Julie falhou.");
}

if (julieVersion.Status != AgentVersionStatus.Active)
    throw new TimeoutException("Tempo esgotado aguardando a Julie ficar ativa.");

await agentsClient.PatchAgentAsync(julieAgentName, new PatchAgentOptions
{
    AgentEndpoint = new AgentEndpointConfiguration
    {
        VersionSelector = new([new FixedRatioVersionSelectionRule(julieVersion.Version, 100)]),
        ProtocolConfiguration = new() { Responses = new ResponsesProtocolConfiguration() }
    }
});
Console.WriteLine($"[Foundry] Endpoint da Julie roteado para a versão {julieVersion.Version}");

// =====================================================================
//  Dar acesso ao projeto à identidade do agente hospedado
//
//  Um agente hospedado é executado com a sua própria identidade do Entra,
//  que é recriada junto com o objeto agente, então o papel é reatribuído a
//  cada execução em vez de ser um passo manual pontual.
//  É necessário 'Foundry User': 'Foundry Agent Consumer' concede apenas
//  endpoints/interact e a Julie recebe 403 em agents/read ao procurar
//  SqlAgent e MarketingAgent. A atribuição deve ter como alvo o escopo do
//  projeto; o escopo da conta sozinho não foi suficiente.
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

// O escopo ARM do projeto (para o RBAC da Julie) não pode mais ser derivado
// oportunisticamente da conexão do Bing (removida). É montado explicitamente
// a partir da assinatura/grupo de recursos configurados e do nome da
// conta/projeto, já embutidos em FoundryProjectEndpoint.
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
    Console.WriteLine("[RBAC] Não foi possível resolver a identidade da Julie ou o escopo do projeto.");
    Console.WriteLine("[RBAC] Atribua manualmente o papel 'Foundry User' à Julie antes de conversar.");
}
else
{
    await AssignFoundryUserAsync(projectScope, juliePrincipalId);
}

async Task AssignFoundryUserAsync(string scope, string principalId)
{
    Console.WriteLine($"[RBAC] Concedendo 'Foundry User' à identidade da Julie {principalId}...");
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
            Console.WriteLine("[RBAC] Papel atribuído. Pode levar cerca de um minuto para fazer efeito.");
        else if (armResponse.StatusCode == HttpStatusCode.Conflict)
            Console.WriteLine("[RBAC] A Julie já tinha o papel.");
        else
        {
            Console.WriteLine($"[RBAC] A atribuição falhou ({(int)armResponse.StatusCode}): {await armResponse.Content.ReadAsStringAsync()}");
            Console.WriteLine($"[RBAC] Execute manualmente: {manualCommand}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[RBAC] A atribuição falhou: {ex.Message}");
        Console.WriteLine($"[RBAC] Execute manualmente: {manualCommand}");
    }
}

Console.WriteLine();
Console.WriteLine("[Foundry] Todos os agentes estão prontos.");

// =====================================================================
//  FASE 2: Chat interativo com Julie
// =====================================================================

ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForAgentEndpoint(julieAgentName);

Console.WriteLine();
Console.WriteLine("=== Chat com Julie (digite 'sair' para terminar) ===");
Console.WriteLine("Exemplo: 'Crie uma campanha para clientes que compraram bicicletas'");
Console.WriteLine();

while (true)
{
    Console.Write("Você: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("sair", StringComparison.OrdinalIgnoreCase))
        break;

    Console.Write("Julie: ");
    try
    {
        ResponseResult response = responseClient.CreateResponse(input);

        Console.WriteLine();
        Console.WriteLine($"  [Status] {response.Status}");

        var outputText = response.GetOutputText();
        if (!string.IsNullOrEmpty(outputText))
        {
            Console.WriteLine();
            Console.WriteLine(outputText);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("[O agente não devolveu texto de saída]");
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

Console.WriteLine("[Foundry] Chat finalizado.");
Console.WriteLine("[Foundry] Os agentes permanecem disponíveis no Microsoft Foundry.");
