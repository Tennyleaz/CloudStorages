using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CloudStorages.Box
{
    public class BoxOauthClient : IOauthClient
    {
        private const string AuthorizeEndpoint = "https://account.box.com/api/oauth2/authorize";
        private const string TokenRequestEndpoint = "https://api.box.com/oauth2/token";      
        private const string TokenRevokeEndpoint = "https://api.box.com/oauth2/revoke";
        private readonly string apiKey, apiSecret;
        private HttpListener listener;

        public string LastError { get; private set; }

        public string AccessToken { get; private set; }

        public string RefreshToken { get; private set; }

        public DateTime? ExpiresAt { get; private set; }

        public BoxOauthClient(string apiKey, string apiSecret)
        {
            this.apiKey = apiKey;
            this.apiSecret = apiSecret;            
        }

        public async Task<bool> GetTokenAsync()
        {
            StopListen();

            // todo: generate random state each time
            string state = null;            

            try
            {
                // Creates a redirect URI using an available port on the loopback address.
                string redirectUrl= string.Format("http://localhost:{0}/", GetRandomUnusedPort());

                // Creates an HttpListener to listen for requests on that redirect URI.
                listener = new HttpListener();
                listener.Prefixes.Add(redirectUrl);
                listener.Start();

                // Creates the OAuth 2.0 authorization request.
                Uri authUri = GetAuthorizeUri(apiKey, state, redirectUrl);

                // Opens request in the browser.
                System.Diagnostics.Process.Start(authUri.AbsoluteUri);

                HttpListenerContext context = null;
                try
                {
                    // Waits for the OAuth authorization response.
                    context = await listener.GetContextAsync();

                    // Sends an HTTP response to the browser.
                    string responseString = "<html><head><meta http-equiv='refresh' content='10;url=https://app.box.com'></head><body>"
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
                    LastError = ex.Message;
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
                if (code == null)
                {

                    return false;
                }

                // Compares the receieved state to the expected value, to ensure that this app made the request which resulted in authorization.
                if (incoming_state != state)
                {

                    return false;
                }

                // Starts the code exchange at the Token Endpoint.
                (AccessToken, RefreshToken) = await PerformCodeExchangeAsync(code);
                if (!string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken))
                    return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return false;
        }

        public async Task<bool> RefreshTokenAsync(string refreshToken)
        {
            WebRequest request = WebRequest.Create(TokenRequestEndpoint);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            //request.Headers.Add(HttpRequestHeader.AcceptLanguage, "application/json;charset=UTF-8");

            // fill post request form data
            string data = $"client_id={apiKey}&client_secret={apiSecret}&refresh_token={refreshToken}&grant_type=refresh_token";
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
                        refreshToken = jObject["refresh_token"]?.ToString() ?? refreshToken;
                        if (jObject["expires_in"] != null)
                            ExpiresAt = DateTime.Now.AddSeconds(jObject["expires_in"].ToObject<int>());
                        else
                            ExpiresAt = null;
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            AccessToken = accessToken;
                            RefreshToken = refreshToken;
                            return true;
                        }
                    }
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
            return false;
        }

        public async Task<bool> RevokeTokenAsync(string accessToken)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(TokenRevokeEndpoint);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";

            // fill post request form data
            string data = $"client_id={apiKey}&client_secret={apiSecret}&access_token={accessToken}";
            using (StreamWriter requestWriter = new StreamWriter(await request.GetRequestStreamAsync()))
            {
                requestWriter.Write(data, 0, data.Length);
                requestWriter.Close();
            }

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                {
                    // Returns an empty response when the token was successfully revoked.
                    if (response.StatusCode == HttpStatusCode.OK)
                        return true;                    
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        private async Task<(string, string)> PerformCodeExchangeAsync(string code)
        {
            // builds the request
            string tokenRequestBody = $"client_id={apiKey}&client_secret={apiSecret}&code={code}&grant_type=authorization_code";

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
                    Newtonsoft.Json.Linq.JObject jObject = Newtonsoft.Json.Linq.JObject.Parse(responseText);

                    string access_token = null, refresh_token = null;
                    if (jObject["access_token"] != null)
                        access_token = jObject["access_token"].ToString();
                    if (jObject["refresh_token"] != null)
                        refresh_token = jObject["refresh_token"].ToString();
                    if (jObject["expires_in"] != null)
                        ExpiresAt = DateTime.Now.AddSeconds(jObject["expires_in"].ToObject<int>());
                    else
                        ExpiresAt = null;
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

        private static Uri GetAuthorizeUri(string clientId, string state, string redirectUrl)
        {
            if (string.IsNullOrEmpty(redirectUrl))
                throw new ArgumentNullException(nameof(redirectUrl), "Reditrct uri is null!");
            if (string.IsNullOrEmpty(clientId))
                throw new ArgumentNullException(nameof(clientId));

            string strUri = AuthorizeEndpoint + $"?response_type=code&client_id={clientId}&redirect_uri={redirectUrl}";
            if (!string.IsNullOrEmpty(state))
                strUri += $"&state={state}";

            return new Uri(strUri);
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
