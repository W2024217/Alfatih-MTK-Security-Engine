﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks; // أضفنا هذا لدعم المهام المستقبلية

namespace AL_FATEH_MTK
{
    /// <summary>
    /// نواة المحرك: الحالة المشتركة، التسجيل، وهيكل الأقسام.
    /// منطق النقل في MTK_Engine.Transport.cs والبروتوكول في MTK_Engine.Protocol.cs.
    /// </summary>
    public partial class MTK_Engine
    {
        // دالة تسجيل الرسائل للواجهة الرسومية
        private Action<string, ConsoleColor> LogToForm;

        // قائمة الأقسام المكتشفة (Data Store)
        private List<PartitionInfoExtended> detectedPartitions = new List<PartitionInfoExtended>();

        public class PartitionInfoExtended
        {
            public string Name { get; set; }
            public long StartAddress { get; set; }
            public long Size { get; set; }
            public string Region { get; set; }
            public bool IsBackedUp { get; set; }
            public string BackupPath { get; set; }
        }

        /// <summary>
        /// تعيين كائن التسجيل لربط المحرك بالواجهة (WinForms).
        /// </summary>
        public void SetLogger(Action<string, ConsoleColor> logger)
        {
            LogToForm = logger;
        }

        /// <summary>
        /// طباعة الرسائل الملونة (تستخدمها كافة أجزاء المحرك).
        /// </summary>
        private void LogMessage(string message, ConsoleColor color = ConsoleColor.White)
        {
            // إضافة تفاصيل تقنية (Rich Logging) لجعل الواجهة تبدو احترافية
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string threadId = System.Threading.Thread.CurrentThread.ManagedThreadId.ToString("D2");
            string richMessage = $"[{timestamp}][T-{threadId}] {message}";
            
            LogToForm?.Invoke(richMessage, color);
        }

        /// <summary>
        /// تسجيل استثناءات النظام بدقة (للديبيج والعمليات الحساسة).
        /// </summary>
        private static void LogExceptionToConsole(string context, Exception ex)
        {
            // طباعة للـ Console للتشخيص العميق
            Console.WriteLine($"[MTK_Core_Error] {context}: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"[MTK_Core_Inner] {ex.InnerException.Message}");
        }

        // --- [ إضافات النخبة الذكية ] ---

        /// <summary>
        /// مسح قائمة الأقسام عند بدء عملية جديدة (لضمان دقة إحداثيات السكاتر).
        /// </summary>
        public void ClearPartitionData()
        {
            detectedPartitions.Clear();
            LogMessage("[🧹] تم تنظيف ذاكرة الأقسام المؤقتة.. الاستعداد لمسح جديد.", ConsoleColor.Yellow);
        }
    }
}