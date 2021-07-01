using System;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace IDOPlatform
{
    [DisplayName("idoPairContract")]
    [ManifestExtra("Author", "NEO")]
    [ManifestExtra("Email", "developer@neo.org")]
    [ManifestExtra("Description", "This is a initial dex offering pair contract")]
    public class IdoPairContract : SmartContract
    {
        private static readonly byte[] superAdminKey = { 0x01, 0x01 };

        private static readonly byte[] assetHashKey = { 0x02, 0x01 };
        private static readonly byte[] tokenHashKey = { 0x02, 0x02 };
        [InitialValue("44baf1fac6dc465d6318e84911fd9bf536c5d6fd", ContractParameterType.ByteArray)]// little endian
        private static readonly byte[] defaultAssetHash = default;
        [InitialValue("44baf1fac6dc465d6318e84911fd9bf536c5d6fd", ContractParameterType.ByteArray)]// little endian
        private static readonly byte[] defaultTokenHash = default;
        private static bool IsOwner() => Runtime.CheckWitness(GetOwner());
        public static UInt160 GetOwner() => (UInt160)Storage.Get(Storage.CurrentContext, superAdminKey);
        public static void _deploy(object data)
        {
            if (((UInt160)data).Length != 20) throw new Exception("baa");//bad admin address
            Storage.Put(Storage.CurrentContext, superAdminKey, (UInt160)data);
        }
        public static bool SetAssetHash(UInt160 assetHash) 
        {
            if (IsOwner()) throw new Exception("not owner");
            Storage.Put(Storage.CurrentContext, assetHashKey, assetHash);
            return true;
        }

        public static UInt160 GetAssetHash() 
        {
            ByteString rawAssetHash = Storage.Get(Storage.CurrentContext, assetHashKey);
            return rawAssetHash is null ? (UInt160)defaultAssetHash : (UInt160)rawAssetHash;
        }

        public static bool SetTokenHash(UInt160 tokenHash) 
        {
            if(IsOwner()) throw new Exception("not owner");
            Storage.Put(Storage.CurrentContext, tokenHashKey, tokenHash);
            return true;
        }

        public static UInt160 GetTokenHash() 
        {
            ByteString rawTokenHash = Storage.Get(Storage.CurrentContext, tokenHashKey);
            return rawTokenHash is null ? (UInt160)rawTokenHash : (UInt160)defaultTokenHash;
        }

    }
}
