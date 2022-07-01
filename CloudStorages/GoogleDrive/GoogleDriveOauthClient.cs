using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CloudStorages.GoogleDrive
{
    public class GoogleDriveOauthClient : IOauthClient
    {
        private const string SCOPE = "https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/drive profile";
        private const string code_challenge_method = "S256";
        private const string authorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string tokenRequestURI = "https://oauth2.googleapis.com/token";
        private const string tokenRevokeUrl = "https://oauth2.googleapis.com/revoke?token=";
        private HttpListener listener;
        private readonly string CLIENT_ID, CLIENT_SECRET;

        public string LastError { get; private set; }

        public string AccessToken { get; private set; }

        public string RefreshToken { get; private set; }

        public DateTime? ExpiresAt { get; private set; }

        public GoogleDriveOauthClient(string apiKey, string apiSecret)
        {
            CLIENT_ID = apiKey;
            CLIENT_SECRET = apiSecret;
        }

        public async Task<bool> GetTokenAsync()
        {
            StopListen();

            // Generates state and PKCE values.
            string state = RandomDataBase64url(8);  // any string value
            string code_verifier = RandomDataBase64url(32);
            string code_challenge = Base64urlencodeNoPadding(Sha256(code_verifier));

            try
            {
                // Creates a redirect URI using an available port on the loopback address.
                string redirectURI = string.Format("http://localhost:{0}/", GetRandomUnusedPort());

                // Creates an HttpListener to listen for requests on that redirect URI.
                listener = new HttpListener();
                listener.Prefixes.Add(redirectURI);
                listener.Start();

                // Creates the OAuth 2.0 authorization request.
                string authorizationRequest = string.Format("{0}?response_type=code&scope={6}&redirect_uri={1}&client_id={2}&state={3}&code_challenge={4}&code_challenge_method={5}",
                    authorizationEndpoint,
                    System.Uri.EscapeDataString(redirectURI),
                    CLIENT_ID,
                    state,
                    code_challenge,
                    code_challenge_method,
                    SCOPE);

                // Opens request in the browser.
                System.Diagnostics.Process.Start(authorizationRequest);
                
                HttpListenerContext context = null;
                try
                {
                    // Waits for the OAuth authorization response.
                    context = await listener.GetContextAsync();

                    // Sends an HTTP response to the browser.
                    string responseString = "<html><head><meta http-equiv='refresh' content='10;url=https://google.com'></head><body>"
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
                catch (HttpListenerException ex)
                {
                    
                    return false;
                }

                // Brings this app back to the foreground.
                //this.Activate();

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
                (AccessToken, RefreshToken) = await PerformCodeExchangeAsync(code, code_verifier, redirectURI);
                if (!string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken))
                    return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return false;
        }

        /// <summary>
        /// 利用 refreshToken 更新 AccessToken 之後存到成員的 AccessToken
        /// </summary>
        public async Task<bool> RefreshTokenAsync(string refreshToken)
        {
            WebRequest request = WebRequest.Create(tokenRequestURI);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            //request.Headers.Add(HttpRequestHeader.AcceptLanguage, "application/json;charset=UTF-8");

            // fill post request form data
            string data = $"client_id={CLIENT_ID}&client_secret={CLIENT_SECRET}&refresh_token={refreshToken}&grant_type=refresh_token";
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
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            AccessToken = accessToken;
                            RefreshToken = refreshToken;
                            return true;
                        }
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
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(tokenRevokeUrl);
            using (HttpWebResponse response = (HttpWebResponse) await request.GetResponseAsync())
            {
                if (response.StatusCode == HttpStatusCode.OK)
                    return true;
                return false;
            }
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

        private async Task<(string, string)> PerformCodeExchangeAsync(string code, string code_verifier, string redirectURI)
        {
            // builds the request
            string tokenRequestBody = string.Format("code={0}&redirect_uri={1}&client_id={2}&code_verifier={3}&client_secret={4}&scope=&grant_type=authorization_code",
                code,
                System.Uri.EscapeDataString(redirectURI),
                CLIENT_ID,
                code_verifier,
                CLIENT_SECRET
                );

            // sends the request
            HttpWebRequest tokenRequest = (HttpWebRequest)WebRequest.Create(tokenRequestURI);
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
                    return (access_token, refresh_token);
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
                            return (null, null);
                        }
                    }
                }                
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return (null, null);
        }

        #region Session states

        /// <summary>
        /// Returns URI-safe data with a given input length.
        /// </summary>
        /// <param name="length">Input length (nb. output will be longer)</param>
        /// <returns></returns>
        private static string RandomDataBase64url(uint length)
        {
            RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
            byte[] bytes = new byte[length];
            rng.GetBytes(bytes);
            return Base64urlencodeNoPadding(bytes);
        }

        /// <summary>
        /// Base64url no-padding encodes the given input buffer.
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        private static string Base64urlencodeNoPadding(byte[] buffer)
        {
            string base64 = Convert.ToBase64String(buffer);

            // Converts base64 to base64url.
            base64 = base64.Replace("+", "-");
            base64 = base64.Replace("/", "_");
            // Strips padding.
            base64 = base64.Replace("=", "");

            return base64;
        }

        /// <summary>
        /// Returns the SHA256 hash of the input string.
        /// </summary>
        /// <param name="inputStirng"></param>
        /// <returns></returns>
        private static byte[] Sha256(string inputStirng)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(inputStirng);
            SHA256Managed sha256 = new SHA256Managed();
            return sha256.ComputeHash(bytes);
        }

        #endregion

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
