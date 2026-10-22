using DeviceCommunication.Abstractions;
using CoreShared;
using DeviceCommunication.Models.BeckoffConfig;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CoreShared.Events;
using TwinCAT.Ads;
using TwinCAT.Ads.SumCommand;

namespace DeviceCommunication.Protocols.BeckOff
{
    internal class BaseAdsClient:IController
    {
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;
        protected AdsClient? _adsClient { get; set; }
        protected BaseTwincatConfiguration? _baseTwincatConfig { get; set; }
        public bool IsConnected => _adsClient?.IsConnected??false;

        public IConfiguration? CreateConfiguration() => _baseTwincatConfig;

        protected void WriteLog(object sender,ExecuteLogEvent e)
        {
            OnExecuteLog?.Invoke(sender, e);
        }
        protected void WriteException(object sender,ExceptionEvent e)
        {
            OnException?.Invoke(sender, e);
        }
        public virtual bool Initialize<T>(T parameter)
        {
            if (parameter is BaseTwincatConfiguration netConfig)
            {
                if (string.IsNullOrEmpty(netConfig.RemoteNetId))
                {
                    WriteLog(this, new ExecuteLogEvent { LogMessage = "RemoteNetId is null or empty.", ClassName = nameof(BaseAdsClient), MethodName = nameof(Initialize) });
                    return false;
                }
                _baseTwincatConfig = netConfig;
                _adsClient = new AdsClient();
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ADS Client initialized successfully.", ClassName = nameof(BaseAdsClient), MethodName = nameof(Initialize) });
                return true;
            }
            return false;
        }

