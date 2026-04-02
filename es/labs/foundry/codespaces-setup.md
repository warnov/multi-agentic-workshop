# Microsoft Foundry — Intro y Setup de Infraestructura

## Introducción

Esta sección del taller cubre la **capa de razonamiento y ejecución** de la arquitectura multi-agéntica de Contoso Retail, implementada sobre **Microsoft Foundry**. Aquí se construyen los agentes inteligentes que interpretan datos y planifican acciones (ejecutando algunas), a partir de la información generada por la capa de datos (Microsoft Fabric).

### Agentes de esta capa

| Agente | Rol | Descripción |
|--------|-----|-------------|
| **Anders** | Executor Agent | Recibe solicitudes de acciones operativas (como la generación de reportes o renderizado de órdenes) y las ejecuta interactuando con servicios externos como la Azure Function `FxContosoRetail`. Tipo: `kind: "prompt"` con herramienta OpenAPI. |
| **Julie** | Planner Workflow | Orquesta campañas de marketing personalizadas. Recibe una descripción de segmento de clientes y ejecuta un flujo de 5 pasos: (1) extrae el filtro de clientes, (2) invoca a **SqlAgent** para generar T-SQL, (3) ejecuta la consulta contra Fabric vía **Function App OpenAPI**, (4) invoca a **MarketingAgent** (con Bing Search) para generar mensajes por cliente, (5) organiza el resultado como JSON de campaña de correos. Tipo: `kind: "workflow"` con 3 herramientas (2 agentes + 1 OpenAPI). |

### Arquitectura general

La capa Foundry se ubica en el centro de la arquitectura de tres capas:

```
┌─────────────────────┐
│   Copilot Studio    │  ← Capa de interacción (Charles, Bill, Ric)
├─────────────────────┤
│  Microsoft Foundry  │  ← Capa de razonamiento (Anders, Julie) ★
├─────────────────────┤
│  Microsoft Fabric   │  ← Capa de datos (Mark, Amy)
└─────────────────────┘
```

Los agentes Anders y Julie utilizan modelos GPT-4.1 desplegados en Azure AI Services para razonar sobre la información del negocio. Anders consume directamente la API de `FxContosoRetail` vía herramienta OpenAPI. Julie orquesta un workflow multi-agente: usa **SqlAgent** (genera T-SQL), una **Function App** (ejecuta el SQL contra Fabric vía OpenAPI) y **MarketingAgent** (genera mensajes personalizados con Bing Search), coordinando todo de forma autónoma como un agente de tipo `workflow`.

---

## Setup con GitHub Codespaces

> 💡 **¿Prefieres trabajar en tu máquina local?** Consulta [setup.md](setup.md) para instrucciones de instalación manual con Azure CLI, .NET y PowerShell 7.

GitHub Codespaces proporciona un entorno de desarrollo completo en la nube, pre-configurado con todas las herramientas necesarias. No necesitas instalar nada en tu máquina — solo necesitas un navegador y acceso al repositorio de GitHub.

