using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AL_FATEH_MTK
{
    public class Scatter_Parser
    {
        public string ExtractPlatform(string filePath)
        {
            try
            {
                string content = File.ReadAllText(filePath);
                // البحث عن كلمة platform: MT6765 مثلاً
                Match match = Regex.Match(content, @"platform:\s*(MT\d+)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Scatter_Parser] ExtractPlatform: {ex.GetType().Name}: {ex.Message}");
            }
            return "Unknown";
        }
    }
}