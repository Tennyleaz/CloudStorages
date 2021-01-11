using Microsoft.Win32;

namespace WpfSample
{
    internal static class UriRegistry
    {
        private const string KEY_NAME = "CloudStorageWpf";
        private const string APP_LOCATION = @"D:\Test Dir\CloudStorages\WpfSample\bin\Debug\WpfSample.exe";

        public static void Register()
        {
            RegistryKey key = Registry.ClassesRoot.CreateSubKey(KEY_NAME);
            if (key != null)
            {
                string uriValue = "URL:" + KEY_NAME;
                key.SetValue("", uriValue, RegistryValueKind.String);  // (Default) "URL:CloudStorageWpf"
                key.SetValue("URL Protocol", "", RegistryValueKind.String);  // URL Protocol ""

                RegistryKey subKey = key.CreateSubKey("DefaultIcon");
                string defaultIcon = APP_LOCATION + ",0";
                subKey.SetValue("", defaultIcon, RegistryValueKind.String);  // (Default) "URL:CloudStorageWpf"

                subKey = key.CreateSubKey("shell");

                subKey = subKey.CreateSubKey("open");

                subKey = subKey.CreateSubKey("command");
                if (subKey != null)
                {
                    string command = APP_LOCATION + " %1";
                    subKey.SetValue("", command, RegistryValueKind.String);
                }
            }
        }

        public static void Unregister()
        {
            Registry.ClassesRoot.DeleteSubKeyTree(KEY_NAME);
        }
    }
}
