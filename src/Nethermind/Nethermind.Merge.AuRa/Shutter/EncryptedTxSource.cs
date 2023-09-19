// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nethermind.Consensus.Transactions;
using Nethermind.Core;

namespace Nethermind.Merge.AuRa.Shutter;

public class EncryptedTxSource : ITxSource
{
    public IEnumerable<Transaction> GetTransactions(BlockHeader parent, long gasLimit)
    {
        return Enumerable.Empty<Transaction>();
    }
}
