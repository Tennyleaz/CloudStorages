using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CloudStorages
{
    public interface ICloudStorageClient
    {
        event EventHandler<CloudStorageProgressArgs> ProgressChanged;

        Func<string> LoadAccessTokenDelegate { get; set; }

        Func<string> LoadRefreshTokenDelegate { get; set; }

        Action<string> SaveAccessTokenDelegate { get; set; }

        Action<string> SaveRefreshTokenDelegate { get; set; }

        /// <summary>
        /// Stop any possible HTTP listener for oauth client. Will also stop <see cref="LoginAsync"/> flow.
        /// </summary>
        void StopListen();

        /// <summary>
        /// 初始化元件，並檢查有沒有舊的登入Token，有的話嘗試refresh token一次
        /// </summary>
        /// <returns>成員成功初始化就會回傳Success</returns>
        Task<CloudStorageResult> InitAsync();

        /// <summary>
        /// 登入並取得帳號和容量資訊
        /// </summary>
        Task<(CloudStorageResult, CloudStorageAccountInfo)> LoginAsync();

        /// <summary>
        /// 如果有先前的登入資訊，就只需要取得剩下欄位
        /// </summary>
        Task<(CloudStorageResult result, CloudStorageAccountInfo info)> GetAccountInfoAsync();

        Task<(CloudStorageResult result, string folderId)> CreateFolderAsync(string parentId, string folderName);

        /// <summary>
        /// 從指定的完整路徑建立資料夾
        /// </summary>
        /// <param name="fullFolderPath"></param>
        /// <returns>成功的話傳回新資料夾 Id</returns>
        Task<(CloudStorageResult result, string folderId)> CreateFolderAsync(string fullFolderPath);

        string GetFolderId(string parentId, string folderName);

        string GetFolderId(string fullFolderPath);

        /// <summary>
        /// 取得單一檔案的資訊。
        /// </summary>
        /// <param name="filePath"></param>
        /// <exception cref="FileNotFoundException">When filePath is not a file or not exist.</exception>
        /// <returns></returns>
        Task<CloudStorageFile> GetFileInfoAsync(string filePath);

        /// <summary>
        /// 取得指定資料夾下單層檔案的資訊。
        /// </summary>
        /// <param name="folderPath"></param>
        /// <exception cref="FileNotFoundException">When folderPath is not a folder or not exist.</exception>
        /// <returns></returns>
        Task <IEnumerable<CloudStorageFile>> GetFileInfosInPathAsync(string folderPath);

        Task <CloudStorageResult> DownloadFileByIdAsync(string fileID, string savePath, CancellationToken ct);

        Task<CloudStorageResult> DownloadFileByPathAsync(string filePath, string savePath, CancellationToken ct);

        Task<CloudStorageResult> UploadFileToFolderByIdAsync(string filePath, CancellationToken ct, string folderId = null);

        Task<CloudStorageResult> UploadFileToFolderByPathAsync(string filePath, CancellationToken ct, string folderName = null);

        Task<CloudStorageResult> DeleteFileByIdAsync(string fileID);

        Task<CloudStorageResult> DeleteFileByPathAsync(string filePath);
    }
}
