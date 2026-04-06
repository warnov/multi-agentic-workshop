// =====================================================================
//  JulieAgent — Agente orquestrador de campanhas de marketing
//
//  Julie é um agente do tipo workflow que coordena:
//  1. SqlAgent (type: agent) — gera T-SQL a partir de linguagem natural
//  2. Function App (type: openapi) — executa o T-SQL contra o BD
//  3. MarketingAgent (type: agent) — gera mensagens personalizadas
//
//  O resultado final é um JSON de campanha com e-mails.
//
//  Ferramentas:
//    - SqlAgent        → agente que gera a consulta T-SQL
//    - SqlExecutor     → OpenAPI tool que executa o SQL contra o BD
//    - MarketingAgent  → agente que gera mensagens de marketing
//
//  A URL da Function App é configurada em appsettings.json
//  (FunctionAppBaseUrl). Se não estiver configurada, Julie é criada
//  sem a ferramenta OpenAPI (aguardando implantação).
// =====================================================================

using System.Text.Json;
using Azure.AI.Projects.OpenAI;

namespace JulieAgent;

public static class JulieOrchestrator
{
    public const string Name = "Julie";

    public static string Instructions => """
        Você é Julie, a agente planejadora e orquestradora de campanhas de marketing
        da Contoso Retail.

        Sua responsabilidade é coordenar a criação de campanhas de marketing
        personalizadas para segmentos específicos de clientes.

        Quando receber uma solicitação de campanha, siga estes passos:

        1. EXTRAÇÃO: Analise o prompt do usuário e extraia a descrição
           do segmento de clientes. Resuma essa descrição em uma frase clara.

        2. GERAÇÃO SQL: Invoque o SqlAgent passando a descrição do segmento.
           SqlAgent retornará uma consulta T-SQL.

        3. EXECUÇÃO SQL: Envie o T-SQL para sua ferramenta OpenAPI (SqlExecutor)
           para executá-la contra o banco de dados. A ferramenta retornará os
           resultados como dados de clientes.

        4. MARKETING PERSONALIZADO: Para CADA cliente retornado, invoque o
           MarketingAgent passando o nome do cliente e sua categoria favorita.
           MarketingAgent buscará eventos relevantes no Bing e gerará uma mensagem
           personalizada.

        5. ORGANIZAÇÃO FINAL: Com todas as mensagens geradas, organize o
           resultado como um JSON de campanha com o seguinte formato:

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
              "subject": "Assunto do e-mail gerado automaticamente",
              "body": "Mensagem de marketing personalizada"
            }
          ]
        }
        ```

        REGRAS:
        - O campo "subject" deve ser um assunto de e-mail atraente e relevante.
        - O campo "body" é a mensagem gerada pelo MarketingAgent para esse cliente.
        - Responda sempre em português.
        - Se algum cliente não tiver e-mail, omita-o do resultado.
        - Gere um nome descritivo para a campanha baseado no segmento.
        """;

    /// <summary>
    /// Constrói a definição do agente Julie como WorkflowAgentDefinition
    /// usando CSDL YAML, compatível com a API atual.
    /// </summary>
    public static WorkflowAgentDefinition GetAgentDefinition(string modelDeployment, JsonElement? openApiSpec = null)
    {
        _ = modelDeployment;
        _ = openApiSpec;

        var workflowYaml = $$"""
kind: workflow
trigger:
  kind: OnConversationStart
  id: julie_workflow
  actions:
    - kind: InvokeAzureAgent
      id: sql_step
      conversationId: =System.ConversationId
      agent:
        name: {{SqlAgent.Name}}
    - kind: InvokeAzureAgent
      id: marketing_step
      conversationId: =System.ConversationId
      agent:
        name: {{MarketingAgent.Name}}
    - kind: EndConversation
      id: end_conversation
name: {{Name}}
""";

        Console.WriteLine("[DEBUG] Workflow YAML que será enviado ao Foundry:");
        Console.WriteLine(workflowYaml);
        Console.WriteLine("[DEBUG] --- fim YAML ---");

        return ProjectsOpenAIModelFactory.WorkflowAgentDefinition(workflowYaml: workflowYaml);
    }
}
