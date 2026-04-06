# Lab 4: Julie Planner Agent

## Índice

- [Lab 4: Julie Planner Agent](#lab-4-julie-planner-agent)
	- [Índice](#índice)
	- [Introdução](#introdução)
	- [Continuidade do setup](#continuidade-do-setup)
	- [Checklist rápido](#checklist-rápido)
		- [1) Verificar valores de conexão SQL](#1-verificar-valores-de-conexão-sql)
		- [2) Alternativa se você não seguir toda a sequência de labs](#2-alternativa-se-você-não-seguir-toda-a-sequência-de-labs)
		- [3) Comportamento quando os valores do Fabric não são fornecidos](#3-comportamento-quando-os-valores-do-fabric-não-são-fornecidos)
	- [Configuração manual de permissões no Fabric (obrigatório para o Lab 4)](#configuração-manual-de-permissões-no-fabric-obrigatório-para-o-lab-4)
		- [Parte A — Acesso ao Workspace](#parte-a--acesso-ao-workspace)
		- [Parte B — Usuário SQL e permissões no banco](#parte-b--usuário-sql-e-permissões-no-banco)
		- [Validação recomendada](#validação-recomendada)
	- [Arquitetura do projeto Julie (detalhes)](#arquitetura-do-projeto-julie-detalhes)
	- [Qual tipo de orquestação foi escolhido?](#qual-tipo-de-orquestação-foi-escolhido)
	- [Como o workflow foi implementado neste laboratório?](#como-o-workflow-foi-implementado-neste-laboratório)
	- [Definição dos agentes especializados](#definição-dos-agentes-especializados)
		- [SqlAgent](#sqlagent)
		- [MarketingAgent](#marketingagent)
		- [JulieOrchestrator](#julieorchestrator)
	- [O que o Program.cs faz exatamente?](#o-que-o-programcs-faz-exatamente)
	- [Passos do laboratório](#passos-do-laboratório)
		- [Passo 1: Configurar appsettings.json](#passo-1-configurar-appsettingsjson)
		- [Passo 2: Garantir que as permissões do Fabric estão configuradas](#passo-2-garantir-que-as-permissões-do-fabric-estão-configuradas)
		- [Passo 3: Executar Julie](#passo-3-executar-julie)
		- [Passo 4: Testar o fluxo end-to-end](#passo-4-testar-o-fluxo-end-to-end)
		- [Validação do laboratório](#validação-do-laboratório)
	- [Challenges](#challenges)
		- [Challenge 1: Melhorar o prompt do MarketingAgent para campanhas atuais](#challenge-1-melhorar-o-prompt-do-marketingagent-para-campanhas-atuais)
		- [Challenge 2: Criar um agente no-code com Code Interpreter](#challenge-2-criar-um-agente-no-code-com-code-interpreter)

---

## Introdução

Neste laboratório você vai construir e validar a Julie como agente planejador de campanhas de marketing no Foundry. Julie é implementada como agente do tipo `workflow` e orquestra o fluxo com dois sub-agentes: `SqlAgent` e `MarketingAgent`. O `SqlAgent` pode usar a tool OpenAPI `SqlExecutor` (Function App `FxContosoRetail`) para executar SQL contra o banco e retornar os clientes segmentados. Neste laboratório, progressivamente, você configurará o ambiente, verificará permissões e a conexão SQL, e executará o fluxo end-to-end para obter a saída final de campanha em formato JSON.

## Continuidade do setup

Este laboratório pressupõe que você já concluiu:

- A implantação base de infraestrutura do Foundry (`pt/labs/foundry/README.md`)
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
5. Na função, selecione **Contributor** (se seu Fabric estiver em inglês) ou **Colaborador** (se estiver em português).
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

Esta solução está organizada em 4 classes principais dentro de `pt/labs/foundry/code/agents/JulieAgent/`:

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

return ProjectsOpenAIModelFactory.WorkflowAgentDefinition(workflowYaml: workflowYaml);
```

> Nota técnica: a Julie fica **exclusivamente workflow** e orquestra sub-agentes por meio de ações `InvokeAzureAgent` do YAML CSDL; a execução SQL por OpenAPI é encapsulada no `SqlAgent` quando a spec está disponível. O trigger `OnConversationStart` com `EndConversation` define um fluxo sequencial que executa os dois passos e encerra a conversa do workflow.

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
searchConfigurations: [new BingGroundingSearchConfiguration(projectConnectionId: bingConnectionName)]));

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

3. EXECUÇÃO SQL: Envie o T-SQL para sua ferramenta OpenAPI (SqlExecutor)
   para executá-lo contra o banco de dados. A ferramenta retornará os
   resultados como dados de clientes.

4. MARKETING PERSONALIZADO: Para CADA cliente retornado, invoque o
   MarketingAgent passando o nome do cliente e sua categoria favorita.
   O MarketingAgent pesquisará eventos relevantes no Bing e gerará uma
   mensagem personalizada.

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
- O campo "body" é a mensagem que o MarketingAgent gerou para esse cliente.
- Responda sempre em português.
- Se algum cliente não tiver e-mail, omita-o do resultado.
- Gere um nome descritivo para a campanha baseado no segmento.
```

Racional de design:

- `workflow` foi escolhido porque há uma sequência dependente de passos (SQL → marketing).
- A Julie não "adivinha" resultados: ela delega a geração de SQL e de conteúdo a sub-agentes especializados.
- Centralizar a saída final na Julie garante um único formato JSON consistente para consumo externo.

## O que o Program.cs faz exatamente?

`Program.cs` não contém a lógica de negócio de campanha; seu papel é operacional:

1. Carregar `appsettings.json`.
2. Ler `db-structure.txt`.
3. Baixar a spec OpenAPI da Function App (se disponível).
4. Resolver o ID completo da conexão Bing (a API requer o ARM resource ID, não apenas o nome).
5. Criar ou reutilizar agentes no Foundry.
6. Abrir chat interativo com a Julie.

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

Em seguida, registra os 3 agentes em ordem. Na implementação atual, o `SqlAgent` também recebe a spec OpenAPI quando disponível:

```csharp
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, bingConnectionId));
await EnsureAgent(JulieOrchestrator.Name, JulieOrchestrator.GetAgentDefinition(modelDeployment, openApiSpecJson));
```

Finalmente, o chat usa `ProjectResponsesClient` com a Julie como agente padrão:

```csharp
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
	defaultAgent: JulieOrchestrator.Name,
	defaultConversationId: conversation.Id);
```

Com isso, o código local se limita a orquestrar infraestrutura de agente; a execução do workflow acontece dentro do Foundry em cada `CreateResponse(...)`.

> **Nota sobre a conexão Bing:** `Program.cs` resolve o nome da conexão Bing (ex: `ais-contosoretail-geoxs-bingsearchconnection`) para seu ARM resource ID completo usando `projectClient.Connections.GetConnectionAsync()`. Isso é necessário porque `BingGroundingSearchConfiguration(projectConnectionId:)` espera o ID completo, não apenas o nome.

> Nota: o `Program.cs` baixa OpenAPI com retentativas para tolerar falhas DNS intermitentes; essa spec é passada ao `SqlAgent` para habilitar a tool `SqlExecutor` e executar SQL a partir do sub-agente.

---

## Passos do laboratório

### Passo 1: Configurar appsettings.json

Abra `pt/labs/foundry/code/agents/JulieAgent/appsettings.json` e substitua todos os valores `<suffix>` pelos outputs da implantação:

```json
{
  "FoundryProjectEndpoint": "https://ais-contosoretail-<suffix>.services.ai.azure.com/api/projects/aip-contosoretail-<suffix>",
  "ModelDeploymentName": "gpt-4.1",
  "FunctionAppBaseUrl": "https://func-contosoretail-<suffix>.azurewebsites.net/api",
  "BingConnectionName": "ais-contosoretail-<suffix>-bingsearchconnection"
}
```

Todos esses valores são obtidos da saída do script de implantação (ou do portal → recurso AI Foundry → **Project settings** → **Overview**).

> Para obter o `BingConnectionName` diretamente, execute:
> ```bash
> az cognitiveservices account connection list \
>     --name ais-contosoretail-<suffix> \
>     --resource-group rg-contoso-retail \
>     --query "[?contains(name,'bing')].name" -o tsv
> ```
> Substitua `<suffix>` pelo seu sufixo. O comando retorna o nome da conexão pronto para colar.

### Passo 2: Garantir que as permissões do Fabric estão configuradas

Antes de executar, confirme que você já completou a seção **Configuração manual de permissões no Fabric** deste mesmo documento (Partes A e B). Se não tiver feito, a Function App não conseguirá executar SQL contra o Warehouse e o `SqlAgent` falhará.

### Passo 3: Executar Julie

A partir do terminal, na raiz do repositório:

```bash
cd pt/labs/foundry/code/agents/JulieAgent
dotnet run
```

Ao iniciar, o programa:
1. Baixa a spec OpenAPI da Function App (pode levar alguns segundos).
2. Cria ou atualiza os três agentes no Foundry: `SqlAgent`, `MarketingAgent` e `Julie`.
3. Abre um chat interativo no terminal.

Você verá mensagens como:

```
Agente SqlAgent criado/atualizado.
Agente MarketingAgent criado/atualizado.
Agente Julie criado/atualizado.
Chat iniciado. Digite sua solicitação de campanha (ou 'exit' para sair):
>
```

### Passo 4: Testar o fluxo end-to-end

Digite um prompt descrevendo o segmento de clientes para a campanha. Por exemplo:

```
Crie uma campanha para clientes cuja categoria favorita seja Bikes
```

```
Gere uma campanha para os 5 clientes mais recentes que compraram na categoria Clothing
```

A Julie invocará o `SqlAgent` (que gerará e executará o SQL contra o Fabric), depois o `MarketingAgent` (que pesquisará eventos no Bing e redigirá mensagens personalizadas para cada cliente), e finalmente consolidará tudo em um JSON de campanha:

```json
{
  "campaign": "Campanha Bikes - Primavera 2026",
  "generatedAt": "2026-03-13T10:30:00",
  "totalEmails": 3,
  "emails": [
    {
      "to": "cliente@exemplo.com",
      "customerName": "Ana García",
      "favoriteCategory": "Bikes",
      "subject": "Ana, prepare-se para a temporada ciclista!",
      "body": "Olá Ana, ..."
    }
  ]
}
```

> A primeira execução pode levar **30–60 segundos** porque o workflow passa por SQL execution + Bing search + geração de texto para cada cliente do segmento.

### Validação do laboratório

O laboratório é considerado completo quando:

- [ ] Os três agentes aparecem criados no portal do Foundry (AI Foundry → seu projeto → **Agents**).
- [ ] Um prompt de campanha retorna um JSON com pelo menos um e-mail gerado.
- [ ] O `body` de cada e-mail inclui uma referência a um evento ou tendência atual pesquisada no Bing.

---

## Challenges

### Challenge 1: Melhorar o prompt do MarketingAgent para campanhas atuais

#### Contexto

Ao testar o fluxo da Julie, é possível que o MarketingAgent gere mensagens baseadas em notícias ou eventos desatualizados (por exemplo, eventos de 2024). Isso acontece porque o prompt atual não restringe o Bing Search para filtrar por data, nem instrui o agente a descartar resultados antigos.

#### Objetivo

Garantir que o MarketingAgent **sempre** gere mensagens de marketing baseadas em eventos atuais ou futuros, nunca em eventos já passados.

#### Parte A — Iterar o prompt no Playground

1. Abra o portal do **Azure AI Foundry** em [https://ai.azure.com](https://ai.azure.com).
2. Navegue até seu projeto e abra a seção **Agents**.
3. Localize o agente **MarketingAgent** e abra-o.
4. No painel de **Instructions**, modifique o prompt para resolver o problema de eventos desatualizados.
5. Use o painel de **Chat** do playground para testar iterativamente. Envie mensagens como:
   - `"Gere uma mensagem de marketing para João Silva, cuja categoria favorita é Bikes"`
   - `"Gere uma mensagem para Maria Santos, categoria Clothing"`
6. Itere o prompt até que **todas** as respostas façam referência a eventos vigentes ou futuros.

> 💡 **Dica:** O playground permite modificar e testar o prompt imediatamente, sem recompilar nem reimplantar. Use-o para experimentar rapidamente.

#### Parte B — Levar o prompt melhorado para o código

Assim que tiver um prompt que funcione corretamente no playground:

1. Copie as instruções finais do playground.
2. Abra o arquivo `MarketingAgent.cs` no projeto `JulieAgent`.
3. Substitua o conteúdo da propriedade `Instructions` pelo prompt melhorado.
4. Execute `dotnet run` e sobrescreva o MarketingAgent quando solicitado.
5. Verifique se o comportamento é idêntico ao que você validou no playground.

#### Critério de sucesso

- No playground, o MarketingAgent gera mensagens que só referenciam eventos atuais ou futuros.
- O mesmo prompt, transferido para o código, produz o mesmo resultado ao executar a Julie end-to-end.

---

### Challenge 2: Criar um agente no-code com Code Interpreter

#### Contexto

O Azure AI Foundry oferece uma experiência visual **no-code/low-code** para criar agentes diretamente pelo portal. Além do Bing Grounding (que já usamos), o Foundry oferece outras ferramentas integradas. Neste challenge você usará o **Code Interpreter** — uma ferramenta que permite ao agente escrever e executar código Python para analisar dados, fazer cálculos e gerar gráficos.

#### Objetivo

Criar um agente chamado **"SalesAnalyst"** a partir da interface visual do Azure AI Foundry que analise dados de vendas da Contoso Retail e gere visualizações.

#### Passos

1. Abra o portal do **Azure AI Foundry** em [https://ai.azure.com](https://ai.azure.com).
2. Navegue até seu projeto (`aip-contosoretail-<suffix>`).
3. No menu lateral, vá em **Agents**.
4. Clique em **+ New Agent**.
5. Configure o agente:
   - **Nome:** `SalesAnalyst`
   - **Model:** Selecione `gpt-4.1`
   - **Instructions:** Copie e cole as seguintes instruções:

```
Você é SalesAnalyst, um analista de dados de vendas da Contoso Retail.

Seu papel é receber dados de vendas (em texto, CSV ou como descrição),
analisá-los e gerar insights úteis para a equipe comercial.

Capacidades:
1. Quando receber dados de vendas, use o Code Interpreter para:
   - Calcular totais, médias e tendências.
   - Gerar gráficos de barras, linhas ou pizza conforme adequado.
   - Identificar os produtos ou categorias mais vendidos.
2. Apresente os resultados de forma clara e executiva.
3. Se o usuário enviar um arquivo CSV, analise-o automaticamente.

Regras:
- Responda sempre em português.
- Gere gráficos quando os dados permitirem.
- Inclua sempre um resumo executivo em texto além do gráfico.
- Use cores profissionais nas visualizações.
```

6. Na seção **Tools**, clique em **+ Add tool**.
7. Selecione **Code Interpreter**.
8. Clique em **Save** (ou **Create**).

#### Testes

Use o painel de **Chat** para testar com estas conversas:

a. `"Tenho estas vendas por categoria: Bikes $45.000, Clothing $12.000, Accessories $8.500, Components $23.000. Gere um gráfico de pizza e diga qual é a categoria mais forte."`

b. `"Compare as vendas do Q1 vs Q2: Q1 — Bikes: 120 unidades, Clothing: 340, Accessories: 210. Q2 — Bikes: 155, Clothing: 290, Accessories: 380. Gere um gráfico comparativo e analise a tendência."`

c. `"Calcule o crescimento percentual de cada categoria entre Q1 e Q2 e ordene da maior para a menor taxa de crescimento."`

#### Critério de sucesso

- O agente gera **código Python** que é executado dentro da conversa.
- As respostas incluem **gráficos** visíveis diretamente no chat.
- O agente fornece um **resumo executivo** em português junto com cada visualização.
- A ferramenta **Code Interpreter** aparece como habilitada na configuração do agente.

#### Reflexão

- Em que o Code Interpreter se diferencia das outras ferramentas (Bing Grounding, OpenAPI)?
- Que tipos de tarefas de negócio você poderia automatizar com um agente que executa código?
- Compare a experiência de criar este agente visualmente vs. a criação programática dos agentes anteriores:
  - Que vantagens cada abordagem tem?
  - Que limitações a abordagem no-code tem que o SDK não tem?

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
