using AgentSolution.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentSolution
{
    public class AgentDbContext : DbContext
    {
        public AgentDbContext(DbContextOptions<AgentDbContext> options) : base(options) { }

        public DbSet<ChatMessageEntity> ChatMessages { get; set; }
    }
}
