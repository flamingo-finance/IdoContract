using System;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;
using Neo.SmartContract.Framework.Attributes;

namespace IdoPairContract
{
    [DisplayName("GM_IDOPairContract")]
    [ContractPermission("*")]
    public class IdoPairContract : SmartContract
    {
        private static readonly byte[] superAdminKey = { 0x01, 0x01 };

        private static readonly byte[] assetHashKey = { 0x02, 0x01 };
        private static readonly byte[] tokenHashKey = { 0x02, 0x02 };
        private static readonly byte[] idoContractHashKey = { 0x02, 0x03 };
        private static readonly byte[] registerReceiveKey = { 0x02, 0x04 };
        private static readonly byte[] swapReceiveKey = { 0x02, 0x05 };

        private static bool IsOwner() => Runtime.CheckWitness(GetOwner());
        public static UInt160 GetOwner() => (UInt160) Storage.Get(Storage.CurrentContext, superAdminKey);

        private const ulong PriceMultiplier = 1000000000000000000;  //18

        public const ulong Price = 1300000000000000; //0.13usdt #### fUSDT decimals is 6, token decimals is 8

        public static void _deploy(object data, bool update)
        {
            Transaction tx = (Transaction) Runtime.ScriptContainer;
            Storage.Put(Storage.CurrentContext, superAdminKey, tx.Sender);
        }

        public static void Update(ByteString nefFile, string manifest)
        {
            ExecutionEngine.Assert(IsOwner(), "not owner");
            ContractManagement.Update(nefFile, manifest);
        }

        public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
        {
            if ((BigInteger)Storage.Get(Storage.CurrentContext, swapReceiveKey) == 1 && GetSpendAssetHash() == Runtime.CallingScriptHash)
            {
                ResetReceiveOnSwap();
                ResetReceiveOnProjectRegister();
                SafeTransfer(GetTokenHash(), Runtime.ExecutingScriptHash, from, amount * PriceMultiplier / Price);
            }
            else if ((BigInteger)Storage.Get(Storage.CurrentContext, registerReceiveKey) == 1 && GetTokenHash() == Runtime.CallingScriptHash)
            {
                ResetReceiveOnSwap();
                ResetReceiveOnProjectRegister();
            }
            else 
            {
                ExecutionEngine.Abort();
            }
        }
        public static void SetReceiveOnProjectRegister()
        {
            if (Runtime.CallingScriptHash == GetIdoContract())
            {
                Storage.Put(Storage.CurrentContext, registerReceiveKey, 1);
            }
        }

        public static void SetReceiveOnSwap()
        {
            if (Runtime.CallingScriptHash == GetIdoContract())
            {
                Storage.Put(Storage.CurrentContext, swapReceiveKey, 1);
            }
        }

        private static void ResetReceiveOnProjectRegister()
        {
            Storage.Delete(Storage.CurrentContext, registerReceiveKey);
        }

        private static void ResetReceiveOnSwap()
        {
            Storage.Delete(Storage.CurrentContext, swapReceiveKey);
        }

        public static bool SetSpendAssetHash(UInt160 assetHash)
        {
            ExecutionEngine.Assert(assetHash.IsValid && !assetHash.IsZero, "bad assetHash");
            ExecutionEngine.Assert(IsOwner(), "not owner");
            Storage.Put(Storage.CurrentContext, assetHashKey, assetHash);
            return true;
        }

        public static UInt160 GetSpendAssetHash()
        {
            ByteString rawAssetHash = Storage.Get(Storage.CurrentContext, assetHashKey);
            ExecutionEngine.Assert(rawAssetHash is not null, "Asset hash not set.");
            return (UInt160) rawAssetHash;
        }

        public static bool SetTokenHash(UInt160 tokenHash)
        {
            ExecutionEngine.Assert(tokenHash.IsValid && !tokenHash.IsZero, "bad tokenHash");
            ExecutionEngine.Assert(IsOwner(), "not owner");
            Storage.Put(Storage.CurrentContext, tokenHashKey, tokenHash);
            return true;
        }

        public static UInt160 GetTokenHash()
        {
            ByteString rawTokenHash = Storage.Get(Storage.CurrentContext, tokenHashKey);
            ExecutionEngine.Assert(rawTokenHash is not null, "Token hash not set.");
            return (UInt160) rawTokenHash;
        }

        public static bool SetIdoContract(UInt160 contractHash)
        {
            ExecutionEngine.Assert(contractHash.IsValid && !contractHash.IsZero, "bad contractHash");
            ExecutionEngine.Assert(IsOwner(), "not owner");
            Storage.Put(Storage.CurrentContext, idoContractHashKey, contractHash);
            return true;
        }

        public static UInt160 GetIdoContract()
        {            
            ByteString rawIdoContract = Storage.Get(Storage.CurrentContext, idoContractHashKey);
            ExecutionEngine.Assert(rawIdoContract is not null, "IDO contract hash not set.");
            return (UInt160) rawIdoContract;
        }

        public static bool WithdrawAsset(BigInteger amount)
        {
            ExecutionEngine.Assert(IsOwner(), "not owner");
            SafeTransfer(GetSpendAssetHash(), Runtime.ExecutingScriptHash, GetOwner(), amount);
            return true;
        }

        public static bool WithdrawToken(BigInteger amount)
        {
            ExecutionEngine.Assert(IsOwner(), "not owner");
            SafeTransfer(GetTokenHash(), Runtime.ExecutingScriptHash, GetOwner(), amount);
            return true;
        }

        public static bool TransferOwnership(UInt160 newOwner)
        {
            ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "new owner address is invalid.");
            ExecutionEngine.Assert(IsOwner(), "not owner");
            Storage.Put(Storage.CurrentContext, superAdminKey, newOwner);
            return true;
        }

        private static void SafeTransfer(UInt160 token, UInt160 from, UInt160 to, BigInteger amount)
        {
            try
            {
                var result = (bool)Contract.Call(token, "transfer", CallFlags.All, new object[] { from, to, amount, null });
                ExecutionEngine.Assert(result, "transfer fail");
            }
            catch (Exception ex) 
            {
                ExecutionEngine.Assert(false, ex.Message);
            }

        }
    }
}
