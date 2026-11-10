using System;
using System.IO;
using System.Windows.Forms;

namespace AL_FATEH_MTK
{
    public static class AssetManager
    {
        // تحديد المسار الرئيسي للأصول السيادية داخل مجلد البرنامج
        private static readonly string AssetsPath = Path.Combine(Application.StartupPath, "Sovereign_Assets");

        // دالة التأكد من وجود المجلد وتأسيسه عند تشغيل البرنامج لأول مرة
        public static void Initialize()
        {
            try
            {
                if (!Directory.Exists(AssetsPath))
                {
                    Directory.CreateDirectory(AssetsPath);
                    // إنشاء مجلدات فرعية للتنظيم (اختياري)
                    Directory.CreateDirectory(Path.Combine(AssetsPath, "Cores"));
                    Directory.CreateDirectory(Path.Combine(AssetsPath, "Modules"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"[!] خطأ في تأسيس قاعدة الأصول: {ex.Message}");
            }
        }

        // دالة التحقق من توفر أصل معين (ملف بايلود) محلياً
        public static bool IsAssetAvailable(string assetName)
        {
            string filePath = Path.Combine(AssetsPath, "Cores", assetName + ".bin");
            return File.Exists(filePath);
        }

        // دالة جلب مسار الأصل لاستخدامه في المحرك
        public static string GetAssetPath(string assetName)
        {
            return Path.Combine(AssetsPath, "Cores", assetName + ".bin");
        }
    }
}