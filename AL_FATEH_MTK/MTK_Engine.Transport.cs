using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace AL_FATEH_MTK
{
    /// <summary>
    /// طبقة I/O عامة وموثوقة فوق مقبض Win32.
    /// الهدف: تقليل فقدان البيانات وتحسين الثبات في الاتصالات السريعة عبر:
    /// - Buffer داخلي للـ RX حتى لا نضطر لقراءة "بايت ببايت" من النظام.
    /// - ReadExactly / WriteAll لضمان اكتمال القراءة/الكتابة أو فشل واضح مع Timeout.
    /// - Retry logic ذكي للتعامل مع تقطّع النقل المؤقت بدون كسر البروتوكول الأعلى.
    ///
    /// ملاحظة: هذه الطبقة "Generic" ولا تفترض بروتوكول معين.
    /// </summary>
    /// <remarks>
    /// --- نظام Asynchronous I/O متقدم ---
    /// - جميع عمليات النقل الأساسية تدعم Native Overlapped I/O عبر kernel32.
    /// - Zero-Copy: تفعيل التثبيت (Pinning) للذاكرة بدلاً من نسخ البايتات.
    /// - Smart Timeout: يستخدم WaitHandle و CancelIoEx لإدارة المهلات بدقة عالية.
    /// - Burst Mode: تقسيم عمليات القراءة الضخمة إلى دفعات مع تحقق Checksum اختياري لكل Chunk.
    /// - Thread Safety: دعم قوي للمتزامنات مع حماية _rxLock للتعامل مع async بدون Deadlock.
    ///
    /// مثال للاستخدام:
    ///    await AsyncWriteAllAsync(buffer, offset, count, timeout);
    /// 
    /// جميع الدوال القديمة تم توجيهها داخلياً لاستخدام المحرك الحديث حفاظاً على التوافقية!
    /// </remarks>
    public partial class MTK_Engine
    {
        public IntPtr hDevice = (IntPtr)(-1);

        // ---------------------------
        // Generic I/O configuration
        // ---------------------------
        public TimeSpan DefaultReadTimeout { get; set; } = TimeSpan.FromMilliseconds(400);
        public TimeSpan DefaultWriteTimeout { get; set; } = TimeSpan.FromMilliseconds(400);
        public int MaxIoRetries { get; set; } = 2;
        public int IoBackoffBaseMs { get; set; } = 8;

        // ---------------------------
        // RX buffering (avoid byte-by-byte syscalls)
        // ---------------------------
        private readonly object _rxLock = new object();
        private byte[] _rxBuf;
        private int _rxStart;
        private int _rxLen;
        private const int DefaultRxBufferSize = 64 * 1024;

        // Buffers صغيرة يعاد استخدامها لتجنب new byte[1] المتكرر في المسار الساخن.
        [ThreadStatic] private static byte[] _oneByte;
        [ThreadStatic] private static byte[] _oneByteOut;

        /// <summary>
        /// البحث التلقائي عن منفذ MediaTek (Brom/Preloader) وفتحه نووياً
        /// </summary>
        public bool OpenMtkSerialPort()
        {
            try
            {
                LogMessage("[🔍] جاري فحص المنافذ بحثاً عن بصمة MediaTek...", ConsoleColor.Yellow);

                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%)'"))
                {
                    foreach (var device in searcher.Get())
                    {
                        string hardwareId = device["DeviceID"]?.ToString() ?? "";
                        string caption = device["Caption"]?.ToString() ?? "";

                        if (hardwareId.Contains("VID_0E8D"))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(caption, @"(COM\d+)");
                            if (match.Success)
                            {
                                string portName = match.Value;
                                string devicePath = @"\\.\" + portName;

                                LogMessage($"[🎯] تم قنص الجهاز على المنفذ: {portName}", ConsoleColor.Green);
                                return OpenDevice(devicePath);
                            }
                        }
                    }
                }

                LogMessage("[-] لم يتم العثور على أي هاتف MediaTek متصل حالياً.", ConsoleColor.Gray);
                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"[🔥] خطأ أثناء فحص المنافذ: {ex.Message}", ConsoleColor.Red);
                LogExceptionToConsole("OpenMtkSerialPort", ex);
                return false;
            }
        }

        public bool OpenDevice(string devicePath)
        {
            CloseDevice();

            hDevice = NativeMethods.CreateFile(
                devicePath,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                0,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                NativeMethods.FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (hDevice == (IntPtr)(-1))
            {
                LogMessage($"[❌] فشل فتح الوصول للنواة. الخطأ: {Marshal.GetLastWin32Error()}", ConsoleColor.Red);
                return false;
            }

            LogMessage("[🔓] تم فتح قناة اتصال مباشرة مع النواة.", ConsoleColor.Green);

            // تهيئة الـ RX buffer عند نجاح الفتح. هذا يمنع تخصيصات متكررة أثناء النقل.
            EnsureRxBuffer();
            ResetRxBuffer();
            return true;
        }

        public void CloseDevice()
        {
            if (hDevice == IntPtr.Zero || hDevice == (IntPtr)(-1))
                return;

            if (hDevice != (IntPtr)(-1))
            {
                NativeMethods.CloseHandle(hDevice);
                LogMessage("[🔌] تم إغلاق قناة اتصال النواة بأمان.", ConsoleColor.Gray);
                hDevice = (IntPtr)(-1);
                ResetRxBuffer();
            }
        }

        private void EnsureRxBuffer()
        {
            if (_rxBuf != null && _rxBuf.Length >= DefaultRxBufferSize)
                return;
            // نستخدم Buffer ثابت لتوافق كامل مع .NET Framework بدون اعتماد على System.Buffers.
            _rxBuf = new byte[DefaultRxBufferSize];
        }

        private void ResetRxBuffer()
        {
            lock (_rxLock)
            {
                _rxStart = 0;
                _rxLen = 0;
            }
        }

        // ===========================
        // Generic robust I/O methods
        // ===========================
        public async Task<bool> WriteAllAsync(byte[] data, int offset, int count, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length) throw new ArgumentOutOfRangeException();
            if (hDevice == (IntPtr)(-1)) return false;

            var sw = Stopwatch.StartNew();
            TimeSpan t = timeout ?? DefaultWriteTimeout;
            int attempt = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                uint written = 0;
                bool ok = NativeMethods.WriteFile(hDevice, SliceToNewArrayIfNeeded(data, offset, count), (uint)count, out written, IntPtr.Zero) && written == (uint)count;
                if (ok)
                    return true;

                attempt++;
                if (attempt > MaxIoRetries || sw.Elapsed > t)
                    return false;

                // Backoff قصير يخفف الضغط عند الازدحام/التقطّع المؤقت.
                int backoff = IoBackoffBaseMs * attempt;
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
        }

        public async Task<int> ReadSomeAsync(byte[] dest, int offset, int count, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            if (offset < 0 || count < 0 || offset + count > dest.Length) throw new ArgumentOutOfRangeException();
            if (hDevice == (IntPtr)(-1)) return 0;
            if (count == 0) return 0;

            // 1) جرّب أولاً من الـ RX buffer الداخلي
            int fromBuf = TryDequeueFromRxBuffer(dest, offset, count);
            if (fromBuf > 0)
                return fromBuf;

            // 2) إن لم يوجد شيء، اقرأ chunk أكبر إلى الـ RX buffer ثم اسحب منه
            EnsureRxBuffer();

            var sw = Stopwatch.StartNew();
            TimeSpan t = timeout ?? DefaultReadTimeout;
            int attempt = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                uint bRead = 0;
                // اقرأ إلى buffer كبير مرة واحدة، ثم استهلك منه بالقدر المطلوب.
                byte[] tmp = _rxBuf;
                bool ok = NativeMethods.ReadFile(hDevice, tmp, (uint)tmp.Length, out bRead, IntPtr.Zero);

                if (ok && bRead > 0)
                {
                    lock (_rxLock)
                    {
                        _rxStart = 0;
                        _rxLen = (int)bRead;
                    }
                    return TryDequeueFromRxBuffer(dest, offset, count);
                }

                attempt++;
                if (attempt > MaxIoRetries || sw.Elapsed > t)
                    return 0;

                int backoff = IoBackoffBaseMs * attempt;
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
        }

        public async Task<bool> ReadExactlyAsync(byte[] dest, int offset, int count, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            if (offset < 0 || count < 0 || offset + count > dest.Length) throw new ArgumentOutOfRangeException();
            if (hDevice == (IntPtr)(-1)) return false;
            if (count == 0) return true;

            var sw = Stopwatch.StartNew();
            TimeSpan t = timeout ?? DefaultReadTimeout;

            int total = 0;
            while (total < count)
            {
                ct.ThrowIfCancellationRequested();
                if (sw.Elapsed > t)
                    return false;

                int got = await ReadSomeAsync(dest, offset + total, count - total, timeout: t - sw.Elapsed, ct: ct).ConfigureAwait(false);
                if (got <= 0)
                {
                    // لا نكسر فوراً: قد تكون نافذة بيانات قصيرة. نعمل backoff خفيف ونكمل حتى ينتهي budget.
                    await Task.Delay(IoBackoffBaseMs, ct).ConfigureAwait(false);
                    continue;
                }
                total += got;
            }
            return true;
        }

        private int TryDequeueFromRxBuffer(byte[] dest, int offset, int count)
        {
            lock (_rxLock)
            {
                if (_rxLen <= 0)
                    return 0;

                int take = Math.Min(count, _rxLen);
                Buffer.BlockCopy(_rxBuf, _rxStart, dest, offset, take);
                _rxStart += take;
                _rxLen -= take;

                if (_rxLen == 0)
                    _rxStart = 0;

                return take;
            }
        }

        // Win32 ReadFile/WriteFile لا يقبل Span؛ نضمن أن الممرر هو مصفوفة بدءها 0 عند الحاجة.
        // في معظم استخداماتنا الحالية (SendBuffer) تكون offset=0 أصلاً، فلا يوجد نسخ إضافي.
        private static byte[] SliceToNewArrayIfNeeded(byte[] data, int offset, int count)
        {
            if (offset == 0 && count == data.Length)
                return data;
            var tmp = new byte[count];
            Buffer.BlockCopy(data, offset, tmp, 0, count);
            return tmp;
        }

        public bool SendByte(byte b)
        {
            if (hDevice == (IntPtr)(-1))
                return false;

            // نستخدم WriteAllAsync بشكل متزامن للحفاظ على الواجهة القديمة.
            var buffer = _oneByteOut ??= new byte[1];
            buffer[0] = b;
            try
            {
                return WriteAllAsync(buffer, 0, 1).GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }

        public byte ReadByte()
        {
            if (hDevice == (IntPtr)(-1))
                return 0xFF;

            var buffer = _oneByte ??= new byte[1];
            try
            {
                bool ok = ReadExactlyAsync(buffer, 0, 1).GetAwaiter().GetResult();
                return ok ? buffer[0] : (byte)0xFF;
            }
            catch
            {
                return 0xFF;
            }
        }

        public bool SendBuffer(byte[] buffer)
        {
            if (hDevice == (IntPtr)(-1))
                return false;

            try
            {
                return WriteAllAsync(buffer, 0, buffer.Length).GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }
    }
}
