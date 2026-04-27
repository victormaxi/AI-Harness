using Azure;
using Azure.AI.Projects;
using Azure.Identity;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Identity.Web;

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
        private readonly IAgentMemoryService _memoryService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AdvancedAgentHarness> _logger;

        public AdvancedAgentHarness(
            AIAgent agent, 
            IToolRuntime toolRuntime, 
            IHumanApprover humanApprover, 
            IConfiguration configuration,
            IWorkspaceService workspaceService,
            IPolicyEngine policyEngine,
            IMcpService mcpService,
            IEventCache eventCache,
            IAgentMemoryService memoryService,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AdvancedAgentHarness> logger)
        {
            _agent = agent;
            _toolRuntime = toolRuntime;
            _humanApprover = humanApprover;
            _configuration = configuration;
            _workspaceService = workspaceService;
            _policyEngine = policyEngine;
            _mcpService = mcpService;
            _eventCache = eventCache;
            _memoryService = memoryService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
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
                AIFunctionFactory.Create(GetRecentAmbientEventsTool),
                AIFunctionFactory.Create(UpdateAgentInstructionsTool)
            };

            // Dynamically add MCP tools
            var mcpTools = await _mcpService.GetMcpToolsAsync();
            tools.AddRange(mcpTools);

            // [Context Engineering] Automatic Workspace Offloading
            tools = tools.Select(WrapToolWithOffloading).ToList();

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
            var history = await _memoryService.GetChatHistoryAsync(sessionId);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(SendEmailTool),
                AIFunctionFactory.Create(MarkEmailAsReadTool),
                AIFunctionFactory.Create(ReplyToEmailTool),
                AIFunctionFactory.Create(SendTeamsMessageTool),
                AIFunctionFactory.Create(SaveToWorkspaceTool),
                AIFunctionFactory.Create(ReadFromWorkspaceTool),
                AIFunctionFactory.Create(ListWorkspaceFilesTool),
                AIFunctionFactory.Create(GetRecentAmbientEventsTool),
                AIFunctionFactory.Create(UpdateAgentInstructionsTool)
            };

            // Dynamically add MCP tools
            var mcpTools = await _mcpService.GetMcpToolsAsync();
            tools.AddRange(mcpTools);

            // [Context Engineering] Automatic Workspace Offloading
            tools = tools.Select(WrapToolWithOffloading).ToList();

            var options = new ChatClientAgentRunOptions
            {
                ChatOptions = new ChatOptions { Tools = tools }
            };

            var userMessage = new ChatMessage(ChatRole.User, userInput);
            var allMessages = history.ToList();
            allMessages.Add(userMessage);

            // [Context Engineering] Automatic Conversational Compaction
            if (allMessages.Count > 20)
            {
                allMessages = await CompactHistoryAsync(sessionId, allMessages);
            }

            // Pass all messages including history + the new user message
            AgentResponse response = await _agent.RunAsync(allMessages, session, options);

            // Save the newly added user message and the assistant's response to history
            var newMessagesToSave = new List<ChatMessage> 
            { 
                userMessage, 
                new ChatMessage(ChatRole.Assistant, response.ToString()) 
            };
            await _memoryService.SaveChatHistoryAsync(sessionId, newMessagesToSave);

            return response.ToString();
        }

        public async Task<string> ProcessAmbientEventAsync(DetectedEvent evt)
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
                AIFunctionFactory.Create(GetRecentAmbientEventsTool),
                AIFunctionFactory.Create(UpdateAgentInstructionsTool)
            };

            // Dynamically add MCP tools
            var mcpTools = await _mcpService.GetMcpToolsAsync();
            tools.AddRange(mcpTools);

            // [Context Engineering] Automatic Workspace Offloading
            tools = tools.Select(WrapToolWithOffloading).ToList();

            var options = new ChatClientAgentRunOptions
            {
                ChatOptions = new ChatOptions { Tools = tools }
            };

            var prompt = $"A background event was detected.\nType: {evt.Type}\nSummary: {evt.Summary}\nDetails: {evt.Detail}\nSourceId: {evt.SourceId}\n" +
                         "Determine if a reply or action is needed. Use your tools to prepare the action. Do not ask the user for permission in chat, just execute the tool (which will trigger a UI approval dialog). If no action is needed, summarize the event. If the user denies an action, you should still mark the email as read using 'MarkEmailAsReadTool'.";

            AgentResponse response = await _agent.RunAsync(new[] { new ChatMessage(ChatRole.User, prompt) }, options: options);
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

            // Validate endpoint URI
            if (!Uri.TryCreate(endpointStr, UriKind.Absolute, out Uri endpointUri))
            {
                return $"Invalid AZURE_OPENAI_ENDPOINT value: '{endpointStr}'. Make sure it's a valid absolute URI (e.g. https://your-resource.openai.azure.com).";
            }

            // Pre-check DNS resolution to provide a clearer error message for common config/network issues
            try
            {
                await Dns.GetHostEntryAsync(endpointUri.Host);
            }
            catch (SocketException)
            {
                return $"DNS lookup failed for host '{endpointUri.Host}'. Check your AZURE_OPENAI_ENDPOINT configuration and network connectivity.";
            }

            AzureOpenAIClient azureClient;
            IChatClient chatClient;
            try
            {
                azureClient = new AzureOpenAIClient(endpointUri, new AzureKeyCredential(apiKey));
                chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();
            }
            catch (Exception ex)
            {
                return $"Failed to create Azure OpenAI client: {ex.Message}. Verify AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY and deployment name.";
            }

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
            
            try
            {
                string token = string.Empty;
                if (_httpContextAccessor.HttpContext != null)
                {
                    token = await _toolRuntime.GetDelegatedTokenAsync("Mail.Send");
                }
                return await _toolRuntime.SendEmailAsync(token, to, subject, body);
            }
            catch (MicrosoftIdentityWebChallengeUserException)
            {
                throw;
            }
            catch (Exception ex) when (ex.InnerException is Microsoft.Identity.Client.MsalUiRequiredException msalEx)
            {
                throw new MicrosoftIdentityWebChallengeUserException(msalEx, new[] { "Mail.Send" });
            }
        }

        [Description("Marks an email as read using its message ID. Call this after you process an email if the user requests it.")]
        private async Task<string> MarkEmailAsReadTool(
            [Description("The Microsoft Graph Message ID of the email")] string messageId,
            [Description("The subject of the email (for user context)")] string subject)
        {
            var policy = await _policyEngine.EvaluateActionAsync("mark_email_read", new { messageId, subject });
            if (policy == PolicyResult.Deny) return "Action denied by security policy.";
            
            // Mark as read is usually safe to auto-approve, but let's follow the policy pattern
            if (policy == PolicyResult.RequiresManualApproval)
            {
                if (!await _humanApprover.RequestApprovalAsync("mark_email_read", new { messageId, subject }))
                    return "User denied the request.";
            }

            try 
            {
                string token = string.Empty;
                if (_httpContextAccessor.HttpContext != null)
                {
                    token = await _toolRuntime.GetDelegatedTokenAsync("Mail.ReadWrite");
                }
                return await _toolRuntime.MarkEmailAsReadAsync(token, messageId);
            }
            catch (MicrosoftIdentityWebChallengeUserException)
            {
                throw;
            }
            catch (Exception ex) when (ex.InnerException is Microsoft.Identity.Client.MsalUiRequiredException msalEx)
            {
                throw new MicrosoftIdentityWebChallengeUserException(msalEx, new[] { "Mail.ReadWrite" });
            }
            catch (Exception ex)
            {
                return $"Permissions or Graph error: {ex.Message}. Make sure you are logged in as the mailbox owner and have consented to Mail.ReadWrite.";
            }
        }

        [Description("Replies to a specific email using its message ID.")]
        private async Task<string> ReplyToEmailTool(
            [Description("The Microsoft Graph Message ID of the email")] string messageId,
            [Description("The subject of the email being replied to")] string subject,
            [Description("The body of the reply")] string body)
        {
            var policy = await _policyEngine.EvaluateActionAsync("reply_email", new { messageId, subject });
            if (policy == PolicyResult.Deny) return "Action denied by security policy.";
            
            if (policy == PolicyResult.RequiresManualApproval)
            {
                if (!await _humanApprover.RequestApprovalAsync("reply_email", new { messageId, subject, body }))
                    return "User denied the request.";
            }

            try 
            {
                string token = string.Empty;
                if (_httpContextAccessor.HttpContext != null)
                {
                    token = await _toolRuntime.GetDelegatedTokenAsync("Mail.ReadWrite");
                }
                return await _toolRuntime.ReplyToEmailAsync(token, messageId, body);
            }
            catch (MicrosoftIdentityWebChallengeUserException)
            {
                throw;
            }
            catch (Exception ex) when (ex.InnerException is Microsoft.Identity.Client.MsalUiRequiredException msalEx)
            {
                throw new MicrosoftIdentityWebChallengeUserException(msalEx, new[] { "Mail.ReadWrite" });
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

            try
            {
                string token = string.Empty;
                if (_httpContextAccessor.HttpContext != null)
                {
                    string scope = targetId.Contains("tacv2") ? "ChannelMessage.Send" : "Chat.ReadWrite";
                    token = await _toolRuntime.GetDelegatedTokenAsync(scope);
                }
                return await _toolRuntime.SendTeamsMessageAsync(token, targetId, message);
            }
            catch (MicrosoftIdentityWebChallengeUserException)
            {
                throw;
            }
            catch (Exception ex) when (ex.InnerException is Microsoft.Identity.Client.MsalUiRequiredException msalEx)
            {
                string scope = targetId.Contains("tacv2") ? "ChannelMessage.Send" : "Chat.ReadWrite";
                throw new MicrosoftIdentityWebChallengeUserException(msalEx, new[] { scope });
            }
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

            return string.Join("\n---\n", events.Select(e => $"Type: {e.Type}\nSummary: {e.Summary}\nDetails: {e.Detail}\nSourceId: {e.SourceId}\nTime: {e.Timestamp}"));
        }

        [Description("Updates the agent's core system instructions. Use this to evolve your behavior, personality, or operational rules based on user feedback.")]
        private async Task<string> UpdateAgentInstructionsTool(
            [Description("The full new system instructions string")] string newInstructions)
        {
            await _workspaceService.SaveFileAsync("agent_instructions.txt", newInstructions);
            return "System instructions updated successfully. These will take effect for new sessions.";
        }

        private AITool WrapToolWithOffloading(AITool tool)
        {
            if (tool is not AIFunction function) return tool;

            return AIFunctionFactory.Create(async (Dictionary<string, object> parameters) =>
            {
                var result = await function.InvokeAsync(parameters);
                var resultStr = result?.ToString() ?? string.Empty;

                if (resultStr.Length > 10000)
                {
                    var fileName = $"offload_{Guid.NewGuid().ToString().Substring(0, 8)}.txt";
                    await _workspaceService.SaveFileAsync(fileName, resultStr);
                    return $"[OFFLOADED TO WORKSPACE: {fileName}. The result was too large for the context window. Use ReadFromWorkspaceTool to inspect specific parts if needed.]";
                }

                return resultStr;
            }, function.Name, function.Description);
        }

        private async Task<List<ChatMessage>> CompactHistoryAsync(string sessionId, List<ChatMessage> history)
        {
            _logger.LogInformation($"[COMPACTION] Session {sessionId} reached {history.Count} messages. Starting compaction...");

            // Take the oldest 50%
            int countToCompact = history.Count / 2;
            var toCompact = history.Take(countToCompact).ToList();
            var remaining = history.Skip(countToCompact).ToList();

            // Summarize the oldest messages
            var summarizerPrompt = "You are a context compaction engine. Summarize the following part of a conversation concisely, preserving all key decisions, entities, and pending tasks. This summary will be used as the new starting point for the conversation:\n\n" +
                                   string.Join("\n", toCompact.Select(m => $"{m.Role}: {m.Text}"));

            AgentResponse summaryResponse = await _agent.RunAsync(new[] { new ChatMessage(ChatRole.User, summarizerPrompt) });
            var summary = summaryResponse.ToString();

            var compactedMessage = new ChatMessage(ChatRole.System, $"[CONTEXT SUMMARY of previous {countToCompact} messages]: {summary}");

            var newHistory = new List<ChatMessage> { compactedMessage };
            newHistory.AddRange(remaining);

            // Update the persistent memory
            await _memoryService.ReplaceChatHistoryAsync(sessionId, newHistory);
            
            return newHistory;
        }
    }
}
