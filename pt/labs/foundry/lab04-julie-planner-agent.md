# Lab 4: Julie Planner Agent

## Introducción

En este laboratorio construirás y validarás a Julie como agente planner de campañas de marketing en Foundry. Julie se implementa como agente de tipo `workflow` y orquesta el flujo con dos sub-agentes: `SqlAgent` y `MarketingAgent`. `SqlAgent` puede usar la tool OpenAPI `SqlExecutor` (Function App `FxContosoRetail`) para ejecutar SQL contra la base y devolver los clientes segmentados. En este laboratorio, progresivamente, configurarás el entorno, verificarás permisos y conexión SQL, y ejecutarás el flujo end-to-end para obtener la salida final de campaña en formato JSON.

## Continuidad del setup

Este laboratorio asume que ya completaste:

- El despliegue base de infraestructura de Foundry (`labs/foundry/README.md`)
- El flujo de datos en Fabric del **Lab 1** (`../fabric/lab01-data-setup.md`)

## Checklist rápido

### 1) Verificar valores de conexión SQL

Para el setup actualizado se usan estos valores:

- `FabricWarehouseSqlEndpoint`
- `FabricWarehouseDatabase`

Se obtienen del connection string SQL del Warehouse de Fabric:

- `FabricWarehouseSqlEndpoint` = `Data Source` sin `,1433`
- `FabricWarehouseDatabase` = `Initial Catalog`

### 2) Alternativa si no sigues toda la secuencia de labs

Si no estás siguiendo toda la secuencia de laboratorios, para Lab 4 también puedes usar una base SQL standalone (por ejemplo Azure SQL Database), ajustando esos dos valores al host y nombre de base correspondientes.

### 3) Comportamiento cuando no se pasan valores de Fabric

Si no proporcionas estos valores durante el setup, el despliegue de infraestructura no falla, pero la conexión SQL para Lab 4 no se configura automáticamente y debe ajustarse manualmente en la Function App.

## Configuración manual de permisos en Fabric (obligatorio para Lab 4)

Después del despliegue, asegúrate de que la Managed Identity de la Function App tenga acceso al workspace y a la base SQL de `retail`.

### Parte A — Acceso al Workspace

1. Abre el workspace donde se desplegó la base de datos de `retail`.
2. Ve a **Manage access**.
3. Haz click en **Add people or groups**.
4. Busca y agrega la identidad de la Function App.
	- Nombre esperado: `func-contosoretail-[sufijo]`
	- Ejemplo: `func-contosoretail-siwhb`
5. En el rol, selecciona **Contributor** (si tu Fabric está en inglés) o **Colaborador** (si está en español).
6. Haz click en **Add**.

### Parte B — Usuario SQL y permisos en la base

1. Dentro del mismo workspace, abre la base de datos `retail`.
2. Haz click en **New Query**.
3. Ejecuta el siguiente código T-SQL para crear el usuario externo:

```sql
CREATE USER [func-contosoretail-[sufijo]] FROM EXTERNAL PROVIDER;
```

Ejemplo real:

```sql
CREATE USER [func-contosoretail-siwhb] FROM EXTERNAL PROVIDER;
```

4. Luego asigna permisos de lectura:

```sql
ALTER ROLE db_datareader ADD MEMBER [func-contosoretail-[sufijo]];
```

Ejemplo real:

```sql
ALTER ROLE db_datareader ADD MEMBER [func-contosoretail-siwhb];
```

### Validación recomendada

- Espera 1–3 minutos para propagación de permisos.

## Arquitectura del proyecto Julie (detalle)

Esta solución está organizada en 4 clases principales dentro de `labs/foundry/code/agents/JulieAgent/`:

- `SqlAgent.cs`: define el agente que transforma lenguaje natural en T-SQL.
- `MarketingAgent.cs`: define el agente que redacta mensajes personalizados apoyado en Bing.
- `JulieAgent.cs`: define a Julie como orquestadora `workflow` en formato CSDL YAML e invoca sub-agentes.
- `Program.cs`: carga configuración, crea/verifica agentes en Foundry y ejecuta el chat.

## ¿Qué tipo de orquestación se escogió?

Se escogió una orquestación de tipo **workflow** para Julie.

- En un agente `prompt`, el modelo responde directamente con su instrucción y tools simples.
- En un agente `workflow`, el modelo coordina pasos y herramientas especializadas para cumplir una tarea compuesta.

Aquí Julie usa `workflow` porque el caso requiere una secuencia multi-etapa:

1. interpretar segmento de negocio,
2. generar SQL,
3. generar mensajes por cliente,
4. consolidar todo en JSON final.

## ¿Cómo se implementó el workflow en este laboratorio?

