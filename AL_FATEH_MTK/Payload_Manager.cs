﻿using System;
using System.Collections.Generic;

namespace AL_FATEH_MTK
{
    public static class Payload_Manager
    {
        // قاموس يحتوي على الذخيرة (Payload) لكل معالج
        private static readonly Dictionary<ushort, byte[]> PayloadStorage = new Dictionary<ushort, byte[]>
        {
            { 0x0000, GenericPayload },
            { 0x6572, GenericPayload },
            { 0x6575, GenericPayload },
            { 0x6577, GenericPayload },
            { 0x6580, GenericPayload },
            { 0x6582, GenericPayload },
            { 0x6589, GenericPayload },
            { 0x6735, GenericPayload },
            { 0x6737, GenericPayload },
            { 0x6739, GenericPayload },
            { 0x6750, GenericPayload },
            { 0x6753, GenericPayload },
            { 0x6763, GenericPayload },
            { 0x6765, new byte[] { 
                0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0xF0, 0x01, 0xFE, 0x00, 0x00, 0x00, 0x00, 0x00, 
                0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0xAA, 0x55 
            }},
            { 0x6771, GenericPayload },
            { 0x6779, GenericPayload },
            { 0x6785, GenericPayload },
            { 0x6889, GenericPayload },
            { 0x6893, GenericPayload },
            { 0x6983, GenericPayload },
        };

        // استخدام بايلود موحد لتقليل حجم الكود حتى يتم تخصيص بايلودات حقيقية
        private static byte[] GenericPayload => new byte[] {
            0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0xAA, 0x55
        };

        /// <summary>
        /// الحصول على Payload لمعالج معين
        /// </summary>
        /// <returns>البايلود متبوعاً بحالته (هل هو حقيقي أم وهمي)</returns>
        public static (byte[] data, bool isVerified) GetPayload(ushort hwCode)
        {
            if (PayloadStorage.ContainsKey(hwCode))
            {
                // يعتبر البايلود حقيقياً إذا لم يكن مطابقاً للبايلود العام (Generic)
                // أو إذا كان لمعالج محدد ببيانات خاصة
                bool isVerified = hwCode == 0x6765; // حالياً MT6765 هو الوحيد الذي يمتلك بصمة مختلفة
                
                System.Diagnostics.Debug.WriteLine($"[{(isVerified ? "✓" : "!")}] تم استدعاء Payload لـ MT{hwCode:X4}");
                return (PayloadStorage[hwCode], isVerified);
            }

            System.Diagnostics.Debug.WriteLine($"[✗] لا يوجد Payload لـ MT{hwCode:X4}");
            return (PayloadStorage[0x0000], false); 
        }

        /// <summary>
        /// إضافة Payload جديد (للتوسع المستقبلي)
        /// </summary>
        public static void AddPayload(ushort hwCode, byte[] payload)
        {
            if (!PayloadStorage.ContainsKey(hwCode))
            {
                PayloadStorage.Add(hwCode, payload);
                System.Diagnostics.Debug.WriteLine($"[+] تم إضافة Payload لـ MT{hwCode:X4}");
            }
        }

        /// <summary>
        /// الحصول على قائمة بجميع المعالجات المدعومة
        /// </summary>
        public static List<ushort> GetSupportedProcessors()
        {
            return new List<ushort>(PayloadStorage.Keys);
        }

        /// <summary>
        /// عدد المعالجات المدعومة
        /// </summary>
        public static int Count => PayloadStorage.Count;
    }
}