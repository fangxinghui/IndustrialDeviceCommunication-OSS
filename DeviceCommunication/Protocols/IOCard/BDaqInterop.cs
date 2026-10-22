using System;
using System.IO;
using System.Reflection;

namespace DeviceCommunication.Protocols.IOCard
{
    /// <summary>
    /// 通过反射包装 Automation.BDaq 托管 API，避免编译时依赖。
    /// 运行时优先从启动目录加载 Automation.BDaq.dll。
    /// </summary>
    internal class BDaqInterop : IDisposable
    {
        private Assembly _asm = null!;
        private object _diCtrl = null!;
        private object _doCtrl = null!;

        private Type _tDeviceInfo = null!;
        private Type _tDiCtrl = null!;
        private Type _tDoCtrl = null!;

        private MethodInfo _mDiRead = null!;
        private MethodInfo _mDoWrite = null!;
        private MethodInfo _mDoRead = null!;

        /// <summary>设备是否已成功初始化</summary>
        public bool Initialized { get; private set; }

        /// <summary>
        /// 加载 Automation.BDaq 并创建设备实例。
        /// </summary>
        /// <param name="deviceName">设备名称，如 "PCIE-1756,BID#0"</param>
        /// <param name="assemblyPath">Automation.BDaq.dll 路径；为 null 时从 AppBase / 程序集解析</param>
        public bool Load(string deviceName, string? assemblyPath = null)
        {
            try
            {
                _asm = LoadBDaqAssembly(assemblyPath);
                _tDeviceInfo = _asm.GetType("Automation.BDaq.DeviceInformation")!;
                _tDiCtrl = _asm.GetType("Automation.BDaq.InstantDiCtrl")!;
                _tDoCtrl = _asm.GetType("Automation.BDaq.InstantDoCtrl")!;

                // 创建 DI 控制器
                _diCtrl = Activator.CreateInstance(_tDiCtrl)!;
                var propDevice = _tDiCtrl.GetProperty("SelectedDevice")!;
                var devInfo = Activator.CreateInstance(_tDeviceInfo, deviceName);
                propDevice.SetValue(_diCtrl, devInfo);

                var propInit = _tDiCtrl.GetProperty("Initialized")!;
                if (!(bool)propInit.GetValue(_diCtrl)!)
                {
                    Initialized = false;
                    return false;
                }

                // 创建 DO 控制器
                _doCtrl = Activator.CreateInstance(_tDoCtrl)!;
                propDevice = _tDoCtrl.GetProperty("SelectedDevice")!;
                propDevice.SetValue(_doCtrl, devInfo);

                propInit = _tDoCtrl.GetProperty("Initialized")!;
                if (!(bool)propInit.GetValue(_doCtrl)!)
                {
                    Initialized = false;
                    return false;
                }

                // 缓存方法
                _mDiRead = _tDiCtrl.GetMethod("Read", new[] { typeof(Int32), typeof(Byte).MakeByRefType() })!;
                _mDoWrite = _tDoCtrl.GetMethod("Write", new[] { typeof(Int32), typeof(Byte) })!;
                _mDoRead = _tDoCtrl.GetMethod("Read", new[] { typeof(Int32), typeof(Byte).MakeByRefType() })!;

                Initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BDaqInterop] Load failed: {ex.Message}");
                Initialized = false;
                return false;
            }
        }

        /// <summary>
        /// .NET Core+ 没有 GAC；Assembly.Load(简单名) 只探测启动项目输出目录，
        /// 不会去 DeviceCommunication 自己的 bin。优先 LoadFrom 明确路径。
        /// </summary>
        private static Assembly LoadBDaqAssembly(string? assemblyPath)
        {
            if (!string.IsNullOrWhiteSpace(assemblyPath))
                return Assembly.LoadFrom(assemblyPath);

            var candidate = Path.Combine(AppContext.BaseDirectory, "Automation.BDaq.dll");
            if (File.Exists(candidate))
                return Assembly.LoadFrom(candidate);

            return Assembly.Load("Automation.BDaq");
        }

        /// <summary>读取指定端口的 DI 值（8位）</summary>
        public byte ReadPort(int port)
        {
            if (!Initialized) throw new InvalidOperationException("BDaqInterop not initialized.");
            var args = new object[] { port, (byte)0 };
            _mDiRead.Invoke(_diCtrl, args);
            return (byte)args[1];
        }

        /// <summary>写入指定端口的 DO 值（8位）</summary>
        public void WritePort(int port, byte value)
        {
            if (!Initialized) throw new InvalidOperationException("BDaqInterop not initialized.");
            _mDoWrite.Invoke(_doCtrl, new object[] { port, value });
        }

        /// <summary>读取指定端口的 DO 当前输出值（8位）</summary>
        public byte ReadOutputPort(int port)
        {
            if (!Initialized) throw new InvalidOperationException("BDaqInterop not initialized.");
            var args = new object[] { port, (byte)0 };
            _mDoRead.Invoke(_doCtrl, args);
            return (byte)args[1];
        }

        public void Dispose()
        {
            var disp = _diCtrl as IDisposable;
            disp?.Dispose();
            disp = _doCtrl as IDisposable;
            disp?.Dispose();
        }
    }
}
