using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        void StopListen();

        /// <summary>
        /// 初始化元件，並檢查有沒有舊的登入Token，有的話嘗試refresh token一次
        /// </summary>
        /// <returns>成員成功初始化就會回傳Success</returns>
        Task<(CloudStorageResult result, bool IsNeedLogin)> InitAsync();

        /// <summary>
        /// 登入並取得帳號和容量資訊
        /// </summary>
        Task<(CloudStorageResult, CloudStorageAccountInfo)> LoginAsync();

        /// <summary>
        /// 如果有先前的登入資訊，就只需要取得剩下欄位
        /// </summary>
        Task<(CloudStorageResult result, CloudStorageAccountInfo info)> GetAccountInfoAsync();

        string CreateFolder(string parentId, string folderName);

        string CreateFolder(string fullFolderPath);

        string GetFolderId(string parentId, string folderName);

        string GetFolderId(string fullFolderPath);

        CloudStorageFile GetFileInfo(string filePath);

        IEnumerable<CloudStorageFile> GetFileInfosInPath(string filePath);

        CloudStorageResult DownloadFileById(string fileID, string savePath);

        CloudStorageResult DownloadFileByPath(string filePath, string savePath);

        CloudStorageResult UploadFileToFolderById(string filePath, string folderId = null);

        CloudStorageResult UploadFileToFolderByPath(string filePath, string folderName = null);

        Task<CloudStorageResult> DeleteFileByIdAsync(string fileID);

        Task<CloudStorageResult> DeleteFileByPathAsync(string filePath);
    }
}
