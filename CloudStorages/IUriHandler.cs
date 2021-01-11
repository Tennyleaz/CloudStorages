using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudStorages
{
    /// <summary>
    /// The cloud storage client which could also handle uri redirect.
    /// </summary>
    public interface IUriHandler : ICloudStorageClient
    {
        /// <summary>
        /// Extract access token from redirect uri, and init dropbox client when authentication success.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="uri"></param>
        /// <returns></returns>
        Task<CloudStorageResult> AuthenticateFromUri(string state, string uri);

        /// <summary>
        /// OAuth login "token" flow through browser. Will not get additional results.
        /// After browser redirects uri back, call <see cref="AuthenticateFromUri"/> to process access token. 
        /// </summary>
        /// <returns>Returns current OAuth state.</returns>
        string LoginToUri();
    }
}
