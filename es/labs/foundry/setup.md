# Microsoft Foundry — Setup en Máquina Local

> 💡 **¿Prefieres evitar instalar herramientas?** La opción recomendada para el taller es usar **GitHub Codespaces** — entorno pre-configurado listo en 2 minutos, sin instalar nada. Consulta [codespaces-setup.md](codespaces-setup.md) para la guía paso a paso.

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

## Setup de infraestructura

Antes de iniciar los laboratorios, cada participante debe desplegar la infraestructura de Azure en su propia suscripción. El proceso es automatizado con Bicep y un script de PowerShell.

### Prerrequisitos

- **Azure CLI** instalado y actualizado ([instalar](https://aka.ms/installazurecli))

- **.NET 8 SDK** instalado ([descargar](https://dotnet.microsoft.com/download/dotnet/8.0))

- **PowerShell 7+** (requerido en todos los sistemas operativos, incluido Windows)
  - Windows: `winget install Microsoft.PowerShell` o [descargar MSI](https://aka.ms/powershell-release?tag=stable)
  - Linux: [instrucciones](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-linux)
  - macOS: `brew install powershell/tap/powershell` o [instrucciones](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-macos)
  > ⚠️ **Importante:** Ejecuta los scripts desde `pwsh` (PowerShell 7), **no** desde `powershell` (5.1). PowerShell 5.1 no es compatible.
  
- **ExecutionPolicy** configurada (solo Windows): Para poder ejecutar script provenientes de un origen como Github, es necesario habilitar ésta opción. Para ello abre `pwsh` como administrador y ejecuta:
  
  ```powershell
  Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
  ```
  
  ✅Esto solo es requerido una vez.
  
- Una **suscripción de Azure** activa con permisos de Owner o Contributor

   - Cuando tengas tu tenant listo para trabajar, anota el **número del tenant temporal** asignado: Si el usuario que te asignaron es usuario@azurehol3387.com, entonces tu número de tenant es 3387.

- Los valores de conexión y base de datos en Microsoft Fabric. Para obtenerlos, sigue [esta](./setup/sql-parameters.md) guía.


### ↗️Despliegue

Para desplegar los elementos requeridos en estos laboratorios, hemos preparado unos scripts con Bicep y PowerShell que nos permiten automatizar el proceso sin necesidad de entrar manualmente al portal de Azure o de Foundry a crear recursos manualmente. 

Estos scripts pueden ser ejecutados desde nuestras máquinas locales. Pero, para poder ejecutar acciones, necesitamos autenticar nuestro proceso local con Azure para obtener dichos permisos. Por lo tanto, debemos comenzar autenticándonos en Azure desde la terminal.

1. **Abrir una terminal en VS Code:** usa el menú **Terminal → New Terminal** o el atajo <kbd>Ctrl</kbd>+<kbd>`</kbd>.

2. **Iniciar sesión con Azure CLI:**

   ```powershell
   az login
   ```
   Esto abrirá el navegador para que te autentiques con la cuenta de Azure que se te asignó para el laboratorio. Una vez completado, la terminal mostrará la lista de suscripciones disponibles. Escoge la opción por defecto (usualmente la primera) o la que corresponda a tu tenant.

3. **Verificar la suscripción activa:**

   ```powershell
   az account show --output table
   ```
   Confirma que la suscripción mostrada es la correcta para el taller. Si necesitas cambiarla:
   
   ```powershell
   az account set --subscription "nombre-o-id-de-la-suscripcion"
   ```

### Ejecución del Script

Una vez confirmado el login con el usuario adecuado a tu suscripción de Azure, ejecuta: 

``` powershell
az bicep upgrade
cd labs\foundry\setup\op-flex
.\deploy.ps1
```

Después de esto, el script te preguntará interactivamente por los parámetros de tu despliegue. Presiona **Enter** para aceptar el valor por defecto en el caso de la zona y grupo de recursos. Aquí puedes ver un ejemplo de una ejecución:

``` powershell
TenantName: 3345
Presiona Enter para default.
Location [eastus]: 
ResourceGroupName [rg-contoso-retail]: 
¿Deseas configurar ahora la conexión SQL de Fabric para Lab04? (s/N): s
FabricWarehouseSqlEndpoint (sin protocolo, sin puerto): kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com
FabricWarehouseDatabase: retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b
```

La siguiente confirmación se te presentará:

``` powershell
========================================
 Taller Multi-Agéntico - Despliegue
 Plan: Flex Consumption (FC1 / Linux)
========================================

  Tenant:         3345
  Location:       eastus
  Resource Group: rg-contoso-retail
  Fabric SQL:     kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com
  Fabric DB:      retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b
```

Luego de esto comenzarás a ver el progreso del despliegue y se te informará acerca de los recursos que se están desplegando. En menos de 10 minutos deberías tener todo tu ambiente de trabajo listo para la acción.

---

> 👁️**Revisar la salida.** Al finalizar, el script muestra los nombres y URLs de todos los recursos creados. Toma nota de estos valores, los necesitarás en los laboratorios!

> **Nota:** Si no proporcionas los parámetros de Fabric, el despliegue **no falla**. Omite la configuración de la conexión SQL y muestra un aviso para configurarla manualmente después. La conexión SQL solo se necesita para el Lab 5 (Julie) y la Function App `SqlExecutor`.

---

### Verificación

Después del despliegue, verifica que los recursos se crearon correctamente:

```powershell
az resource list --resource-group rg-contoso-retail --output table
```

---

El resultado debería contener estos mismos elementos (los nombres pueden variar):

| Recurso             | Nombre                        | Descripción                                                  |
| ------------------- | ----------------------------- | ------------------------------------------------------------ |
| Storage Account     | `stcontosoretail{suffix}`     | Almacenamiento para la Function App                          |
| App Service Plan    | `asp-contosoretail-{suffix}`  | Plan de hosting: Flex para Azure Functions                   |
| Function App        | `func-contosoretail-{suffix}` | API de Contoso Retail (.NET 8, dotnet-isolated)              |
| AI Foundry Resource | `ais-contosoretail-{suffix}`  | Recurso unificado de AI Foundry (AI Services + gestión de proyectos) con modelo GPT-4.1 desplegado |
| AI Foundry Project  | `aip-contosoretail-{suffix}`  | Proyecto de trabajo dentro del Foundry Resource              |

> **Nota:** El `{suffix}` es un identificador único de 5 caracteres generado automáticamente a partir del número de tenant que suministraste. Esto garantiza que los nombres de los recursos no colisionen entre participantes.

### Permisos RBAC para Microsoft Foundry

Para que los agentes puedan crearse y ejecutarse en Microsoft Foundry, tu usuario necesita el rol **Cognitive Services User** sobre el recurso de AI Services. Este rol incluye el data action `Microsoft.CognitiveServices/*` necesario para operaciones de agentes. Si no lo tienes, obtendrás un error `PermissionDenied` al intentar crear agentes.

Ejecuta los siguientes comandos para asignar el rol (reemplaza `{suffix}` con tu sufijo de 5 caracteres):

```powershell
# Obtener tu nombre de usuario (UPN)
$upn = az account show --query "user.name" -o tsv

# Asignar el rol Cognitive Services User sobre el recurso de AI Services
az role assignment create `
    --assignee $upn `
    --role "Cognitive Services User" `
    --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-contoso-retail/providers/Microsoft.CognitiveServices/accounts/ais-contosoretail-{suffix}"
```

> **Nota:** Si no conoces el nombre exacto del recurso, puedes averiguarlo con:
> ```powershell
> az cognitiveservices account list --resource-group rg-contoso-retail --query "[].name" -o tsv
> ```
>
> La propagación de RBAC puede tardar hasta 1 minuto. Espera antes de intentar crear agentes.

---

## Estructura del código

```
labs/foundry/
├── setup.md                               ← Este archivo (setup en máquina local)
├── codespaces-setup.md                    ← Guía de setup con Codespaces (recomendada)
├── lab03-anders-executor-agent.md          ← Lab 3: Agente Anders
├── lab04-julie-planner-agent.md           ← Lab 4: Agente Julie
├── setup/
│   ├── op-flex/                           ← ⭐ Opción recomendada (Flex Consumption / Linux)
│   │   ├── main.bicep
│   │   ├── storage-rbac.bicep
│   │   ├── deploy.ps1                     ← Script para máquina local
│   │   └── deployFromAzure.ps1            ← Script para Codespaces / Azure Cloud Shell
│   └── op-consumption/                    ← Opción clásica (Consumption Y1 / Windows)
│       ├── main.bicep
│       ├── storage-rbac.bicep
│       └── deploy.ps1
└── code/
    ├── api/
    │   └── FxContosoRetail/               ← Azure Function (API)
    │       ├── FxContosoRetail.cs          ← Endpoints: HolaMundo, OrdersReporter, SqlExecutor
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
    │       ├── MarketingAgent.cs           ← Sub-agente: genera mensajes con Bing Search
    │       ├── db-structure.txt            ← DDL de la BD inyectada en SqlAgent
    │       └── appsettings.json
    └── tests/
        ├── bruno/                         ← Colección Bruno (REST client)
        │   ├── bruno.json
        │   ├── OrdersReporter.bru
        │   └── environments/
        └── http/
            └── FxContosoRetail.http       ← Archivo .http (VS Code REST Client)
```

---

## Laboratorios

| Lab   | Archivo                                                   | Descripción                                                  |
| ----- | --------------------------------------------------------- | ------------------------------------------------------------ |
| Lab 4 | [Anders — Executor Agent](lab03-anders-executor-agent.md) | Crear el agente ejecutor que genera reportes e interactúa con servicios de Contoso Retail. |
| Lab 5 | [Julie — Planner Agent](lab04-julie-planner-agent.md)     | Crear el agente orquestador de campañas de marketing usando el patrón workflow con sub-agentes (SqlAgent, MarketingAgent) y herramienta OpenAPI. |

---

## Siguiente paso

Una vez completado el setup, continúa con el [Lab 3 — Anders (Executor Agent)](lab03-anders-executor-agent.md).
