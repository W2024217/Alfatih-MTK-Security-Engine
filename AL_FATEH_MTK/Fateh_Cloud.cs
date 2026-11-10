using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;

namespace AL_FATEH_MTK
{
    public class Fateh_Cloud
    {
        private static readonly HttpClient client = new HttpClient();

        // دالة تحميل الأصل من السحاب وحفظه محلياً لاستخدامه مستقبلاً
        public async Task<bool> DownloadSovereignAsset(string assetName, string expectedHash = "")
        {
            try
            {
                // رابط المستودع (سنقوم بتغييره لاحقاً لرابطك الفعلي)
                string url = $"https://your-server.com/assets/{assetName}.bin";
                string directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sovereign_Assets", "Cores");
                string localPath = Path.Combine(directoryPath, assetName + ".bin");

                // جلب البيانات من السحاب
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    byte[] assetData = await response.Content.ReadAsByteArrayAsync();

                    if (assetData != null && assetData.Length > 0)
                    {
                        // التحقق من صحة الملف إذا تم توفير الهاش
                        if (!string.IsNullOrEmpty(expectedHash))
                        {
                            if (!VerifyChecksum(assetData, expectedHash))
                            {
                                return false;
                            }
                        }

                        // التأكد من وجود المجلد قبل الكتابة لتجنب DirectoryNotFoundException
                        if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

                        File.WriteAllBytes(localPath, assetData);
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // فشل بسبب النت أو الرابط - سنتعامل مع هذا لاحقاً بهدوء
                return false;
            }
            return false;
        }

        private bool VerifyChecksum(byte[] data, string expectedHash)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(data);
                string currentHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                return currentHash == expectedHash.ToLower();
            }
        }
    }
}