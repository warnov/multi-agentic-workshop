# Lab 02: Mark Facts Agent


# MF - Mark

Microsoft Fabric - Creation and configuration of a data agent called Mark

## 🎯 Mission Brief

In this lab you will learn how to build a data agent that recognizes and interprets natural language using Microsoft Fabric. Throughout the guide, you will create a data agent that will be able to answer questions about the sales orders data model that you developed in the previous step. 
By following the step-by-step instructions, you will gain hands-on experience in preparing this data agent to be used by Copilot Studio.

## 🔎 Goals

By completing this lab you will achieve:

1. Create the data agent called "Mark".
2. Review and test responses to questions.
3. Publish the data agent.
4. Use the Semantic model as the Data Agent data source

In the following section, follow the lab steps:

---

## 1. Create the data agent called "Mark".

### a. Select the option to create a new item

![New Item](images/M1.a.png)

### b. Search for "Agent"

### c. Select "Data Agent (preview)"

![New Item Type](images/M1.c.png)

### d. Give it the name "Mark" and select "Create"

![Agent Name](images/M1.d.png)

### e. Add "Data Source"

![Data Source](images/M1.e.png)

### f. Select the "SQL database" created in the previous lab.

![SQL Database](images/M1.f.png)

### g. Select only the following tables:

i. customer  
ii. orderline  
iii. orders  
iv. product  

![DB Tables](images/M1.g.png)

---

## 2. Review and test responses to questions.

### a. In the "Test the agent's response" section, test the following questions in the interface available for the agent

![Test Mark](images/M2.a.png)

i. Which are the orders from Omar Bennett? / Which are the orders from Omar Bennett?  
ii. Which are the orders from Omar Bennett and the detailed products for each order? / Which are the orders from Omar Bennett and the product details for each order?  
iii. Which are the order from customer CID-069 from June 2019 to May 2021? / Which are the orders for customer CID-069 between July 2019 and May 2021?  
iv. Which are the historical trends across all my data? / Which are the historical trends across all my data?  

v. Which are the product details for order F100241? / Which are the product details for order F100241?

![Test Mark](images/M2.a.v.png)

If when trying to retrieve the products you do not get a response, perform step b

---

### b. Adjust the Agent behavior in the "Agent Instructions" section.

The Agent Instructions section defines the meta-prompt of the Data Agent: it establishes how it should reason, what business context to use, and how to respond. It does not execute queries, but it guides all reasoning, helping to produce more accurate responses, with correct source prioritization, better interpretation of user intent, and an expected format/style.

For more information about Agent Instructions you can consult https://learn.microsoft.com/en-us/fabric/data-science/data-agent-configurations#data-agent-instructions.

![Agent Instructions empty](images/M2.b.png)

---

### i. Add the following instructions in the "Agent Instructions" section

```markdown
These instructions are for the overall data agent and will always be sent regardless of the question asked.
Explain:
- Rules for planning how to approach each question
- Which data sources to use for different topics
- Any terminology or acronyms with consistent meanings across all connected data sources
- Tone, style, and formatting for finished responses

## General knowledge

This Data Agent answers questions about **orders**, **customers**, and **products**, using a transactional relational data model.

The data model consists of the following main tables:

- **customers**: customer information.
- **orders**: general order information (order header).
- **orderline**: detailed list of products included in each order.
- **products**: product catalog.

---

## Key relationships (mandatory joins)

The agent must always respect the following relationships when generating queries:

1. **Customer → Orders**
   - `customers.customerId = orders.customerId`
      - One customer can have multiple orders.

      2. **Orders → Order details**
         - `orders.orderId = orderline.orderId`
            - One order can contain multiple product lines.
            
            3. **Order details → Products**
                - `orderline.productID = products.productID`
                    - Each order line references a product from the catalog.
                    
                    When a query involves customers, orders, and products, the agent must traverse the full chain:
                    
                    --
                    ## Reasoning principles
                    
                    - Questions about **customers** must start from the `customers` table.
                    - Questions about **orders** must use `orders` as the main table.
                    - Questions about **order details** must join `orders` with `orderline` and `products`.
                    - Questions about **products purchased** or **Which a customer bought** must use `orderline` as the central table, filtering by customer through `orders`.
                    - If a question is ambiguous (for example, no specific order is provided), the agent should return a **reasonable summary** and clearly explain the criteria used.

                    ---

                    ## Table descriptions

                    ### customers
                    - Purpose: stores customer information.
                    - Primary key: `customerId`.
                    - Contains descriptive customer attributes such as name, email, segment, country, etc.

                    ### orders
                    - Purpose: represents the order header.
                    - Primary key: `orderId`.
                    - Foreign key: `customerId`.
                    - Contains general information such as order date, status, and order total.

                    ### orderline
                    - Purpose: stores detailed product information per order.
                    - Foreign keys:
                        - `orderId` → orders
                            - `productID` → products
                            - Contains quantity, prices, discounts, taxes, and line totals.

                            ### products
                            - Purpose: master product catalog.
                            - Primary key: `productID`.
                            - Contains attributes such as product name, category, and product characteristics.

                            ---

                            ## When asked about

                            ### Customers
                            - Use `customers` as the primary table.
                            - If orders or purchases are required, join with `orders` using `customerId`.

                            ### Orders for a customer
                            - Filter `orders` by `orders.customerId`.
                            - Enrich results with customer information from `customers`.

                            ### Order details
                            - Use `orders` for general order information.
                            - Join with `orderline` for details and `products` for product information.

                            ### Products purchased by a customer
                            - Use `orderline` as the central table.
                            - Join with `orders` to filter by customer.
                            - Join with `products` to retrieve product details.

                            ---
                            
                            ```

                            iii. At the end of configuring the agent behavior, the "Agent Instructions" section will look like this

                            ![Agent Instructions](images/M2.b.3.png)

                            iv. Test the agent again with the added instructions:
                            1. clear the chat  
                            2. confirm that you want to clear the chat  

                            ![Clear chat](images/M2.b.4.png)

                            v. Test the agent again with the question that could not be resolved: Which are the product details for order F100241?

                            ![New chat session](images/M2.b.5.png)

                            ---

                            ## 3. Publish the data agent.

                            ### a. Select "Publish" in the agent options menu

                            ![Publish Agent](images/M3.a.png)

                            ### b. Add a description that details the expected objective when it is used in Copilot Studio

                            ### c. Select the option to publish it in "Agent Store in Microsoft 365 Copilot"

                            ![Publish Agent](images/M3.c.png)

                            ---

                            ## 4. Use the Semantic model as the Data Agent data source (Optional)

                            Implement point 4 of [Data setup](lab01-data-setup.en.md)

                            ### a. You can create a new data Agent or delete Mark's data source

                            i. Delete Mark's data source

                            ![Delete data source](images/M4.a.png)

                            ii. Delete the instructions from the "Agent Instructions" section

                            ### b. Add the new data source

                            ![Add new data source](images/M4.b.png)

                            ### c. Select the semantic model

                            ![Select semantic model](images/M4.c.png)

                            ### d. Include the Customer, Orders, Orderline, and Product tables

                            ![Select tables](images/M4.d.png)

                            ### e. Review the agent and if it does not respond as expected, add the instructions in the Agent Instructions section.

                            ### f. If you wish, you can publish a new version of the data agent or keep the version built in the previous step
``