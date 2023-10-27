// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Nethermind.Core.Buffers;
using Nethermind.Logging;
using Nethermind.Network.Rlpx;
using Nethermind.Serialization.Rlp;
using NSubstitute;

namespace Nethermind.Network.Test.Rlpx.TestWrappers
{
    internal class ZeroPacketSplitterTestWrapper : ZeroPacketSplitter
    {
        private readonly IChannelHandlerContext _context = Substitute.For<IChannelHandlerContext>();

        public IByteBuffer Encode(IByteBuffer input)
        {
            IByteBuffer result = NethPooledBufferAllocator.Instance.Buffer();
            while (input.IsReadable())
            {
                base.Encode(_context, input, result);
            }

            return result;
        }

        public ZeroPacketSplitterTestWrapper() : base(LimboLogs.Instance)
        {
            _context.Allocator.Returns(NethPooledBufferAllocator.Instance);
        }
    }
}
