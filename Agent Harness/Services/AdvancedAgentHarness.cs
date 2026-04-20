using Azure;
using Azure.AI.Projects;
using Azure.Identity;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Drawing;

namespace Agent_Harness.Services
{
    public class AdvancedAgentHarness
    {
        private readonly AIAgent _agent;
        private readonly IToolRuntime _toolRuntime;
        private readonly IHumanApprover _humanApprover;
        private readonly IConfiguration _configuration;
        private readonly IWorkspaceService _workspaceService;
        private readonly IPolicyEngine _policyEngine;
        private readonly IMcpService _mcpService;
        private readonly IEventCache _eventCache;

        public AdvancedAgentHarness(
            AIAgent agent, 
            IToolRuntime toolRuntime, 
            IHumanApprover humanApprover, 
            IConfiguration configuration,
            IWorkspaceService workspaceService,
            IPolicyEngine policyEngine,
            IMcpService mcpService,
            IEventCache eventCache)
        {
            _agent = agent;
            _toolRuntime = toolRuntime;
            _humanApprover = humanApprover;
            _configuration = configuration;
            _workspaceService = workspaceService;
            _policyEngine = policyEngine;
            _mcpService = mcpService;
            _eventCache = eventCache;
        }

        public async Task<string> RunSimpleQueryAsync(string userInput)
        {
            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(SendEmailTool),
                AIFunctionFactory.Create(MarkEmailAsReadTool),
                AIFunctionFactory.Create(ReplyToEmailTool),
                AIFunctionFactory.Create(SendTeamsMessageTool),
                AIFunctionFactory.Create(SaveToWorkspaceTool),
                AIFunctionFactory.Create(ReadFromWorkspaceTool),
                AIFunctionFactory.Create(ListWorkspaceFilesTool),
                AIFunctionFactory.Create(GetRecentAmbientEventsTool)
            };

            // Dynamically add MCP tools
            var mcpTools = await _mcpService.GetMcpToolsAsync();
            tools.AddRange(mcpTools);

            var options = new ChatClientAgentRunOptions
            {
                ChatOptions = new ChatOptions { Tools = tools }
            };

            // Pass userInput as a collection of ChatMessage
            AgentResponse response = await _agent.RunAsync(new[] { new ChatMessage(ChatRole.User, userInput) }, options: options);
            return response.ToString();
        }

        public async Task<string> RunConversationWithSessionAsync(string sessionId, string userInput)
        {
            AgentSession session = await GetOrCreateSessionAsync();

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(SendEmailTool),
                AIFunctionFactory.Create(MarkEmailAsReadTool),
                AIFunctionFactory.Create(ReplyToEmailTool),
                AIFunctionFactory.Create(SendTeamsMessageTool),
                AIFunctionFactory.Create(SaveToWorkspaceTool),
                AIFunctionFactory.Create(ReadFromWorkspaceTool),
                AIFunctionFactory.Create(ListWorkspaceFilesTool),
                AIFunctionFactory.Create(GetRecentAmbientEventsTool)
            };

            // Dynamically add MCP tools
            var mcpTools = await _mcpService.GetMcpToolsAsync();
            tools.AddRange(mcpTools);

            var options = new ChatClientAgentRunOptions
            {
                ChatOptions = new ChatOptions { Tools = tools }
            };

