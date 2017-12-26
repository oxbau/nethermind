using System.Numerics;
using Nevermind.Core;
using Nevermind.Core.Crypto;

namespace Nevermind.Store
{
    public interface IStateProvider : ISnapshotable
    {
        Keccak StateRoot { get; set; }

        void DeleteAccount(Address address);

        void CreateAccount(Address address, BigInteger balance);

        bool AccountExists(Address address);

        bool IsDeadAccount(Address address);

        bool IsEmptyAccount(Address address);

        BigInteger GetNonce(Address address);

        BigInteger GetBalance(Address address);
        
        Keccak GetStorageRoot(Address address);

        byte[] GetCode(Address address);

        byte[] GetCode(Keccak codeHash);

        void UpdateCodeHash(Address address, Keccak codeHash);

        void UpdateBalance(Address address, BigInteger balanceChange);

        void UpdateStorageRoot(Address address, Keccak storageRoot);

        void IncrementNonce(Address address);

        Keccak UpdateCode(byte[] code);

        void ClearCaches(); // TODO: temp while designing DB <-> store interaction
    }
}