using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Agent_Harness.Services
{
    public interface IMcpService
    {
        Task<IEnumerable<AITool>> GetMcpToolsAsync();
    }

    public class McpIntegrationService : IMcpService, IAsyncDisposable
    {
        private readonly ILogger<McpIntegrationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly List<AITool> _cachedTools = new();
        private readonly List<McpClient> _mcpClients = new();
        private bool _isInitialized = false;

        public McpIntegrationService(ILogger<McpIntegrationService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<IEnumerable<AITool>> GetMcpToolsAsync()
        {
            if (_isInitialized) return _cachedTools;

            var servers = _configuration.GetSection("McpServers").GetChildren();

            foreach (var serverConfig in servers)
            {
                try
                {
                    var command = serverConfig["Command"];
                    var args = serverConfig.GetSection("Arguments").Get<string[]>() ?? Array.Empty<string>();
                    var envVars = serverConfig.GetSection("EnvironmentVariables").Get<Dictionary<string, string?>>();

                    if (string.IsNullOrEmpty(command)) continue;

                    _logger.LogInformation($"Connecting to REAL MCP Server: {serverConfig.Key}...");

                    // Initialize the transport
                    var transport = new StdioClientTransport(new StdioClientTransportOptions 
                    { 
                        Command = command, 
                        Arguments = args,
                        EnvironmentVariables = envVars 
                    });

                    // Create the client
                    var client = await McpClient.CreateAsync(transport);
                    _mcpClients.Add(client);

                    // List tools from the server and map them to AITools
                    var tools = await client.ListToolsAsync();
                    foreach (var tool in tools)
                    {
                        _logger.LogInformation($"Discovered MCP Tool: {serverConfig.Key}.{tool.Name}");

                        // Create a tool that invokes the MCP tool
                        _cachedTools.Add(AIFunctionFactory.Create(
                            async (Dictionary<string, object> parameters) => 
                                await InvokeMcpToolInternalAsync(client, tool.Name, parameters),
                            $"{serverConfig.Key.ToLower()}_{tool.Name}",
                            tool.Description ?? $"MCP Tool: {tool.Name} from {serverConfig.Key}"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to connect to MCP server {serverConfig.Key}");
                }
            }

            _isInitialized = true;
            return _cachedTools;
        }

        private async Task<string> InvokeMcpToolInternalAsync(McpClient client, string toolName, Dictionary<string, object> parameters)
        {
            try
            {
                _logger.LogInformation($"Invoking MCP Tool '{toolName}' over protocol...");
                var result = await client.CallToolAsync(toolName, parameters);
                
                // MCP results are usually structured; we return the text content
                return result.ToString() ?? "Tool executed successfully with no output.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error calling MCP tool {toolName}");
                return $"Error executing tool: {ex.Message}";
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in _mcpClients)
            {
                try { await client.DisposeAsync(); } catch { /* ignore */ }
            }
        }
    }
}
