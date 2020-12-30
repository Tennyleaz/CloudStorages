using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CloudStorages.DropBox
{
    public class DropBoxOauthClient : IOauthClient
    {
        private const int PKCEVerifierLength = 64;
        private const string TokenRefreshEndpoint = "https://api.dropbox.com/oauth2/token";
        private const string TokenRevokeEndpoint = "https://api.dropboxapi.com/2/auth/token/revoke";
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

        public string GetTokenToUri()
        {
            StopListen();

            string state = Guid.NewGuid().ToString("N");

            // Creates the OAuth 2.0 authorization request.
            Uri authorizeUri = GetAuthorizeUri(apiKey, RedirectUri, state, oauthResponseType: OAuthResponseType.Token, tokenAccessType: TokenAccessType.Null);

            // Opens request in the browser.
            System.Diagnostics.Process.Start(authorizeUri.ToString());
            return state;
        }

        public bool ProcessUri(string state, string uri)
        {
            NameValueCollection collection = System.Web.HttpUtility.ParseQueryString(uri);
            // extracts the code
            string accessToken = collection.Get("access_token");
            string incoming_state = collection.Get("state");
            if (accessToken == null || incoming_state == null)
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
            AccessToken = accessToken;
            RefreshToken = null;
            ExpiresAt = null;
            
            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="PortUnavailableException">Throws when given loopback url port is occupied.</exception>
        /// <returns></returns>
        public async Task<bool> GetTokenAsync()
        {
            StopListen();

            // Generates state and PKCE values.
            string state = Guid.NewGuid().ToString("N");
            string codeVerifier = GeneratePKCECodeVerifier();
            string codeChallenge = GeneratePKCECodeChallenge(codeVerifier);

            // Test port is free, because dropbox oauth url need a fixed port assigned.
            int port = RedirectUri.Port;
            if (!IsPortAvailable(port))
                throw new PortUnavailableException { RequiredPort = port };

            try
            {
                // Creates a redirect URI using an available port on the loopback address.

                // Creates an HttpListener to listen for requests on that redirect URI.
                listener = new HttpListener();
                listener.Prefixes.Add(LoopbackHost);
                listener.Start();

                // Creates the OAuth 2.0 authorization request.
                Uri authorizeUri = GetAuthorizeUri(apiKey, RedirectUri, state, codeChallenge: codeChallenge);

                // Opens request in the browser.
                System.Diagnostics.Process.Start(authorizeUri.ToString());

                HttpListenerContext context = null;
                try
                {
                    // Waits for the OAuth authorization response.
                    context = await listener.GetContextAsync();
                    //WSSystem.GetSystem().WriteLog("GoogleLoginInfo", WSBUtility.LOG_LEVEL.LL_TRACE_LOG, "Got listner context.");

                    // Sends an HTTP response to the browser.
                    string responseString = "<html><head>" 
                                            //+ "<meta http-equiv='refresh' content='10;url=https://www.dropbox.com'>"
                                            + "</head><body>"
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
                var tokenResult = await ProcessCodeFlowAsync(code, apiKey, redirectUri: RedirectUri.ToString(), codeVerifier: codeVerifier);
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

        public async Task<bool> RevokeTokenAsync(string accessToken)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, TokenRevokeEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = await client.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.OK)
                    return true;
                return false;
            }
        }

        #region Get tokens from code

        /// <summary>
        /// Gets the URI used to start the OAuth2.0 authorization flow.
        /// </summary>
        /// <param name="clientId">The apps key, found in the
        /// <a href="https://www.dropbox.com/developers/apps">App Console</a>.</param>
        /// <param name="redirectUri">Where to redirect the user after authorization has completed. This must be the exact URI
        /// registered in the <a href="https://www.dropbox.com/developers/apps">App Console</a>; even <c>localhost</c>
        /// must be listed if it is used for testing. A redirect URI is required for a token flow, but optional for code. 
        /// If the redirect URI is omitted, the code will be presented directly to the user and they will be invited to enter
        /// the information in your app.</param>
        /// <param name="state">Up to 500 bytes of arbitrary data that will be passed back to <paramref name="redirectUri"/>.
        /// This parameter should be used to protect against cross-site request forgery (CSRF).</param>
        /// <param name="forceReapprove">Whether or not to force the user to approve the app again if they've already done so.
        /// If <c>false</c> (default), a user who has already approved the application may be automatically redirected to
        /// <paramref name="redirectUri"/>If <c>true</c>, the user will not be automatically redirected and will have to approve
        /// the app again.</param>
        /// <param name="disableSignup">When <c>true</c> (default is <c>false</c>) users will not be able to sign up for a
        /// Dropbox account via the authorization page. Instead, the authorization page will show a link to the Dropbox
        /// iOS app in the App Store. This is only intended for use when necessary for compliance with App Store policies.</param>
        /// <param name="requireRole">If this parameter is specified, the user will be asked to authorize with a particular
        /// type of Dropbox account, either work for a team account or personal for a personal account. Your app should still
        /// verify the type of Dropbox account after authorization since the user could modify or remove the require_role
        /// parameter.</param>
        /// <param name="forceReauthentication"> If <c>true</c>, users will be signed out if they are currently signed in.
        /// This will make sure the user is brought to a page where they can create a new account or sign in to another account.
        /// This should only be used when there is a definite reason to believe that the user needs to sign in to a new or
        /// different account.</param>
        /// <param name="scopeList">list of scopes to request in base oauth flow.  If left blank, will default to all scopes for app</param>
        /// <param name="codeChallenge">If using PKCE, please us the PKCEOAuthFlow object</param>
        /// <param name="oauthResponseType"> </param>
        /// <param name="tokenAccessType"> Could be null, offline (short-lived access_token and a long-lived refresh_token), online (short-lived access_token)</param>
        /// <returns>The uri of a web page which must be displayed to the user in order to authorize the app.</returns>
        private static Uri GetAuthorizeUri(string clientId, Uri redirectUri, string state = null, bool forceReapprove = false, bool disableSignup = false, 
            string requireRole = null, bool forceReauthentication = false, string[] scopeList = null, string codeChallenge = null, OAuthResponseType oauthResponseType = OAuthResponseType.Code, TokenAccessType tokenAccessType = TokenAccessType.Null)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new ArgumentNullException("clientId");
            }

            if (redirectUri == null /*&& oauthResponseType != OAuthResponseType.Code*/)
            {
                throw new ArgumentNullException("redirectUri");
            }

            var queryBuilder = new StringBuilder();

            queryBuilder.Append("response_type=");
            switch (oauthResponseType)
            {
                case OAuthResponseType.Token:
                    queryBuilder.Append("token");
                    break;
                default:
                case OAuthResponseType.Code:
                    queryBuilder.Append("code");
                    break;
            }

            queryBuilder.Append("&client_id=").Append(Uri.EscapeDataString(clientId));

            //if (redirectUri != null)
            //{
                queryBuilder.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri.ToString()));
            //}

            if (!string.IsNullOrWhiteSpace(state))
            {
                queryBuilder.Append("&state=").Append(Uri.EscapeDataString(state));
            }

            if (forceReapprove)
            {
                queryBuilder.Append("&force_reapprove=true");
            }

            if (disableSignup)
            {
                queryBuilder.Append("&disable_signup=true");
            }

            if (!string.IsNullOrWhiteSpace(requireRole))
            {
                queryBuilder.Append("&require_role=").Append(requireRole);
            }

            if (forceReauthentication)
            {
                queryBuilder.Append("&force_reauthentication=true");
            }

            if (tokenAccessType != TokenAccessType.Null)
                queryBuilder.Append("&token_access_type=").Append(tokenAccessType.ToString().ToLower());
            //queryBuilder.Append("&token_access_type=").Append("offline");

            if (scopeList != null)
            {
                queryBuilder.Append("&scope=").Append(String.Join(" ", scopeList));
            }

            //if (includeGrantedScopes != IncludeGrantedScopes.None)
            //{
            //    queryBuilder.Append("&include_granted_scopes=").Append(includeGrantedScopes.ToString().ToLower());
            //}

            if (codeChallenge != null)
            {
                queryBuilder.Append("&code_challenge_method=S256&code_challenge=").Append(codeChallenge);
            }

            var uriBuilder = new UriBuilder("https://www.dropbox.com/oauth2/authorize")
            {
                Query = queryBuilder.ToString()
            };

            return uriBuilder.Uri;
        }

        /// <summary>
        /// Processes the second half of the OAuth 2.0 code flow.
        /// </summary>
        /// <param name="code">The code acquired in the query parameters of the redirect from the initial authorize url.</param>
        /// <param name="appKey">The application key, found in the
        /// <a href="https://www.dropbox.com/developers/apps">App Console</a>.</param>
        /// <param name="appSecret">The application secret, found in the 
        /// <a href="https://www.dropbox.com/developers/apps">App Console</a> This is optional if using PKCE.</param>
        /// <param name="redirectUri">The redirect URI that was provided in the initial authorize URI,
        /// this is only used to validate that it matches the original request, it is not used to redirect
        /// again.</param>
        /// <param name="client">An optional http client instance used to make requests.</param>
        /// <param name="codeVerifier">The code verifier for PKCE flow.  If using PKCE, please us the PKCEOauthFlow object</param>
        /// <returns>The authorization response, containing the access token and uid of the authorized user</returns>
        private static async Task<OAuth2Response> ProcessCodeFlowAsync(string code, string appKey, string appSecret = null, string redirectUri = null, HttpClient client = null, string codeVerifier = null)
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentNullException("code");
            }
            else if (string.IsNullOrEmpty(appKey))
            {
                throw new ArgumentNullException("appKey");
            }
            else if (string.IsNullOrEmpty(appSecret) && string.IsNullOrEmpty(codeVerifier))
            {
                throw new ArgumentNullException("appSecret or codeVerifier");
            }

            var httpClient = client ?? new HttpClient();

            try
            {
                var parameters = new Dictionary<string, string>
                {
                    { "code", code },
                    { "grant_type", "authorization_code" },
                    { "client_id", appKey }
                };

                if (!string.IsNullOrEmpty(appSecret))
                {
                    parameters["client_secret"] = appSecret;
                }

                if (!string.IsNullOrEmpty(codeVerifier))
                {
                    parameters["code_verifier"] = codeVerifier;
                }

                if (!string.IsNullOrEmpty(redirectUri))
                {
                    parameters["redirect_uri"] = redirectUri;
                }
                var content = new FormUrlEncodedContent(parameters);
                var response = await httpClient.PostAsync("https://api.dropbox.com/oauth2/token", content).ConfigureAwait(false);

                var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JObject json = JObject.Parse(raw);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    //throw new OAuth2Exception(json["error"].ToString(), json.Value<string>("error_description"));
                    throw new Exception(json.Value<string>("error_description"));
                }

                string refreshToken = null;
                if (json.Value<string>("refresh_token") != null)
                {
                    refreshToken = json["refresh_token"].ToString();
                }

                int expiresIn = -1;
                if (json.Value<string>("expires_in") != null)
                {
                    expiresIn = json["expires_in"].ToObject<int>();
                }

                string[] scopeList = null;
                if (json.Value<string>("scope") != null)
                {
                    scopeList = json["scope"].ToString().Split(' ');
                }

                if (expiresIn == -1)
                {
                    return new OAuth2Response(
                        json["access_token"].ToString(),
                        json["uid"].ToString(),
                        null,
                        json["token_type"].ToString());
                }

                return new OAuth2Response(
                    json["access_token"].ToString(),
                    refreshToken,
                    json["uid"].ToString(),
                    null,
                    json["token_type"].ToString(),
                    expiresIn,
                    scopeList);
            }
            finally
            {
                if (client == null)
                {
                    httpClient.Dispose();
                }
            }
        }
        #endregion

        private static string GeneratePKCECodeVerifier()
        {
            var bytes = new byte[PKCEVerifierLength];
            RandomNumberGenerator.Create().GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_')
                .Substring(0, PKCEVerifierLength);
        }

        private static string GeneratePKCECodeChallenge(string codeVerifier)
        {
            using (var sha256 = SHA256.Create())
            {
                var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
                return Convert.ToBase64String(challengeBytes)
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
            }
        }

        // from: https://stackoverflow.com/a/51262610/3576052
        private bool IsPortAvailable(int myPort)
        {
            var properties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();

            // Active connections
            var connections = properties.GetActiveTcpConnections();
            foreach (var c in connections)
            {
                if (myPort == c.LocalEndPoint.Port)
                    return false;
            }

            // Active tcp listners
            var endPointsTcp = properties.GetActiveTcpListeners();
            foreach (var e in endPointsTcp)
            {
                if (myPort == e.Port)
                    return false;
            }

            // Active udp listeners
            var endPointsUdp = properties.GetActiveUdpListeners();
            foreach (var e in endPointsUdp)
            {
                if (myPort == e.Port)
                    return false;
            }
            return true;
        }

        private enum OAuthResponseType
        {
            Code,
            /// <summary>
            /// Legacy. We recommend the PKCE flow.
            /// </summary>
            Token
        }

        private enum TokenAccessType
        {
            /// <summary>
            /// If omitted, the response will default to returning a long-lived access_token if they are allowed in the app console.
            /// If long-lived access tokens are disabled in the app console, this parameter defaults to online.
            /// </summary>
            Null,
            /// <summary>
            /// Contain a short-lived access_token and a long-lived refresh_token that can be used to request a new short-lived access token
            /// as long as a user's approval remains valid.
            /// </summary>
            Offline,
            /// <summary>
            /// Only a short-lived access_token will be returned.
            /// </summary>
            Online
        }
    }
}
