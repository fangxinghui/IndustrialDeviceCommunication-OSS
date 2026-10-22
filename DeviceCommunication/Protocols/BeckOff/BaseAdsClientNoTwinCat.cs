using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;
using DeviceCommunication.Models.BeckoffConfig;
using TwinCAT.Ads;
using TwinCAT.Ads.Configuration;
using TwinCAT.Ads.TcpRouter;

namespace DeviceCommunication.Protocols.BeckOff
{
    internal class BaseAdsClientNoTwinCat:BaseAdsClient
    {
        public override bool Initialize<T>(T parameter)
        {
            if(parameter is BaseConfiguration config)
            {
                if (string.IsNullOrEmpty(config.LocalAddress) ||
                    !IPAddress.TryParse(config.LocalAddress, out var localAddress))
                {
                    WriteLog(this,new ExecuteLogEvent{LogMessage="LocalAddress is null or invalid.",ClassName = nameof(BaseAdsClientNoTwinCat),MethodName = nameof(Initialize)});
                    return false;
                }
                if(string.IsNullOrEmpty(config.RemoteAddress)||!IPAddress.TryParse(config.RemoteAddress, out var remoteAddress))
                {
                    WriteLog(this,new ExecuteLogEvent{LogMessage="RemoteAddress is null or invalid.",ClassName = nameof(BaseAdsClientNoTwinCat),MethodName = nameof(Initialize)});
                    return false;
                }

                if (string.IsNullOrEmpty(config.RouterName))
                {
                    WriteLog(this,new ExecuteLogEvent{LogMessage="RouterName is null or empty.",ClassName = nameof(BaseAdsClientNoTwinCat),MethodName = nameof(Initialize)});
                    return false;
                }

                if (!NetIdMatch(config.LocalNetId!))
                {
                    WriteLog(this,new ExecuteLogEvent{LogMessage="LocalNetId is null or invalid.",ClassName = nameof(BaseAdsClientNoTwinCat),MethodName = nameof(Initialize)});
                    return false;
                }

                if (!NetIdMatch(config.RemoteNetId!))
                {
                    WriteLog(this,new ExecuteLogEvent{LogMessage="RemoteNetId is null or invalid.",ClassName = nameof(BaseAdsClientNoTwinCat),MethodName = nameof(Initialize)});
                    return false;
                }

                try
                {
                    AmsNetId localNetId = new AmsNetId(config.LocalNetId!);  //PC配置
                    AmsNetId remoteNetId = new AmsNetId(config.RemoteNetId!); //PLC配置
                    var router = new AmsTcpIpRouter(localNetId);
                    bool result=router.RemoveRoute(config.RouterName);
                    if (result)
                    {
                        WriteLog(this,new ExecuteLogEvent{LogMessage=$"Route {config.RouterName} removed successfully.",ClassName = nameof(BaseAdsClientNoTwinCat),MethodName = nameof(Initialize)});
                    }

                    result = router.TryAddRoute(
                        new Route(config.RouterName, remoteNetId, new IPAddress[] { remoteAddress }));
                    StartAsync(router, new CancellationTokenSource()).Wait();
                    if (router.IsRunning)
                    {
                        WriteLog(this,new ExecuteLogEvent{LogMessage=$"Route {config.RouterName} added and router started successfully.",ClassName = nameof(BaseAdsClientNoTwinCat),MethodName = nameof(Initialize)});
                    }

                    return true;
                }
                catch (Exception ex)
                {
                   WriteException(this,new ExceptionEvent{ErrorMessage= ex.Message,ClassName = nameof(BaseAdsClientNoTwinCat),MethodName = nameof(Initialize)});
                    return false;
                }
                
            }
            WriteLog(this,new ExecuteLogEvent{LogMessage="Parameter is not of type BaseConfiguration.",ClassName = nameof(BaseAdsClientNoTwinCat),MethodName = nameof(Initialize)});
            return false;
        }

        private async Task StartAsync(AmsTcpIpRouter router,CancellationTokenSource cts)
        {
            await router.StartAsync(cts.Token);
        }
    }
}