using AgentSolution.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AgentSolution.Tools
{
    public class SecureAgentTools(AgentDbContext db)
    {
        [Description("Submits content for human review. Required for all production updates.")]
        public async Task<string> RequestApproval(string taskId, string content)
        {
            var request = new ApprovalRequest { TaskId = taskId, ProposedContent = content };
            db.ApprovalRequests.Add(request);
            await db.SaveChangesAsync();
            return $"PAUSED: Request {request.Id} is awaiting human approval.";
        }
    }
}
