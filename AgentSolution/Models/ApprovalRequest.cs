using System;
using System.Collections.Generic;
using System.Text;

namespace AgentSolution.Models
{
    public class ApprovalRequest
    {
        public int Id { get; set; }
        public string TaskId { get; set; }
        public string ProposedContent { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Approved
    }
}
