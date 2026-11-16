using Jarvis.Infrastructure.Google;

var options = new GoogleOAuthOptions();

var credentialStore = new GoogleCredentialStore(options);
var networkPolicy = GoogleEndpointPolicy.FromSettingsFile(
    Path.GetFullPath(Path.Combine("config", "settings.json")));
var httpClientFactory = new GoogleApiClientFactory(networkPolicy);

var googleOAuth = new GoogleOAuthService(
    options,
    credentialStore,
    httpClientFactory);

try
{
    Console.WriteLine("Starting Google authentication...");

    var account = await googleOAuth.ConnectAsync();

    Console.WriteLine("\nGoogle authentication successful!");
    Console.WriteLine($"Email: {account.Email}");
    Console.WriteLine($"Account ID: {account.AccountId}");

    Console.WriteLine("\nChecking saved account...");

    var savedAccount =
        await googleOAuth.GetConnectedAccountAsync();

    Console.WriteLine($"Saved account: {savedAccount?.Email}");

    Console.WriteLine("\nChecking credentials...");

    var credential =
        await googleOAuth.GetCredentialAsync();

    Console.WriteLine(
        $"Credential loaded: {credential.Token is not null}");
}
catch (Exception ex)
{
    Console.WriteLine("\nAuthentication failed!");
    Console.WriteLine(ex);
}