using System;

namespace CoreShared
{
    /// <summary>
    /// 控制器信号值归一化 — 兼容 bool 与数值型（short 等）启动/停止信号
    /// </summary>
    public static class SignalValueHelper
    {
        public static bool IsSignalDataType(DataConvert.DataType dataType) =>
            dataType is DataConvert.DataType.Bool
                or DataConvert.DataType.Int16
                or DataConvert.DataType.UInt16
                or DataConvert.DataType.Byte
                or DataConvert.DataType.Int32
                or DataConvert.DataType.UInt32;

        /// <summary>
        /// 将信号值归一化为激活状态：bool true 或数值非零视为激活。
        /// </summary>
        public static bool IsActive(object? value) => value switch
        {
            bool b => b,
            null => false,
            _ => TryConvertToInt64(value, out var n) && n != 0,
        };

        public static bool CanRaiseSignal(ControllerAddress addr, object? value) =>
            value is not null && IsSignalDataType(addr.DataType);

        public static bool HasValueChanged(object? oldValue, object? newValue) =>
            !Equals(Normalize(oldValue), Normalize(newValue));

        private static object? Normalize(object? value)
        {
            if (value is bool or null)
                return value;

            return TryConvertToInt64(value, out var n) ? n : value;
        }

        private static bool TryConvertToInt64(object value, out long result)
        {
            try
            {
                result = Convert.ToInt64(value);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }
    }
}
