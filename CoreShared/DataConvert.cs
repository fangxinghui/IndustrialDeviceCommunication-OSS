using System;

namespace CoreShared
{
    public class DataConvert
    {
        public enum DataType
        {
            Bool,
            Byte,
            UInt16,
            Int16,
            UInt32,
            Int32,
            Float,
            Double,
            ULong,
            Long,
            String
        }
        public enum StringEncoding
        {
            ASCII,      // ASCII编码（每个字节一个字符）
            UTF8,       // UTF-8编码
            UTF16LE,    // UTF-16小端序
            UTF16BE,    // UTF-16大端序
            GB2312      // 中文GB2312编码
        }

        /// <summary>
        /// 字符串存储方式
        /// </summary>
        public enum StringStorageMode
        {
            FixedLength,        // 固定长度（包括末尾的空字符或空格填充）
            NullTerminated,     // 以空字符('\0')结尾
            LengthPrefixed      // 前缀长度（第一个寄存器或字节存储长度）
        }
        public static Type ConvertType(DataType dataType)
        {
            Type type = null!;
            switch (dataType)
            {
                case DataType.Bool: type = typeof(bool); break;
                case DataType.Byte: type = typeof(byte); break;
                case DataType.UInt16: type = typeof(ushort); break;
                case DataType.Int16: type = typeof(short); break;
                case DataType.UInt32: type = typeof(uint); break;
                case DataType.Int32: type = typeof(int); break;
                case DataType.Float: type = typeof(float); break;
                case DataType.Double: type = typeof(double); break;
                case DataType.Long: type = typeof(long); break;
                case DataType.String: type = typeof(string); break;
            }
            return type;
        }
        
        
    }
}