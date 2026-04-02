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
//  Anders - Agente Executor (Microsoft Foundry - nova experiência)
//
//  Esta versão usa o SDK Azure.AI.Projects + Azure.AI.Projects.OpenAI
//  com a API de Responses (nova experiência do Microsoft Foundry).
//
//  A ferramenta OpenAPI é configurada via protocol method (BinaryContent)
//  pois os tipos OpenApiAgentTool são internos no SDK 1.2.x.
// =====================================================================

// --- Carregar configuração ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("Ausente FoundryProjectEndpoint no appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("Ausente ModelDeploymentName no appsettings.json");
var functionAppBaseUrl = config["FunctionAppBaseUrl"]
    ?? throw new InvalidOperationException("Ausente FunctionAppBaseUrl no appsettings.json");
var agentName = "Anders";

// =====================================================================
//  FASE 1: Obter a especificação OpenAPI da Function App
// =====================================================================

Console.WriteLine("[OpenAPI] Baixando especificação da Function App...");

var httpClient = new HttpClient();
var openApiSpecUrl = $"{functionAppBaseUrl}/openapi/v3.json";
var openApiSpec = await httpClient.GetStringAsync(openApiSpecUrl);

Console.WriteLine($"[OpenAPI] Especificação baixada ({openApiSpec.Length} bytes)");

// =====================================================================
//  FASE 2: Criar agente com ferramenta OpenAPI (protocol method)
// =====================================================================

// Instruções do agente Anders
var andersInstructions = """
    Você é Anders, o agente executor da Contoso Retail.

    Sua responsabilidade é executar ações operacionais concretas quando solicitado.
    Sua principal capacidade é gerar relatórios de pedidos de compra de clientes
    usando a API da Contoso Retail disponível como ferramenta OpenAPI.

    Quando receber dados de pedidos, você deve construir o JSON do request body
    com EXATAMENTE este schema para invocar o endpoint ordersReporter:

    {
      "customerName": "Nome do Cliente",
      "startDate": "YYYY-MM-DD",
      "endDate": "YYYY-MM-DD",
      "orders": [
        {
          "orderNumber": "código do pedido",
          "orderDate": "YYYY-MM-DD",
          "orderLineNumber": 1,
          "productName": "nome do produto",
          "brandName": "nome da marca",
          "categoryName": "nome da categoria",
          "quantity": 1.0,
          "unitPrice": 0.00,
          "lineTotal": 0.00
        }
      ]
    }

    Regras:
    - TODOS os campos são obrigatórios para cada linha de pedido.
    - Se um pedido tiver múltiplos produtos, cada produto é um elemento
      separado no array "orders" com o mesmo "orderNumber" e "orderDate"
      mas diferente "orderLineNumber" (sequencial: 1, 2, 3...).
    - As datas devem estar no formato ISO: YYYY-MM-DD.
    - "quantity", "unitPrice" e "lineTotal" são numéricos (double).

    Sempre confirme a ação realizada ao usuário, incluindo a URL do relatório.
    Se os dados forem insuficientes ou inválidos, explique o que falta.
    Responda em português.
    """;

// Cliente do projeto Foundry (nova experiência)
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

// Verificar se o agente já existe
bool shouldCreateAgent = true;
AgentRecord? existingAgent = null;

Console.WriteLine($"[Foundry] Procurando agente existente '{agentName}'...");
try
{
    existingAgent = projectClient.Agents.GetAgent(agentName);
    Console.WriteLine($"[Foundry] Agente encontrado: {existingAgent.Name} (ID: {existingAgent.Id})");
    Console.Write("[Foundry] Deseja substituir por uma nova versão? (s/N): ");
    var answer = Console.ReadLine();
    shouldCreateAgent = answer?.Trim().Equals("s", StringComparison.OrdinalIgnoreCase) == true
                     || answer?.Trim().Equals("sim", StringComparison.OrdinalIgnoreCase) == true;

    if (!shouldCreateAgent)
    {
        Console.WriteLine("[Foundry] Agente existente mantido.");
    }
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] Nenhum agente encontrado com nome '{agentName}'. Um novo será criado.");
}

AgentRecord agentRecord;

if (shouldCreateAgent)
{
    // Construir o JSON com a definição do agente incluindo a ferramenta OpenAPI
    // (os tipos OpenApiAgentTool são internos; usa-se protocol method com BinaryContent)
    Console.WriteLine("[Foundry] Criando/atualizando agente Anders com ferramenta OpenAPI...");

    var openApiSpecJson = JsonSerializer.Deserialize<JsonElement>(openApiSpec);

    var agentDefinitionJson = new
    {
        definition = new
        {
            kind = "prompt",
            model = modelDeployment,
            instructions = andersInstructions,
            tools = new object[]
            {
                new
                {
                    type = "openapi",
                    openapi = new
                    {
                        name = "ContosoRetailAPI",
                        description = "API da Contoso Retail para gerar relatórios de pedidos de compra",
                        spec = openApiSpecJson,
                        auth = new { type = "anonymous" }
                    }
                }
            }
        }
    };

    var jsonContent = JsonSerializer.Serialize(agentDefinitionJson, new JsonSerializerOptions { WriteIndented = false });
    var result = await projectClient.Agents.CreateAgentVersionAsync(
        agentName,
        BinaryContent.Create(BinaryData.FromString(jsonContent)),
        new RequestOptions());

    // Processar resposta para obter informações do agente
    var responseJson = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
    var version = responseJson.RootElement.TryGetProperty("version", out var vProp) ? vProp.GetString() : "?";
    Console.WriteLine($"[Foundry] Agente criado/atualizado: {agentName} (v{version})");
}

// Obter o agente registrado
agentRecord = projectClient.Agents.GetAgent(agentName);
Console.WriteLine($"[Foundry] Agente obtido: {agentRecord.Name} (ID: {agentRecord.Id})");

// =====================================================================
//  FASE 3: Interagir com o agente (Responses API + Conversations)
// =====================================================================

// Criar conversa para multi-turn
ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
Console.WriteLine($"[Foundry] Conversa criada: {conversation.Id}");

// Obter cliente de Responses vinculado ao agente e à conversa
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: agentName,
    defaultConversationId: conversation.Id);

Console.WriteLine();
Console.WriteLine("=== Chat com Anders (digite 'sair' para terminar) ===");
Console.WriteLine();

while (true)
{
    Console.Write("Você: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("sair", StringComparison.OrdinalIgnoreCase))
        break;

    // Enviar mensagem e obter resposta do agente
    Console.Write("Anders: ");
    try
    {
        ResponseResult response = responseClient.CreateResponse(input);
        Console.WriteLine(response.GetOutputText());
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[Erro] {ex.Message}");
    }

    Console.WriteLine();
}

Console.WriteLine("[Foundry] Chat finalizado.");
Console.WriteLine($"[Foundry] O agente '{agentName}' permanece disponível.");
