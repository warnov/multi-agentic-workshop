# Lab 01: Configuração de Dados

# Microsoft Fabric – Configuração do Ambiente


## 🎯 Resumo da Missão

Neste laboratório, você aprenderá a construir a base da sua plataforma de dados usando o Microsoft Fabric. Ao longo do guia, você criará a capacidade do Fabric que servirá como um ambiente central para hospedar o banco de dados e gerenciar as informações de forma organizada e escalável. Em seguida, você desenvolverá o modelo semântico, permitindo que os dados sejam consumidos com eficiência por diferentes experiências analíticas e de inteligência artificial.

Seguindo as instruções passo a passo, você obterá experiência prática na preparação de dados e na criação de uma base sólida que permitirá a integração com soluções como o Copilot e agentes de IA.

## 🔎 Objetivos

Ao concluir este laboratório, você será capaz de:

1. Criar a capacidade do Microsoft Fabric "wsfbcagentic".
2. Criar o workspace "wsfcagentic". O nome deve ser único; portanto, concatene o nome do seu usuário a "wsfcagentic".
3. Criar o banco de dados SQL "db_retail" e carregar os dados.
4. Criar o Modelo Semântico sobre os dados carregados no banco de dados "db_retail".

Na próxima seção, são apresentados os passos do laboratório:

---

## 0 Registrar Microsoft.Fabric como recurso na assinatura

a. Abrir a assinatura no Portal do Azure

![Abrir assinatura](images/0.1.png)

b. Registrar o recurso na assinatura
![Registrar o Fabric na assinatura](images/0.2.png)

## 1. Criar sua capacidade do Microsoft Fabric

a. Inicie sessão no [Microsoft Azure](https://portal.azure.com/#home)

b. Pesquise o serviço do Microsoft Fabric e selecione-o

![Buscar serviço](images/1.1.png)
c. Clique em Criar uma nova capacidade do Microsoft Fabric

![Criar capacidade](images/1.1.c.png)

d. Criar um grupo de recursos para a capacidade do Microsoft Fabric

![Criar grupo de recursos](images/1.2.png)

e. Definir a configuração que será criada:

i. Definir o nome. O nome deve ser único; portanto, concatene o nome do seu usuário a "wsfcagentic".
ii. Selecionar a região 
iii. Alterar o tamanho da capacidade 
iv. Selecionar o tamanho da capacidade 
v. Revisar a configuração

![Validação](images/1.3.e.png)

f. Depois de validar a configuração, prossiga para criar a capacidade do Microsoft Fabric

![Criar capacidade](images/1.6.png)

g. Após a conclusão da criação da capacidade, você já pode acessar o recurso

![Explorar o recurso](images/1.7.png)

h. Explorar o recurso do Microsoft Fabric implantado

i. Iniciar ou pausar a capacidade 
ii. Alterar o tamanho da capacidade 
iii. Nomear novos administradores da capacidade


![Criar capacidade](images/1.8.png)

---

## 2. Criar seu workspace "wsfcagentic"

a. Inicie sessão no [Microsoft Fabric](https://app.fabric.microsoft.com/)

b. Vá até a aba Workspaces e selecione Novo workspace


![Criar capacidade](images/2.1.png)

c. Especificar a configuração do workspace

![Criar capacidade](images/2.2.png)

d. Especificar o tipo de workspace (Fabric)

![Criar capacidade](images/2.3.png)

e. Selecionar a capacidade do Fabric que o workspace usará. Somente aparecerão as capacidades que estiverem ligadas. 


![Criar capacidade](images/2.4.png)

f. Ao finalizar a configuração, especifique que a capacidade usará o formato de armazenamento padrão e aplique as alterações para criar o workspace. Para mais informações sobre Large semantic models in Power BI Premium, consulte o [link](https://learn.microsoft.com/pt-br/fabric/enterprise/powerbi/service-premium-large-models#enable-large-models).

![Criar capacidade](images/2.5.png)

g. Depois que o workspace for criado, você terá uma área de trabalho semelhante à imagem a seguir:

![Criar capacidade](images/2.6.png)
 
---

## 3. Criar Banco de Dados e Carregar Dados

a. Selecionar a opção para criar um novo item

![Novo item](images/3.1.png)

b. Filtrar por SQL database e selecionar a opção SQL database, como mostrado na imagem

![Buscar banco de dados SQL](images/3.2.png)

c. Atribuir o nome db_retail e criar o banco de dados

![Criar BD](images/3.3.png)

d. Depois que o banco de dados for criado, você terá uma nova guia aberta, que permitirá acessar o banco rapidamente. Além disso, você poderá navegar rapidamente pelos elementos do banco de dados, como tabelas, views, procedimentos armazenados, funções etc., por meio do explorador de objetos.


![Explorar BD](images/3.4.png)

e. Abrir uma guia New Query para executar scripts SQL

![Nova consulta](images/3.5.png)

f. Para criar as tabelas com seus respectivos dados, copie o código SQL contido no arquivo [Create database.sql ](SQLScripts/CreateDatabase.sql) e execute-o clicando na opção Run.

![Criação de tabelas e inserção de dados](images/3.6.png)

g. Confirmar a execução correta do script

![Script executado corretamente](images/3.7.png)

h. Para finalizar o ajuste dos dados, na guia SQL Query 1 substitua o código SQL que já foi executado no passo anterior pelo código do arquivo [Update Dates.sql](SQLScripts/UpdateDates.sql) e execute-o.

![Abrir guia para execução de código SQL](images/3.8.png)

i. Após executá-lo, será exibido como resultado que várias linhas das tabelas SQL foram afetadas. Esse script serve apenas para fazer ajustes nas datas dos dados do banco.

![Atualização de dados](images/3.9.png)

---

## 4. Criar Modelo Semântico (opcional)

No Microsoft Fabric, um modelo semântico é a camada de negócios que dá significado aos dados técnicos e os torna fáceis de analisar, reutilizar e governar.

a. Ir para o workspace

![Ir para o workspace](images/sm4.a.png)

b. Abrir o SQL Analytics Endpoint do banco de dados db_retail

![SQL Analytics Endpoint](images/sm4.b.png)

c. Criar um novo modelo semântico

![Novo modelo semântico](images/sm4.c.png)

d. Configurar o modelo semântico:

i. Nome: sm_retail 
ii. Workspace correspondente 
iii. Tabelas: customer, orders, orderline, product 
iv. Confirmar


![Configuração do modelo semântico](images/sm4.d.png)

e. Abrir o modelo semântico criado

![Abrir modelo semântico](images/sm4.e.png)

f. Mudar para a visualização de edição


![Visualização de edição](images/sm4.f.png)

g. Criar relacionamentos do modelo semântico:

![Novo relacionamento](images/sm.4.g.png)
Adicionar relacionamento
![Visualização de edição](images/sm4.g.1.png)

i. Customer → Orders (1:*) 

![Customer → Orders](images/sm4.g.2.png)

ii. Orders → Orderline (1:*)  

![Orders → Orderline](images/sm4.g.3.png)

iii. Orderline → Product (1:1)

![Orderline → Product](images/sm4.g.4.png)


h. Resultado final do modelo semântico


![Modelo semántico](images/sm4.g.5.png)

---

## Mission Concluída

Sua plataforma de dados foi criada e seus dados estão prontos para serem processados e consumidos por agentes de IA.
