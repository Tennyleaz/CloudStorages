using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudStorages;
using CloudStorages.DropBox;
using CloudStorages.GoogleDrive;

namespace ConsoleSample
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // create dropbox client
                string apiKey;
                string redirectUrl = @"http://localhost:51001/";
                try
                {
                    apiKey = File.ReadAllText(@"D:\Test Dir\CloudStorages\dbkey.txt");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Cannot read api key: " + ex.Message);
                    throw ex;
                }

                ICloudStorageClient dropBoxClient = new DropBoxStorage(apiKey, redirectUrl);
                dropBoxClient.SaveAccessTokenDelegate = SaveAccessToken;
                dropBoxClient.SaveRefreshTokenDelegate = SaveRefreshToken;
                dropBoxClient.LoadAccessTokenDelegate = LoadAccessToken;
                dropBoxClient.LoadRefreshTokenDelegate = LoadRefresToken;

                Task.Run(() => Test(dropBoxClient)).Wait();
                Console.Write("Finish testing dropbox.\n\n");

                // create google drive client
                string appName = "Test App";
                string apiSecret;
                try
                {
                    apiKey = File.ReadAllText(@"D:\Test Dir\CloudStorages\gdkey.txt");
                    apiSecret = File.ReadAllText(@"D:\Test Dir\CloudStorages\gdsecret.txt");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Cannot read api key/secret: " + ex.Message);
                    throw ex;
                }

                ICloudStorageClient googleClient = new GoogleDriveStorage(apiKey, apiSecret, appName);
                //googleClient.SaveAccessTokenDelegate = SaveAccessToken;
                //googleClient.SaveRefreshTokenDelegate = SaveRefreshToken;
                //googleClient.LoadAccessTokenDelegate = LoadAccessToken;
                //googleClient.LoadRefreshTokenDelegate = LoadRefresToken;

                Task.Run(() => Test(googleClient)).Wait();
                Console.Write("Finish testing google drive.\n\n");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            Console.WriteLine("Press any key to end.");
            Console.ReadKey();
        }

        private static async Task Test(ICloudStorageClient client)
        {
            var result = await client.InitAsync();
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Initial failed, reason=" + result.Message);
                return;
            }
            
            CloudStorageAccountInfo accountInfo;
            if (result.Status == Status.NeedAuthenticate)
            {
                (result, accountInfo) = await client.LoginAsync();
                client.StopListen();
            }
            else
                (result, accountInfo) = await client.GetAccountInfoAsync();

            if (result.Status != Status.Success)
            {
                Console.WriteLine("Login failed, reason=" + result.Message);
                return;
            }
            
            Console.WriteLine("Login success, account=" + accountInfo.userEmail);
            IFormatProvider formatter = new CloudStorages.Utility.FileSizeFormatProvider();
            Console.WriteLine(string.Format(formatter, "Account space: {0:fs} / {1:fs}", accountInfo.usedSpace, accountInfo.totalSpace));

            string folderId, folderName = "/test/folder1";
            (result, folderId) = await client.CreateFolderAsync(folderName);
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Create folder failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Created folder {folderName}, id={folderId}");

            string localFile = "test.txt";
            File.WriteAllText(localFile, "I am a test file.");
            CancellationTokenSource cts = new CancellationTokenSource();
            result = await client.UploadFileToFolderByPathAsync(localFile, cts.Token, folderName);
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Upload file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Uploaded file {localFile}");
            File.Delete(localFile);

            string cloudFile = folderName + "/" + localFile;
            result = await client.DownloadFileByPathAsync(cloudFile, localFile, cts.Token);
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Download file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Downloaded file {localFile}");

            result = await client.DeleteFileByPathAsync(cloudFile);
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Delete file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Deleted file {localFile}");
        }

        #region Token save/load
        private static string LoadAccessToken()
        {
            try
            {
                string path = @"D:\Test Dir\CloudStorages\dbaccess.txt";
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        private static string LoadRefresToken()
        {
            try
            {
                string path = @"D:\Test Dir\CloudStorages\dbrefresh.txt";
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        private static void SaveAccessToken(string token)
        {
            try
            {
                string path = @"D:\Test Dir\CloudStorages\dbaccess.txt";
                File.WriteAllText(path, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void SaveRefreshToken(string token)
        {
            try
            {
                string path = @"D:\Test Dir\CloudStorages\dbrefresh.txt";
                File.WriteAllText(path, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        #endregion
    }
}