            // Pass userInput as a collection of ChatMessage
            AgentResponse response = await _agent.RunAsync(new[] { new ChatMessage(ChatRole.User, userInput) }, session, options);
            return response.ToString();
        }

        private async Task<AgentSession> GetOrCreateSessionAsync()
        {
            return await _agent.CreateSessionAsync();
        }

        public async Task<string> RunResearchAndReportingWorkflowAsync(string topic, string teamsChannelId)
        {
            // Use the same AI Foundry project client as the main agent
            var endpointStr = _configuration["AZURE_OPENAI_ENDPOINT"] ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT not set.");
            var apiKey = _configuration["AZURE_OPENAI_API_KEY"] ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY not set.");
            var deploymentName = _configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4o-mini";

            var azureClient = new AzureOpenAIClient(new Uri(endpointStr), new AzureKeyCredential(apiKey));
            var chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();

            var researcherAgent = new ChatClientAgent(chatClient,
                instructions: "You are a researcher. Find 3 key facts about a given topic. Output as a bulleted list.",
                name: "Researcher");

            var summarizerAgent = new ChatClientAgent(chatClient,
                instructions: "You are a summarizer. Create a one-paragraph executive summary from the provided research.",
                name: "Summarizer");

            var researchExecutor = new Func<string, Task<AgentResponse>>(async (input) =>
            {
                return await researcherAgent.RunAsync(new[] { new ChatMessage(ChatRole.User, input) });
            }).BindAsExecutor("ResearchExecutor");

            var reportExecutor = new Func<string, Task<AgentResponse>>(async (input) =>
            {
                return await summarizerAgent.RunAsync(new[] { new ChatMessage(ChatRole.User, $"Create an executive summary from this research: {input}") });
            }).BindAsExecutor("ReportExecutor");

            var builder = new WorkflowBuilder(researchExecutor);
            builder.AddEdge(researchExecutor, reportExecutor).WithOutputFrom(reportExecutor);
            var workflow = builder.Build();

            // RunAsync expects CancellationToken; use default
            await using var run = await InProcessExecution.RunAsync(workflow, topic);

            foreach (WorkflowEvent evt in run.NewEvents)
            {
                if (evt is ExecutorCompletedEvent complete && complete.ExecutorId == "ReportExecutor")
                {
                    var report = complete.Data?.ToString() ?? "Report generation failed.";
                    await SendTeamsMessageTool(teamsChannelId, $"**Agent Report on '{topic}':**\n{report}");
                    return report;
                }
            }
            return "Workflow completed.";
        }

        [Description("Sends an email to a specified recipient.")]
        private async Task<string> SendEmailTool(
            [Description("Recipient email address")] string to,
            [Description("Email subject")] string subject,
            [Description("Email body")] string body)
        {
            var policy = await _policyEngine.EvaluateActionAsync("send_email", new { to, subject });
            
            if (policy == PolicyResult.Deny) return "Action denied by security policy.";
            
            if (policy == PolicyResult.RequiresManualApproval)
            {
                if (!await _humanApprover.RequestApprovalAsync("send_email", new { to, subject }))
                    return "User denied the email request.";
            }

            var token = await _toolRuntime.GetDelegatedTokenAsync("Mail.Send");
            return await _toolRuntime.SendEmailAsync(token, to, subject, body);
        }

        [Description("Marks an email as read using its message ID. Call this after you process an email if the user requests it.")]
        private async Task<string> MarkEmailAsReadTool(
            [Description("The Microsoft Graph Message ID of the email")] string messageId)
        {
            var policy = await _policyEngine.EvaluateActionAsync("mark_email_read", new { messageId });
            if (policy == PolicyResult.Deny) return "Action denied by security policy.";
            
            // Mark as read is usually safe to auto-approve, but let's follow the policy pattern
            if (policy == PolicyResult.RequiresManualApproval)
            {
                if (!await _humanApprover.RequestApprovalAsync("mark_email_read", new { messageId }))
                    return "User denied the request.";
            }

            try 
            {
                var token = await _toolRuntime.GetDelegatedTokenAsync("Mail.ReadWrite");
                return await _toolRuntime.MarkEmailAsReadAsync(token, messageId);
            }
            catch (Exception ex)
            {
                return $"Permissions or Graph error: {ex.Message}. Make sure you are logged in as the mailbox owner and have consented to Mail.ReadWrite.";
            }
        }

        [Description("Replies to a specific email using its message ID.")]
        private async Task<string> ReplyToEmailTool(
            [Description("The Microsoft Graph Message ID of the email")] string messageId,
            [Description("The body of the reply")] string body)
        {
            var policy = await _policyEngine.EvaluateActionAsync("reply_email", new { messageId });
            if (policy == PolicyResult.Deny) return "Action denied by security policy.";
            
            if (policy == PolicyResult.RequiresManualApproval)
            {
                if (!await _humanApprover.RequestApprovalAsync("reply_email", new { messageId, body }))
                    return "User denied the request.";
            }

            try 
            {
                var token = await _toolRuntime.GetDelegatedTokenAsync("Mail.ReadWrite");
                return await _toolRuntime.ReplyToEmailAsync(token, messageId, body);
            }
            catch (Exception ex)
            {
                return $"Permissions or Graph error: {ex.Message}. Make sure you are logged in as the mailbox owner and have consented to Mail.ReadWrite.";
            }
        }

        [Description("Sends a message to a specified Microsoft Teams channel or chat.")]
        private async Task<string> SendTeamsMessageTool(
            [Description("The ID of the target Teams channel (tacv2) or chat (v2/private)")] string targetId,
            [Description("The message to send")] string message)
        {
            var policy = await _policyEngine.EvaluateActionAsync("send_teams_message", new { targetId, message });
            
            if (policy == PolicyResult.Deny) return "Action denied by security policy.";
            
            if (policy == PolicyResult.RequiresManualApproval)
            {
                if (!await _humanApprover.RequestApprovalAsync("send_teams_message", new { targetId, message }))
                    return "User denied the request to send a Teams message.";
            }

            // Dynamically select scope based on target type
            string scope = targetId.Contains("tacv2") ? "ChannelMessage.Send" : "Chat.ReadWrite";
            
            var token = await _toolRuntime.GetDelegatedTokenAsync(scope);
            return await _toolRuntime.SendTeamsMessageAsync(token, targetId, message);
        }

        [Description("Saves large content to the agent's local workspace to avoid bloating the conversation context.")]
        private async Task<string> SaveToWorkspaceTool(
            [Description("The name of the file to create")] string fileName,
            [Description("The content to save")] string content)
        {
            return await _workspaceService.SaveFileAsync(fileName, content);
        }

        [Description("Reads a file from the agent's local workspace.")]
        private async Task<string> ReadFromWorkspaceTool(
            [Description("The name of the file to read")] string fileName)
        {
            return await _workspaceService.ReadFileAsync(fileName);
        }

        [Description("Lists all files currently in the agent's workspace.")]
        private string ListWorkspaceFilesTool()
        {
            var files = _workspaceService.ListFiles();
            return files.Length == 0 ? "Workspace is empty." : string.Join(", ", files);
        }

        [Description("Retrieves recent background events (like emails) that the system notified the user about. Use this when the user says 'yes' or 'process it' to a background notification.")]
        private string GetRecentAmbientEventsTool()
        {
            var events = _eventCache.GetRecentEvents();
            if (!events.Any()) return "No recent background events found.";

            return string.Join("\n---\n", events.Select(e => $"Type: {e.Type}\nSummary: {e.Summary}\nDetails: {e.Detail}\nTime: {e.Timestamp}"));
        }
    }
}
