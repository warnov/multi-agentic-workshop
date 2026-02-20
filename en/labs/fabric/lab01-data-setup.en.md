# Lab 01: Data Setup

# MF – Environment Setup

MF – Environment Setup

Microsoft Fabric – Environment Setup


## 🎯 Mission Brief

In this lab you will learn how to build the foundation for your data platform using Microsoft Fabric. Throughout the guide, you will create a Fabric capacity that will serve as the central environment to host the database and manage information in an organized and scalable way. You will then develop the semantic model, enabling data to be consumed efficiently by different analytics and artificial intelligence experiences.

By following the step-by-step instructions, you will gain hands-on experience in data preparation and how to create a solid foundation that will enable integration with solutions such as Copilot and AI agents.

## 🔎 Objectives

By completing this lab you will:

1. Create a Fabric capacity named "wsfbcagentic".
2. Create the workspace "wsfcagentic". The name must be unique, therefore concatenate your username to "wsfcagentic".
3. Create a SQL database "db_retail" and load the data.
4. Create a Semantic Model on top of the data loaded into the "db_retail" database.

In the following section, follow the lab steps:

---

## 0 Register Microsoft.Fabric as a resource in the subscription

a. Open Subscription in Azure Portal

![Open subscription](images/0.1.png)

b. Register the resource in the subscription

![Register Fabric in the subscription](images/0.2.png)

## 1. Create your Microsoft Fabric capacity

a. Sign in to [Microsoft Azure](https://portal.azure.com/#home)

b. Search for the Microsoft Fabric service and select it

![Search Service](images/1.1.png)

c. Click Create a new Microsoft Fabric capacity

![Create Capacity](images/1.1.c.png)

d. Create a resource group for the Microsoft Fabric capacity

![Create Resource Group](images/1.2.png)

e. Set the configuration to be created:

i. Define name. The name must be unique, therefore concatenate your username to "wsfcagentic".
ii. Select region
iii. Change capacity size
iv. Select capacity size
v. Review the configuration

![Validation](images/1.3.e.png)

f. Once the configuration is validated, proceed to create the Microsoft Fabric capacity

![Create Capacity](images/1.6.png)

g. Once the capacity creation is finished, you can go to the resource

![Explore the resource](images/1.7.png)

h. Explore the deployed Microsoft Fabric resource
i. Start or pause the capacity
ii. Change the capacity size
iii. Assign new capacity administrators

![Create Capacity](images/1.8.png)

---

## 2. Create your workspace "wsfcagentic"

a. Sign in to [Microsoft Fabric](https://app.fabric.microsoft.com/)

b. Go to the Workspaces tab and select New Workspace

![Create Capacity](images/2.1.png)

c. Specify the workspace configuration

![Create Capacity](images/2.2.png)

d. Specify the workspace type (Fabric)

![Create Capacity](images/2.3.png)

e. Select the Fabric capacity that the workspace will use. Only capacities that are turned on will appear.

![Create Capacity](images/2.4.png)

f. After completing the configuration, specify that the capacity will use the default storage format and apply the changes to create the workspace. 

For more information about Large semantic models in Power BI Premium, see the [link](https://learn.microsoft.com/es-es/fabric/enterprise/powerbi/service-premium-large-models#enable-large-models).

![Create Capacity](images/2.5.png)

f. Once the workspace has been created, it should looks like the following image:

![Create Capacity](images/2.6.png)

---

## 3. Create Database and Load Data

a. Select the option to create a new item

![New Item](images/3.1.png)

b. Filter by SQL database and select the SQL database option as shown in the image

![Search SQL Database](images/3.2.png)

c. Assign the name db_retail and create the database

![Create DB](images/3.3.png)

d. Once the database is created, you will have a new tab open that will allow you to quickly access the database. Likewise, you will be able to quickly navigate through database elements such as tables, views, stored procedures, functions, etc., through the object explorer.

![Explore DB](images/3.4.png)

e. Open a New Query tab to execute SQL scripts

![New Query](images/3.5.png)

f. To create the tables with their respective data, copy the SQL code contained in the file [Create database.sql ](SQLScripts/CreateDatabase.sql) and execute it by clicking the Run option.

![Table creation and data insertion](images/3.6.png)

g. Confirm successful execution of the script

![Script executed successfully](images/3.7.png)

h. To finish adjusting the data, in the SQL Query 1 tab, replace the SQL code that was already executed in the previous step with the code from the file [Update Dates.sql](SQLScripts/UpdateDates.sql) and execute it.

![Open SQL code execution tab](images/3.8.png)

i. After executing it, it will be shown that several rows of the SQL tables have been affected as a result. This script is only responsible for making date adjustments to the database data.

![Data update](images/3.9.png)

---

## 4. Create Semantic Model (optional)

In Microsoft Fabric, a semantic model is the business layer that gives meaning to technical data and makes it easy to analyze, reuse, and govern.

a. Go to the workspace

![Go to Workspace](images/sm4.a.png)

b. Open the SQL Analytics Endpoint of the db_retail database

![SQL Analytics Endpoint ](images/sm4.b.png)

c. Create a new semantic model

![New semantic model](images/sm4.c.png)

d. Configure the semantic model:

i. Name: sm_retail
ii. Corresponding workspace
iii. Tables: customer, orders, orderline, product
iv. Confirm

![Semantic model configuration](images/sm4.d.png)

e. Open the created semantic model

![Open semantic model](images/sm4.e.png)

f. Switch to edit view

![Edit view](images/sm4.f.png)

g. Create semantic model relationships:

![New relationship](images/sm.4.g.png)

Add relationship

![Edit view](images/sm4.g.1.png)

i. Customer → Orders (1:*)

![Customer → Orders](images/sm4.g.2.png)

ii. Orders → Orderline (1:*)

![Orders → Orderline](images/sm4.g.3.png)

iii. Orderline → Product (1:1)

![Orderline → Product](images/sm4.g.4.png)

h. Final result of the semantic model

![Semantic model](images/sm4.g.5.png)

---

## Mission Complete

Your data platform has been created and your data is ready to be processed and consumed by AI agents.
