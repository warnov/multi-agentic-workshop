# MCS – Let’s build the Charlie agent

## 🎯 Mission summary

In this hands-on lab, you will create, publish, and deploy Charlie, our Product Analyst agent, which will focus on:  
Knowledge retrieval: searching for product descriptions from a file, answering user questions based on that “data,” and performing competitive market analysis for those products.  
You will also create a SharePoint site and store product documents as a knowledge source.

## 🔎 Objectives

By completing this lab, you will:

- Build the Charlie agent by following the instructions described in this document.
- Create a SharePoint site and store the product documentation.
- Test and publish.

---

## Create the new agent

**Navigate** to Copilot Studio. Make sure your environment is still selected in the Environment selector in the upper-right corner.

1. Select the **Agents** tab in the left navigation and select **Create an Agent**.

   ![imagen](img/image1_Charlie.png)

2. Select the **Configure** tab and complete the following properties:
   - **Edit the name to**: Charlie
   - **Description**: "Helps users answer product questions using SharePoint content and perform market or competitor comparisons using public information when explicitly requested".
   - **Leave the AI model as default.**

3. Add the agent instructions as indicated below:

   ![imagen](img/image2_Charlie.png)

   **Agent instructions to add:**

   ```text
   You are a Product Q&A and Market Comparison Agent.

   # Your goal is to help users:
   - Understand products using internal information stored in SharePoint.
   - Answer questions, summarize, and analyze that information.
   - Compare with the market using public internet information ONLY when the user explicitly requests it.

   # Key rules:
   1. Use SharePoint as the primary source by default.
   2. If the question can be answered using SharePoint, DO NOT use the internet.
   3. Use internet information only when the user asks for:
      - market analysis
      - competitor comparison
      - external or public information
   4. Do not invent information. If something is not available, state it clearly.

   # Response format:
   - Clear and structured responses.
   - Use lists or tables when they help comprehension.
   - Clearly distinguish between:
     - Internal information (SharePoint)
     - Public information (internet)
   - If important information is missing, indicate it instead of making assumptions.
   ```

---

## SharePoint creation

### Create the knowledge repository in SharePoint

1. In another tab, navigate to <https://www.office.com>.
2. Select the **Apps** section in the lower-left corner.

   ![imagen](img/image3_Charlie.png)

3. Open SharePoint.
4. Create a new site by selecting **+ create a site** in the upper-left corner.
5. Select **Teams Site**.

   ![imagen](img/image4_Charlie.png)

6. Choose a standard team template and select **Use Template**.
7. For the name, use **Product Repository**.
8. For Privacy settings: **Public – anyone in the organization can access this site**.
9. In the **add members** section, select your user and select **Finish**.

Great! Now we have our SharePoint site. Go to the **Documents** section:

![imagen](img/image5_Charlie.png)

10. Create a new folder and name it **Products**.
11. When ready, upload the file **Product_Catalog** that you downloaded from the GitHub repository [taller-multi-agentic/assets/Product_Catalog.docx](https://github.com/warnov/taller-multi-agentic/blob/main/assets/Product_Catalog.docx).
12. The knowledge base is ready! Let’s go back to the agent configuration.

---

## Configure knowledge sources

In the agent **Overview** section, add the agent knowledge sources as shown below:

![imagen](img/image6_Charlie.png)

**Make sure the “Web Search” option is enabled.**

![imagen](img/image7_Charlie.png)

1. Choose **SharePoint** and then select **Browse items**.

   ![imagen](img/image8_Charlie.png)

2. In the **Product Repository** site, select the **Products** folder and then select **Confirm Selection**.
3. Now select **Add to agent** to complete the process.

   ![imagen](img/image9_Charlie.png)

---

## Publish the agent

1. Select the **Publish** button in the upper-right corner. A pop-up window will open to confirm that you really want to publish your agent.

   ![imagen](img/image10_Charlie.png)

2. Select **Publish** to confirm publishing your agent. A message will appear indicating that the agent is being published. You do not need to keep that window open. You will receive a notification when the agent is published.

   ![imagen](img/image11_Charlie.png)

3. When the agent finishes publishing, you will see the notification at the top of the agent page.
4. Now, before testing the agent, let’s configure a channel. Select the **Channels** section as shown below.

   ![imagen](img/image12_Charlie.png)

5. In the **Channels** section, select **Teams and Microsoft 365 Copilot**.

   ![imagen](img/image13_Charlie.png)

6. Now, in the side panel, select **Turn on Microsoft 365** and then select **Add Channel**.

   ![imagen](img/image14_Charlie.png)

7. Adding the channel will take a moment. When it completes, a green notification will appear at the top of the side panel. If a pop-up appears asking you to publish again, select **Publish** and wait for it to complete.
8. Select **See agent in Microsoft 365** to open a new tab.
9. Now, in the Microsoft 365 app, you will see a pop-up window. Select **Add**.

   ![imagen](img/image15_Charlie.png)

10. Now our agent is ready to be tested!

---

## Test the agent

Let’s test Charlie from the Microsoft 365 app.

1. Use this prompt: "List the names of the available products in a bullet-point structure".
2. Choose the product you want to perform market research on.
3. Use this prompt: "Perform a light market research for the product \"Insert product\"; list competitive advantages and disadvantages and compare prices".

---

# **🎉 Mission completed**

✅ Great job! Our agent Charlie is ready.