En la versión actual del laboratorio, Julie se construye con el enfoque **tipado del SDK** usando `WorkflowAgentDefinition`.

En `JulieAgent.cs`, `GetAgentDefinition(...)` retorna explícitamente `WorkflowAgentDefinition`:

```csharp
public static WorkflowAgentDefinition GetAgentDefinition(string modelDeployment, JsonElement? openApiSpec = null)
```

La definición se construye con `WorkflowAgentDefinition` y un `workflowYaml` CSDL, luego se materializa con la factoría del SDK:

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

> Nota técnica: Julie queda **workflow-only** y orquesta sub-agentes mediante acciones `InvokeAzureAgent` del YAML CSDL; la ejecución SQL por OpenAPI se encapsula en `SqlAgent` cuando la spec está disponible.

La orquestación actual usa 2 sub-agentes:

- `SqlAgent` (tool tipo `agent`)
- `MarketingAgent` (tool tipo `agent`)


## Definición de agentes especializados

### SqlAgent

`SqlAgent.cs` define un agente de tipo `prompt` con instrucciones estrictas para retornar exactamente 4 columnas (`FirstName`, `LastName`, `PrimaryEmail`, `FavoriteCategory`) y usa `db-structure.txt` como contexto.

Instrucciones completas:

```text
Eres SqlAgent, un agente especializado en generar consultas T-SQL
para la base de datos de Contoso Retail.

Tu ÚNICA responsabilidad es recibir una descripción en lenguaje natural
de un segmento de clientes y generar una consulta T-SQL válida que retorne
EXACTAMENTE estas columnas:
- FirstName (nombre del cliente)
- LastName (apellido del cliente)
- PrimaryEmail (correo electrónico del cliente)
- FavoriteCategory (la categoría de producto en la que el cliente ha gastado más dinero)

Para determinar la FavoriteCategory, debes hacer JOIN entre las tablas de
órdenes, líneas de orden y productos, agrupar por categoría y seleccionar
la que tenga el mayor monto total (SUM de LineTotal).

ESTRUCTURA DE LA BASE DE DATOS:
{dbStructure}

REGLAS:
1. SIEMPRE retorna EXACTAMENTE las 4 columnas: FirstName, LastName, PrimaryEmail, FavoriteCategory.
2. Usa JOINs apropiados entre customer, orders, orderline, product y productcategory.
3. Para FavoriteCategory, usa una subconsulta o CTE que agrupe por categoría
	y seleccione la de mayor gasto (SUM(ol.LineTotal)).
4. Solo incluye clientes activos (IsActive = 1).
5. Solo incluye clientes que tengan PrimaryEmail no nulo y no vacío.
6. NO ejecutes la consulta, solo genérala.
7. Retorna ÚNICAMENTE el código T-SQL, sin explicación, sin markdown,
	sin bloques de código. Solo el SQL puro.
8. Responde siempre en español si necesitas agregar algún comentario SQL.
```

Racional de diseño:

- Restringir explícitamente las columnas reduce ambigüedad en la salida.
- Obligar SQL puro (sin markdown) evita ambigüedad al encadenar la salida con Julie.
- Inyectar `db-structure.txt` mejora precisión de joins y nombres de tablas.

```csharp
return new PromptAgentDefinition(modelDeployment)
{
	Instructions = GetInstructions(dbStructure)
};
```

### MarketingAgent

`MarketingAgent.cs` también es `prompt`, pero incorpora tool de Bing grounding por `connection.id`:

Instrucciones completas:

```text
Eres MarketingAgent, un agente especializado en crear mensajes de marketing
personalizados para clientes de Contoso Retail.

Tu flujo de trabajo es el siguiente:

1. Recibes el nombre completo de un cliente y su categoría de compra favorita.
2. Usas la herramienta de Bing Search para buscar eventos recientes o próximos
	relacionados con esa categoría. Por ejemplo:
	- Si la categoría es "Bikes", busca eventos de ciclismo.
	- Si la categoría es "Clothing", busca eventos de moda.
	- Si la categoría es "Accessories", busca eventos de tecnología o lifestyle.
	- Si la categoría es "Components", busca eventos de ingeniería o manufactura.
3. De los resultados de búsqueda, selecciona el evento más relevante y actual.
4. Genera un mensaje de marketing breve y motivacional (máximo 3 párrafos) que:
	- Salude al cliente por su nombre.
	- Mencione el evento encontrado y por qué es relevante para el cliente.
	- Invite al cliente a visitar el catálogo online de Contoso Retail
	  para encontrar los mejores productos de la categoría y estar preparado
	  para el evento.
	- Tenga un tono cálido, entusiasta y profesional.
	- Esté en español.

5. Retorna ÚNICAMENTE el texto del mensaje de marketing. Sin JSON, sin metadata,
	sin explicaciones adicionales. Solo el mensaje listo para enviar por correo.

IMPORTANTE: Si no encuentras eventos relevantes, genera un mensaje general sobre
tendencias actuales en esa categoría e invita al cliente a explorar las novedades
de Contoso Retail.
```

