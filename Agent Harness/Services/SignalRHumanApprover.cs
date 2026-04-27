using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Agent_Harness.Hubs;

namespace Agent_Harness.Services
{
    public class SignalRHumanApprover : IHumanApprover
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _requests = new();
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

        public SignalRHumanApprover(IHubContext<ChatHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task<bool> RequestApprovalAsync(string toolName, object toolArguments)
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests[requestId] = tcs;

            Console.WriteLine($"[APPROVAL] New request {requestId} for {toolName}");
            await _hubContext.Clients.All.SendAsync("ApprovalRequested", requestId, toolName, toolArguments);

            var delayTask = Task.Delay(DefaultTimeout);
            var winner = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);

            if (winner == tcs.Task)
            {
                bool result = await tcs.Task;
                Console.WriteLine($"[APPROVAL] Request {requestId} completed, result={result}");
                _requests.TryRemove(requestId, out _);
                return result;
            }

            Console.WriteLine($"[APPROVAL] Request {requestId} timed out");
            _requests.TryRemove(requestId, out _);
            return false;
        }
        //public async Task<bool> RequestApprovalAsync(string toolName, object toolArguments)
        //{
        //    var requestId = Guid.NewGuid().ToString();
        //    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        //    _requests[requestId] = tcs;

        //    // Notify client via SignalR
        //    await _hubContext.Clients.All.SendAsync("ApprovalRequested", requestId, toolName, toolArguments);

        //    var delay = Task.Delay(DefaultTimeout);
        //    var winner = await Task.WhenAny(tcs.Task, delay).ConfigureAwait(false);

        //    if (_requests.TryRemove(requestId, out _))
        //    {
        //         if (winner == tcs.Task)
        //        {
        //            return await tcs.Task;
        //        }
        //    }

        //    return false; // Timed out or already removed
        ////}

        public static void Respond(string requestId, bool approved)
        {
            Console.WriteLine($"[APPROVAL] Respond called: {requestId} = {approved}");
            if (_requests.TryRemove(requestId, out var tcs))
            {
                Console.WriteLine("[APPROVAL] Found TCS, setting result");
                tcs.TrySetResult(approved);
            }
            else
            {
                Console.WriteLine("[APPROVAL] Request ID not found in dictionary");
            }
        }
    }
}
