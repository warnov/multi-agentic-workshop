using System.ComponentModel;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace MsFoundryAgent;

public static class Program
{
    private const string DefaultAgentName = "Andres-Agent";
    private const string DefaultAgentInstructions =
        "You are an analytical AI agent specialized in reading, understanding, and extracting insights from provided information.";

    // -----------------------------------------------------------------------
    // Tool: a simple weather stub to demonstrate function calling
    // -----------------------------------------------------------------------
    [Description("Get the current weather for a given location.")]
    public static string GetWeather(
        [Description("The city or location name, e.g. 'Seattle'")] string location)
    {
        var rand = new Random();
        string[] conditions = ["sunny", "cloudy", "rainy", "stormy"];
        return $"The weather in {location} is {conditions[rand.Next(conditions.Length)]} " +
               $"with a high of {rand.Next(10, 35)}°C.";
    }

    public static async Task Main(string[] args)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        string projectEndpoint = config["Foundry:ProjectEndpoint"]
            ?? throw new InvalidOperationException("Foundry:ProjectEndpoint is not configured.");

        string modelDeployment = config["Foundry:ModelDeployment"]
            ?? throw new InvalidOperationException("Foundry:ModelDeployment is not configured.");

        string agentName = config["Foundry:AgentName"] ?? DefaultAgentName;
        string agentInstructions = config["Foundry:AgentInstructions"] ?? DefaultAgentInstructions;

        var aiProjectClient = new AIProjectClient(
            new Uri(projectEndpoint),
            new DefaultAzureCredential());

        Console.WriteLine($"Creating agent '{agentName}' on Azure AI Foundry...");

        ChatClientAgent agent = await aiProjectClient.CreateAIAgentAsync(
            name: agentName,
            model: modelDeployment,
            instructions: agentInstructions,
            description: null,
            tools: [AIFunctionFactory.Create(GetWeather)]);

        if (args.Length > 0 && args[0].Equals("deploy", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Agent '{agent.Name}' deployed successfully.");
            Console.WriteLine("The agent was left in Azure AI Foundry and was not deleted.");
            return;
        }

        if (args.Length > 0 && args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
        {
            ChatClientAgent found = await aiProjectClient.GetAIAgentAsync(agentName, tools: [AIFunctionFactory.Create(GetWeather)]);
            Console.WriteLine("Agent verification succeeded.");
            Console.WriteLine($"Name: {found.Name}");
            Console.WriteLine($"Model: {modelDeployment}");
            Console.WriteLine($"Endpoint: {projectEndpoint}");
            Console.WriteLine("If the portal does not show it, confirm you are in the same Foundry project and tenant.");
            return;
        }

        Console.WriteLine("Agent created. Starting multi-turn conversation (type 'quit' to exit).\n");

        var history = new List<ChatMessage>();

        while (true)
        {
            Console.Write("You: ");
            string? userInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(userInput) ||
                userInput.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            history.Add(new ChatMessage(ChatRole.User, userInput));

            Console.Write("Agent: ");
            var assistantText = new System.Text.StringBuilder();
            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(history))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    Console.Write(update.Text);
                    assistantText.Append(update.Text);
                }
            }
            Console.WriteLine("\n");

            // Append the assistant reply so future turns have full context
            if (assistantText.Length > 0)
                history.Add(new ChatMessage(ChatRole.Assistant, assistantText.ToString()));
        }

        Console.WriteLine("Cleaning up agent...");
        await aiProjectClient.Agents.DeleteAgentAsync(agent.Name);
        Console.WriteLine("Done.");
    }
}
