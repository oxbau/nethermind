using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nethermind.Core;
using Nethermind.Core.Crypto;

namespace Nethermind.State.Snap
{
    public class PathWithAccount
    {
        public PathWithAccount() { }

        public PathWithAccount(Keccak path, Account account)
        {
            Path = path;
            Account = account;
        }

        public Keccak Path { get; set; }
        public Account Account { get; set; }
    }

    public class PathWithNode
    {
        public PathWithNode() { }

        public PathWithNode(byte[] path, byte[] node)
        {
            Path = path;
            Node = node;
        }

        public byte[] Path { get; set; }
        public byte[] Node { get; set; }
    }
}
