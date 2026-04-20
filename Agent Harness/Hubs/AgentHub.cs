using Agent_Harness.Services;
using Microsoft.AspNetCore.SignalR;

namespace Agent_Harness.Hubs
{
    public class AgentHub : Hub
    {
        private readonly AdvancedAgentHarness _agentHarness;
        private readonly ILogger<AgentHub> _logger;

        public AgentHub(AdvancedAgentHarness agentHarness, ILogger<AgentHub> logger)
        {
            _agentHarness = agentHarness;
            _logger = logger;
        }

        public async Task SendMessage(string message)
        {
            var user = Context.User.Identity?.Name ?? "User";
            await Clients.Caller.SendAsync("ReceiveMessage", "User", message);

            try
            {
                // We'll update AdvancedAgentHarness to emit events, but for now, we trigger the agent
                var agentResponse = await _agentHarness.RunSimpleQueryAsync(message);
                await Clients.Caller.SendAsync("ReceiveMessage", "Agent", agentResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from user");
                await Clients.Caller.SendAsync("ReceiveMessage", "System", $"Error: {ex.Message}");
            }
        }

        public async Task ApproveAction(string requestId, bool approved)
        {
            // This will be called from the UI when a user clicks Approve/Deny
            // SignalRHumanApprover will be waiting for this
            SignalRHumanApprover.Respond(requestId, approved);
            await Clients.Caller.SendAsync("SystemStatus", $"Request {requestId} {(approved ? "approved" : "denied")}.");
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("SystemStatus", "Connected to Agent Hub.");
            await base.OnConnectedAsync();
        }
    }
}
