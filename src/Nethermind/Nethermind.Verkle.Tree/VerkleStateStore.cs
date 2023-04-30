using System.Buffers.Binary;
using System.Diagnostics;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Db;
using Nethermind.Trie;
using Nethermind.Trie.Pruning;
using Nethermind.Verkle.Tree.Nodes;
using Nethermind.Verkle.Tree.VerkleDb;

namespace Nethermind.Verkle.Tree;

public class VerkleStateStore : IVerkleStore, ISyncTrieStore
{
    /// <summary>
    ///  maximum number of blocks that should be stored in cache (not persisted in db)
    /// </summary>
    private int MaxNumberOfBlocksInCache { get; set; }

    /// <summary>
    /// the blockNumber for with the fullState is persisted in the database.
    /// </summary>
    private long FullStatePersistedBlock { get; set; }
    private long FullStateCacheBlock { get; set; }

    private byte[]? PersistedStateRoot { get;  set; }

    private StackQueue<(long, VerkleMemoryDb)> BlockCache { get; set; }

    public byte[] StateRoot { get; private set; }

    public byte[] RootHash
    {
        get => GetStateRoot();
        set => MoveToStateRoot(value);
    }


    // The underlying key value database
    // We try to avoid fetching from this, and we only store at the end of a batch insert
    private VerkleKeyValueDb Storage { get; }

    // This stores the key-value pairs that we need to insert into the storage. This is generally
    // used to batch insert changes for each block. This is also used to generate the forwardDiff.
    // This is flushed after every batch insert and cleared.
    private VerkleMemoryDb Batch { get; set; } = new VerkleMemoryDb();

    private VerkleHistoryStore History { get; }

    private readonly StateRootToBlockMap _stateRootToBlocks;

    public VerkleStateStore(IDbProvider dbProvider, int maxNumberOfBlocksInCache = 128)
    {
        Storage = new VerkleKeyValueDb(dbProvider);
        History = new VerkleHistoryStore(dbProvider);
        _stateRootToBlocks = new StateRootToBlockMap(dbProvider.StateRootToBlocks);
        BlockCache = new StackQueue<(long, VerkleMemoryDb)>(maxNumberOfBlocksInCache);
        MaxNumberOfBlocksInCache = maxNumberOfBlocksInCache;
        InitRootHash();
        StateRoot = GetStateRoot();
        FullStatePersistedBlock = _stateRootToBlocks[StateRoot];
        FullStateCacheBlock = -1;

        // TODO: why should we store using block number - use stateRoot to index everything
        // but i think block number is easy to understand and it maintains a sequence
        if (FullStatePersistedBlock == -2) throw new Exception("StateRoot To BlockNumber Cache Corrupted");
    }

    public ReadOnlyVerkleStateStore AsReadOnly(VerkleMemoryDb keyValueStore)
    {
        return new ReadOnlyVerkleStateStore(this, keyValueStore);
    }

    // This generates and returns a batchForwardDiff, that can be used to move the full state from fromBlock to toBlock.
    // for this fromBlock < toBlock - move forward in time
    public VerkleMemoryDb GetForwardMergedDiff(long fromBlock, long toBlock)
    {
        return History.GetBatchDiff(fromBlock, toBlock).DiffLayer;
    }

    // This generates and returns a batchForwardDiff, that can be used to move the full state from fromBlock to toBlock.
    // for this fromBlock > toBlock - move back in time
    public VerkleMemoryDb GetReverseMergedDiff(long fromBlock, long toBlock)
    {
        return History.GetBatchDiff(fromBlock, toBlock).DiffLayer;
    }
    public event EventHandler<ReorgBoundaryReached>? ReorgBoundaryReached;

    private void InitRootHash()
    {
        InternalNode? node = GetBranch(Array.Empty<byte>());
        if(node is null) SetBranch(Array.Empty<byte>(), new BranchNode());
    }

    public byte[]? GetLeaf(byte[] key)
    {
#if DEBUG
        if (key.Length != 32) throw new ArgumentException("key must be 32 bytes", nameof(key));
#endif
        if (Batch.GetLeaf(key, out byte[]? value)) return value;

        using StackQueue<(long, VerkleMemoryDb)>.StackEnumerator diffs = BlockCache.GetStackEnumerator();
        while (diffs.MoveNext())
        {
            if (diffs.Current.Item2.LeafTable.TryGetValue(key, out byte[]? node)) return node;
        }

        return Storage.GetLeaf(key, out value) ? value : null;
    }

    public SuffixTree? GetStem(byte[] stemKey)
    {
#if DEBUG
        if (stemKey.Length != 31) throw new ArgumentException("stem must be 31 bytes", nameof(stemKey));
#endif
        if (Batch.GetStem(stemKey, out SuffixTree? value)) return value;

        using StackQueue<(long, VerkleMemoryDb)>.StackEnumerator diffs = BlockCache.GetStackEnumerator();
        while (diffs.MoveNext())
        {
            if (diffs.Current.Item2.StemTable.TryGetValue(stemKey, out SuffixTree? node)) return node;
        }


        return Storage.GetStem(stemKey, out value) ? value : null;
    }

