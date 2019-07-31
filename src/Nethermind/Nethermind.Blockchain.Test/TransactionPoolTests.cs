/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Blockchain.Test.Synchronization;
using Nethermind.Blockchain.TxPools;
using Nethermind.Blockchain.TxPools.Filters;
using Nethermind.Blockchain.TxPools.Storages;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Builders;
using Nethermind.Dirichlet.Numerics;
using Nethermind.Logging;
using Nethermind.Store;
using NUnit.Framework;

namespace Nethermind.Blockchain.Test
{
    [TestFixture]
    public class TxPoolTests
    {
        private Block _genesisBlock;
        private IBlockTree _remoteBlockTree;
        private ILogManager _logManager;
        private IEthereumEcdsa _ethereumEcdsa;
        private ISpecProvider _specProvider;
        private ITxPool _txPool;
        private ITxStorage _noTxStorage;
        private ITxStorage _inMemoryTxStorage;
        private ITxStorage _persistentTxStorage;

        [SetUp]
        public void Setup()
        {
            _genesisBlock = Build.A.Block.WithNumber(0).TestObject;
            _remoteBlockTree = Build.A.BlockTree(_genesisBlock).OfChainLength(0).TestObject;
            _logManager = LimboLogs.Instance;
            _specProvider = RopstenSpecProvider.Instance;
            _ethereumEcdsa = new EthereumEcdsa(_specProvider, _logManager);
            _noTxStorage = NullTxStorage.Instance;
            _inMemoryTxStorage = new InMemoryTxStorage();
            _persistentTxStorage = new PersistentTxStorage(new MemDb(), _specProvider);
        }

        [Test]
        public void should_add_peers()
        {
            _txPool = CreatePool(_noTxStorage);
            var peers = GetPeers();

            foreach ((ISyncPeer peer, _) in peers)
            {
                _txPool.AddPeer(peer);
            }
        }

        [Test]
        public void should_delete_peers()
        {
            _txPool = CreatePool(_noTxStorage);
            var peers = GetPeers();

            foreach ((ISyncPeer peer, _) in peers)
            {
                _txPool.AddPeer(peer);
            }

            foreach ((ISyncPeer peer, _) in peers)
            {
                _txPool.RemovePeer(peer.Node.Id);
            }
        }

        [Test]
        public void should_ignore_transactions_with_different_chain_id()
        {
            _txPool = CreatePool(_noTxStorage);
            EthereumEcdsa ecdsa = new EthereumEcdsa(MainNetSpecProvider.Instance, _logManager);
            Transaction tx = Build.A.Transaction.SignedAndResolved(ecdsa, TestItem.PrivateKeyA, MainNetSpecProvider.ByzantiumBlockNumber).TestObject;
            AddTxResult result = _txPool.AddTransaction(tx, 1);
            _txPool.GetPendingTransactions().Length.Should().Be(0);
            result.Should().Be(AddTxResult.InvalidChainId);
        }
        
        [Test]
        public void should_ignore_old_scheme_signatures()
        {
            _txPool = CreatePool(_noTxStorage);
            Transaction tx = Build.A.Transaction.SignedAndResolved(_ethereumEcdsa, TestItem.PrivateKeyA, 1).TestObject;
            AddTxResult result = _txPool.AddTransaction(tx, 1);
            _txPool.GetPendingTransactions().Length.Should().Be(0);
            result.Should().Be(AddTxResult.OldScheme);
        }
        
        [Test]
        public void should_ignore_already_known()
        {
            _txPool = CreatePool(_noTxStorage);
            Transaction tx = Build.A.Transaction.SignedAndResolved(_ethereumEcdsa, TestItem.PrivateKeyA, RopstenSpecProvider.ByzantiumBlockNumber).TestObject;
            AddTxResult result1 = _txPool.AddTransaction(tx, 1);
            AddTxResult result2 = _txPool.AddTransaction(tx, 1);
            _txPool.GetPendingTransactions().Length.Should().Be(1);
            result1.Should().Be(AddTxResult.Added);
            result2.Should().Be(AddTxResult.AlreadyKnown);
        }
        
        [Test]
        public void should_add_valid_transactions()
        {
            _txPool = CreatePool(_noTxStorage);
            Transaction tx = Build.A.Transaction.SignedAndResolved(_ethereumEcdsa, TestItem.PrivateKeyA, RopstenSpecProvider.ByzantiumBlockNumber).TestObject;
            AddTxResult result = _txPool.AddTransaction(tx, 1);
            _txPool.GetPendingTransactions().Length.Should().Be(1);
            result.Should().Be(AddTxResult.Added);
        }

        [Test]
        public void should_add_pending_transactions()
        {
            _txPool = CreatePool(_noTxStorage);
            var transactions = AddTransactionsToPool();
            _txPool.GetPendingTransactions().Length.Should().Be(transactions.Length);
        }

        [Test]
        public void should_delete_pending_transactions()
        {
            _txPool = CreatePool(_noTxStorage);
            var transactions = AddTransactionsToPool();
            DeleteTransactionsFromPool(transactions);
            _txPool.GetPendingTransactions().Should().BeEmpty();
        }

        [Test]
        public void should_add_transactions_to_in_memory_storage()
        {
            var transactions = AddAndFilterTransactions(_inMemoryTxStorage);
            transactions.Pending.Count().Should().Be(transactions.Filtered.Count());
        }

