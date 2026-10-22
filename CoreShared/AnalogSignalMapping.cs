using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CoreShared;

public class AnalogSignalMapping
{
    public double SignalMin { get; set; }
    public double SignalMax { get; set; }
    public string? SignalUnit { get; set; }

    public double PhysicalMin { get; set; }
    public double PhysicalMax { get; set; }
    public string? PhysicalUnit { get; set; }

    public double Map(double signalValue)
    {
        var signalSpan = SignalMax - SignalMin;
        if (Math.Abs(signalSpan) < double.Epsilon)
            throw new InvalidOperationException("Signal range cannot be zero.");

        var result = (signalValue - SignalMin) / signalSpan * (PhysicalMax - PhysicalMin) + PhysicalMin;

        // 信号超出传感器范围时，输出不超出物理量程
        var lo = Math.Min(PhysicalMin, PhysicalMax);
        var hi = Math.Max(PhysicalMin, PhysicalMax);
        return Math.Clamp(result, lo, hi);
    }

    public double MapPhysicalToSignal(double physicalValue)
    {
        var physicalSpan = PhysicalMax - PhysicalMin;
        if (Math.Abs(physicalSpan) < double.Epsilon)
            throw new InvalidOperationException("Physical range cannot be zero.");

        return (physicalValue - PhysicalMin) / physicalSpan * (SignalMax - SignalMin) + SignalMin;
    }
}

public static class AnalogSignalMappingParser
{
    public static bool TryParse(string? sensorSignal, string? physical, out AnalogSignalMapping mapping)
    {
        mapping = new AnalogSignalMapping();
        if (!TryParseRange(sensorSignal, out var signalMin, out var signalMax, out var signalUnit) ||
            !TryParseRange(physical, out var physicalMin, out var physicalMax, out var physicalUnit))
        {
            return false;
        }

        mapping.SignalMin = signalMin;
        mapping.SignalMax = signalMax;
        mapping.SignalUnit = signalUnit;
        mapping.PhysicalMin = physicalMin;
        mapping.PhysicalMax = physicalMax;
        mapping.PhysicalUnit = physicalUnit;
        return true;
    }

    public static bool TryParseRange(string? text, out double min, out double max, out string? unit)
    {
        min = 0;
        max = 0;
        unit = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = Regex.Match(text.Trim(),
            @"^\s*(?<min>[-+]?\d+(?:\.\d+)?)\s*(?:-|~|～|to)\s*(?<max>[-+]?\d+(?:\.\d+)?)\s*(?<unit>[^\d\s].*)?\s*$",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;

        if (!double.TryParse(match.Groups["min"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out min) ||
            !double.TryParse(match.Groups["max"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out max))
        {
            return false;
        }

        unit = string.IsNullOrWhiteSpace(match.Groups["unit"].Value)
            ? null
            : match.Groups["unit"].Value.Trim();
        return true;
    }
}
