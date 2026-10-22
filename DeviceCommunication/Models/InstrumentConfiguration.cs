using System.ComponentModel;
using System.Data;
using CoreShared;
using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Models
{
    public class InstrumentConfiguration:IConfiguration
    {
        /// <summary>
        /// 图标
        /// </summary>
        public string? Icon { get; set; }
        public string? EnglishTypeName { get; set; }
        public string? ChineseTypeName{ get; set; }
        public string? Description { get; set; }
        public CommonType CommonType { get; set; }
        public string? VisaResourceString { get; set; }
        public int ChannelCount { get; set; } = 3;
        public IConfiguration? CommoConfiguration { get; set; }
    }
    
}