# Limpeza e reimplantação do Microsoft Foundry

Use este guia quando precisar remover completamente os recursos do Azure criados pelo setup do Foundry e implantá-los novamente do zero.

> [!WARNING]
> Estas operações excluem permanentemente os recursos e dados do Foundry usados no workshop. Verifique a assinatura ativa e os nomes dos recursos antes de continuar.

## Por que excluir o Resource Group pode não ser suficiente

Por padrão, a implantação cria os recursos do Azure em `rg-contoso-retail`. A exclusão desse Resource Group remove a Function App, a Storage Account, o App Service Plan, o projeto do Foundry, a implantação do modelo, as conexões e as atribuições de função.

O recurso do Foundry, chamado `ais-contosoretail-<suffix>`, é um recurso do Azure AI Services. O Azure o mantém em estado de exclusão temporária (soft-delete) por até 48 horas. Enquanto permanecer nesse estado, seu nome não poderá ser reutilizado e uma nova implantação com o mesmo sufixo poderá falhar.

O sufixo é gerado de forma determinística a partir do `TenantName` enviado ao script de implantação. Reutilizar esse valor gera o mesmo sufixo e os mesmos nomes de recursos.

## Pré-requisitos

- Azure CLI instalado e atualizado.
- Uma sessão ativa na assinatura correta do Azure.
- Permissão para excluir o Resource Group.
- Permissão para eliminar permanentemente recursos do Cognitive Services. Pode ser necessário ter a função `Contributor` no escopo da assinatura; apenas o escopo do Resource Group não é suficiente para a operação de purge.

Confirme a assinatura ativa:

```powershell
az account show --output table
```

Se necessário, selecione a assinatura correta:

```powershell
az account set --subscription "<nome-ou-id-da-assinatura>"
```

## Passo 1 - Registre os dados do recurso do Foundry

Antes de excluir o Resource Group, registre o nome, a localização e o Resource Group do recurso do Foundry:

```powershell
az cognitiveservices account list `
  --resource-group rg-contoso-retail `
  --query "[].{Name:name, Location:location, ResourceGroup:resourceGroup}" `
  --output table
```

Por padrão, os valores esperados são:

- Resource Group: `rg-contoso-retail`
- Recurso do Foundry: `ais-contosoretail-<suffix>`
- Localização: `eastus`, a menos que outra localização tenha sido escolhida durante a implantação

## Passo 2 - Exclua o Resource Group

```powershell
az group delete `
  --name rg-contoso-retail `
  --yes
```

Aguarde a conclusão da exclusão antes de procurar o recurso excluído temporariamente.

## Passo 3 - Localize o recurso do Foundry excluído temporariamente

```powershell
az cognitiveservices account list-deleted `
  --query "[].{Name:name, Location:location, ResourceGroup:resourceGroup}" `
  --output table
```

O recurso pode levar alguns minutos para aparecer. Se ele não for listado imediatamente, aguarde um pouco e execute o comando novamente.

## Passo 4 - Elimine permanentemente o recurso do Foundry

Substitua `<suffix>` e a localização quando necessário:

```powershell
az cognitiveservices account purge `
  --name ais-contosoretail-<suffix> `
  --resource-group rg-contoso-retail `
  --location eastus
```

O valor de `--resource-group` deve ser o nome do Resource Group original, mesmo que esse grupo já tenha sido excluído.

> [!CAUTION]
> A operação de purge é permanente. Depois de executá-la, o recurso, seus dados e suas chaves não poderão ser recuperados.

## Passo 5 - Verifique a operação de purge

```powershell
az cognitiveservices account list-deleted `
  --query "[?name=='ais-contosoretail-<suffix>']" `
  --output table
```

Se nenhum resultado for exibido, o recurso excluído não está mais na lista. O Azure ainda pode levar alguns minutos para liberar o nome antes de permitir uma nova implantação.

Agora você pode executar novamente o script de implantação do Foundry usando o mesmo `TenantName` e nome de Resource Group.

## Erros comuns

### O recurso excluído não foi encontrado

A exclusão ainda pode estar sendo propagada. Aguarde alguns minutos, execute `az cognitiveservices account list-deleted` novamente e use exatamente o nome, o Resource Group original e a localização exibidos no resultado.

### Falha de autorização durante a operação de purge

Verifique se sua conta tem a permissão de purge necessária no escopo da assinatura. A função `Contributor` atribuída somente ao Resource Group excluído não é suficiente.

### A nova implantação informa que o nome já existe

Confirme que o recurso do Foundry não aparece mais em `list-deleted`. Se a operação de purge acabou de ser concluída, aguarde alguns minutos antes de tentar a implantação novamente.