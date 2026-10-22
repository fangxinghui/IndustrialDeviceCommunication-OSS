using System;
using System.Collections.Generic;
using CoreShared;
using DeviceCommunication.Attributes;
using DeviceCommunication.Models;

namespace DeviceCommunication.Protocols.IOCard;

/// <summary>
/// 北京阿尔泰 DAM-3163A：16 路 DA/AO，Modbus RTU，输出码值 0~0xFFFF。
/// DA 设置值寄存器：40792+ch（单寄存器 16 位），DA 量程寄存器：40136+ch。
/// </summary>
[ControllerDriver("ART Technology", "DAM-3163A", ControllerType.AnalogModule)]
[ControllerDriver("Altai", "DAM-3163A", ControllerType.AnalogModule)]
public class AltaiDam3163A : ModbusAnalogOutputBase
{
    private const double FullScaleRaw = 0xFFFF;
    private const int ModeRegisterStart = 0x0110; // 40273，第 1 路 DA 量程寄存器偏移

    protected override int ChannelCount => 16;
    protected override int StartRegister => 0x0317;
    protected override int ChannelRegisterStride => 1;

    // 缓存每路已设置过的量程值，避免重复写量程寄存器
    private readonly Dictionary<int, ushort> _configuredRange = new();

    public AltaiDam3163A(ControllerConfiguration controllerConfiguration) : base(controllerConfiguration)
    {
    }

    public override bool WriteAnalogOutputRaw(ControllerAddress channel, ushort rawValue)
    {
        if (rawValue > FullScaleRaw)
        {
            WriteLog(nameof(WriteAnalogOutputRaw), $"Raw value {rawValue} exceeds DAM-3163A full-scale 0xFFFF.");
            return false;
        }

        if (!TryResolveChannelIndex(channel, out var channelIndex))
        {
            WriteLog(nameof(WriteAnalogOutputRaw), "Invalid analog output channel.");
            return false;
        }

        // 只第一次写量程寄存器，后续跳过（DAM-3163 量程写入后持久化）
        var options = ResolveChannelOptions(channel);
        var modeValue = GetModeRegisterValue(options.OutputRange);
        if (modeValue.HasValue &&
            (!_configuredRange.TryGetValue(channelIndex, out var prevMode) || prevMode != modeValue.Value))
        {
            var modeAddr = BuildHoldingRegisterAddress(ModeRegisterStart + channelIndex);
            Controller!.WriteValue(new ControllerAddress { Address = modeAddr, DataType = DataConvert.DataType.UInt16 }, modeValue.Value);
            _configuredRange[channelIndex] = modeValue.Value;
        }

        // DAM-3163 手册：40792+channelIndex 为单寄存器 16 位 DA 设置值
        var setValueRegister = GetChannelRegisterIndex(channelIndex);
        var setValueAddr = new ControllerAddress { Address = BuildHoldingRegisterAddress(setValueRegister), DataType = DataConvert.DataType.UInt16 };
        return Controller!.WriteValue(setValueAddr, rawValue);
    }

    private static ushort? GetModeRegisterValue(string? outputRange)
    {
        return NormalizeRange(outputRange) switch
        {
            "-5-5V" or "-5~5V" or "+/-5V" or "±5V" => 0x0008,
            "-10-10V" or "-10~10V" or "+/-10V" or "±10V" => 0x0009,
            "0-20MA" or "0~20MA" => 0x000B,
            "4-20MA" or "4~20MA" => 0x000C,
            "0-5V" or "0~5V" => 0x000D,
            "0-10V" or "0~10V" => 0x000E,
            _ => null // 未知量程不写模式寄存器，保留设备当前设置
        };
    }

    protected override double ConvertSignalToRaw(double signalValue, AnalogOutputChannelOptions options)
    {
        var outputRange = NormalizeRange(options.OutputRange);
        var (min, max) = outputRange switch
        {
            "-5-5V" => (-5.0, 5.0),
            "-5~5V" => (-5.0, 5.0),
            "+/-5V" => (-5.0, 5.0),
            "±5V" => (-5.0, 5.0),
            "-10-10V" => (-10.0, 10.0),
            "-10~10V" => (-10.0, 10.0),
            "+/-10V" => (-10.0, 10.0),
            "±10V" => (-10.0, 10.0),
            "0-5V" => (0.0, 5.0),
            "0~5V" => (0.0, 5.0),
            "0-10V" => (0.0, 10.0),
            "0~10V" => (0.0, 10.0),
            "0-20MA" => (0.0, 20.0),
            "0~20MA" => (0.0, 20.0),
            "4-20MA" => (4.0, 20.0),
            "4~20MA" => (4.0, 20.0),
            _ => (0.0, 10.0)
        };

        if (signalValue < min || signalValue > max)
            return -1;

        return (signalValue - min) / (max - min) * FullScaleRaw;
    }

    private static string NormalizeRange(string? range)
    {
        return string.IsNullOrWhiteSpace(range)
            ? "0-10V"
            : range.Trim().Replace(" ", string.Empty).ToUpperInvariant();
    }
}
