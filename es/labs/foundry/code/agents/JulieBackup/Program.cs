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
//  JulieBackup — Agente orquestador de campañas (prompt, no workflow)
//
//  Versión backup de Julie que funciona como agente NORMAL
//  (PromptAgentDefinition) con function tools. Evita el tipo workflow
//  para mayor estabilidad en despliegue y operación.
//
//  SqlAgent y MarketingAgent se exponen como function tools.
//  El Program.cs intercepta las llamadas a funciones y las redirige
//  a los agentes reales en Foundry usando conversaciones independientes.
//
//  Uso: dotnet run
// =====================================================================

// --- Cargar configuración ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("Falta FoundryProjectEndpoint en appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("Falta ModelDeploymentName en appsettings.json");

// --- Cliente del proyecto Foundry ---
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

Console.WriteLine("[Foundry] Verificando que los agentes dependientes existen...");

foreach (var dependentAgent in new[] { sqlAgentName, marketingAgentName })
{
    try
    {
        projectClient.Agents.GetAgent(dependentAgent);
        Console.WriteLine($"  ✓ '{dependentAgent}' encontrado");
    }
    catch (ClientResultException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"  ✗ '{dependentAgent}' NO encontrado en Foundry.");
        Console.WriteLine($"    Cree el agente primero y vuelva a ejecutar JulieBackup.");
        Console.WriteLine();
        Console.WriteLine("[Abortado] No se puede crear JulieBackup sin sus agentes dependientes.");
        return;
    }
}

Console.WriteLine();

// =====================================================================
//  FASE 2: Crear el agente JulieBackup (prompt + function tools)
// =====================================================================

var julieInstructions = """
    Eres JulieBackup, la agente planificadora y orquestadora de campañas de marketing
    de Contoso Retail.

    Tu responsabilidad es coordinar la creación de campañas de marketing
    personalizadas para segmentos específicos de clientes.

    Dispones de dos herramientas:
    - consultar_clientes: consulta la base de datos para obtener clientes
      de un segmento específico (retorna FirstName, LastName, PrimaryEmail,
      FavoriteCategory).
    - generar_mensaje_marketing: genera un mensaje de marketing personalizado
      para un cliente dado su nombre y categoría favorita.

    Cuando recibas una solicitud de campaña sigue estos pasos:

    1. EXTRACCIÓN: Analiza el prompt del usuario y extrae la descripción
       del segmento de clientes.

    2. CONSULTA DE CLIENTES: Invoca consultar_clientes con la descripción
       del segmento. Recibirás los datos de clientes.

    3. MARKETING PERSONALIZADO: Para CADA cliente retornado, invoca
       generar_mensaje_marketing con su nombre completo y categoría favorita.

    4. ORGANIZACIÓN FINAL: Con todos los mensajes generados, organiza el
       resultado como un JSON de campaña:

    ```json
    {
      "campaign": "Nombre descriptivo de la campaña",
      "generatedAt": "YYYY-MM-DDTHH:mm:ss",
      "totalEmails": N,
      "emails": [
        {
          "to": "email@ejemplo.com",
          "customerName": "Nombre Apellido",
          "favoriteCategory": "Categoría",
          "subject": "Asunto del correo atractivo",
          "body": "Mensaje de marketing personalizado"
        }
      ]
    }
    ```

    REGLAS:
    - Responde siempre en español.
    - Si algún cliente no tiene email, omítelo del resultado.
    - Genera un nombre descriptivo para la campaña basado en el segmento.
    """;

// Definir function tools que representan a SqlAgent y MarketingAgent
var consultarClientesParams = BinaryData.FromObjectAsJson(new
{
    type = "object",
    properties = new
    {
        descripcion_segmento = new
        {
            type = "string",
            description = "Descripción en lenguaje natural del segmento de clientes a consultar. Ejemplo: 'clientes que han comprado bicicletas en el último año'"
        }
    },
    required = new[] { "descripcion_segmento" }
});

var generarMensajeParams = BinaryData.FromObjectAsJson(new
{
    type = "object",
    properties = new
    {
        nombre_cliente = new
        {
            type = "string",
            description = "Nombre completo del cliente (FirstName LastName)"
        },
        categoria_favorita = new
        {
            type = "string",
            description = "Categoría de producto favorita del cliente (ej: Bikes, Clothing, Accessories, Components)"
        }
    },
    required = new[] { "nombre_cliente", "categoria_favorita" }
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
            functionDescription: "Consulta la base de datos de Contoso Retail para obtener clientes que cumplen con un segmento dado. Retorna una lista con FirstName, LastName, PrimaryEmail y FavoriteCategory."
        ).AsAgentTool(),
        ResponseTool.CreateFunctionTool(
            functionName: "generar_mensaje_marketing",
            functionParameters: generarMensajeParams,
            strictModeEnabled: false,
            functionDescription: "Genera un mensaje de marketing personalizado para un cliente, buscando eventos relevantes en Bing según su categoría favorita."
        ).AsAgentTool()
    }
};

// Verificar si JulieBackup ya existe
Console.WriteLine($"[Foundry] Buscando agente '{julieBackupAgentName}'...");
AgentRecord? existingAgent = null;
bool shouldCreate = true;

