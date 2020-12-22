using System;

namespace CloudStorages
{
    public struct CloudStorageResult
    {
        public bool Success;
        public string Message;
    }

    public struct CloudStorageAccountInfo
    {
        public string userName, userEmail;
        public long totalSpace, usedSpace;
    }

    public struct CloudStorageFile
    {
        public long Size;
        public string Name;
        public string Id;
        public DateTime ModifiedTime;
        public DateTime CreatedTime;
    }

    public class CloudStorageProgressArgs : EventArgs
    {
        public long BytesSent;
    }
}
