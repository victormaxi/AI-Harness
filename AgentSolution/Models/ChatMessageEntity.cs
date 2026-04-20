using System;
using System.Collections.Generic;
using System.Text;

namespace AgentSolution.Models
{
    public class ChatMessageEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string SessionId { get; set; }
        public string Role { get; set; } = "user"; // user, agent, system
        public string Content { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
