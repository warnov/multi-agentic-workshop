# Bill & Child Customer Notifications Agent

## 🎯 Mission summary

In this hands-on lab, you will create the initial definition of Bill and execute the main instructions to create Ric as a child agent.  
As a child agent, Ric will be responsible for sending an email to the user with the required information when requested.

## 🔎 Objectives

By completing this lab, you will obtain:

- Initial construction of the Bill agent based on the instructions described in this document.
- Creation of Ric as a child agent for Bill.
- Testing the workflow.

---

## Create your agent

### Configure Bill agent instructions

1. **Navigate** to Microsoft Copilot Studio. Make sure the **MultiAgentWrkshp** environment is selected in the upper-right corner, in the **environment selector**.
2. Select **Agents** and click **+ Create Blank Agent**.
3. In the **Details** card, click **Edit** to change the name and add a description:
   - **Name**: Bill
   - **Description**: Central orchestrator for all retail customer support activities
   - Select **Save** to save the agent (it may take a moment for the changes to become visible).

   ![imagen](img/image1_4f5190e7.png)  
   ![imagen](img/image2_4f5190e7.png)

4. Select **Edit** in the **Instructions** section of the agent **Overview** tab:

   ![imagen](img/image3_4f5190e7.png)

5. Copy and paste the following instructions into the instructions input field:

   ```text
   You are Bill, an orchestrator agent. You do not process data, do not execute
   queries, and do not generate reports. You only detect the user's intent and
   delegate the request to the correct agent with the minimum possible
   transformation.

   Email sending requests
   Phrases such as:
   "send by email"
   "send it by mail"
   "email this to me"
   → Delegate directly to Ric.
   ```

6. Select **Save**.

   ![imagen](img/image4_4f5190e7.png)

7. Select the **Settings** button in the upper-right corner of the screen.

   ![imagen](img/image5_4f5190e7.png)

   Review the page and make sure the following settings are applied:

   | Setting | Value |
   |---|---|
   | Use generative AI orchestration for agent responses | **Yes** |
   | Deep reasoning | **Disabled** |
   | Allow other agents to connect to and use this agent | **Enabled** |
   | Continue using retired models | **Disabled** |
   | Content moderation | **Moderate** |
   | Collect user reactions to agent messages | **Enabled** |
   | Use general knowledge | **Disabled** |
   | Use web information | **Disabled** |
   | File upload | **Enabled** |
   | Code interpreter | **Disabled** |

   ![imagen](img/image6_4f5190e7.png)  
   ![imagen](img/image7_4f5190e7.png)  
   ![imagen](img/image8_4f5190e7.png)

8. Click **Save**.
9. Click the **X** in the upper-right corner to close the settings menu.

   ![imagen](img/image9_4f5190e7.png)

---

## Add Ric as a Child Agent

1. **Navigate** to the **Agents** tab within the Bill agent (this is where you add specialist agents) and select **Add**.

   ![imagen](img/image10_4f5190e7.png)

2. Select **New child agent**.

   ![imagen](img/image11_4f5190e7.png)

3. **Name** your agent **Ric**.
4. Select **The agent chooses – Based on description** from the **When will this be used?** dropdown.
5. Set the **Description** to: "This agent is responsible for sending emails to the user with the information when required."

   ![imagen](img/image12_4f5190e7.png)

### Ric instructions

Add the following instructions to Ric:

```text
Role
You are Ric, an agent specialized in email notification.
Your only responsibility is to send an email that contains the most recent
information provided by the user in the chat, or the exact content explicitly
provided by the parent agent.

Hard boundaries (critical)
- You do not query business data.
- You do not use web search.
- You do not use knowledge sources.
- You do not request conversation history.
- You do not infer, enrich, or rewrite content.
- You only use the minimal parameters provided by the parent agent
  and the required system variables.

Supported intent
- "Email me what I just said"
- "Send the last update from this chat by email"
- "Send me an email with the latest information"
If the request is outside this scope, you must indicate that you can only
send an email notification.

Inputs (minimal)
You receive only:

- EmailTo (optional)
  If missing, default to the signed-in user's email (current user).
- EmailSubject (optional)
  If missing: "Latest chat update"
- EmailBodyContent (required)
  This is the exact content that must be sent by email (last user message
  or summary prepared by the parent agent).
  Format the content exactly as it was shown to the user in the chat.
- ConversationId (optional)

Critical passthrough rule
- Preserve EmailBodyContent as literally as possible.
- Do not paraphrase or summarize it.
- If length limits exist, truncate only at the end.

Execution (MCP tools only)
You must send the email using the tools from the Outlook Mail MCP server.

Preferred deterministic flow (2 steps):
1. Create a draft using:
   /mcp_MailTools_graph_mail_createMessage
2. Send the draft using:
   /mcp_MailTools_graph_mail_sendDraft

Draft creation requirements (for createMessage)
- subject: EmailSubject
- toRecipients: array with the destination email(s)
- body: with contentType and content (Text or HTML)

After creating the draft, capture the returned draft id and call:
mcp_MailTools_graph_mail_sendDraft with that id.

Body format rule
- Use Text by default.
- If the parent provides explicit HTML, set body contentType
  to HTML.

Guardrails
- Only one recipient is allowed.
- If EmailTo contains multiple addresses, reject the request and indicate
  that you can only send to a single recipient.
- Do not send to distribution lists or groups.
- Do not add CC/BCC unless the parent agent explicitly provides it.
- Do not attach files unless the parent agent explicitly indicates it
  and it is supported by the MCP tool set.

User-facing confirmation
After sending:
- Success:
  "Done — I sent an email to {EmailTo} with the latest information."
- Failure:
  "I couldn't send the email. Please try again or verify the recipient."
- Do not reveal technical errors.
```

---

## Add MCP Server

Now we will add the **Email Management MCP Server** as an agent tool.

1. In **Tools**, select **+ Add**.
2. Search for **Email Management MCP Server** and select the connector.

   ![imagen](img/image13_4f5190e7.png)

3. The pop-up window will ask you to create a new Office 365 connection. Select **Create**.

   ![imagen](img/image14_4f5190e7.png)

4. Select the user and click **Add and configure**.

   ![imagen](img/image15_4f5190e7.png)

Done! You can now test Ric.

---

## Test Ric

Run the following prompt in Bill’s test window:

```text
Send an email with the following information: The purchase orders from the customer CID-069 are up to date
```

---

## 🎉 Mission completed

Great job! Ric is complete and can now send emails.

This is what you completed in this lab:

- ✅ Create an orchestrator agent
- ✅ Create a child agent
- ✅ Add an MCP Server as a tool