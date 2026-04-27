using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Agent_Harness.Controllers
{
    [Authorize]
    [Route("[controller]")]
    public class AuthController : Controller
    {
        private readonly ITokenAcquisition _tokenAcquisition;

        public AuthController(ITokenAcquisition tokenAcquisition)
        {
            _tokenAcquisition = tokenAcquisition;
        }

        [HttpGet("Challenge")]
        public async Task<IActionResult> Challenge([FromQuery] string scope)
        {
            try
            {
                var scopes = scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                if (scopes.Any())
                {
                    // This will throw MicrosoftIdentityWebChallengeUserException if consent/login is required
                    await _tokenAcquisition.GetAccessTokenForUserAsync(scopes);
                }
                
                // If successful, redirect back to the home page
                return Redirect("/");
            }
            catch (MicrosoftIdentityWebChallengeUserException)
            {
                // The AuthorizeForScopes filter (if registered) would catch this. 
                // Alternatively, we can explicitly trigger a challenge response:
                var properties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = "/" };
                return Challenge(properties);
            }
            catch (Exception)
            {
                return Redirect("/");
            }
        }
    }
}
