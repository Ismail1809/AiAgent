using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

class GetGoogleOAuthToken
{
    static async Task Main(string[] args)
    {
        string[] Scopes = { CalendarService.Scope.Calendar };
        string ApplicationName = "AiAgentBot";

        using (var stream = new FileStream("client_secret.json", FileMode.Open, FileAccess.Read))
        {
            string credPath = "token.json";
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(credPath, true));

            Console.WriteLine("Credential file saved to: " + credPath);
            Console.WriteLine("Access Token: " + credential.Token.AccessToken);
            Console.WriteLine("Refresh Token: " + credential.Token.RefreshToken);
            Console.WriteLine("\nCopy the refresh token above and use it in your bot configuration.");
        }
    }
}
