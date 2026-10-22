using System;
using System.Collections.Generic;

namespace DeviceCommunication.Models;

public class AnalogInputReading
{
    public ushort RawValue { get; set; }
    public double SignalValue { get; set; }
    public string? SignalUnit { get; set; }

    public double Value => SignalValue;
}

public class AnalogInputChannelOptions
{
    public string? InputRange { get; set; }
    public IReadOnlyDictionary<string, string> Parameters { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public class AnalogOutputChannelOptions
{
    public string? OutputRange { get; set; }
    public IReadOnlyDictionary<string, string> Parameters { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