try
{
    existingAgent = projectClient.Agents.GetAgent(julieBackupAgentName);
    Console.WriteLine($"[Foundry] Agente '{julieBackupAgentName}' encontrado");
    Console.Write($"[Foundry] ¿Desea sobreescribir con una nueva versión? (s/N): ");
    var answer = Console.ReadLine();
    shouldCreate = answer?.Trim().Equals("s", StringComparison.OrdinalIgnoreCase) == true
                || answer?.Trim().Equals("si", StringComparison.OrdinalIgnoreCase) == true
                || answer?.Trim().Equals("sí", StringComparison.OrdinalIgnoreCase) == true;

    if (!shouldCreate)
    {
        Console.WriteLine($"[Foundry] Se conserva '{julieBackupAgentName}' existente.");
        return;
    }
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] Agente '{julieBackupAgentName}' no encontrado. Se creará uno nuevo.");
}

// Crear/actualizar JulieBackup
try
{
    Console.WriteLine($"[Foundry] Creando/actualizando agente '{julieBackupAgentName}'...");

    var result = await projectClient.Agents.CreateAgentVersionAsync(
        julieBackupAgentName,
        new AgentVersionCreationOptions(julieDefinition));

    var responseJson = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
    var version = responseJson.RootElement.TryGetProperty("version", out var vProp) ? vProp.GetString() : "?";
    Console.WriteLine($"[Foundry] Agente '{julieBackupAgentName}' creado exitosamente (v{version})");
}
catch (ClientResultException ex) when (ex.Status == 400 && existingAgent is not null)
{
    Console.WriteLine($"[Foundry] No se pudo crear nueva versión: {ex.Message}");
    Console.WriteLine($"[Foundry] Se reutilizará la versión existente.");
}

Console.WriteLine();
Console.WriteLine("[Foundry] Agente listo. Iniciando chat interactivo...");

// =====================================================================
//  FASE 3: Chat interactivo con manejo de function calls
//
//  Cuando JulieBackup invoca consultar_clientes → redirigimos a SqlAgent
//  Cuando JulieBackup invoca generar_mensaje_marketing → redirigimos a MarketingAgent
// =====================================================================

ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
Console.WriteLine($"[Foundry] Conversación creada: {conversation.Id}");

ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: julieBackupAgentName,
    defaultConversationId: conversation.Id);

Console.WriteLine();
Console.WriteLine("=== Chat con JulieBackup (escribe 'salir' para terminar) ===");
Console.WriteLine("Ejemplo: 'Crea una campaña para clientes que hayan comprado bicicletas'");
Console.WriteLine();

// --- Helper: enviar mensaje a un sub-agente y obtener respuesta ---
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
        var error = $"Error al invocar {agentName}: {ex.Message}";
        Console.WriteLine($"  [✗ {agentName}] {error}");
        return error;
    }
}

while (true)
{
    Console.Write("Tú: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("salir", StringComparison.OrdinalIgnoreCase))
        break;

    try
    {
        // Enviar mensaje a JulieBackup
        ResponseResult response = responseClient.CreateResponse(input);

        // Loop de function calls: JulieBackup puede pedir N function calls
        while (true)
        {
            // Recoger todas las function calls pendientes
            var functionCalls = response.OutputItems.OfType<FunctionCallResponseItem>().ToList();

            if (functionCalls.Count == 0)
                break; // No hay más function calls, salir del loop

            Console.WriteLine($"  [JulieBackup] Invocando {functionCalls.Count} herramienta(s)...");

            var functionOutputs = new List<ResponseItem>();

            foreach (var funcCall in functionCalls)
            {
                var funcArgs = funcCall.FunctionArguments?.ToString() ?? "{}";
                var argsJson = JsonDocument.Parse(funcArgs).RootElement;

                string result;
                switch (funcCall.FunctionName)
                {
                    case "consultar_clientes":
                        var segmento = argsJson.TryGetProperty("descripcion_segmento", out var seg)
                            ? seg.GetString() ?? ""
                            : funcArgs;
                        result = await InvokeSubAgent(sqlAgentName, segmento);
                        break;

                    case "generar_mensaje_marketing":
                        var nombre = argsJson.TryGetProperty("nombre_cliente", out var n)
                            ? n.GetString() ?? ""
                            : "";
                        var categoria = argsJson.TryGetProperty("categoria_favorita", out var c)
                            ? c.GetString() ?? ""
                            : "";
                        var prompt = $"Genera un mensaje de marketing personalizado para el cliente {nombre} cuya categoría favorita es {categoria}.";
                        result = await InvokeSubAgent(marketingAgentName, prompt);
                        break;

                    default:
                        result = $"Función desconocida: {funcCall.FunctionName}";
                        break;
                }

                functionOutputs.Add(
                    ResponseItem.CreateFunctionCallOutputItem(funcCall.CallId, result));
            }

            // Enviar resultados de funciones de vuelta a JulieBackup
            // No pasar previousResponseId porque ProjectResponsesClient ya inyecta conversationId
            response = responseClient.CreateResponse(functionOutputs, previousResponseId: null);
        }

        // Mostrar respuesta final de JulieBackup
        var outputText = response.GetOutputText();
        if (!string.IsNullOrEmpty(outputText))
        {
            Console.WriteLine();
            Console.WriteLine($"JulieBackup: {outputText}");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("[JulieBackup] Sin texto de salida.");
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
Console.WriteLine("[Foundry] El agente JulieBackup permanece disponible en Microsoft Foundry.");
