using Microsoft.AspNetCore.SignalR;
using Microsoft.Graph;
using Microsoft.Extensions.Hosting;
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

                        // Store in cache for Agent access
                        _eventCache.AddEvent(new DetectedEvent
                        {
                            SourceId = latest.Id, // The Microsoft Graph Message ID
                            Summary = $"Email from {latest.From?.EmailAddress?.Name}: {latest.Subject}",
                            Detail = $"From: {latest.From?.EmailAddress?.Address}\nSubject: {latest.Subject}\nBody Preview: {latest.BodyPreview}",
                            Type = "email"
                        });

                        await _hubContext.Clients.All.SendAsync("ReceiveNotification", 
                            $"Ambient Agent: I've detected a new email from {latest.From?.EmailAddress?.Name}. Subject: {latest.Subject}. Should I process it?", 
                            stoppingToken);
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
