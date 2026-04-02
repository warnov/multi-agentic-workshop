# Lab 4: Julie Planner Agent

## Introdução

Neste laboratório você vai construir e validar a Julie como agente planejador de campanhas de marketing no Foundry. Julie é implementada como agente do tipo `workflow` e orquestra o fluxo com dois sub-agentes: `SqlAgent` e `MarketingAgent`. O `SqlAgent` pode usar a tool OpenAPI `SqlExecutor` (Function App `FxContosoRetail`) para executar SQL contra o banco e retornar os clientes segmentados. Neste laboratório, progressivamente, você configurará o ambiente, verificará permissões e a conexão SQL, e executará o fluxo end-to-end para obter a saída final de campanha em formato JSON.

## Continuidade do setup

Este laboratório pressupõe que você já concluiu:

- A implantação base de infraestrutura do Foundry (`labs/foundry/README.md`)
- O fluxo de dados no Fabric do **Lab 1** (`../fabric/lab01-data-setup.md`)

## Checklist rápido

### 1) Verificar valores de conexão SQL

Para o setup atualizado são usados estes valores:

- `FabricWarehouseSqlEndpoint`
- `FabricWarehouseDatabase`

São obtidos da connection string SQL do Warehouse do Fabric:

- `FabricWarehouseSqlEndpoint` = `Data Source` sem `,1433`
- `FabricWarehouseDatabase` = `Initial Catalog`

### 2) Alternativa se você não seguir toda a sequência de labs

Se você não estiver seguindo toda a sequência de laboratórios, para o Lab 4 você também pode usar um banco SQL standalone (por exemplo Azure SQL Database), ajustando esses dois valores para o host e nome do banco correspondentes.

### 3) Comportamento quando os valores do Fabric não são fornecidos

Se você não fornecer esses valores durante o setup, a implantação de infraestrutura não falha, mas a conexão SQL para o Lab 4 não é configurada automaticamente e deve ser ajustada manualmente na Function App.

## Configuração manual de permissões no Fabric (obrigatório para o Lab 4)

Após a implantação, certifique-se de que a Managed Identity da Function App tenha acesso ao workspace e ao banco SQL do `retail`.

### Parte A — Acesso ao Workspace

1. Abra o workspace onde o banco de dados `retail` foi implantado.
2. Vá em **Manage access**.
3. Clique em **Add people or groups**.
4. Pesquise e adicione a identidade da Function App.
- Nome esperado: `func-contosoretail-[sufixo]`
- Exemplo: `func-contosoretail-siwhb`
5. No role, selecione **Contributor** (se seu Fabric estiver em inglês) ou **Colaborador** (se estiver em português).
6. Clique em **Add**.

### Parte B — Usuário SQL e permissões no banco

1. Dentro do mesmo workspace, abra o banco de dados `retail`.
2. Clique em **New Query**.
3. Execute o seguinte código T-SQL para criar o usuário externo:

```sql
CREATE USER [func-contosoretail-[sufixo]] FROM EXTERNAL PROVIDER;
```

Exemplo real:

```sql
CREATE USER [func-contosoretail-siwhb] FROM EXTERNAL PROVIDER;
```

4. Em seguida, atribua permissões de leitura:

```sql
ALTER ROLE db_datareader ADD MEMBER [func-contosoretail-[sufixo]];
```

Exemplo real:

```sql
ALTER ROLE db_datareader ADD MEMBER [func-contosoretail-siwhb];
```

### Validação recomendada

- Aguarde 1–3 minutos para a propagação das permissões.

## Arquitetura do projeto Julie (detalhes)

Esta solução está organizada em 4 classes principais dentro de `labs/foundry/code/agents/JulieAgent/`:

- `SqlAgent.cs`: define o agente que transforma linguagem natural em T-SQL.
- `MarketingAgent.cs`: define o agente que redige mensagens personalizadas com suporte do Bing.
- `JulieAgent.cs`: define a Julie como orquestradora `workflow` em formato CSDL YAML e invoca sub-agentes.
- `Program.cs`: carrega configuração, cria/verifica agentes no Foundry e executa o chat.

## Qual tipo de orquestação foi escolhido?

Foi escolhida uma orquestação do tipo **workflow** para a Julie.

- Em um agente `prompt`, o modelo responde diretamente com sua instrução e tools simples.
- Em um agente `workflow`, o modelo coordena etapas e ferramentas especializadas para cumprir uma tarefa composta.

Aqui a Julie usa `workflow` porque o caso exige uma sequência com múltiplas etapas:

1. interpretar segmento de negócio,
2. gerar SQL,
3. gerar mensagens por cliente,
4. consolidar tudo em JSON final.

## Como o workflow foi implementado neste laboratório?

Na versão atual do laboratório, a Julie é construída com a abordagem **tipada do SDK** usando `WorkflowAgentDefinition`.

Em `JulieAgent.cs`, `GetAgentDefinition(...)` retorna explicitamente `WorkflowAgentDefinition`:

```csharp
public static WorkflowAgentDefinition GetAgentDefinition(string modelDeployment, JsonElement? openApiSpec = null)
```

