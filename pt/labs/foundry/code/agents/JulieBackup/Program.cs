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
//  JulieBackup — Agente orquestrador de campanhas (prompt, não workflow)
//
//  Versão backup de Julie que funciona como agente NORMAL
//  (PromptAgentDefinition) com function tools. Evita o tipo workflow
//  para maior estabilidade na implantação e operação.
//
//  SqlAgent e MarketingAgent são expostos como function tools.
//  O Program.cs intercepta as chamadas de funções e as redireciona
//  para os agentes reais no Foundry usando conversas independentes.
//
//  Uso: dotnet run
// =====================================================================

// --- Carregar configuração ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("Ausente FoundryProjectEndpoint no appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("Ausente ModelDeploymentName no appsettings.json");

// --- Cliente do projeto Foundry ---
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

Console.WriteLine();
Console.WriteLine("========================================================");
Console.WriteLine(" JulieBackup - Agente orquestador (prompt + functions)");
Console.WriteLine("========================================================");
Console.WriteLine();

// =====================================================================
//  FASE 1: Verificar que SqlAgent y MarketingAgent existen en Foundry
// =====================================================================

const string sqlAgentName = "SqlAgent";
const string marketingAgentName = "MarketingAgent";
const string julieBackupAgentName = "JulieBackup";

Console.WriteLine("[Foundry] Verificando que os agentes dependentes existem...");

foreach (var dependentAgent in new[] { sqlAgentName, marketingAgentName })
{
    try
    {
        projectClient.Agents.GetAgent(dependentAgent);
        Console.WriteLine($"  ✓ '{dependentAgent}' encontrado");
    }
    catch (ClientResultException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"  ✗ '{dependentAgent}' NÃO encontrado no Foundry.");
        Console.WriteLine($"    Crie o agente primeiro e execute JulieBackup novamente.");
        Console.WriteLine();
        Console.WriteLine("[Abortado] Não é possível criar JulieBackup sem seus agentes dependentes.");
        return;
    }
}

Console.WriteLine();

// =====================================================================
//  FASE 2: Criar o agente JulieBackup (prompt + function tools)
// =====================================================================

var julieInstructions = """
    Você é JulieBackup, a agente planejadora e orquestradora de campanhas de marketing
    da Contoso Retail.

    Sua responsabilidade é coordenar a criação de campanhas de marketing
    personalizadas para segmentos específicos de clientes.

    Você dispõe de duas ferramentas:
    - consultar_clientes: consulta o banco de dados para obter clientes
      de um segmento específico (retorna FirstName, LastName, PrimaryEmail,
      FavoriteCategory).
    - gerar_mensagem_marketing: gera uma mensagem de marketing personalizada
      para um cliente dado seu nome e categoria favorita.

    Quando receber uma solicitação de campanha, siga estes passos:

    1. EXTRAÇÃO: Analise o prompt do usuário e extraia a descrição
       do segmento de clientes.

    2. CONSULTA DE CLIENTES: Invoque consultar_clientes com a descrição
       do segmento. Você receberá os dados dos clientes.

    3. MARKETING PERSONALIZADO: Para CADA cliente retornado, invoque
       gerar_mensagem_marketing com seu nome completo e categoria favorita.

    4. ORGANIZAÇÃO FINAL: Com todas as mensagens geradas, organize o
       resultado como um JSON de campanha:

    ```json
    {
      "campaign": "Nome descritivo da campanha",
      "generatedAt": "YYYY-MM-DDTHH:mm:ss",
      "totalEmails": N,
      "emails": [
        {
          "to": "email@exemplo.com",
          "customerName": "Nome Sobrenome",
          "favoriteCategory": "Categoria",
          "subject": "Assunto do e-mail atraente",
          "body": "Mensagem de marketing personalizada"
        }
      ]
    }
    ```

    REGRAS:
    - Responda sempre em português.
    - Se algum cliente não tiver e-mail, omita-o do resultado.
    - Gere um nome descritivo para a campanha baseado no segmento.
    """;

