using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Dropbox.Api;
using Newtonsoft.Json.Linq;

namespace CloudStorages.DropBox
{
    public class DropBoxOauthClient : IOauthClient
    {
        private const string TokenRefreshEndpoint = "https://api.dropbox.com/oauth2/token";
        // This loopback host is for demo purpose. If this port is not
        // available on your machine you need to update this URL with an unused port.
        private readonly string LoopbackHost;
        // URL to receive OAuth 2 redirect from Dropbox server.
        // You also need to register this redirect URL on https://www.dropbox.com/developers/apps.
        private readonly Uri RedirectUri;
        private readonly string apiKey;
        private HttpListener listener;

        public string AccessToken { get; private set; }

        public string RefreshToken { get; private set; }

        public DateTime? ExpiresAt { get; private set; }


        public DropBoxOauthClient(string apiKey, string redirectUrl)
        {
            this.apiKey = apiKey;
            LoopbackHost = redirectUrl;
            RedirectUri = new Uri(LoopbackHost);
        }

        public async Task<bool> GetTokenAsync()
        {
            StopListen();

            // Generates state and PKCE values.
            string state = Guid.NewGuid().ToString("N");

            try
            {
                // Creates a redirect URI using an available port on the loopback address.

                // Creates an HttpListener to listen for requests on that redirect URI.
                listener = new HttpListener();
                listener.Prefixes.Add(LoopbackHost);
                listener.Start();

                // Creates the OAuth 2.0 authorization request.
                PKCEOAuthFlow OAuthFlow = new PKCEOAuthFlow();
                Uri authorizeUri = OAuthFlow.GetAuthorizeUri(OAuthResponseType.Code, apiKey, RedirectUri.ToString(), state, tokenAccessType: TokenAccessType.Offline, includeGrantedScopes: IncludeGrantedScopes.None);

                // Opens request in the browser.
                System.Diagnostics.Process.Start(authorizeUri.ToString());

                HttpListenerContext context = null;
                try
                {
                    // Waits for the OAuth authorization response.
                    context = await listener.GetContextAsync();
                    //WSSystem.GetSystem().WriteLog("GoogleLoginInfo", WSBUtility.LOG_LEVEL.LL_TRACE_LOG, "Got listner context.");

                    // Sends an HTTP response to the browser.
                    string responseString = "<html><head><meta http-equiv='refresh' content='10;url=https://www.dropbox.com'></head><body>"
                                            + "Please close this tab."
                                            + "</body></html>";
                    var buffer = Encoding.UTF8.GetBytes(responseString);
                    HttpListenerResponse response = context.Response;
                    response.ContentLength64 = buffer.Length;
                    response.ContentType = "text/html; charset=utf-8";
                    var responseOutput = response.OutputStream;
                    await responseOutput.WriteAsync(buffer, 0, buffer.Length);
                    await responseOutput.FlushAsync();
                    //WSSystem.GetSystem().WriteLog("GoogleLoginInfo", WSBUtility.LOG_LEVEL.LL_TRACE_LOG, "Wrote result html.");

                    await Task.Delay(800);
                    StopListen();
                }
                catch (ObjectDisposedException)
                {
                    //WSSystem.GetSystem().WriteLog("GoogleLoginInfo", WSBUtility.LOG_LEVEL.LL_NORMAL_LOG, "TCP listener is disposed.");
                    return false;
                }
                catch (HttpListenerException ex)
                {
                    //WSSystem.GetSystem().WriteLog("GoogleLoginInfo", WSBUtility.LOG_LEVEL.LL_SUB_FUNC, "Http listener error: " + ex);
                    return false;
                }

                // Checks for errors.
                if (context.Request.QueryString.Get("error") != null)
                {
                    string log = "Oauth reques error: " + context.Request.QueryString.Get("error");
                    //WSSystem.GetSystem().WriteLog("GoogleLoginInfo", WSBUtility.LOG_LEVEL.LL_SERIOUS_ERROR, log);
                    return false;
                }
                // extracts the code
                string code = context.Request.QueryString.Get("code");
                string incoming_state = context.Request.QueryString.Get("state");
                if (code == null || incoming_state == null)
                {
                    //WSSystem.GetSystem().WriteLog("GoogleLoginInfo", WSBUtility.LOG_LEVEL.LL_SUB_FUNC, "Oauth request does not give code!");
                    return false;
                }
                // check state
                if (!string.Equals(incoming_state, state))
                {
                    return false;
                }

                // Exchanging code for token
                var tokenResult = await OAuthFlow.ProcessCodeFlowAsync(code, apiKey, RedirectUri.AbsoluteUri);
                AccessToken = tokenResult.AccessToken;
                RefreshToken = tokenResult.RefreshToken;
                ExpiresAt = tokenResult.ExpiresAt;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return false;
            }
        }

        public async Task<bool> RefreshTokenAsync(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken))
                return false;
            
            var parameters = new Dictionary<string, string>
            {
                { "refresh_token", refreshToken} ,
                { "grant_type", "refresh_token" },
                { "client_id", apiKey }
            };
            var bodyContent = new FormUrlEncodedContent(parameters);
            using (HttpClient defaultHttpClient = new HttpClient())
            {
                try
                {
                    var response = await defaultHttpClient.PostAsync(TokenRefreshEndpoint, bodyContent).ConfigureAwait(false);
                    //if response is an invalid grant, we want to throw this exception rather than the one thrown in 
                    // response.EnsureSuccessStatusCode();
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        var reason = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (reason == "invalid_grant")
                        {
                            return false;
                        }
                    }
                    if (response.IsSuccessStatusCode)
                    {
                        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                        AccessToken = json["access_token"]?.ToString();
                        if (json["expires_in"] != null)
                            ExpiresAt = DateTime.Now.AddSeconds(json["expires_in"].ToObject<int>());
                        else
                            ExpiresAt = null;
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return false;
                }
            }
            return false;
        }

        public void StopListen()
        {
            if (listener != null)
            {
                try
                {
                    listener?.Stop();
                    listener = null;
                    Console.WriteLine("HTTP server stopped.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }
    }
}
