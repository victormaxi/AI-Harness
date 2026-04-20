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
                // Handle simple agent interactions
                var agentResponse = await _agentHarness.RunSimpleQueryAsync(message);
                await Clients.Caller.SendAsync("ReceiveMessage", "Agent", agentResponse);
            }
        }

        public async Task ApproveAction(string requestId, bool approved)
        {
            SignalRHumanApprover.Respond(requestId, approved);
            await Clients.Caller.SendAsync("ReceiveMessage", "System", $"Action {requestId} was {(approved ? "approved" : "denied")}.");
        }
    }
}