    public InternalNode? GetBranch(byte[] key)
    {
        if (Batch.GetBranch(key, out InternalNode? value)) return value;

        using StackQueue<(long, VerkleMemoryDb)>.StackEnumerator diffs = BlockCache.GetStackEnumerator();
        while (diffs.MoveNext())
        {
            if (diffs.Current.Item2.BranchTable.TryGetValue(key, out InternalNode? node)) return node;
        }
        return Storage.GetBranch(key, out value) ? value : null;
    }

    public void SetLeaf(byte[] leafKey, byte[] leafValue)
    {
#if DEBUG
        if (leafKey.Length != 32) throw new ArgumentException("key must be 32 bytes", nameof(leafKey));
        if (leafValue.Length != 32) throw new ArgumentException("value must be 32 bytes", nameof(leafValue));
#endif
        Batch.SetLeaf(leafKey, leafValue);
    }

    public void SetStem(byte[] stemKey, SuffixTree suffixTree)
    {
#if DEBUG
        if (stemKey.Length != 31) throw new ArgumentException("stem must be 32 bytes", nameof(stemKey));
#endif
        Batch.SetStem(stemKey, suffixTree);
    }

    public void SetBranch(byte[] branchKey, InternalNode internalNodeValue)
    {
        Batch.SetBranch(branchKey, internalNodeValue);
    }

    // This method is called at the end of each block to flush the batch changes to the storage and generate forward and reverse diffs.
    // this should be called only once per block, right now it does not support multiple calls for the same block number.
    // if called multiple times, the full state would be fine - but it would corrupt the diffs and historical state will be lost
    // TODO: add capability to update the diffs instead of overwriting if Flush(long blockNumber) is called multiple times for the same block number
    public void Flush(long blockNumber)
    {
        if (blockNumber <= FullStateCacheBlock)
            throw new InvalidOperationException("Cannot flush for same block number multiple times");

        if (!BlockCache.EnqueueAndReplaceIfFull((blockNumber, Batch), out (long, VerkleMemoryDb) element))
        {
            PersistedStateRoot = GetStateRoot(element.Item2);
            VerkleMemoryDb reverseDiff = PersistBlockChanges(element.Item2, Storage);

            History.InsertDiff(element.Item1, element.Item2, reverseDiff);
            FullStatePersistedBlock = element.Item1;
            Storage.LeafDb.Flush();
            Storage.BranchDb.Flush();
            Storage.StemDb.Flush();
        }

        FullStateCacheBlock = blockNumber;
        StateRoot = GetStateRoot();
        _stateRootToBlocks[StateRoot] = blockNumber;
        Batch = new VerkleMemoryDb();
    }

    // now the full state back in time by one block.
    public void ReverseState()
    {

        if (BlockCache.Count != 0)
        {
            BlockCache.Pop(out _);
            return;
        }

        VerkleMemoryDb reverseDiff = History.GetBatchDiff(FullStatePersistedBlock, FullStatePersistedBlock - 1).DiffLayer;

        foreach (KeyValuePair<byte[], byte[]?> entry in reverseDiff.LeafTable)
        {
            reverseDiff.GetLeaf(entry.Key, out byte[]? node);
            if (node is null)
            {
                Storage.RemoveLeaf(entry.Key);
            }
            else
            {
                Storage.SetLeaf(entry.Key, node);
            }
        }

        foreach (KeyValuePair<byte[], SuffixTree?> entry in reverseDiff.StemTable)
        {
            reverseDiff.GetStem(entry.Key, out SuffixTree? node);
            if (node is null)
            {
                Storage.RemoveStem(entry.Key);
            }
            else
            {
                Storage.SetStem(entry.Key, node);
            }
        }

        foreach (KeyValuePair<byte[], InternalNode?> entry in reverseDiff.BranchTable)
        {
            reverseDiff.GetBranch(entry.Key, out InternalNode? node);
            if (node is null)
            {
                Storage.RemoveBranch(entry.Key);
            }
            else
            {
                Storage.SetBranch(entry.Key, node);
            }
        }
        FullStatePersistedBlock -= 1;
    }

