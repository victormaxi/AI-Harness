using Azure;
using Azure.Identity;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Identity.Web;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Azure.AI.Projects;
using Agent_Harness.Services;
using Agent_Harness.Hubs;
using AgentSolution;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Entra ID authentication
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

// 2. Register Microsoft Graph (Fixed: Use .AddMicrosoftGraph() on IServiceCollection)
builder.Services.AddMicrosoftGraph(options =>
{
    options.Scopes = new[] { "Mail.Send", "Mail.ReadWrite", "ChannelMessage.Send", "Chat.ReadWrite", "User.Read" };
});

// 3. Register EF Core SQL Database Context
builder.Services.AddDbContext<AgentDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();

// Register the Agent Harness as a Singleton service

// IHumanApprover has no scoped dependencies → can be Singleton
builder.Services.AddSingleton<IHumanApprover, SignalRHumanApprover>();

// IToolRuntime depends on ITokenAcquisition (scoped) → must be Scoped
builder.Services.AddScoped<IToolRuntime, EntraOboToolRuntime>();

// AdvancedAgentHarness depends on IToolRuntime (scoped) → must be Scoped
builder.Services.AddScoped<AdvancedAgentHarness>();

// IAgentMemoryService uses DbContext (scoped) → Scoped
builder.Services.AddScoped<IAgentMemoryService, EFCoreAgentMemoryService>();

// [NEW] Workspace and Policy services
builder.Services.AddSingleton<IEventCache, MemoryEventCache>();
builder.Services.AddSingleton<IWorkspaceService, LocalWorkspaceService>();
builder.Services.AddSingleton<IPolicyEngine, BasicPolicyEngine>();

// [NEW] MCP Integration
builder.Services.AddSingleton<IMcpService, McpIntegrationService>();

// [NEW] Ambient Agent Background Service
builder.Services.AddHostedService<GraphEventListenerService>();

// Register AI client and agent
builder.Services.AddSingleton<AIAgent>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpointStr = config["AZURE_OPENAI_ENDPOINT"];
    var apiKey = config["AZURE_OPENAI_API_KEY"];
    
    if (string.IsNullOrEmpty(endpointStr)) throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
    if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not configured.");

    var endpoint = new Uri(endpointStr);
    var deploymentName = config["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4o-mini";

    // Use AzureOpenAIClient with the dedicated API Key
    var azureClient = new AzureOpenAIClient(endpoint, new AzureKeyCredential(apiKey));

    // Create a ChatClientAgent using the OpenAI client
    var workspace = sp.GetRequiredService<IWorkspaceService>();
    string defaultInstructions = """
            You are a helpful executive assistant with secure delegated access.
            Use available functions to send emails or Teams messages on behalf of the user.
            
            AMBIENT CONTEXT:
            If the user says 'yes', 'do it', or 'process it' in response to a background notification, 
            you MUST call 'GetRecentAmbientEventsTool' to understand the context of what was detected.
            Once you have processed an email (e.g. replied to it or summarized it), ALWAYS call 'MarkEmailAsReadTool' to mark it as read.
            
            IF AUTHORIZATION IS DENIED:
            If a tool call (like 'ReplyToEmailTool' or 'SendEmailTool') returns "User denied the request.", 
            you MUST still call 'MarkEmailAsReadTool' for that email so it is no longer marked as new. 
            Then, inform the user and ask if they would like to provide a custom draft or take other actions.

            FORMATTING:
            Always use professional Markdown for your responses. Use tables, bold text, and lists to make information readable.
            
            CONTEXT ENGINEERING:
            If a tool returns a large amount of data (e.g., long reports or JSON), use the 'SaveToWorkspace' 
            tool to store it locally and provide the user with a concise summary instead of dumping 
            all text into the chat. Reference files in the workspace when needed.
            """;

    string instructions = defaultInstructions;
    if (workspace.FileExists("agent_instructions.txt"))
    {
        instructions = workspace.ReadFileAsync("agent_instructions.txt").GetAwaiter().GetResult();
    }

    return new ChatClientAgent(
        azureClient.GetChatClient(deploymentName).AsIChatClient(),
        instructions: instructions,
        name: "DashboardAgent"
    );
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapHub<ChatHub>("/chatHub");
app.MapHub<AgentHub>("/agentHub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
