//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
// 

using System.Collections.Generic;
using Nethermind.AccountAbstraction.Data;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Int256;

namespace Nethermind.AccountAbstraction.Executor
{
    public interface IUserOperationTxBuilder
    {
        Transaction BuildTransaction(
            long gaslimit,
            byte[] callData,
            Address sender,
            BlockHeader parent,
            IReleaseSpec specFor1559,
            UInt256 nonce,
            bool systemTransaction);

        Transaction BuildTransactionFromUserOperations(
            IEnumerable<UserOperation> userOperations,
            BlockHeader parent,
            long gasLimit,
            UInt256 nonce,
            IReleaseSpec specFor1559);

        FailedOp? DecodeEntryPointOutputError(byte[] output);
    }
}