// Definir function tools representando SqlAgent e MarketingAgent
var consultarClientesParams = BinaryData.FromObjectAsJson(new
{
    type = "object",
    properties = new
    {
        descricao_segmento = new
        {
            type = "string",
            description = "Descrição em linguagem natural do segmento de clientes a consultar. Exemplo: 'clientes que compraram bicicletas no último ano'"
        }
    },
    required = new[] { "descricao_segmento" }
});

var gerarMensagemParams = BinaryData.FromObjectAsJson(new
{
    type = "object",
    properties = new
    {
        nome_cliente = new
        {
            type = "string",
            description = "Nome completo do cliente (FirstName LastName)"
        },
        categoria_favorita = new
        {
            type = "string",
            description = "Categoria de produto favorita do cliente (ex: Bikes, Clothing, Accessories, Components)"
        }
    },
    required = new[] { "nome_cliente", "categoria_favorita" }
});

var julieDefinition = new PromptAgentDefinition(modelDeployment)
{
    Instructions = julieInstructions,
    Tools =
    {
        ResponseTool.CreateFunctionTool(
            functionName: "consultar_clientes",
            functionParameters: consultarClientesParams,
            strictModeEnabled: false,
            functionDescription: "Consulta o banco de dados da Contoso Retail para obter clientes que atendem a um segmento. Retorna uma lista com FirstName, LastName, PrimaryEmail e FavoriteCategory."
        ).AsAgentTool(),
        ResponseTool.CreateFunctionTool(
            functionName: "gerar_mensagem_marketing",
            functionParameters: gerarMensagemParams,
            strictModeEnabled: false,
            functionDescription: "Gera uma mensagem de marketing personalizada para um cliente, buscando eventos relevantes no Bing com base em sua categoria favorita."
        ).AsAgentTool()
    }
};

// Verificar se JulieBackup já existe
Console.WriteLine($"[Foundry] Procurando agente '{julieBackupAgentName}'...");
AgentRecord? existingAgent = null;
bool shouldCreate = true;

try
{
    existingAgent = projectClient.Agents.GetAgent(julieBackupAgentName);
    Console.WriteLine($"[Foundry] Agente '{julieBackupAgentName}' encontrado");
    Console.Write($"[Foundry] Deseja substituir por uma nova versão? (s/N): ");
    var answer = Console.ReadLine();
    shouldCreate = answer?.Trim().Equals("s", StringComparison.OrdinalIgnoreCase) == true
                || answer?.Trim().Equals("sim", StringComparison.OrdinalIgnoreCase) == true;

    if (!shouldCreate)
    {
        Console.WriteLine($"[Foundry] Agente '{julieBackupAgentName}' mantido.");
        return;
    }
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] Agente '{julieBackupAgentName}' não encontrado. Um novo será criado.");
}

// Criar/atualizar JulieBackup
try
{
    Console.WriteLine($"[Foundry] Criando/atualizando agente '{julieBackupAgentName}'...");

    var result = await projectClient.Agents.CreateAgentVersionAsync(
        julieBackupAgentName,
        new AgentVersionCreationOptions(julieDefinition));

    var responseJson = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
    var version = responseJson.RootElement.TryGetProperty("version", out var vProp) ? vProp.GetString() : "?";
    Console.WriteLine($"[Foundry] Agente '{julieBackupAgentName}' criado com sucesso (v{version})");
}
catch (ClientResultException ex) when (ex.Status == 400 && existingAgent is not null)
{
    Console.WriteLine($"[Foundry] Não foi possível criar nova versão: {ex.Message}");
    Console.WriteLine($"[Foundry] A versão existente será reutilizada.");
}

Console.WriteLine();
Console.WriteLine("[Foundry] Agente pronto. Iniciando chat interativo...");