Racional de diseño:

- Separar marketing en un agente propio desacopla creatividad de la lógica SQL.
- Bing grounding aporta contexto actual sin “contaminar” a Julie con búsquedas web.
- Limitar formato/salida facilita consolidación posterior en JSON de campaña.

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

`JulieAgent.cs` define el agente principal `workflow` que coordina los otros dos agentes con CSDL YAML.

Instrucciones completas:

```text
Eres Julie, la agente planificadora y orquestadora de campañas de marketing
de Contoso Retail.

Tu responsabilidad es coordinar la creación de campañas de marketing
personalizadas para segmentos específicos de clientes.

Cuando recibas una solicitud de campaña sigues estos pasos:

1. EXTRACCIÓN: Analiza el prompt del usuario y extrae la descripción
	del segmento de clientes. Resume esa descripción en una frase clara.

2. GENERACIÓN SQL: Invoca a SqlAgent pasándole la descripción del segmento.
	SqlAgent te retornará una consulta T-SQL.

3. MARKETING PERSONALIZADO: Invoca a
	MarketingAgent pasándole el nombre del cliente y su categoría favorita.
	MarketingAgent buscará eventos relevantes en Bing y generará un mensaje
	personalizado.

4. ORGANIZACIÓN FINAL: Con todos los mensajes generados, organiza el
	resultado como un JSON de campaña con el siguiente formato:

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
		"subject": "Asunto del correo generado automáticamente",
		"body": "Mensaje de marketing personalizado"
	 }
  ]
}
```

REGLAS:
- El campo "subject" debe ser un asunto de correo atractivo y relevante.
- El campo "body" es el mensaje que generó MarketingAgent para ese cliente.
- Responde siempre en español.
- Si algún cliente no tiene email, omítelo del resultado.
- Genera un nombre descriptivo para la campaña basado en el segmento.
```

Racional de diseño:

- `workflow` se eligió porque hay una secuencia dependiente de pasos (SQL → marketing).
- Julie no “adivina” resultados: delega la generación de SQL y de contenido a sub-agentes especializados.
- Centralizar la salida final en Julie asegura un único formato JSON consistente para consumo externo.

## ¿Qué hace Program.cs exactamente?

`Program.cs` no contiene la lógica de negocio de campaña; su rol es operativo:

1. Cargar `appsettings.json`.
2. Leer `db-structure.txt`.
3. Descargar spec OpenAPI de la Function App (si está disponible).
4. Crear o reutilizar agentes en Foundry.
5. Abrir chat interactivo con Julie.

El helper `EnsureAgent(...)` implementa el patrón **buscar → decidir override → crear versión** con tipos del SDK:

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

Luego registra los 3 agentes en orden. En la implementación actual, `SqlAgent` recibe también la spec OpenAPI cuando está disponible:

```csharp
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, bingConnectionId));
await EnsureAgent(JulieOrchestrator.Name, JulieOrchestrator.GetAgentDefinition(modelDeployment, openApiSpecJson));
```

Finalmente, el chat usa `ProjectResponsesClient` con Julie como agente por defecto:

```csharp
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
	defaultAgent: JulieOrchestrator.Name,
	defaultConversationId: conversation.Id);
```

Con esto, el código local se limita a orquestar infraestructura de agente; la ejecución del workflow ocurre dentro de Foundry en cada `CreateResponse(...)`.

> Nota: el `Program.cs` descarga OpenAPI con reintentos para tolerar fallas DNS intermitentes; esa spec se pasa a `SqlAgent` para habilitar la tool `SqlExecutor` y ejecutar SQL desde el sub-agente.

## Patrón recomendado aplicado en este lab

Para mantener consistencia y mantenibilidad, este laboratorio aplica el siguiente patrón:

1. **Definiciones tipadas en código**
	- `SqlAgent` y `MarketingAgent` retornan `PromptAgentDefinition`.
	- `JulieOrchestrator` retorna `WorkflowAgentDefinition`.

2. **Creación tipada de versiones**
	- Se usa `CreateAgentVersionAsync(..., new AgentVersionCreationOptions(agentDefinition))`.

3. **Separación clara de responsabilidades**
	- `Program.cs` crea/versiona agentes y abre conversación.
	- Cada clase de agente encapsula sus instrucciones y tools.

4. **Contrato de salida estable**
	- Julie mantiene salida JSON final homogénea para facilitar consumo por otros sistemas o validaciones automáticas.
