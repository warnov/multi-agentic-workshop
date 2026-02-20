# MCS – Bill: Publishing and Testing

## 🎯 Mission summary

In this lab, we will publish the Bill agent and perform tests through the Microsoft 365 application.

## 🔎 Objectives

By completing this lab, you will learn:

- How to publish the agent using the Microsoft 365 and Microsoft Teams channel.
- How to query the agent from Microsoft 365.

---

## Publishing process

1. Now, select the **Publish** button in the upper-right corner. A pop-up window will open to confirm that you really want to publish your agent.

   ![imagen](img/image1_Publish_Bill.png)  
   ![imagen](img/image2_Publish_Bill.png)

2. Select **Publish** to confirm publishing your agent. A message will appear indicating that your agent is being published. You do not need to keep that pop-up window open. You will receive a notification when the agent has been published.

   ![imagen](img/image3_Publish_Bill.png)

3. When the agent finishes publishing, you will see the notification at the top of the agent page.
4. Now, before testing the agent, let’s configure a channel. Select the **Channels** section as shown below.

   ![imagen](img/image4_Publish_Bill.png)

5. In the **Channels** section, select **Teams and Microsoft 365 Copilot**.

   ![imagen](img/image5_Publish_Bill.png)

6. Now, in the side panel, select the **Turn on Microsoft 365** option and then select **Add channel**.

   ![imagen](img/image6_Publish_Bill.png)

7. It will take time for the channel to be added. When finished, a green notification will appear at the top of the side panel. If you see a pop-up message asking you to publish again, select **Publish** and wait for it to complete.
8. Select **See agent in Microsoft 365** to open a new tab.

   ![imagen](img/image7_Publish_Bill.png)

9. Now, in the Microsoft 365 application, you will see a pop-up window. Select **Add**.

   ![imagen](img/image8_Publish_Bill.png)

10. Now our agent is ready to be tested!

---

## Test Bill

Let’s test Bill from the Microsoft 365 application.

1. Select Bill from your agents.

   ![imagen](img/image9_Publish_Bill.png)

2. **Test prompt — Scenario 1:**

   ```text
   Generate a report with the purchase orders for customer CID-069, include order start and end dates, product, brand, category, quantity, price, customer name, and order numbers.
   ```

3. **Test prompt — Scenario 2:**

   ```text
   Show the product details for Coffee maker
   ```

   When the agent has responded, enter the following prompt:

   ```text
   Send me an email with that information
   ```

4. Now, in another tab, open <https://outlook.office.com> and in your inbox you will find the email with the information.

---

## 🎉 Mission completed

Great job! Our Bill agent is now complete.

✅ Congratulations! You have successfully published your agent, deployed it to a Microsoft 365 experience, and tested it before rolling it out to end users on the retail company site.
``