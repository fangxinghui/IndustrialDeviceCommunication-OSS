using CoreShared;

namespace DeviceCommunication.Abstractions
{
    /// <summary>
    /// DAQ 连续采样环形缓冲能力。实现后可供 SnapshotAiBurst 等通用步骤导出同步窗口。
    /// </summary>
    public interface IAnalogBurstSource
    {
        bool IsBurstAvailable { get; }

        double SampleRateHz { get; }

        int BurstChannelCount { get; }

        /// <summary>
        /// 拷贝环形缓冲中最近的样本。返回数组维度为 [sampleIndex, channelIndex]。
        /// USB3145A 单位为安培（A）。
        /// </summary>
        bool TryCopyRecentSamples(out double[,] samplesByIndexThenChannel, out int sampleCount);

        /// <summary>将地址解析为缓冲通道下标（0 基）。</summary>
        bool TryResolveBurstChannel(ControllerAddress address, out int channelIndex);
    }
}
