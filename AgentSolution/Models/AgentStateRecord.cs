using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace AgentSolution.Models
{
    public class AgentStateRecord
    {
        [Key] public string TaskId { get; set; }
        public string SerializedHistory { get; set; }
    }
}
