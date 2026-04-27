using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Http;

namespace Agent_Harness.Services
{
    public interface IToolRuntime
    {
        Task<string> GetDelegatedTokenAsync(string scope);
        Task<string> SendEmailAsync(string token, string to, string subject, string body);
        Task<string> SendTeamsMessageAsync(string token, string targetId, string message);
        Task<string> MarkEmailAsReadAsync(string token, string messageId);
        Task<string> ReplyToEmailAsync(string token, string messageId, string body);
    }

    public class EntraOboToolRuntime : IToolRuntime
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly GraphServiceClient _delegatedGraphClient;
        private readonly GraphServiceClient _appGraphClient;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public EntraOboToolRuntime(
            ITokenAcquisition tokenAcquisition,
            GraphServiceClient graphServiceClient,
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor)
        {
            _tokenAcquisition = tokenAcquisition;
            _delegatedGraphClient = graphServiceClient;
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;

            // Build an application‑only client (same identity as the background service)
            var tenantId = configuration["AzureAd:TenantId"];
            var clientId = configuration["AzureAd:ClientId"];
            var clientSecret = configuration["AzureAd:ClientSecret"];
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            _appGraphClient = new GraphServiceClient(credential);
        }

        public async Task<string> GetDelegatedTokenAsync(string scope)
        {
            return await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { scope });
        }

        // Send email uses dual auth logic
        public async Task<string> SendEmailAsync(string token, string to, string subject, string body)
        {
            try
            {
                if (_httpContextAccessor.HttpContext != null)
                {
                    // UI Context - Delegated Auth
                    var requestBody = new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
                    {
                        Message = new Message
                        {
                            Subject = subject,
                            Body = new ItemBody { Content = body, ContentType = BodyType.Text },
                            ToRecipients = new List<Recipient> { new Recipient { EmailAddress = new EmailAddress { Address = to } } }
                        }
                    };
                    await _delegatedGraphClient.Me.SendMail.PostAsync(requestBody);
                    return $"Email sent to {to} successfully.";
                }
                else
                {
                    // Background - App Auth
                    var requestBody = new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                    {
                        Message = new Message
                        {
                            Subject = subject,
                            Body = new ItemBody { Content = body, ContentType = BodyType.Text },
                            ToRecipients = new List<Recipient> { new Recipient { EmailAddress = new EmailAddress { Address = to } } }
                        }
                    };
                    string mailboxUser = _configuration["BackgroundAuth:MailboxUser"] ?? "onu@yqvgs.onmicrosoft.com";
                    await _appGraphClient.Users[mailboxUser].SendMail.PostAsync(requestBody);
                    return $"Email sent to {to} successfully.";
                }
            }
            catch (Exception ex)
            {
                return $"Failed to send email: {ex.Message}";
            }
        }

        // Application‑only: mark an email in onu@...'s mailbox as read
        public async Task<string> MarkEmailAsReadAsync(string token, string messageId)
        {
            string mailboxUser = _configuration["BackgroundAuth:MailboxUser"] ?? "onu@yqvgs.onmicrosoft.com";
            try
            {
                var requestBody = new Message { IsRead = true };
                await _appGraphClient.Users[mailboxUser].Messages[messageId].PatchAsync(requestBody);
                return $"Email {messageId} marked as read.";
            }
            catch (Exception ex)
            {
                return $"Failed to mark email as read: {ex.Message}";
            }
        }

        // Application‑only: reply to an email in onu@...'s mailbox
        public async Task<string> ReplyToEmailAsync(string token, string messageId, string body)
        {
            string mailboxUser = _configuration["BackgroundAuth:MailboxUser"] ?? "onu@yqvgs.onmicrosoft.com";
            try
            {
                var requestBody = new Microsoft.Graph.Users.Item.Messages.Item.Reply.ReplyPostRequestBody
                {
                    // Convert plain text newlines to HTML line breaks so it doesn't mesh together in the recipient's client
                    Comment = body.Replace("\r\n", "<br>").Replace("\n", "<br>")
                };
                await _appGraphClient.Users[mailboxUser].Messages[messageId].Reply.PostAsync(requestBody);
                return $"Replied to email {messageId} successfully.";
            }
            catch (Exception ex)
            {
                return $"Failed to reply to email: {ex.Message}";
            }
        }

        // Teams – uses dual auth logic
        public async Task<string> SendTeamsMessageAsync(string token, string targetId, string message)
        {
            try
            {
                var chatMessage = new ChatMessage
                {
                    Body = new ItemBody { Content = message, ContentType = BodyType.Html }
                };

                if (_httpContextAccessor.HttpContext != null)
                {
                    // Delegated Auth
                    if (targetId.Contains("tacv2"))
                    {
                        var teamId = _configuration["Teams:TeamId"] ?? throw new InvalidOperationException("Teams:TeamId not configured.");
                        await _delegatedGraphClient.Teams[teamId].Channels[targetId].Messages.PostAsync(chatMessage);
                        return $"Message sent to Teams channel {targetId} successfully.";
                    }
                    else
                    {
                        await _delegatedGraphClient.Chats[targetId].Messages.PostAsync(chatMessage);
                        return $"Message sent to Teams chat {targetId} successfully.";
                    }
                }
                else
                {
                    // App Auth
                    if (targetId.Contains("tacv2"))
                    {
                        var teamId = _configuration["Teams:TeamId"] ?? throw new InvalidOperationException("Teams:TeamId not configured.");
                        await _appGraphClient.Teams[teamId].Channels[targetId].Messages.PostAsync(chatMessage);
                        return $"Message sent to Teams channel {targetId} successfully.";
                    }
                    else
                    {
                        await _appGraphClient.Chats[targetId].Messages.PostAsync(chatMessage);
                        return $"Message sent to Teams chat {targetId} successfully.";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Failed to send Teams message: {ex.Message}";
            }
        }
    }
}