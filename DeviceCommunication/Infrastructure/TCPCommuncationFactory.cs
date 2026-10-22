using System;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;
using HPSocket.Tcp;

namespace DeviceCommunication.Infrastructure
{
    public class TcpFactory
    {
        public static ITcp TryCreateTcp(NetConfiguration configuration, TcpType type)
        {
            if (configuration is null)
                throw new ArgumentNullException(nameof(configuration), "TCP server configuration cannot be null.");
            if (type == TcpType.Client)
            {
                Protocols.Tcp.TcpClient tcpClient = new Protocols.Tcp.TcpClient(configuration);
                tcpClient.Initialize();
                return tcpClient;
            }
            else
            {
                Protocols.Tcp.TcpServer tcpServer = new Protocols.Tcp.TcpServer(configuration);
                tcpServer.Initialize();
                return tcpServer;
            }
        }
    }
}