using Microsoft.AspNetCore.SignalR;
using Microsoft.Graph;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Agent_Harness.Hubs;
using Azure.Identity;
using System.Threading;
using System.Threading.Tasks;

namespace Agent_Harness.Services
{
    public class GraphEventListenerService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<GraphEventListenerService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IEventCache _eventCache;
        private GraphServiceClient? _graphClient;

        public GraphEventListenerService(
            IServiceProvider serviceProvider,
            IHubContext<ChatHub> hubContext,
            ILogger<GraphEventListenerService> logger,
            IConfiguration configuration,
            IEventCache eventCache)
        {
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _logger = logger;
            _configuration = configuration;
            _eventCache = eventCache;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Real Ambient Agent Event Listener started.");

            // Initialize the Graph Client for Background (Application) Auth
            var azureAd = _configuration.GetSection("AzureAd");
            var credential = new ClientSecretCredential(
                azureAd["TenantId"],
                azureAd["ClientId"],
                azureAd["ClientSecret"]);

            _graphClient = new GraphServiceClient(credential);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Check for new unread emails in the system mailbox
                    // (Requires Mail.Read Application Permission)
                    var unreadMessages = await _graphClient.Users["onu@yqvgs.onmicrosoft.com"].Messages
                        .GetAsync(config => 
                        {
                            config.QueryParameters.Filter = "isRead eq false";
                            config.QueryParameters.Top = 1;
                        }, stoppingToken);

                    if (unreadMessages?.Value?.Count > 0)
                    {
                        var latest = unreadMessages.Value[0];
                        _logger.LogInformation($"Real Event Detected: New Email from {latest.From?.EmailAddress?.Address}");

                        var evt = new DetectedEvent
                        {
                            SourceId = latest.Id, // The Microsoft Graph Message ID
                            Summary = $"Email from {latest.From?.EmailAddress?.Name}: {latest.Subject}",
                            Detail = $"From: {latest.From?.EmailAddress?.Address}\nSubject: {latest.Subject}\nBody Preview: {latest.BodyPreview}",
                            Type = "email"
                        };

                        // Store in cache for Agent access
                        _eventCache.AddEvent(evt);

                        // Process in background to avoid blocking the polling loop
                        _ = Task.Run(async () => 
                        {
                            try 
                            {
                                await _hubContext.Clients.All.SendAsync("ReceiveNotification", 
                                    $"⏳ **Ambient Agent**: Detected new email from {latest.From?.EmailAddress?.Name}. Processing in background...", 
                                    stoppingToken);

                                using var scope = _serviceProvider.CreateScope();
                                var agentHarness = scope.ServiceProvider.GetRequiredService<AdvancedAgentHarness>();
                                var response = await agentHarness.ProcessAmbientEventAsync(evt);

                                await _hubContext.Clients.All.SendAsync("ReceiveNotification", 
                                    $"✅ **Ambient Agent Task Completed**\n\n{response}", 
                                    stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error processing ambient event.");
                                await _hubContext.Clients.All.SendAsync("ReceiveNotification", 
                                    $"❌ **Ambient Agent Error**: Failed to process email. {ex.Message}", 
                                    stoppingToken);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    // If permissions are missing, we fall back to logging the requirement
                    _logger.LogWarning($"Background Event Check skipped or failed: {ex.Message}. (Make sure 'Mail.Read' Application permission is granted to the App Registration).");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