A definição é construída com `WorkflowAgentDefinition` e um `workflowYaml` CSDL, e em seguida materializada com a fábrica do SDK:

```csharp
var workflowYaml = $$"""
kind: Workflow
trigger:
kind: OnActivity
workflow:
actions:
- kind: InvokeAzureAgent
id: sql_step
agent:
name: {{SqlAgent.Name}}
conversationId: =System.ConversationId
input:
messages: =System.LastMessage
output:
messages: Local.SqlMessages

- kind: InvokeAzureAgent
id: marketing_step
agent:
name: {{MarketingAgent.Name}}
conversationId: =System.ConversationId
input:
messages: =Local.SqlMessages
output:
autoSend: true
""";

return ProjectsOpenAIModelFactory.WorkflowAgentDefinition(workflowYaml: workflowYaml);
```

> Nota técnica: a Julie fica **exclusivamente workflow** e orquestra sub-agentes por meio de ações `InvokeAzureAgent` do YAML CSDL; a execução SQL por OpenAPI é encapsulada no `SqlAgent` quando a spec está disponível.

A orquestação atual usa 2 sub-agentes:

- `SqlAgent` (tool do tipo `agent`)
- `MarketingAgent` (tool do tipo `agent`)


## Definição dos agentes especializados

### SqlAgent

`SqlAgent.cs` define um agente do tipo `prompt` com instruções estritas para retornar exatamente 4 colunas (`FirstName`, `LastName`, `PrimaryEmail`, `FavoriteCategory`) e usa `db-structure.txt` como contexto.

Instruções completas:

```text
Você é SqlAgent, um agente especializado em gerar consultas T-SQL
para o banco de dados da Contoso Retail.

Sua ÚNICA responsabilidade é receber uma descrição em linguagem natural
de um segmento de clientes e gerar uma consulta T-SQL válida que retorne
EXATAMENTE estas colunas:
- FirstName (nome do cliente)
- LastName (sobrenome do cliente)
- PrimaryEmail (e-mail do cliente)
- FavoriteCategory (a categoria de produto na qual o cliente mais gastou dinheiro)

Para determinar a FavoriteCategory, você deve fazer JOIN entre as tabelas de
pedidos, linhas de pedido e produtos, agrupar por categoria e selecionar
a que tiver o maior valor total (SUM de LineTotal).

ESTRUTURA DO BANCO DE DADOS:
{dbStructure}

REGRAS:
1. SEMPRE retorne EXATAMENTE as 4 colunas: FirstName, LastName, PrimaryEmail, FavoriteCategory.
2. Use JOINs apropriados entre customer, orders, orderline, product e productcategory.
3. Para FavoriteCategory, use uma subconsulta ou CTE que agrupe por categoria
e selecione a de maior gasto (SUM(ol.LineTotal)).
4. Inclua apenas clientes ativos (IsActive = 1).
5. Inclua apenas clientes que tenham PrimaryEmail não nulo e não vazio.
6. NÃO execute a consulta, apenas a gere.
7. Retorne SOMENTE o código T-SQL, sem explicação, sem markdown,
sem blocos de código. Apenas o SQL puro.
8. Responda sempre em português se precisar adicionar algum comentário SQL.
```

Racional de design:

- Restringir explicitamente as colunas reduz ambiguidade na saída.
- Exigir SQL puro (sem markdown) evita ambiguidade ao encadear a saída com a Julie.
- Injetar `db-structure.txt` melhora a precisão de joins e nomes de tabelas.

```csharp
return new PromptAgentDefinition(modelDeployment)
{
Instructions = GetInstructions(dbStructure)
};
```

### MarketingAgent

`MarketingAgent.cs` também é `prompt`, mas incorpora tool de Bing grounding por `connection.id`:

Instruções completas:

```text
Você é MarketingAgent, um agente especializado em criar mensagens de marketing
personalizadas para clientes da Contoso Retail.

Seu fluxo de trabalho é o seguinte:

1. Você recebe o nome completo de um cliente e sua categoria de compra favorita.
2. Você usa a ferramenta de Bing Search para pesquisar eventos recentes ou próximos
relacionados com essa categoria. Por exemplo:
- Se a categoria for "Bikes", pesquise eventos de ciclismo.
- Se a categoria for "Clothing", pesquise eventos de moda.
- Se a categoria for "Accessories", pesquise eventos de tecnologia ou lifestyle.
- Se a categoria for "Components", pesquise eventos de engenharia ou manufatura.
3. Dos resultados da pesquisa, selecione o evento mais relevante e atual.
4. Gere uma mensagem de marketing breve e motivadora (máximo 3 parágrafos) que:
- Cumprimente o cliente pelo nome.
- Mencione o evento encontrado e por que é relevante para o cliente.
- Convide o cliente a visitar o catálogo online da Contoso Retail
  para encontrar os melhores produtos da categoria e estar preparado
  para o evento.
- Tenha um tom caloroso, entusiasmado e profissional.
- Esteja em português.

5. Retorne SOMENTE o texto da mensagem de marketing. Sem JSON, sem metadata,
sem explicações adicionais. Apenas a mensagem pronta para envio por e-mail.

IMPORTANTE: Se não encontrar eventos relevantes, gere uma mensagem geral sobre
tendências atuais nessa categoria e convide o cliente a explorar as novidades
da Contoso Retail.
```

