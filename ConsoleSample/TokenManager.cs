using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleSample
{
    internal class TokenManager
    {
        public TokenManager(string appName)
        {
            Name = appName;
        }

        public string Name { get; }

        public string LoadAccessToken()
        {
            try
            {
                string path = $@"D:\Test Dir\CloudStorages\{Name}_access.txt";
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public string LoadRefresToken()
        {
            try
            {
                string path = $@"D:\Test Dir\CloudStorages\{Name}_refresh.txt";
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public void SaveAccessToken(string token)
        {
            try
            {
                string path = $@"D:\Test Dir\CloudStorages\{Name}_access.txt";
                File.WriteAllText(path, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public void SaveRefreshToken(string token)
        {
            try
            {
                string path = $@"D:\Test Dir\CloudStorages\{Name}_refresh.txt";
                File.WriteAllText(path, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
