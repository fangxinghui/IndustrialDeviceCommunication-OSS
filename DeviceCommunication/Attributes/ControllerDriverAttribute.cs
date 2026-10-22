using System;

namespace DeviceCommunication.Attributes
{
    /// <summary>
    /// 标记控制器驱动类支持的品牌、型号与控制器类型。
    /// 一个驱动类可标注多个 Attribute，表示适用于多个型号或别名。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ControllerDriverAttribute : Attribute
    {
        /// <summary>品牌名称（如 "AMSAMOTION"）；"*" 表示任意品牌。</summary>
        public string Brand { get; }

        /// <summary>型号名称（如 "JY-MODBUS-8AI"）；"*" 表示该类型下的默认驱动。</summary>
        public string Model { get; }

        /// <summary>控制器类型。</summary>
        public ControllerType ControllerType { get; }

        /// <summary>是否为该 ControllerType 的默认驱动（型号未精确匹配时使用）。</summary>
        public bool IsDefault { get; set; }

        public ControllerDriverAttribute(string brand, string model, ControllerType controllerType)
        {
            Brand = brand ?? throw new ArgumentNullException(nameof(brand));
            Model = model ?? throw new ArgumentNullException(nameof(model));
            ControllerType = controllerType;
        }
    }
}