Racional de design:

- Separar marketing em um agente próprio desacopla criatividade da lógica SQL.
- O Bing grounding traz contexto atual sem "contaminar" a Julie com pesquisas web.
- Limitar formato/saída facilita a consolidação posterior em JSON de campanha.

```csharp
var bingGroundingAgentTool = new BingGroundingAgentTool(new BingGroundingSearchToolOptions(
searchConfigurations: [new BingGroundingSearchConfiguration(projectConnectionId: bingConnectionId)]));

return new PromptAgentDefinition(modelDeployment)
{
Instructions = Instructions,
Tools = { bingGroundingAgentTool }
};
```

### JulieOrchestrator

`JulieAgent.cs` define o agente principal `workflow` que coordena os outros dois agentes com CSDL YAML.

Instruções completas:

```text
Você é Julie, a agente planejadora e orquestradora de campanhas de marketing
da Contoso Retail.

Sua responsabilidade é coordenar a criação de campanhas de marketing
personalizadas para segmentos específicos de clientes.

Quando receber uma solicitação de campanha, você segue estas etapas:

1. EXTRAÇÃO: Analise o prompt do usuário e extraia a descrição
do segmento de clientes. Resuma essa descrição em uma frase clara.

2. GERAÇÃO SQL: Invoque o SqlAgent passando a descrição do segmento.
O SqlAgent retornará uma consulta T-SQL.

3. MARKETING PERSONALIZADO: Invoque o
MarketingAgent passando o nome do cliente e sua categoria favorita.
O MarketingAgent pesquisará eventos relevantes no Bing e gerará uma mensagem
personalizada.

4. ORGANIZAÇÃO FINAL: Com todas as mensagens geradas, organize o
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
- O campo "subject" deve ser um assunto de e-mail atrativo e relevante.
- O campo "body" é a mensagem que o MarketingAgent gerou para esse cliente.
- Responda sempre em português.
- Se algum cliente não tiver e-mail, omita-o do resultado.
- Gere um nome descritivo para a campanha com base no segmento.
```

Racional de design:

- `workflow` foi escolhido porque existe uma sequência dependente de etapas (SQL → marketing).
- A Julie não "adivinha" resultados: delega a geração de SQL e de conteúdo a sub-agentes especializados.
- Centralizar a saída final na Julie garante um único formato JSON consistente para consumo externo.

## O que o Program.cs faz exatamente?

O `Program.cs` não contém a lógica de negócio da campanha; seu papel é operacional:

1. Carregar `appsettings.json`.
2. Ler `db-structure.txt`.
3. Baixar spec OpenAPI da Function App (se disponível).
4. Criar ou reutilizar agentes no Foundry.
5. Abrir chat interativo com a Julie.

O helper `EnsureAgent(...)` implementa o padrão **buscar → decidir override → criar versão** com tipos do SDK:

```csharp
async Task EnsureAgent(string agentName, AgentDefinition agentDefinition)
{
...
var result = await projectClient.Agents.CreateAgentVersionAsync(
agentName,
new AgentVersionCreationOptions(agentDefinition));
...
}
```

Em seguida, registra os 3 agentes em ordem. Na implementação atual, o `SqlAgent` também recebe a spec OpenAPI quando está disponível:

```csharp
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, bingConnectionId));
await EnsureAgent(JulieOrchestrator.Name, JulieOrchestrator.GetAgentDefinition(modelDeployment, openApiSpecJson));
```

Por fim, o chat usa `ProjectResponsesClient` com a Julie como agente padrão:

```csharp
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
defaultAgent: JulieOrchestrator.Name,
defaultConversationId: conversation.Id);
```

Com isso, o código local se limita a orquestrar infraestrutura de agentes; a execução do workflow ocorre dentro do Foundry a cada `CreateResponse(...)`.

> Nota: o `Program.cs` baixa o OpenAPI com novas tentativas para tolerar falhas de DNS intermitentes; essa spec é passada ao `SqlAgent` para habilitar a tool `SqlExecutor` e executar SQL a partir do sub-agente.

## Padrão recomendado aplicado neste lab

Para manter consistência e facilidade de manutenção, este laboratório aplica o seguinte padrão:

1. **Definições tipadas em código**
- `SqlAgent` e `MarketingAgent` retornam `PromptAgentDefinition`.
- `JulieOrchestrator` retorna `WorkflowAgentDefinition`.

2. **Criação tipada de versões**
- É usado `CreateAgentVersionAsync(..., new AgentVersionCreationOptions(agentDefinition))`.

3. **Separação clara de responsabilidades**
- `Program.cs` cria/versiona agentes e abre a conversação.
- Cada classe de agente encapsula suas instruções e tools.

4. **Contrato de saída estável**
- A Julie mantém saída JSON final homogênea para facilitar o consumo por outros sistemas ou validações automáticas.