        public bool Connect(int timeout=5000)
        {
            try
            {
                if (_adsClient != null)
                    _adsClient = new AdsClient();
                AmsAddress amsAddress = new AmsAddress(_baseTwincatConfig?.RemoteNetId!, _baseTwincatConfig?.Port??851);
                _adsClient!.Timeout = timeout;
                _adsClient?.Connect(amsAddress);
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ADS Client connected successfully.", ClassName = nameof(BaseAdsClient), MethodName = nameof(Connect) });
                return true;
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(BaseAdsClient), MethodName = nameof(Connect) });
                return false;
            }
        }

        public void Disconnect()
        {
            _adsClient?.Disconnect();
            WriteLog(this, new ExecuteLogEvent { LogMessage = "ADS Client disconnected.", ClassName = nameof(BaseAdsClient), MethodName = nameof(Disconnect) });
        }

        public (bool success,object value) ReadValue(ControllerAddress? address)
        {
            if (address is null)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ControllerAddress is null.", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValue) });
                return(false,null!);
            }
            if(_adsClient is null || !IsConnected)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ADS Client is not connected.", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValue) });
                return (false, null!);
            }
            if(string.IsNullOrEmpty(address.Address))
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "Address is null or empty.", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValue) });
                return (false, null!);
            }
            if (!IsConnected)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ADS Client is not connected.", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValue) });
                return (false, null!);
            }
            try
            {
                var handle = _adsClient?.CreateVariableHandle(address.Address);
                var value = _adsClient?.ReadAny(handle ?? 0, typeof(object));
                WriteLog(this, new ExecuteLogEvent { LogMessage = $"Read value: {value} from address: {address.Address}", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValue) });
                return (true, value!);
            }
            catch (Exception e)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = e.Message, ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValue) });
                return (false, null!);
            }
        }

        public bool WriteValue<T>(ControllerAddress? address, T value)
        {
            if (_adsClient is null)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ADS Client is not initialized.", ClassName = nameof(BaseAdsClient), MethodName = nameof(WriteValue) });
                return false;
            }

            if (address is null)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ControllerAddress is null.", ClassName = nameof(BaseAdsClient), MethodName = nameof(WriteValue) });
                return false;
            }
            if (!IsConnected)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ADS Client is not connected.", ClassName = nameof(BaseAdsClient), MethodName = nameof(WriteValue) });
                return false;
            }
            
            try
            {
                var handle = _adsClient?.CreateVariableHandle(address.Address!);
                _adsClient?.WriteAny(handle ?? 0, value!);
                return true;
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(BaseAdsClient), MethodName = nameof(WriteValue) });
                return false;
            }
        }

        public (bool success,object[]values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
        {
            if (_adsClient is null)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ADS Client is not initialized.", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValues) });
                return (false,null!);
            }

            if (!IsConnected)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ADS Client is not connected.", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValues) });
                return (false, null!);
            }

            if (addresses is null || addresses.Length == 0)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ControllerAddress array is null or empty.", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValues) });
                return (false, null!);
            }

            if (addresses.Length != length)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "The length of addresses does not match the specified length.", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValues) });
                return (false, null!);
            }
            try
            {
                List<string> tags = new List<string>();
                Dictionary<uint,Type> types = new Dictionary<uint,Type>();
                uint index = 0;
                foreach (var address in addresses)
                {
                    if(string.IsNullOrEmpty(address.Address))
                        continue;
                    tags.Add(address.Address);
                    types.Add(index,CoreShared.DataConvert.ConvertType(address.DataType));
                    index++;
                }

                SumCreateHandles createHandles = new SumCreateHandles(_adsClient, tags);
                var handles = createHandles?.CreateHandles();
                if (handles is null)
                {
                    WriteLog(this, new ExecuteLogEvent { LogMessage = "Failed to create variable handles.", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValues) });
                    return (false, null!);
                }
                SumHandleReadAnyType sumHandleReadAnyType=new SumHandleReadAnyType(_adsClient,types);
                object values=sumHandleReadAnyType.Read();
                WriteLog(this, new ExecuteLogEvent { LogMessage = $"Read values successful.", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValues) });
                //释放句柄
                foreach (var handle in handles.Handles!)
                {
                    _adsClient.DeleteVariableHandle(handle);
                }
                WriteLog(this, new ExecuteLogEvent { LogMessage = "Variable handles released.", ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValues) });
                return (true, (object[])values);
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(BaseAdsClient), MethodName = nameof(ReadValues) });
                return (false, null!);
            }
        }

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values)
        {
            if (_adsClient is null)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ADS Client is not initialized.", ClassName = nameof(BaseAdsClient), MethodName = nameof(WriteValues) });
                return false;
            }

            if (addresses is null || values is null)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ControllerAddress array or values array is null.", ClassName = nameof(BaseAdsClient), MethodName = nameof(WriteValues) });
                return false;
            }

            if (addresses.Length == 0 || values.Length == 0)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "ControllerAddress array or values array is empty.", ClassName = nameof(BaseAdsClient), MethodName = nameof(WriteValues) });
                return false;
            }
            if(addresses.Length!=values.Length)
            {
                WriteLog(this, new ExecuteLogEvent { LogMessage = "The length of addresses does not match the length of values.", ClassName = nameof(BaseAdsClient), MethodName = nameof(WriteValues) });
                return false;
            }
            
            try
            {
                List<string> tags = new List<string>();
                Dictionary<uint, Type> types = new Dictionary<uint, Type>();
                List<object>writevalues=new List<object>();
                for (uint i = 0; i < addresses.Length; i++)
                {
                    if (addresses[i] is null || string.IsNullOrEmpty(addresses[i].Address) || values[i] is null)
                        continue;
                    tags.Add(addresses[i].Address!);
                    types.Add(i, CoreShared.DataConvert.ConvertType(addresses[i].DataType));
                    writevalues.Add(values[i]!);
                }
                SumCreateHandles createHandles = new SumCreateHandles(_adsClient, tags);
                var handles = createHandles?.CreateHandles();
                if (handles is null)
                {
                    WriteLog(this, new ExecuteLogEvent { LogMessage = "Failed to create variable handles.", ClassName = nameof(BaseAdsClient), MethodName = nameof(WriteValues) });
                    return false;
                }
                SumHandleWriteAnyType sumHandleWriteAnyType = new SumHandleWriteAnyType(_adsClient,types);
                sumHandleWriteAnyType.Write(writevalues.ToArray());
                    WriteLog(this, new ExecuteLogEvent { LogMessage = $"Write values successful.", ClassName = nameof(BaseAdsClient), MethodName = nameof(WriteValues) });
                //释放句柄
                foreach (var handle in handles.Handles!)
                {
                    _adsClient.DeleteVariableHandle(handle);
                }
                return true;
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(BaseAdsClient), MethodName = nameof(WriteValues) });
                return false;
            }
        }

        public static bool NetIdMatch(string netid)
        {
            if (string.IsNullOrEmpty(netid))
                return false;
            Regex _amsNetIdRegex = new Regex(
                @"^(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){5}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            );
            return _amsNetIdRegex.IsMatch(netid.Trim());
        }
    }
}