        [Test]
        public void should_add_transactions_to_persistent_storage()
        {
            var transactions = AddAndFilterTransactions(_persistentTxStorage);
            transactions.Pending.Count().Should().Be(transactions.Filtered.Count());
        }

        [Test]
        public void should_add_all_transactions_to_storage_when_using_accept_all_filter()
        {
            var transactions = AddAndFilterTransactions(_inMemoryTxStorage, new AcceptAllTxFilter());
            transactions.Pending.Count().Should().Be(transactions.Filtered.Count());
        }

        [Test]
        public void should_not_add_any_transaction_to_storage_when_using_reject_all_filter()
        {
            var transactions = AddAndFilterTransactions(_inMemoryTxStorage, new RejectAllTxFilter());
            transactions.Filtered.Count().Should().Be(0);
            transactions.Pending.Count().Should().NotBe(transactions.Filtered.Count());
        }

        [Test]
        public void should_not_add_any_transaction_to_storage_when_using_accept_all_and_reject_all_filter()
        {
            var transactions = AddAndFilterTransactions(_inMemoryTxStorage,
                new AcceptAllTxFilter(), new RejectAllTxFilter());
            transactions.Filtered.Count().Should().Be(0);
            transactions.Pending.Count().Should().NotBe(transactions.Filtered.Count());
        }

        [Test]
        public void should_add_some_transactions_to_storage_when_using_accept_when_filter()
        {
            var filter = AcceptWhenTxFilter
                .Create()
                .Nonce(n => n >= 0)
                .GasPrice(p => p > 2 && p < 1500)
                .Build();
            var transactions = AddAndFilterTransactions(_inMemoryTxStorage, filter);
            transactions.Filtered.Count().Should().NotBe(0);
        }

        private Transactions AddAndFilterTransactions(ITxStorage storage, params ITxFilter[] filters)
        {
            _txPool = CreatePool(storage);
            foreach (var filter in filters ?? Enumerable.Empty<ITxFilter>())
            {
                _txPool.AddFilter(filter);
            }

            var pendingTransactions = AddTransactionsToPool();
            var filteredTransactions = GetTransactionsFromStorage(storage, pendingTransactions);

            return new Transactions(pendingTransactions, filteredTransactions);
        }

        private IDictionary<ISyncPeer, PrivateKey> GetPeers(int limit = 100)
        {
            var peers = new Dictionary<ISyncPeer, PrivateKey>();
            for (var i = 0; i < limit; i++)
            {
                var privateKey = Build.A.PrivateKey.TestObject;
                peers.Add(GetPeer(privateKey.PublicKey), privateKey);
            }

            return peers;
        }

        private TxPool CreatePool(ITxStorage txStorage)
            => new TxPool(txStorage,
                Timestamper.Default, _ethereumEcdsa, _specProvider, new TxPoolConfig(), _logManager);

        private ISyncPeer GetPeer(PublicKey publicKey)
            => new SyncPeerMock(_remoteBlockTree, publicKey);

        private Transaction[] AddTransactionsToPool(int transactionsPerPeer = 10)
        {
            var transactions = GetTransactions(GetPeers(transactionsPerPeer));
            foreach (var transaction in transactions)
            {
                _txPool.AddTransaction(transaction, 1);
            }

            return transactions;
        }

        private void DeleteTransactionsFromPool(IEnumerable<Transaction> transactions)
        {
            foreach (var transaction in transactions)
            {
                _txPool.RemoveTransaction(transaction.Hash, 0);
            }
        }

        private static IEnumerable<Transaction> GetTransactionsFromStorage(ITxStorage storage,
            IEnumerable<Transaction> transactions)
            => transactions.Select(t => storage.Get(t.Hash)).Where(t => !(t is null)).ToArray();

        private Transaction[] GetTransactions(IDictionary<ISyncPeer, PrivateKey> peers,
            int transactionsPerPeer = 10)
        {
            var transactions = new List<Transaction>();
            foreach ((_, PrivateKey privateKey) in peers)
            {
                for (var i = 0; i < transactionsPerPeer; i++)
                {
                    transactions.Add(GetTransaction(privateKey, Address.FromNumber(i)));
                }
            }

            return transactions.ToArray();
        }

        private Transaction GetTransaction(PrivateKey privateKey, Address to = null)
            => GetTransaction(0, 1, 1000, to, new byte[0], privateKey);

        private Transaction GetTransaction(UInt256 nonce, UInt256 gasLimit, UInt256 gasPrice, Address to, byte[] data,
            PrivateKey privateKey)
            => Build.A.Transaction
                .WithNonce(nonce)
                .WithGasLimit(gasLimit)
                .WithGasPrice(gasPrice)
                .WithData(data)
                .To(to)
                .DeliveredBy(privateKey.PublicKey)
                .SignedAndResolved(_ethereumEcdsa, privateKey, RopstenSpecProvider.ByzantiumBlockNumber)
                .TestObject;

        private class Transactions
        {
            public IEnumerable<Transaction> Pending { get; }
            public IEnumerable<Transaction> Filtered { get; }

            public Transactions(IEnumerable<Transaction> pending, IEnumerable<Transaction> filtered)
            {
                Pending = pending;
                Filtered = filtered;
            }
        }
    }
}