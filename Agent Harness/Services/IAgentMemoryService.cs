using AgentSolution;
using AgentSolution.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Agent_Harness.Services
{
    // Services/IAgentMemoryService.cs
    public interface IAgentMemoryService
    {
        Task<IEnumerable<ChatMessage>> GetChatHistoryAsync(string sessionId);
        Task SaveChatHistoryAsync(string sessionId, IEnumerable<ChatMessage> history);
    }

    // Services/EFCoreAgentMemoryService.cs
    public class EFCoreAgentMemoryService : IAgentMemoryService
    {
        private readonly AgentDbContext _dbContext;
        public EFCoreAgentMemoryService(AgentDbContext dbContext) => _dbContext = dbContext;

        public async Task<IEnumerable<ChatMessage>> GetChatHistoryAsync(string sessionId)
        {
            var entities = await _dbContext.ChatMessages
                .Where(m => m.SessionId == sessionId)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();
           
            return entities.Select(e => new ChatMessage(new ChatRole(e.Role), e.Content));
        }

        public async Task SaveChatHistoryAsync(string sessionId, IEnumerable<ChatMessage> history)
        {
            var entities = history.Select(m => new ChatMessageEntity
            {
                SessionId = sessionId,
                Role = m.Role.Value,
                Content = m.Text ?? string.Empty,
                Timestamp = DateTime.UtcNow
            });
            await _dbContext.ChatMessages.AddRangeAsync(entities);
            await _dbContext.SaveChangesAsync();
        }
    }
}
