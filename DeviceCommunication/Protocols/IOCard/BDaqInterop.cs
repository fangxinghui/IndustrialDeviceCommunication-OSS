using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace DeviceCommunication.Protocols.IOCard
{
    /// <summary>
    /// Runtime reflection over Automation.BDaq InstantDiCtrl and InstantDoCtrl.
    /// The Advantech assembly is loaded by path and is not a compile-time reference.
    /// </summary>
    internal sealed class BDaqInterop : IDisposable
    {
        private object? _inputs;
        private object? _outputs;
        private MethodInfo? _readInputs;
        private MethodInfo? _readOutputs;
        private MethodInfo? _writeOutputs;

        public bool Initialized { get; private set; }

        public bool Load(string deviceName, string? assemblyPath = null)
        {
            try
            {
                var assembly = OpenAssembly(assemblyPath);
                var descriptionType = assembly.GetType("Automation.BDaq.DeviceInformation")!;
                var inputType = assembly.GetType("Automation.BDaq.InstantDiCtrl")!;
                var outputType = assembly.GetType("Automation.BDaq.InstantDoCtrl")!;
                var description = Activator.CreateInstance(descriptionType, deviceName);

                _inputs = Activator.CreateInstance(inputType)!;
                inputType.GetProperty("SelectedDevice")!.SetValue(_inputs, description);
                if (inputType.GetProperty("Initialized")!.GetValue(_inputs) is not true)
                {
                    Initialized = false;
                    return false;
                }

                _outputs = Activator.CreateInstance(outputType)!;
                outputType.GetProperty("SelectedDevice")!.SetValue(_outputs, description);
                if (outputType.GetProperty("Initialized")!.GetValue(_outputs) is not true)
                {
                    Initialized = false;
                    return false;
                }

                _readInputs = inputType.GetMethod("Read", new[] { typeof(int), typeof(byte).MakeByRefType() })!;
                _writeOutputs = outputType.GetMethod("Write", new[] { typeof(int), typeof(byte) })!;
                _readOutputs = outputType.GetMethod("Read", new[] { typeof(int), typeof(byte).MakeByRefType() })!;
                Initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BDaqInterop] Load failed: {ex.Message}");
                Initialized = false;
                return false;
            }
        }

        public byte ReadPort(int port)
        {
            if (!Initialized)
                throw new InvalidOperationException("BDaqInterop not initialized.");

            var args = new object[] { port, (byte)0 };
            _readInputs!.Invoke(_inputs, args);
            return (byte)args[1];
        }

        public byte ReadOutputPort(int port)
        {
            if (!Initialized)
                throw new InvalidOperationException("BDaqInterop not initialized.");

            var args = new object[] { port, (byte)0 };
            _readOutputs!.Invoke(_outputs, args);
            return (byte)args[1];
        }

        public void WritePort(int port, byte value)
        {
            if (!Initialized)
                throw new InvalidOperationException("BDaqInterop not initialized.");

            _writeOutputs!.Invoke(_outputs, new object[] { port, value });
        }

        public void Dispose()
        {
            (_inputs as IDisposable)?.Dispose();
            (_outputs as IDisposable)?.Dispose();
        }

        private static Assembly OpenAssembly(string? assemblyPath)
        {
            if (!string.IsNullOrWhiteSpace(assemblyPath))
                return Assembly.LoadFrom(assemblyPath);

            var besideApp = Path.Combine(AppContext.BaseDirectory, "Automation.BDaq.dll");
            if (File.Exists(besideApp))
                return Assembly.LoadFrom(besideApp);

            return Assembly.Load("Automation.BDaq");
        }
    }
}
