namespace Agent_Harness.Services
{
    // IHumanApprover.cs
    public interface IHumanApprover
    {
        Task<bool> RequestApprovalAsync(string toolName, object toolArguments);
    }

    // ConsoleHumanApprover.cs
    public class ConsoleHumanApprover : IHumanApprover
    {
        public async Task<bool> RequestApprovalAsync(string toolName, object toolArguments)
        {
            await Task.Yield(); // Keep it async
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n⚠️ AGENT WANTS TO USE TOOL: {toolName}");
            Console.WriteLine($"   Arguments: {System.Text.Json.JsonSerializer.Serialize(toolArguments)}");
            Console.Write("   Approve execution? (y/n): ");
            Console.ResetColor();

            var key = Console.ReadKey().KeyChar;
            Console.WriteLine();
            return key == 'y' || key == 'Y';
        }
    }
}
