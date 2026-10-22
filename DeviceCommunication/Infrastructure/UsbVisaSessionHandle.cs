using System;
using System.Collections.Generic;
using System.IO;
using DeviceCommunication.Models;
#if NET9_0_OR_GREATER
using Ivi.Visa;
#endif

namespace DeviceCommunication.Infrastructure
{
    /// <summary>
    /// 延迟加载 Ivi.Visa 的会话封装，仅在 VisaRuntime 可用后实例化。
    /// </summary>
    internal sealed class UsbVisaSessionHandle : IUsbVisaSessionBridge
    {
#if NET9_0_OR_GREATER
        private IMessageBasedSession? _session;
#endif

        public bool IsOpen
        {
            get
            {
#if NET9_0_OR_GREATER
                return _session is not null;
#else
                return false;
#endif
            }
        }

        public static IReadOnlyList<UsbVisaDeviceInfo> FindUsbInstruments()
        {
#if NET9_0_OR_GREATER
            var devices = new List<UsbVisaDeviceInfo>();
            foreach (var resourceName in GlobalResourceManager.Find("USB?*INSTR"))
                devices.Add(UsbVisaResourceDiscovery.ParseResourceString(resourceName));
            return devices;
#else
            throw new PlatformNotSupportedException("USB VISA discovery requires the net9.0 target.");
#endif
        }

        public bool TryOpen(string resourceName, int timeoutMs, out string? errorMessage)
        {
#if NET9_0_OR_GREATER
            errorMessage = null;
            try
            {
                if (_session is not null)
                    return true;

                _session = GlobalResourceManager.Open(resourceName) as IMessageBasedSession
                    ?? throw new InvalidOperationException($"VISA resource is not message based: {resourceName}");
                _session.TimeoutMilliseconds = timeoutMs;
                return true;
            }
            catch (Exception ex)
            {
                InvalidateSession();
                errorMessage = ex.Message;
                return false;
            }
#else
            errorMessage = "USB VISA communication requires the net9.0 target.";
            return false;
#endif
        }

        public void WriteBytes(byte[] data)
        {
#if NET9_0_OR_GREATER
            if (!TryWriteBytes(data))
                throw new IOException("USB VISA 设备已断开或不可用。");
#endif
        }

        public void WriteString(string data)
        {
#if NET9_0_OR_GREATER
            if (!TryWriteString(data))
                throw new IOException("USB VISA 设备已断开或不可用。");
#endif
        }

        public byte[] ReadBytes()
        {
#if NET9_0_OR_GREATER
            if (!TryReadBytes(out var response))
                throw new IOException("USB VISA 设备已断开或不可用。");
            return response;
#else
            throw new PlatformNotSupportedException("USB VISA communication requires the net9.0 target.");
#endif
        }

        public string ReadString()
        {
#if NET9_0_OR_GREATER
            if (!TryReadString(out var response))
                throw new IOException("USB VISA 设备已断开或不可用。");
            return response;
#else
            throw new PlatformNotSupportedException("USB VISA communication requires the net9.0 target.");
#endif
        }

        public bool TryWriteAndReadString(string data, out string response)
        {
            response = string.Empty;
#if NET9_0_OR_GREATER
            if (_session is null)
                return false;

            try
            {
                _session.RawIO.Write(data);
                try
                {
                    response = _session.RawIO.ReadString();
                    return true;
                }
                catch (NativeVisaException ex) when (IsInputProtocolError(ex))
                {
                    TryClear();
                    if (_session is null)
                        return false;

                    _session.RawIO.Write(data);
                    response = _session.RawIO.ReadString();
                    return true;
                }
            }
            catch (IOTimeoutException)
            {
                throw;
            }
            catch (NativeVisaException ex) when (IsLinkLost(ex))
            {
                InvalidateSession();
                return false;
            }
#else
            return false;
#endif
        }

        public string WriteAndReadString(string data)
        {
            if (!TryWriteAndReadString(data, out var response))
                throw new IOException("USB VISA 设备已断开或不可用。");
            return response;
        }

