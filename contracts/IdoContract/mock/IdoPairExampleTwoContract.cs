using System;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace MockContracts
{
    [DisplayName("IdoPairExampleTwoContract")]
    [ManifestExtra("Author", "NEO")]
    [ManifestExtra("Email", "developer@neo.org")]
    [ManifestExtra("Description", "This is an initial dex offering pair contract.")]
    [ContractPermission("*")]
    public class IdoPairExampleTwoContract : SmartContract
    {
        private static readonly byte[] superAdminKey = { 0x01, 0x01 };

        private static readonly byte[] assetHashKey = { 0x02, 0x01 };
        private static readonly byte[] tokenHashKey = { 0x02, 0x02 };
        private static readonly byte[] idoContractHashKey = { 0x02, 0x03 };

        private static bool IsOwner() => Runtime.CheckWitness(GetOwner());
        public static UInt160 GetOwner() => (UInt160) Storage.Get(Storage.CurrentContext, superAdminKey);

        public const ulong Price = 21;

        public static void _deploy(object data, bool update)
        {
            Transaction tx = (Transaction) Runtime.ScriptContainer;
            Storage.Put(Storage.CurrentContext, superAdminKey, tx.Sender);
        }

        public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
        {
            if (!IfCallFromIdoContractSwap()) throw new Exception("Not allowed call!");

            if (GetAssetHash() == Runtime.CallingScriptHash)
            {
                SafeTransfer(GetTokenHash(), Runtime.ExecutingScriptHash, from, amount / Price);
            }
        }

        public static bool IfCallFromIdoContractSwap()
        {
            Notification[] notifications = Runtime.GetNotifications(GetIdoContract());

            foreach (var notification in notifications)
            {
                if (notification.EventName == "SwapAsset")
                {
                    return true;
                }
            }

            return false;
        }

        public static bool SetAssetHash(UInt160 assetHash)
        {
            if (!IsOwner()) throw new Exception("Unauthorized!");
            Storage.Put(Storage.CurrentContext, assetHashKey, assetHash);
            return true;
        }

        public static UInt160 GetAssetHash()
        {
            ByteString rawAssetHash = Storage.Get(Storage.CurrentContext, assetHashKey);

            if (rawAssetHash is null) throw new Exception("Asset hash not set.");

            return (UInt160) rawAssetHash;
        }

        public static bool SetTokenHash(UInt160 tokenHash)
        {
            if (!IsOwner()) throw new Exception("Unauthorized!");
            Storage.Put(Storage.CurrentContext, tokenHashKey, tokenHash);
            return true;
        }

        public static UInt160 GetTokenHash()
        {
            ByteString rawTokenHash = Storage.Get(Storage.CurrentContext, tokenHashKey);

            if (rawTokenHash is null) throw new Exception("Token hash not set.");

            return (UInt160) rawTokenHash;
        }

        public static bool SetIdoContract(UInt160 contractHash)
        {
            if (!IsOwner()) throw new Exception("Unauthorized!");
            Storage.Put(Storage.CurrentContext, idoContractHashKey, contractHash);
            return true;
        }

        public static UInt160 GetIdoContract()
        {
            ByteString rawIdoContract = Storage.Get(Storage.CurrentContext, idoContractHashKey);

            if (rawIdoContract is null) throw new Exception("IDO contract hash not set.");

            return (UInt160) rawIdoContract;
        }

        public static bool WithdrawAsset(BigInteger amount)
        {
            if (!IsOwner()) throw new Exception("Witness check fail!");
            SafeTransfer(GetAssetHash(), Runtime.ExecutingScriptHash, GetOwner(), amount);
            return true;
        }

        public static bool WithdrawToken(BigInteger amount)
        {
            if (!IsOwner()) throw new Exception("Witness check fail!");
            SafeTransfer(GetTokenHash(), Runtime.ExecutingScriptHash, GetOwner(), amount);
            return true;
        }

        public static void Update(ByteString nefFile, string manifest, object data = null)
        {
            if (!IsOwner()) throw new Exception("Unauthorized!");

            ContractManagement.Update(nefFile, manifest, data);
        }

        public static bool TransferOwnership(UInt160 newOwner)
        {
            if (!newOwner.IsValid) throw new Exception("The new owner address is invalid.");
            if (!IsOwner()) throw new Exception("Unauthorized!");

            Storage.Put(Storage.CurrentContext, superAdminKey, newOwner);
            return true;
        }

        private static void SafeTransfer(UInt160 token, UInt160 from, UInt160 to, BigInteger amount)
        {
            var result = (bool) Contract.Call(token, "transfer", CallFlags.All, new object[] { from, to, amount, null });
            if (!result) throw new Exception("Transfer failed!");
        }
    }
}