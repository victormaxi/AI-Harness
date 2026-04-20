using System;
using System.Collections.Generic;
using System.Text;

namespace AgentSolution.Models
{
    public class ChatMessageEntity
    {
        public int Id { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
