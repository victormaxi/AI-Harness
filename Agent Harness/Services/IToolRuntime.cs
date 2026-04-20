using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Identity.Web;

namespace Agent_Harness.Services
{
    public interface IToolRuntime
    {
        Task<string> GetDelegatedTokenAsync(string scope);
        Task<string> SendEmailAsync(string token, string to, string subject, string body);
        Task<string> SendTeamsMessageAsync(string token, string channelId, string message);
        Task<string> MarkEmailAsReadAsync(string token, string messageId);
        Task<string> ReplyToEmailAsync(string token, string messageId, string body);
    }

    public class EntraOboToolRuntime : IToolRuntime
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly GraphServiceClient _graphServiceClient;
        private readonly IConfiguration _configuration;

        public EntraOboToolRuntime(ITokenAcquisition tokenAcquisition, GraphServiceClient graphServiceClient, IConfiguration configuration)
        {
            _tokenAcquisition = tokenAcquisition;
            _graphServiceClient = graphServiceClient;
            _configuration = configuration;
        }

        public async Task<string> GetDelegatedTokenAsync(string scope)
        {
            return await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { scope });
        }

        public async Task<string> SendEmailAsync(string token, string to, string subject, string body)
        {
            try
            {
                var requestBody = new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
                {
                    Message = new Message
                    {
                        Subject = subject,
                        Body = new ItemBody { Content = body, ContentType = BodyType.Text },
                        ToRecipients = new List<Recipient> { new Recipient { EmailAddress = new EmailAddress { Address = to } } }
                    }
                };
                await _graphServiceClient.Me.SendMail.PostAsync(requestBody);
                return $"Email sent to {to} successfully.";
            }
            catch (Exception ex)
            {
                return $"Failed to send email: {ex.Message}";
            }
        }

        public async Task<string> MarkEmailAsReadAsync(string token, string messageId)
        {
            try
            {
                var requestBody = new Message { IsRead = true };
                await _graphServiceClient.Me.Messages[messageId].PatchAsync(requestBody);
                return $"Email {messageId} marked as read successfully.";
            }
            catch (Exception ex)
            {
                return $"Failed to mark email as read: {ex.Message}";
            }
        }

        public async Task<string> ReplyToEmailAsync(string token, string messageId, string body)
        {
            try
            {
                var requestBody = new Microsoft.Graph.Me.Messages.Item.Reply.ReplyPostRequestBody
                {
                    Comment = body
                };
                await _graphServiceClient.Me.Messages[messageId].Reply.PostAsync(requestBody);
                return $"Replied to email {messageId} successfully.";
            }
            catch (Exception ex)
            {
                return $"Failed to reply to email: {ex.Message}";
            }
        }

        public async Task<string> SendTeamsMessageAsync(string token, string targetId, string message)
        {
            try
            {
                var chatMessage = new ChatMessage
                {
                    Body = new ItemBody { Content = message, ContentType = BodyType.Html }
                };

                // Detect if it's a Channel (tacv2) or a Chat (v2 or private)
                if (targetId.Contains("tacv2"))
                {
                    var teamId = _configuration["Teams:TeamId"] ?? throw new InvalidOperationException("Teams:TeamId not configured.");
                    await _graphServiceClient.Teams[teamId].Channels[targetId].Messages.PostAsync(chatMessage);
                    return $"Message sent to Teams channel {targetId} successfully.";
                }
                else
                {
                    // Assume it's a Chat ID
                    await _graphServiceClient.Chats[targetId].Messages.PostAsync(chatMessage);
                    return $"Message sent to Teams chat {targetId} successfully.";
                }
            }
            catch (Exception ex)
            {
                return $"Failed to send Teams message: {ex.Message}";
            }
        }
    }
}