// =====================================================================
//  FASE 3: Chat interativo com tratamento de function calls
//
//  Quando JulieBackup invoca consultar_clientes → redirecionamos para SqlAgent
//  Quando JulieBackup invoca gerar_mensagem_marketing → redirecionamos para MarketingAgent
// =====================================================================

ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
Console.WriteLine($"[Foundry] Conversa criada: {conversation.Id}");

ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: julieBackupAgentName,
    defaultConversationId: conversation.Id);

Console.WriteLine();
Console.WriteLine("=== Chat com JulieBackup (digite 'sair' para terminar) ===");
Console.WriteLine("Exemplo: 'Crie uma campanha para clientes que compraram bicicletas'");
Console.WriteLine();

// --- Helper: enviar mensagem a um sub-agente e obter resposta ---
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
        var error = $"Erro ao invocar {agentName}: {ex.Message}";
        Console.WriteLine($"  [✗ {agentName}] {error}");
        return error;
    }
}

while (true)
{
    Console.Write("Você: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("sair", StringComparison.OrdinalIgnoreCase))
        break;

    try
    {
        // Enviar mensagem a JulieBackup
        ResponseResult response = responseClient.CreateResponse(input);

        // Loop de function calls: JulieBackup pode solicitar N function calls
        while (true)
        {
            // Coletar todas as function calls pendentes
            var functionCalls = response.OutputItems.OfType<FunctionCallResponseItem>().ToList();

            if (functionCalls.Count == 0)
                break; // Sem mais function calls, sair do loop

            Console.WriteLine($"  [JulieBackup] Invocando {functionCalls.Count} ferramenta(s)...");

            var functionOutputs = new List<ResponseItem>();

            foreach (var funcCall in functionCalls)
            {
                var funcArgs = funcCall.FunctionArguments?.ToString() ?? "{}";
                var argsJson = JsonDocument.Parse(funcArgs).RootElement;

                string result;
                switch (funcCall.FunctionName)
                {
                    case "consultar_clientes":
                        var segmento = argsJson.TryGetProperty("descricao_segmento", out var seg)
                            ? seg.GetString() ?? ""
                            : funcArgs;
                        result = await InvokeSubAgent(sqlAgentName, segmento);
                        break;

                    case "gerar_mensagem_marketing":
                        var nome = argsJson.TryGetProperty("nome_cliente", out var n)
                            ? n.GetString() ?? ""
                            : "";
                        var categoria = argsJson.TryGetProperty("categoria_favorita", out var c)
                            ? c.GetString() ?? ""
                            : "";
                        var prompt = $"Gere uma mensagem de marketing personalizada para o cliente {nome} cuja categoria favorita é {categoria}.";
                        result = await InvokeSubAgent(marketingAgentName, prompt);
                        break;

                    default:
                        result = $"Função desconhecida: {funcCall.FunctionName}";
                        break;
                }

                functionOutputs.Add(
                    ResponseItem.CreateFunctionCallOutputItem(funcCall.CallId, result));
            }

            // Enviar resultados das funções de volta a JulieBackup
            // Não passar previousResponseId pois ProjectResponsesClient já injeta conversationId
            response = responseClient.CreateResponse(functionOutputs, previousResponseId: null);
        }

        // Exibir resposta final de JulieBackup
        var outputText = response.GetOutputText();
        if (!string.IsNullOrEmpty(outputText))
        {
            Console.WriteLine();
            Console.WriteLine($"JulieBackup: {outputText}");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("[JulieBackup] Sem texto de saída.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[Erro] {ex.Message}");
        if (ex.InnerException != null)
            Console.WriteLine($"  [Inner] {ex.InnerException.Message}");
    }

    Console.WriteLine();
}

Console.WriteLine("[Foundry] Chat finalizado.");
Console.WriteLine("[Foundry] O agente JulieBackup permanece disponível no Microsoft Foundry.");
