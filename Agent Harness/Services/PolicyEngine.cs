using System;
using System.Threading.Tasks;

namespace Agent_Harness.Services
{
    public enum PolicyResult
    {
        Allow,
        Deny,
        RequiresManualApproval
    }

    public interface IPolicyEngine
    {
        Task<PolicyResult> EvaluateActionAsync(string actionType, object parameters);
    }

    public class BasicPolicyEngine : IPolicyEngine
    {
        private readonly IConfiguration _configuration;

        public BasicPolicyEngine(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<PolicyResult> EvaluateActionAsync(string actionType, object parameters)
        {
            // Implementation of simple guardrails
            
            if (actionType == "send_teams_message")
            {
                var targetId = parameters?.GetType().GetProperty("targetId")?.GetValue(parameters, null)?.ToString();
                var safeChannelId = _configuration["Teams:ChannelId"];

                // Rule: Always allow messages to the designated safe channel
                if (!string.IsNullOrEmpty(targetId) && targetId == safeChannelId)
                {
                    return Task.FromResult(PolicyResult.Allow);
                }
            }

            if (actionType == "send_email")
            {
                // Rule: Emails always require manual approval for security
                return Task.FromResult(PolicyResult.RequiresManualApproval);
            }

            // Default to manual approval for everything else
            return Task.FromResult(PolicyResult.RequiresManualApproval);
        }
    }
}