    // use the batch diff to move the full state back in time to access historical state.
    public void ApplyDiffLayer(BatchChangeSet changeSet)
    {
        if (changeSet.FromBlockNumber != FullStatePersistedBlock) throw new ArgumentException($"Cannot apply diff FullStateBlock:{FullStatePersistedBlock}!=fromBlock:{changeSet.FromBlockNumber}", nameof(changeSet.FromBlockNumber));

        VerkleMemoryDb reverseDiff = changeSet.DiffLayer;

        foreach (KeyValuePair<byte[], byte[]?> entry in reverseDiff.LeafTable)
        {
            reverseDiff.GetLeaf(entry.Key, out byte[]? node);
            if (node is null)
            {
                Storage.RemoveLeaf(entry.Key);
            }
            else
            {
                Storage.SetLeaf(entry.Key, node);
            }
        }

        foreach (KeyValuePair<byte[], SuffixTree?> entry in reverseDiff.StemTable)
        {
            reverseDiff.GetStem(entry.Key, out SuffixTree? node);
            if (node is null)
            {
                Storage.RemoveStem(entry.Key);
            }
            else
            {
                Storage.SetStem(entry.Key, node);
            }
        }

        foreach (KeyValuePair<byte[], InternalNode?> entry in reverseDiff.BranchTable)
        {
            reverseDiff.GetBranch(entry.Key, out InternalNode? node);
            if (node is null)
            {
                Storage.RemoveBranch(entry.Key);
            }
            else
            {
                Storage.SetBranch(entry.Key, node);
            }
        }
        FullStatePersistedBlock = changeSet.ToBlockNumber;
    }
    public bool IsFullySynced(Keccak stateRoot)
    {
        return false;
    }

    public byte[] GetStateRoot()
    {
        return GetBranch(Array.Empty<byte>())?._internalCommitment.Point.ToBytes().ToArray() ?? throw new InvalidOperationException();
    }

    public static byte[] GetStateRoot(IVerkleDb db)
    {
        if(db.GetBranch(Array.Empty<byte>(), out InternalNode? node))
        {
            return node!._internalCommitment.Point.ToBytes().ToArray();
        }
        throw new InvalidOperationException();
    }

    public InternalNode? GetRootNode()
    {
        return GetBranch(Array.Empty<byte>());
    }

    public void MoveToStateRoot(byte[] stateRoot)
    {
        byte[] currentRoot = GetBranch(Array.Empty<byte>())?._internalCommitment.PointAsField.ToBytes().ToArray() ?? throw new InvalidOperationException();

        if (currentRoot.SequenceEqual(stateRoot)) return;
        if (Keccak.EmptyTreeHash.Equals(stateRoot)) return;

        long fromBlock = _stateRootToBlocks[currentRoot];
        if(fromBlock == -1) return;
        long toBlock = _stateRootToBlocks[stateRoot];
        if (toBlock == -1) return;

        ApplyDiffLayer(History.GetBatchDiff(fromBlock, toBlock));

        Debug.Assert(GetStateRoot().Equals(stateRoot));
    }

    private static VerkleMemoryDb PersistBlockChanges(VerkleMemoryDb blockChanges, VerkleKeyValueDb storage)
    {
        // we should not have any null values in the Batch db - because deletion of values from verkle tree is not allowed
        // nullable values are allowed in MemoryStateDb only for reverse diffs.
        VerkleMemoryDb reverseDiff = new VerkleMemoryDb();

        foreach (KeyValuePair<byte[], byte[]?> entry in blockChanges.LeafTable)
        {
            Debug.Assert(entry.Value is not null, "nullable value only for reverse diff");
            if (storage.GetLeaf(entry.Key, out byte[]? node)) reverseDiff.LeafTable[entry.Key] = node;
            else reverseDiff.LeafTable[entry.Key] = null;

            storage.SetLeaf(entry.Key, entry.Value);
        }

        foreach (KeyValuePair<byte[], SuffixTree?> entry in blockChanges.StemTable)
        {
            Debug.Assert(entry.Value is not null, "nullable value only for reverse diff");
            if (storage.GetStem(entry.Key, out SuffixTree? node)) reverseDiff.StemTable[entry.Key] = node;
            else reverseDiff.StemTable[entry.Key] = null;

            storage.SetStem(entry.Key, entry.Value);
        }

        foreach (KeyValuePair<byte[], InternalNode?> entry in blockChanges.BranchTable)
        {
            Debug.Assert(entry.Value is not null, "nullable value only for reverse diff");
            if (storage.GetBranch(entry.Key, out InternalNode? node)) reverseDiff.BranchTable[entry.Key] = node;
            else reverseDiff.BranchTable[entry.Key] = null;

            storage.SetBranch(entry.Key, entry.Value);
        }

        return reverseDiff;
    }

    private readonly struct StateRootToBlockMap
    {
        private readonly IDb _stateRootToBlock;

        public StateRootToBlockMap(IDb stateRootToBlock)
        {
            _stateRootToBlock = stateRootToBlock;
        }

        public long this[byte[] key]
        {
            get
            {
                if (key.IsZero()) return -1;
                byte[]? encodedBlock = _stateRootToBlock[key];
                return encodedBlock is null ? -2 : BinaryPrimitives.ReadInt64LittleEndian(encodedBlock);
            }
            set
            {
                Span<byte> encodedBlock = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(encodedBlock, value);
                _stateRootToBlock.Set(key, encodedBlock.ToArray());
            }
        }
    }
}