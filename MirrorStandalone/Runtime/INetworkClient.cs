﻿using Cysharp.Threading.Tasks;

namespace Mirror
{
    public interface INetworkClient
    {
        void Disconnect();

        void Send<T>(T message, int channelId = Channel.Reliable);

        UniTask SendAsync<T>(T message, int channelId = Channel.Reliable);
    }
}