        public void ConfigureReadTermination(byte terminationChar, bool enabled)
        {
#if NET9_0_OR_GREATER
            if (_session is null)
                return;

            try
            {
                _session.TerminationCharacterEnabled = enabled;
                if (enabled)
                    _session.TerminationCharacter = terminationChar;
            }
            catch (NativeVisaException ex) when (IsLinkLost(ex))
            {
                InvalidateSession();
            }
#endif
        }

        public void TryClear()
        {
#if NET9_0_OR_GREATER
            if (_session is null)
                return;

            try
            {
                _session.Clear();
            }
            catch
            {
                // 部分 USB 设备不支持 CLEAR，忽略以免拖垮连接流程。
            }
#endif
        }

        public void SetTimeout(int timeoutMs)
        {
#if NET9_0_OR_GREATER
            if (_session is null)
                return;

            try
            {
                _session.TimeoutMilliseconds = timeoutMs;
            }
            catch (NativeVisaException ex) when (IsLinkLost(ex))
            {
                InvalidateSession();
            }
#endif
        }

        public void Dispose()
        {
#if NET9_0_OR_GREATER
            InvalidateSession();
#endif
        }

#if NET9_0_OR_GREATER
        private bool TryWriteBytes(byte[] data)
        {
            if (_session is null)
                return false;

            try
            {
                _session.RawIO.Write(data);
                return true;
            }
            catch (NativeVisaException ex) when (IsLinkLost(ex))
            {
                InvalidateSession();
                return false;
            }
        }

        private bool TryWriteString(string data)
        {
            if (_session is null)
                return false;

            try
            {
                _session.RawIO.Write(data);
                return true;
            }
            catch (NativeVisaException ex) when (IsLinkLost(ex))
            {
                InvalidateSession();
                return false;
            }
        }

        private bool TryReadBytes(out byte[] response)
        {
            response = Array.Empty<byte>();
            if (_session is null)
                return false;

            try
            {
                response = _session.RawIO.Read();
                return true;
            }
            catch (IOTimeoutException)
            {
                throw;
            }
            catch (NativeVisaException ex) when (IsLinkLost(ex))
            {
                InvalidateSession();
                return false;
            }
        }

        private bool TryReadString(out string response)
        {
            response = string.Empty;
            if (_session is null)
                return false;

            try
            {
                response = _session.RawIO.ReadString();
                return true;
            }
            catch (IOTimeoutException)
            {
                throw;
            }
            catch (NativeVisaException ex) when (IsLinkLost(ex))
            {
                InvalidateSession();
                return false;
            }
        }

        private void InvalidateSession()
        {
            var session = _session;
            _session = null;
            if (session is null)
                return;

            try
            {
                session.Dispose();
            }
            catch
            {
                // 掉线后 Dispose 也可能失败，忽略。
            }
        }

        private static bool IsInputProtocolError(NativeVisaException ex) =>
            ex.ErrorCode == NativeErrorCode.InputProtocolViolation;

        /// <summary>
        /// USB 拔线、总线故障、资源消失等：会话已不可用，应关闭后由上层提示重连。
        /// SystemError（中文常为“未知系统错误”）在拔线写失败时很常见。
        /// </summary>
        internal static bool IsLinkLost(NativeVisaException ex) =>
            ex.ErrorCode == NativeErrorCode.SystemError
            || ex.ErrorCode == NativeErrorCode.ConnectionLost
            || ex.ErrorCode == NativeErrorCode.IOError
            || ex.ErrorCode == NativeErrorCode.BusError
            || ex.ErrorCode == NativeErrorCode.ResourceNotFound
            || ex.ErrorCode == NativeErrorCode.InvalidObject
            || ex.ErrorCode == NativeErrorCode.InterfaceNumberNotConfigured
            || ex.ErrorCode == NativeErrorCode.MachineNotAvailable;
#endif
    }
}
