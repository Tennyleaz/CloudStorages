using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CloudStorages.OneDrive
{
    public class OneDriveOauthClient : IOauthClient
    {
        private const string AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={0}&scope={1}&response_type=code&redirect_uri={2}&state={3}";
        private const string TokenRequestEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
        //private const string TokenRevokeEndpoint = "https://oauth2.googleapis.com/revoke?token=";
        private const string SCOPE = "files.readwrite offline_access User.Read";
        private const string REDIRECT_URI = "http://localhost";
        private HttpListener listener;
        private readonly string CLIENT_ID, CLIENT_SECRET, LOCAL_REDIRECT_URI;

        public string LastError { get; private set; }

        public string AccessToken { get; private set; }

        public string RefreshToken { get; private set; }

        public DateTime? ExpiresAt { get; private set; }

        /// <summary>
        /// </summary>
        /// <param name="apiKey">Required client ID.</param>
        /// <param name="apiSecret">Public clients can't send a client secret.</param>
        /// <param name="redirectUri">Custom redirect uri to open.</param>
        public OneDriveOauthClient(string apiKey, string apiSecret = null, string redirectUri = null)
        {
            CLIENT_ID = apiKey;
            CLIENT_SECRET = apiSecret;
            LOCAL_REDIRECT_URI = redirectUri;
        }

        /// <summary>
        /// Open default browser to perform oauth login, will redirect to custom local uri.
        /// </summary>
        public string GetTokenToUri()
        {
            StopListen();

            string state = Guid.NewGuid().ToString("N");

            // Creates the OAuth 2.0 authorization request.
            string authorizationRequest = string.Format(AuthorizationEndpoint,
                CLIENT_ID,
                SCOPE,
                LOCAL_REDIRECT_URI, 
                state);

            // Opens request in the browser.
            System.Diagnostics.Process.Start(authorizationRequest);
            return state;
        }

        /// <summary>
        /// Parse local redirect uri and extract tokens.
        /// </summary>
        public async Task<bool> ProcessUriAsync(string state, string uri)
        {
            uri = uri.Replace(LOCAL_REDIRECT_URI, string.Empty);
            System.Collections.Specialized.NameValueCollection collection = System.Web.HttpUtility.ParseQueryString(uri);
            // extracts the code
            string code = collection.Get("code");
            string incoming_state = collection.Get("state");
            if (code == null || incoming_state == null)
            {
                return false;
            }
            // check state
            if (!string.Equals(incoming_state, state))
            {
                return false;
            }

            // Exchanging code for token
            OAuth2Response oAuth2Response = await PerformCodeExchangeAsync(code, LOCAL_REDIRECT_URI);
            AccessToken = oAuth2Response.AccessToken;
            RefreshToken = oAuth2Response.RefreshToken;
            ExpiresAt = oAuth2Response.ExpiresAt;
            if (!string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken))
                return true;

            return true;
        }

        public async Task<bool> GetTokenAsync()
        {
            StopListen();

            // Generates state and PKCE values.
            string state = Guid.NewGuid().ToString("N");
            //string codeVerifier = "";//GeneratePKCECodeVerifier();

            try
            {
                // Creates a redirect URI using an available port on the loopback address.
                string redirectURI = string.Format(REDIRECT_URI + ":{0}/", GetRandomUnusedPort());
                // Creates an HttpListener to listen for requests on that redirect URI.
                listener = new HttpListener();
                listener.Prefixes.Add(redirectURI);
                listener.Start();

                // Creates the OAuth 2.0 authorization request.
                string authorizationRequest = string.Format(AuthorizationEndpoint,
                    CLIENT_ID,
                    SCOPE,
                    redirectURI,
                    state);

                // Opens request in the browser.
                System.Diagnostics.Process.Start(authorizationRequest);

                HttpListenerContext context = null;
                try
                {
                    // Waits for the OAuth authorization response.
                    context = await listener.GetContextAsync();

                    // Sends an HTTP response to the browser.
                    string responseString = "<html><head></head><body>"
                                            + "Please close this tab."
                                            + "</body></html>";
                    var buffer = Encoding.UTF8.GetBytes(responseString);
                    HttpListenerResponse response = context.Response;
                    response.ContentLength64 = buffer.Length;
                    response.ContentType = "text/html; charset=utf-8";
                    var responseOutput = response.OutputStream;
                    await responseOutput.WriteAsync(buffer, 0, buffer.Length);
                    await responseOutput.FlushAsync();

                    await Task.Delay(800);
                    StopListen();
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
                catch (HttpListenerException)
                {
                    return false;
                }

                // Checks for errors.
                if (context.Request.QueryString.Get("error") != null)
                {
                    LastError = context.Request.QueryString.Get("error");
                    return false;
                }
                // extracts the code
                string code = context.Request.QueryString.Get("code");
                string incoming_state = context.Request.QueryString.Get("state");
                if (code == null || incoming_state == null)
                {
                    return false;
                }

                // Compares the receieved state to the expected value, to ensure that this app made the request which resulted in authorization.
                if (incoming_state != state)
                {
                    return false;
                }

                // Starts the code exchange at the Token Endpoint.
                OAuth2Response oAuth2Response = await PerformCodeExchangeAsync(code, redirectURI);
                AccessToken = oAuth2Response.AccessToken;
                RefreshToken = oAuth2Response.RefreshToken;
                ExpiresAt = oAuth2Response.ExpiresAt;
                if (!string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken))
                    return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return false;
        }

        private async Task<OAuth2Response> PerformCodeExchangeAsync(string code, string redirectUri)
        {
            // builds the request
            string tokenRequestBody = $"client_id={CLIENT_ID}&redirect_uri={redirectUri}&code={code}&grant_type=authorization_code";
            if (!string.IsNullOrEmpty(CLIENT_SECRET))
                tokenRequestBody += $"&client_secret={CLIENT_SECRET}";

            // sends the request
            HttpWebRequest tokenRequest = (HttpWebRequest)WebRequest.Create(TokenRequestEndpoint);
            tokenRequest.Method = "POST";
            tokenRequest.ContentType = "application/x-www-form-urlencoded";
            tokenRequest.Accept = "application/json"; //"Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            byte[] requestBuffer = Encoding.ASCII.GetBytes(tokenRequestBody);
            tokenRequest.ContentLength = requestBuffer.Length;
            Stream stream = tokenRequest.GetRequestStream();
            await stream.WriteAsync(requestBuffer, 0, requestBuffer.Length);
            stream.Close();
            stream.Dispose();

            try
            {
                // gets the response
                WebResponse tokenResponse = await tokenRequest.GetResponseAsync();
                using (StreamReader reader = new StreamReader(tokenResponse.GetResponseStream()))
                {
                    // reads response body
                    string responseText = await reader.ReadToEndAsync();

                    // converts to dictionary
                    Dictionary<string, string> tokenEndpointDecoded = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(responseText);

                    string access_token = null, refresh_token = null;
                    if (tokenEndpointDecoded.ContainsKey("access_token"))
                        access_token = tokenEndpointDecoded["access_token"];
                    if (tokenEndpointDecoded.ContainsKey("refresh_token"))
                        refresh_token = tokenEndpointDecoded["refresh_token"];
                    int expiresIn = -1;
                    if (tokenEndpointDecoded.ContainsKey("expires_in"))
                    {
                        int.TryParse(tokenEndpointDecoded["expires_in"], out expiresIn);
                    }
                    string[] scopeList = null;
                    if (tokenEndpointDecoded.ContainsKey("scope"))
                    {
                        scopeList = tokenEndpointDecoded["scope"].Split(' ');
                    }
                    return new OAuth2Response(access_token, refresh_token, scopeList, "bearer", expiresIn);
                }
            }
            catch (WebException ex)
            {
                if (ex.Status == WebExceptionStatus.ProtocolError)
                {
                    if (ex.Response is HttpWebResponse response)
                    {
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        {
                            // reads response body
                            string responseText = await reader.ReadToEndAsync();
                            LastError = responseText;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return null;
        }

        public async Task<bool> RefreshTokenAsync(string refreshToken)
        {
            WebRequest request = WebRequest.Create(TokenRequestEndpoint);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";

            // fill post request form data
            string data = $"client_id={CLIENT_ID}&redirect_uri={REDIRECT_URI}&refresh_token={refreshToken}&grant_type=refresh_token";
            if (!string.IsNullOrEmpty(CLIENT_SECRET))
                data += $"&client_secret={CLIENT_SECRET}";
            using (StreamWriter requestWriter = new StreamWriter(await request.GetRequestStreamAsync()))
            {
                requestWriter.Write(data, 0, data.Length);
                requestWriter.Close();
            }

            try
            {
                using (WebResponse response = await request.GetResponseAsync())
                {
                    using (StreamReader streamReader = new StreamReader(response.GetResponseStream()))
                    {
                        string streamtoStr = await streamReader.ReadToEndAsync();
                        Newtonsoft.Json.Linq.JObject jObject = Newtonsoft.Json.Linq.JObject.Parse(streamtoStr);
                        string accessToken = jObject["access_token"]?.ToString();
                        string newRefreshToken = jObject["refresh_token"]?.ToString();
                        int expiresIn = -1;
                        if (jObject["expires_in"] != null)
                        {
                            expiresIn = jObject["expires_in"].ToObject<int>();
                        }
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            AccessToken = accessToken;
                            if (!string.IsNullOrEmpty(newRefreshToken))
                                RefreshToken = newRefreshToken;
                            else
                                RefreshToken = refreshToken;
                            if (expiresIn > 0)
                                ExpiresAt = DateTime.Now.AddSeconds(expiresIn);
                            else
                                ExpiresAt = null;
                            return true;
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse response)
                {
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        // reads response body
                        string responseText = await reader.ReadToEndAsync();
                        LastError = responseText;
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return false;
        }

        public async Task<bool> RevokeTokenAsync(string accessToken)
        {
            return false;
        }

        public void StopListen()
        {
            if (listener != null)
            {
                try
                {
                    listener.Stop();
                    listener = null;
                    Console.WriteLine("HTTP server stopped.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        // ref http://stackoverflow.com/a/3978040
        private static int GetRandomUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
