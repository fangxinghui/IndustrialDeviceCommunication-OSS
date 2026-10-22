using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;
using HPSocket;
using HPSocket.Sdk;
using HPSocket.Tcp;
using ITcpServer = DeviceCommunication.Abstractions.ITcpServer;

namespace DeviceCommunication.Protocols.Tcp
{
    public struct ClientInfo
    {
        public int Port { get; set; }
        public IntPtr IntPtr { get; set; }
    }
    internal class TcpServer:ITcp,ITcpServer
    { 
        public Action<byte[],object>? OnDataReceived { get; set; }
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;
        protected HPSocket.Tcp.TcpServer _tcpServer;
        protected Dictionary<String, ClientInfo> _clients;
        /// <summary>
        /// lan ip
        /// </summary>
        public string? IpAddress { get; set; }
        /// <summary>
        /// server port
        /// </summary>
        public int Port { get; set; }
        public bool IsOpen=>ServerState;
        private bool ServerState { get; set; }
        public NetConfiguration? Configuration { get; set; }
        public TcpServer(NetConfiguration configuration)
        {
            Configuration = configuration;
        }
        public bool Initialize()
        {
            if (Configuration!=null)
            {
                this.IpAddress = Configuration.IpAddress!;
                this.Port = Configuration.Port;
                _tcpServer = new HPSocket.Tcp.TcpServer();
                _tcpServer.Address = this.IpAddress;
                _tcpServer.Port = (ushort)this.Port;
                _tcpServer.OnAccept += OnAccept;
                _tcpServer.OnClose += OnClose;
                _tcpServer.OnReceive+= OnReceive;
                _tcpServer.OnShutdown += OnShutdown;
                _clients=new Dictionary<String, ClientInfo>();
            }
            return false;
        }
        private HandleResult OnShutdown(IServer server)
        {
            _clients?.Clear();
            return HandleResult.Ok;
        }
        private HandleResult OnReceive(HPSocket.IServer sender, IntPtr connId, byte[] data)
        {
            try
            {
                if(_tcpServer.GetRemoteAddress(connId, out string ip, out ushort port))
                {
                    OnDataReceived?.Invoke(data,ip);
                }
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,new ExceptionEvent(){ErrorMessage = e.Message,ClassName = nameof(TcpServer),MethodName = nameof(OnReceive)});
            }
            return HandleResult.Ok;
        }
        private HandleResult OnClose(IServer server,IntPtr connId,SocketOperation enOperation, int errorCode)
        {
            return HandleResult.Ok;
        }
        private HandleResult OnAccept(IServer server,IntPtr connId,IntPtr client)
        {
            try
            {
                if (_tcpServer?.GetRemoteAddress(connId, out string ip, out ushort port) ?? false)
                {
                    if (_clients.TryAdd(ip,new ClientInfo(){IntPtr = client,Port = port}))
                    {
                        OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(TcpServer),MethodName = nameof(OnAccept),LogMessage = $"Client connected: {ip}:{port}"});
                    }
                }
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,new ExceptionEvent(){ErrorMessage = e.Message,ClassName = nameof(TcpServer),MethodName = nameof(OnAccept)});
            }
            return HandleResult.Ok;
        }
        /// <summary>
        /// 打开服务器，开始监听连接
        /// </summary>
        public bool Open()
        {
            try
            {
                OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(TcpServer),MethodName = nameof(Open),LogMessage = $"TCP Server started at {IpAddress}:{Port}"});
                ServerState=_tcpServer?.Start()??false;
                return ServerState;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,new ExceptionEvent(){ErrorMessage = e.Message,ClassName = nameof(TcpServer),MethodName = nameof(Open)});
                return false;
            }
        }
        /// <summary>
        /// 关闭服务器，断开所有连接
        /// </summary>
        public void Close()
        {
            try
            {
                _tcpServer?.Stop();
                ServerState=false;
                OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(TcpServer),MethodName = nameof(Close),LogMessage = $"TCP Server stopped at {IpAddress}:{Port}"});
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,new ExceptionEvent(){ErrorMessage = e.Message,ClassName = nameof(TcpServer),MethodName = nameof(Close)});
            }
        }
        public bool SendBytes(byte[] data)
        {
            try
            {
                foreach (var item in _clients)
                {
                    if(item.Value.IntPtr==IntPtr.Zero)
                        continue;
                    _tcpServer.Send(item.Value.IntPtr,data,data.Length);
                }
                return true;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,new ExceptionEvent(){ErrorMessage = e.Message,ClassName = nameof(TcpServer),MethodName = nameof(SendBytes)});
                return false;
            }
        }

        public bool SendAsciiString(string data)
        {
            try
            {
                byte[]buff=Encoding.ASCII.GetBytes(data);
                return SendBytes(buff);
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,new ExceptionEvent()
                {
                    ErrorMessage = e.Message,
                    ClassName = nameof(TcpServer),
                    MethodName = nameof(SendAsciiString)
                });
                return false;
            }
        }

        public Task SendBytesAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
        public Task<byte[]> ReceiveAsync(int byteCount, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
        public Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public byte[] Receive(int byteCount)
        {
            return null;
        }
        public void Dispose()
        {
            _tcpServer?.Stop();
            _tcpServer = null;
        }

        public Task<byte[]> SendAndReceiveAsync(byte[] data, int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<string> SendAndReceiveStringAsync(string data, int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<string> SendAndReceiveStringAsync(
            string data,
            Func<string, bool> isComplete,
            int timeoutMs = 5000,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}