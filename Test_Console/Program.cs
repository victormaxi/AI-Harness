using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;

// ----- CONFIGURATION (from your appsettings.json) -----
string tenantId = "0c6cf881-2ee3-4104-b0e2-6b3e310f616d";
string clientId = "0333727f-6281-4f88-bdb2-167b66bb73e4";
string cs = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
string fromEmail = "onu@yqvgs.onmicrosoft.com";

string toEmail = "maximillianonu@gmail.com";
string subject = "Hello from .NET 10 and Microsoft Graph!";
string bodyContent = "This email was sent using the Microsoft Graph API.";
// ----------------------------------------------------

// 1. Client credentials authentication
var credential = new ClientSecretCredential(tenantId, clientId,cs );

// 2. Graph client
var graphClient = new GraphServiceClient(credential);

// 3. Build the message
var message = new Message
{
    Subject = subject,
    Body = new ItemBody
    {
        ContentType = BodyType.Text,
        Content = bodyContent
    },
    ToRecipients = new List<Recipient>
    {
        new Recipient
        {
            EmailAddress = new EmailAddress { Address = toEmail }
        }
    }
};

// 4. Send mail
try
{
    await graphClient.Users[fromEmail].SendMail.PostAsync(
        new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
        {
            Message = message,
            SaveToSentItems = true
        });

    Console.WriteLine("Email sent successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error sending email: {ex.Message}");
}