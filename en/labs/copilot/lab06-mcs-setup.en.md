# **Microsoft Copilot Studio – Configuration**

## 🎯 Mission summary

In this guided, hands-on lab, you will learn how to establish your base environment for Copilot Studio by creating and configuring a workspace called “MultiAgentWrkshp”. You will also create a solution that will serve as the central hub for all your Copilot Studio agents and components, ensuring that your development process is organized and efficient. By following the step-by-step instructions, you will gain practical experience configuring, managing, and customizing your environment to streamline future Copilot Studio projects.

## 🔎 Objectives

By completing this lab, you will produce the following:

- Create your **MultiAgentWrkshp** workspace for Copilot Studio
- Create a solution that contains all your Copilot Studio agents and components
- Set your solution as the default solution so that new components are stored in it by default

**Now, let’s move on to the lab steps:**

- Create your environment
- In this lab you will create a Power Platform environment where all your Microsoft Copilot Studio agents will live.

1- Go to the Power Platform admin center at http://aka.ms/ppac and select the “Manage” option in the left-hand navigation pane

![imagen](img/image1_MCS_Setup.png)

2- Select the **Environments** option:

![imagen](img/image2_MCS_Setup.png)

3- Select **New** to create a new environment:

![imagen](img/image3_MCS_Setup.png)

4- The **New Environment** details pane appears on the right side of the screen.

![imagen](img/image4_MCS_Setup.png)

In the **New Environment** details pane, enter the following:

- Name: **MultiAgentWrkshp**
- Region: **make sure “United States – Default” is selected**
- Type: **Sandbox**
- Purpose: **this is the environment used to run the Multi-Agent workshop labs**
- Add a Dataverse data store?: **turn the toggle ON**
- Your new environment details pane should look like this:

![imagen](img/image5_MCS_Setup.png)

- Select **Next** to enter additional configuration settings for your Microsoft Copilot Studio environment.

![imagen](img/image6_MCS_Setup.png)

In the **Next** pane, follow the steps in the table below:

| Select “+ Select” under “Security Group *” | Select the “All Company” option in the “Restricted Access” section | Your additional environment details should look like this |
| --- | --- | --- |
| ![imagen](img/image7_MCS_Setup.png) | ![imagen](img/image8_MCS_Setup.png) | ![imagen](img/image9_MCS_Setup.png) |

Select **Save** to create your Microsoft Copilot Studio environment.

![imagen](img/image10_MCS_Setup.png)

You should see a screen similar to the following, indicating that your environment is being prepared:

![imagen](img/image11_MCS_Setup.png)

Once provisioning is complete and the environment is ready, you will receive a confirmation similar to the image below. Use the **Refresh** button to update the environment creation status.

![imagen](img/image12_MCS_Setup.png)

Verify that the properties of your newly created environment are correct. In particular: Name, Type, State = Ready, and Dataverse = YES.

# Create a solution to store all your working components

In this lab, you will learn how to build a solution (Solution), the official deployment vehicle for your Microsoft Copilot Studio agents.  
Think of this as creating a digital briefcase that contains your agent and its artifacts/components.  
Every agent needs a well-structured home. That is what a Power Platform solution provides: order, portability, and production readiness.

Let’s get started.

1. Go to Copilot Studio. Make sure you are in the correct environment (Environment) = **MultiAgentWrkshp**

![imagen](img/image13_MCS_Setup.png)

2. Select **…** in the left navigation menu.

![imagen](img/image14_MCS_Setup.png)

3. Select **Solutions**

![imagen](img/image15_MCS_Setup.png)

4. This will open a new tab in your browser.
5. Now we will create a <u>Solution</u>. The **Solution Explorer** will load in Copilot Studio. Select **+ New solution**

![imagen](img/image16_MCS_Setup.png)

6. The **New solution** pane will appear, where we can define the details of our solution.

![imagen](img/image17_MCS_Setup.png)

7. First, we need to create a new publisher. Select **+ New publisher**. The **Properties** tab of the **New publisher** pane will appear, with required and optional fields to complete. Here we can define the publisher information, which will be used as the label or brand that identifies who created or owns the solution.

![imagen](img/image18_MCS_Setup.png)

| Property | Description | Required |
| --- | --- | --- |
| Display name | Display name of the publisher | Yes |
| Name | The unique name and schema name for the publisher | Yes |
| Description | Describes the purpose of the solution | No |
| Prefix | Publisher prefix applied to newly created components | Yes |
| Choice value prefix | Generates a number based on the publisher prefix. This number is used when adding options to choices and indicates which solution was used to add the option. | Yes |

Copy and paste the following:

- **Display name**: **My Multi Agent Publisher**
- **Name**: **MyMultiAgentPublr**
- **Description**: **This is the publisher for my Multi Agent Workshop Solution**
- **Prefix**: **mmap**
- By default, the **Choice value** prefix will display an integer value. Update this integer to the nearest thousand. For example, in the screenshot below it was initially 77074. Update it from 77074 to 77000.

![imagen](img/image19_MCS_Setup.png)

8. If you want to provide contact details for the solution, select the **Contact** tab and complete the fields shown.

![imagen](img/image20_MCS_Setup.png)

9. Select the **Properties** tab and select **Save** to create the publisher.

![imagen](img/image21_MCS_Setup.png)

9. The **New publisher** pane will close and you will return to the **New solution** pane with the newly created publisher selected.

Well done, you have created a Solution Publisher! 🙌🏼

# Next, you will learn how to create a new custom solution.

Now that we have the new Solution Publisher, we can complete the rest of the form in the **New solution** pane.

1. Copy and paste the following:
- **Display name**: **My Multi Agent Solution**
- **Name**: **MyMultiAgentSln**
- Since we are creating a new solution, the **Version** number will default to 1.0.0.0
- Check the **Set as your preferred solution** box
- Expand **More options** to see additional details that can be provided for a solution

You will see the following:
- **Installed on** – the date the solution was installed
- **Configuration page** – developers configure an HTML web resource to help users interact with their app, agent, or tool; it appears as a web page in the Information section with instructions or buttons. This is mostly used in enterprises or by developers who create and share solutions with others.
- **Description** – describes the solution or provides a high-level description of the configuration page

2. Leave these fields blank for this lab.

![imagen](img/image22_MCS_Setup.png)

3. Select **Create**.

![imagen](img/image23_MCS_Setup.png)

4. The **My Multi Agent Solution** solution has now been created. There will be no components until you create an agent in Copilot Studio.

![imagen](img/image24_MCS_Setup.png)

5. Select the back arrow icon to return to the Solution Explorer.

![imagen](img/image25_MCS_Setup.png)

6. Make your solution the default solution / confirm it
7. Verify that your solution has the “Preferred Solution” label next to it.

![imagen](img/image26_MCS_Setup.png)

8. If it does not, select the ellipsis **…** next to your solution and then select **Set preferred solution** from the drop-down menu, as shown below:

![imagen](img/image27_MCS_Setup.png)

9. In the pop-up window, select your solution **MyMultiAgentSln** from the drop-down list.

![imagen](img/image28_MCS_Setup.png)

10. Select **Apply** to confirm that you want to set your solution **MyMultiAgentSln** as the preferred solution.

![imagen](img/image29_MCS_Setup.png)

11. Your solution **MyMultiAgentSln** should now have the “Preferred solution” label next to it.

![imagen](img/image26_MCS_Setup.png)

# **🎉 Mission completed**

✅ **You have now finished setting up your lab environment for Microsoft Copilot Studio.** Congratulations!