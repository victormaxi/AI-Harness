using Agent_Harness.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace Agent_Harness.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly AdvancedAgentHarness _agentHarness;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(AdvancedAgentHarness agentHarness, ILogger<ChatHub> logger)
        {
            _agentHarness = agentHarness;
            _logger = logger;
        }

        public async Task SendMessage(string user, string message)
        {
            // Broadcast the user's message to the client
            await Clients.Caller.SendAsync("ReceiveMessage", user, message);

            try
            {
                // For demo, if the message contains "report on", trigger the workflow
                if (message.StartsWith("report on", StringComparison.OrdinalIgnoreCase))
                {
                    var topic = message.Substring(9).Trim();
                    var teamsChannelId = "YOUR_TEAMS_CHANNEL_ID"; // Replace with actual ID
                    var report = await _agentHarness.RunResearchAndReportingWorkflowAsync(topic, teamsChannelId);
                    await Clients.Caller.SendAsync("ReceiveMessage", "Agent", report);
                }
                else
                {
                    // Handle agent interactions with session context
                    var agentResponse = await _agentHarness.RunConversationWithSessionAsync(user, message);
                    await Clients.Caller.SendAsync("ReceiveMessage", "Agent", agentResponse);
                }
            }
            catch (Microsoft.Identity.Web.MicrosoftIdentityWebChallengeUserException ex)
            {
                // If the agent attempted an action that requires user consent or sign-in, redirect the user
                var requiredScopes = string.Join(" ", ex.Scopes);
                await Clients.Caller.SendAsync("RequireSignIn", requiredScopes);
            }
        }

        public async Task ApproveAction(string requestId, bool approved)
        {
            _logger.LogInformation($"[APPROVAL] Received request {requestId} with approved={approved} from connection {Context.ConnectionId}");
            try
            {
                SignalRHumanApprover.Respond(requestId, approved);
                _logger.LogInformation("[APPROVAL] Respond call completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[APPROVAL] Exception in Respond");
            }
            await Clients.Caller.SendAsync("ReceiveMessage", "System", $"Action {requestId} was {(approved ? "approved" : "denied")}.");
        }
    }
}