> [!IMPORTANT]
> **Requisito: cuenta de GitHub**
> Para usar GitHub Codespaces necesitas una cuenta de GitHub. Si aún no tienes una, puedes crear una cuenta gratuita siguiendo las instrucciones en [https://docs.github.com/en/get-started/start-your-journey/creating-an-account-on-github](https://docs.github.com/en/get-started/start-your-journey/creating-an-account-on-github).

### ¿Qué incluye el entorno?

| Herramienta | Viene preinstalada |
|---|---|
| .NET 8 SDK | ✅ |
| Azure CLI (última versión) | ✅ |
| PowerShell 7+ | ✅ |
| C# Dev Kit (extensión VS Code) | ✅ |
| Bicep (extensión VS Code) | ✅ |
| REST Client (extensión VS Code) | ✅ |

---

### Paso 1: Acceder al repositorio en GitHub

1. Abre el navegador y navega a la URL del repositorio GitHub del taller que te indicó el instructor.
2. Inicia sesión en GitHub.
---

### Paso 2: Crear el Codespace

1. En la página principal del repositorio, haz clic en el botón verde **`< > Code`**.
2. Selecciona la pestaña **Codespaces**.
3. Haz clic en **"Create codespace on main"**.

   > 💡 Si ves la opción de elegir el tipo de máquina, la opción **2-core** es más que suficiente para este taller.

4. Espera entre **2 y 4 minutos** mientras GitHub construye el entorno. Verás una pantalla de carga con logs de construcción. **Esto solo ocurre la primera vez** — las sesiones posteriores inician en segundos porque el entorno queda guardado.

5. Cuando el entorno esté listo, se abrirá **VS Code en el navegador** con todos los archivos del repositorio ya disponibles en el panel izquierdo y las dependencias de .NET ya restauradas. Si la conexión a través del navegador se complica, se recomienda lanzar el codespace en Visual Studio local, indicando esto desde la sección de Code del repo en Github:  
   ![Instrucciones para abrir Codespace en VS Code local](../../assets/codespaces-instructions.png)


> 💡 **¿Prefieres VS Code de escritorio?** Si tienes VS Code instalado en tu máquina con la extensión **GitHub Codespaces**, puedes hacer clic en el ícono `><` (esquina inferior izquierda) → *Connect to Codespace* para conectarte desde VS Code local sin perder el entorno de nube.

---

### Paso 3: Verificar que el entorno está listo

Abre la terminal integrada con <kbd>Ctrl</kbd>+<kbd>`</kbd> (o **Terminal → New Terminal**) y ejecuta los tres comandos siguientes para confirmar que todo está instalado:

```bash
dotnet --version
```
Debes ver algo como `8.0.xxx`.

```bash
az version
```
Debes ver la versión de Azure CLI instalada (JSON con `"azure-cli": "2.x.x"`).

```bash
pwsh --version
```
Debes ver `PowerShell 7.x.x`.

Si los tres responden correctamente, restaura las dependencias .NET del taller:

```bash
dotnet restore es/labs/foundry/code/taller-multi-agentic.sln
```

El entorno está listo para los siguientes pasos.

---

### Paso 4: Autenticarse en Azure

> ℹ️ **El sufijo único de tus recursos se genera automáticamente** a partir del ID de tu suscripción de Azure (un UUID globalmente único). No necesitas ingresar ningún número de tenant ni identificador manual.

---

En la terminal del Codespace, ejecuta:

```bash
az login --use-device-code
```

> ⚠️ Es importante usar `--use-device-code` en Codespaces. El flujo normal (`az login`) intenta abrir un browser local desde el servidor remoto, lo que no funciona correctamente en este entorno. Recuerda que debes abrir la URL de autenticación en tu navegador local, en donde estás logeado con tu cuenta de laboratorio que fue creada para el taller (la que termina en `@azurehol<número>.com`).

Verás una salida similar a esta:

```
To sign in, use a web browser to open the page https://microsoft.com/devicelogin
and enter the code XXXXXXXX to authenticate.
```

Sigue estos pasos:
1. Abre `https://microsoft.com/devicelogin` en tu navegador (en una nueva pestaña).
2. Ingresa el código de 8 caracteres que aparece en la terminal del Codespace.
3. Selecciona la **cuenta de Azure del taller** (la que termina en `@azurehol<número>.com`).
4. Autoriza el acceso cuando se te solicite.
5. Vuelve a la terminal del Codespace — en unos segundos verás la lista de suscripciones disponibles.

Verifica que la suscripción activa sea la correcta:

```bash
az account show --output table
```

Si necesitas cambiarla:

```bash
az account set --subscription "nombre-o-id-de-la-suscripcion"
```

---

### Paso 6: Obtener los parámetros de Microsoft Fabric

Para el Lab 4 (Julie/SqlAgent), necesitarás dos valores del Warehouse de Fabric:

- **FabricWarehouseSqlEndpoint**: endpoint SQL del Warehouse, sin `tcp://` ni puerto. Ejemplo: `xyz.datawarehouse.fabric.microsoft.com`
- **FabricWarehouseDatabase**: nombre exacto y completo de la base de datos.

Para obtenerlos desde el portal de Fabric, sigue la guía [sql-parameters.md](./setup/sql-parameters.md).

> **Nota:** Si no tienes estos valores aún, puedes omitirlos. El despliegue continuará sin ellos y el resto de la infraestructura se creará correctamente. Podrás configurar la conexión SQL manualmente más adelante desde el portal de Azure.

---

### Paso 7: Ejecutar el script de despliegue

En la terminal del Codespace, navega a la carpeta del script:

```bash
cd /workspaces/taller-multi-agentic/es/labs/foundry/setup/op-flex
```

Ejecuta el script de despliegue (usando `pwsh` para iniciar PowerShell 7):

```bash
pwsh ./deployFromAzure.ps1
```

El script solo te pedirá los parámetros opcionales. Presiona <kbd>Enter</kbd> para aceptar los valores por defecto de `Location` y `ResourceGroupName`:

```
Presiona Enter para default.
Location [eastus]:
ResourceGroupName [rg-contoso-retail]:
¿Deseas configurar ahora la conexión SQL de Fabric para Lab04? (s/N): s
FabricWarehouseSqlEndpoint (sin protocolo, sin puerto): kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com
FabricWarehouseDatabase: retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b
```

Verás la confirmación del plan antes de que comience la ejecución:

```
========================================
 Taller Multi-Agéntico - Despliegue
 Plan: Flex Consumption (FC1 / Linux)
 Modo: Azure Cloud Shell
========================================

  Suscripción:    Mi Suscripción Azure (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)
  Sufijo:         ab3f2
  Location:       eastus
  Resource Group: rg-contoso-retail
  Fabric SQL:     kqbvkknqlijebcyrtw2rgtsx2e-...
  Fabric DB:      retail_sqldatabase_...
```

A continuación verás el progreso del despliegue recurso a recurso:

```
  ⏳ CognitiveServices/accounts/ais-contosoretail-ab3f2 ...
  ✅ Storage/storageAccounts/stcontosoretailab3f2
  ✅ Web/serverFarms/asp-contosoretail-ab3f2
  ✅ Web/sites/func-contosoretail-ab3f2
  ✅ CognitiveServices/accounts/ais-contosoretail-ab3f2
  ✅ Código publicado exitosamente.
```

El proceso completo toma entre **5 y 10 minutos**.

> 👁️ **¡Toma nota de la salida final!** Al terminar, el script muestra los nombres y URLs de todos los recursos creados. Necesitarás estos valores para configurar los agentes en los siguientes pasos.

---

### Paso 8: Registrar los outputs del despliegue

Al finalizar, anota estos valores de la salida del script:

| Output del script | Descripción | Dónde se usa |
|---|---|---|
| `Sufijo único` | 5 caracteres, ej: `ab3f2` | Para identificar tus recursos en Azure |
| `Function App Base URL` | URL base de la API | `appsettings.json` de Anders y Julie |
| `Foundry Project Endpoint` | Endpoint del proyecto Foundry | `appsettings.json` de Anders y Julie |
| `Bing Connection Name` | Nombre de la conexión Bing | `appsettings.json` de Julie |
| `Bing Connection ID (Julie)` | ID de la conexión Bing | `appsettings.json` de Julie |

---

### Paso 9: Configurar los appsettings.json de los agentes

#### Anders (Lab 3)

En el panel de archivos del Codespace, abre:
`es/labs/foundry/code/agents/AndersAgent/ms-foundry/appsettings.json`

Reemplaza los valores `<sufijo>` con el sufijo que obtuviste en el paso anterior:

```json
{
  "FoundryProjectEndpoint": "https://ais-contosoretail-<sufijo>.services.ai.azure.com/api/projects/aip-contosoretail-<sufijo>",
  "ModelDeploymentName": "gpt-4.1",
  "FunctionAppBaseUrl": "https://func-contosoretail-<sufijo>.azurewebsites.net/api",
  "TenantId": ""
}
```

> **TenantId**: déjalo vacío si solo tienes una cuenta de Azure activa en el Codespace (lo habitual). Si estás trabajando localmente y tienes múltiples tenants, ingresa el Tenant ID del tenant del taller:
> ```bash
> az account show --query tenantId -o tsv
> ```

#### Julie (Lab 4)

Abre `es/labs/foundry/code/agents/JulieAgent/appsettings.json` y completa todos los valores usando los outputs del despliegue anotados en el Paso 8.

---

### Paso 10: Asignar permisos RBAC en Foundry

Para que los agentes puedan crearse y ejecutarse, tu usuario necesita el rol **Cognitive Services User** sobre el recurso de AI Services. Sin este rol obtendrás un error `PermissionDenied` al intentar crear agentes.

Ejecuta estos comandos en la terminal del Codespace (bash):

```bash
# Obtener el Object ID del usuario autenticado (funciona con cuentas MSA/personales y cuentas de trabajo)
objectId=$(az ad signed-in-user show --query id -o tsv 2>/dev/null || \
    az account get-access-token --query accessToken -o tsv | \
    python3 -c "import sys,base64,json; t=sys.stdin.read().strip(); p=t.split('.')[1]; p+='='*(4-len(p)%4); print(json.loads(base64.b64decode(p))['oid'])")

# Obtener el nombre del recurso AI Services creado por el despliegue
aisName=$(az cognitiveservices account list \
    --resource-group rg-contoso-retail \
    --query "[0].name" -o tsv)

# Asignar el rol usando el Object ID (no requiere permisos de Graph API)
az role assignment create \
    --assignee-object-id "$objectId" \
    --assignee-principal-type User \
    --role "Cognitive Services User" \
    --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-contoso-retail/providers/Microsoft.CognitiveServices/accounts/$aisName"
```

Espera **1 minuto** para que se propague el permiso antes de ejecutar los agentes.

> **Nota:** Si obtienes un error `RoleAssignmentExists`, el rol ya fue asignado automáticamente por el script de despliegue. Puedes continuar.

---

### Paso 11: Verificar el despliegue

Confirma que todos los recursos se crearon correctamente:

```bash
az resource list --resource-group rg-contoso-retail --output table
```

El resultado debe incluir estos recursos:

| Recurso             | Nombre                          | Descripción |
| ------------------- | ------------------------------- | ----------- |
| Storage Account     | `stcontosoretail{suffix}`       | Almacenamiento para la Function App |
| App Service Plan    | `asp-contosoretail-{suffix}`    | Plan de hosting Flex Consumption |
| Function App        | `func-contosoretail-{suffix}`   | API de Contoso Retail (.NET 8, dotnet-isolated) |
| AI Foundry Resource | `ais-contosoretail-{suffix}`    | AI Services + proyectos Foundry con GPT-4.1 |
| AI Foundry Project  | `aip-contosoretail-{suffix}`    | Proyecto de trabajo en Foundry |
| Bing Search         | `bing-contosoretail-{suffix}`   | Conexión de búsqueda web para el agente Julie |

> **Nota:** El `{suffix}` es un identificador único de 5 caracteres generado automáticamente a partir del número de tenant que suministraste. Esto garantiza que los nombres de recursos no colisionen entre participantes.

---

### Gestión del Codespace

#### Pausar (para conservar horas gratuitas)

El Codespace se pausa automáticamente tras **30 minutos de inactividad**. También puedes pausarlo manualmente desde la pestaña Codespaces en GitHub. Tus archivos y configuración se conservan entre sesiones.

#### Retomar una sesión guardada

- Ve al repositorio en GitHub → **Code** → **Codespaces** → haz clic en tu Codespace existente.
- El entorno reabre en pocos segundos con todo tal como lo dejaste.
- Verifica que la sesión de Azure CLI sigue activa con `az account show`. Si expiró, repite el Paso 5.

#### Borrar al finalizar el taller

Para liberar las horas de tu cuota:
- Ve a `github.com/codespaces`
- Busca tu Codespace → clic en `···` → **Delete**.

> ⚠️ Al borrar el Codespace se pierden los cambios locales no commiteados. Si modificaste los `appsettings.json` y quieres guardarlos, cópialos en un lugar seguro antes de borrar.

#### Horas gratuitas disponibles

GitHub ofrece **120 horas/mes** gratuitas en máquinas de 2-core para cuentas personales. Un taller de 8 horas consume solo el 7% del límite mensual.

---

## Estructura del código

```
labs/foundry/
├── setup.md                               ← Guía de setup en máquina local
├── codespaces-setup.md                    ← Este archivo (guía Codespaces — recomendada)
├── lab03-anders-executor-agent.md         ← Lab 3: Agente Anders
├── lab04-julie-planner-agent.md           ← Lab 4: Agente Julie
├── setup/
│   ├── op-flex/                           ← ⭐ Opción recomendada (Flex Consumption / Linux)
│   │   ├── main.bicep
│   │   ├── storage-rbac.bicep
│   │   ├── deploy.ps1                     ← Script para máquina local (Windows/macOS/Linux)
│   │   └── deployFromAzure.ps1            ← Script para Codespaces / Azure Cloud Shell
│   └── op-consumption/                    ← Opción clásica (Consumption Y1 / Windows)
│       ├── main.bicep
│       ├── storage-rbac.bicep
│       └── deploy.ps1
└── code/
    ├── api/
    │   └── FxContosoRetail/               ← Azure Function (API)
    │       ├── FxContosoRetail.cs         ← Endpoints: HolaMundo, OrdersReporter, SqlExecutor
    │       ├── Program.cs
    │       ├── Models/
    │       └── ...
    ├── agents/
    │   ├── AndersAgent/                   ← Console App: Agente Anders (kind: prompt + OpenAPI tool)
    │   │   ├── ms-foundry/                ← Versión Responses API (recomendada)
    │   │   │   ├── Program.cs
    │   │   │   └── appsettings.json
    │   │   └── ai-foundry/                ← Versión Persistent Agents API (alternativa)
    │   │       └── ...
    │   └── JulieAgent/                    ← Console App: Agente Julie (kind: workflow)
    │       ├── Program.cs                 ← Crea los 3 agentes + chat con Julie
    │       ├── JulieAgent.cs              ← Julie: workflow con 3 tools (SqlAgent, MarketingAgent, OpenAPI)
    │       ├── SqlAgent.cs                ← Sub-agente: genera T-SQL a partir de lenguaje natural
    │       ├── MarketingAgent.cs          ← Sub-agente: genera mensajes con Bing Search
    │       ├── db-structure.txt           ← DDL de la BD inyectada en SqlAgent
    │       └── appsettings.json
    └── tests/
        ├── bruno/                         ← Colección Bruno (REST client)
        │   ├── bruno.json
        │   ├── OrdersReporter.bru
        │   └── environments/
        │       └── local.bru
        └── http/
            └── FxContosoRetail.http       ← Archivo .http (VS Code REST Client)
```

---

## Laboratorios

| Lab   | Archivo                                                   | Descripción                                                  |
| ----- | --------------------------------------------------------- | ------------------------------------------------------------ |
| Lab 3 | [Anders — Executor Agent](lab03-anders-executor-agent.md) | Crear el agente ejecutor que genera reportes e interactúa con servicios de Contoso Retail. |
| Lab 4 | [Julie — Planner Agent](lab04-julie-planner-agent.md)     | Crear el agente orquestador de campañas de marketing usando el patrón workflow con sub-agentes (SqlAgent, MarketingAgent) y herramienta OpenAPI. |

---

## Siguiente paso

Una vez completado el setup, continúa con el [Lab 3 — Anders (Executor Agent)](lab03-anders-executor-agent.md).
