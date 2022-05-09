using System;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;
using Neo.SmartContract.Framework.Attributes;

namespace IdoContract
{
    [DisplayName("IDOContract")]
    [ContractPermission("*")]
    public class IdoContract : SmartContract
    {
        #region prefix

        private static readonly byte[] userStakePrefix = new byte[] { 0x01, 0x01 };
        private static readonly byte[] stakeAssetHashKey = new byte[] { 0x01, 0x02 };
        private static readonly byte[] spendAssetHashKey = new byte[] { 0x01, 0x03 };
        private static readonly byte[] projectUserPrefix = new byte[] { 0x01, 0x04 };

        //Test
        //[InitialValue("0x1415ab3b409a95555b77bc4ab6a7d9d7be0eddbd", ContractParameterType.Hash160)] // big endian
        //private static readonly byte[] defaultStakeAssetHash = default; //FLM

        //[InitialValue("0x83c442b5dc4ee0ed0e5249352fa7c75f65d6bfd6", ContractParameterType.Hash160)] // big endian
        //private static readonly byte[] defaultSpendAssetHash = default; //fUSDT

        //[InitialValue("NVGUQ1qyL4SdSm7sVmGVkXetjEsvw2L3NT", ContractParameterType.Hash160)]
        //private static readonly UInt160 originOwner = default;

        //Main
        [InitialValue("0xf0151f528127558851b39c2cd8aa47da7418ab28", ContractParameterType.Hash160)] // big endian
        private static readonly byte[] defaultStakeAssetHash = default; //FLM

        [InitialValue("0xcd48b160c1bbc9d74997b803b9a7ad50a4bef020", ContractParameterType.Hash160)] // big endian
        private static readonly byte[] defaultSpendAssetHash = default; //fUSDT

        [InitialValue("NU5ffmnBpXXxkFwMNgEdPC8ZxK2vkRC1CT", ContractParameterType.Hash160)]
        private static readonly UInt160 originOwner = default;

        private static readonly byte[] unstakeTimeSpanKey = new byte[] { 0x02, 0x02 };
        public const uint DefaultUnstakeTimeSpan = 23040;
        private static readonly byte[] voteTimeSpanKey = new byte[] { 0x02, 0x03 };
        private const uint DefaultVoteTimeSpan = 22400;
        private static readonly byte[] swapTimeSpanKey = new byte[] { 0x02, 0x04 };
        private const uint DefaultSwapTimeSpan = 11200;
        private static readonly byte[] levelAmountKey = { 0x03, 0x01 };

        private const ulong PriceDenominator = 1000000000000000000; //10^18
        private const int WithdrawFeeDenominator = 10000;
        private const int DefaultWithdrawFee = 7500;
        private static readonly byte[] withdrawFeeKey = { 0x04, 0x01 };

        private static readonly byte[] superAdminKey = { 0x05, 0x01 };

        private static readonly byte[] registeredProjectPrefix = new byte[] { 0x06, 0x01 };

        #endregion

        #region event

        public static event Action<object> Error;
        public static event Action<byte[], UInt160> OnDeploy;
        public static event Action<UInt160, BigInteger> SwapAsset;

        #endregion

        #region admin setting

        private static bool IsOwner() => Runtime.CheckWitness(GetOwner());

        public static void _deploy(object data, bool update)
        {
            if (update) return;
            Storage.Put(Storage.CurrentContext, superAdminKey, originOwner);
            OnDeploy(superAdminKey, originOwner);
        }

        public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
        {
            if (GetStakeAssetHash() == Runtime.CallingScriptHash)
            {
                UserStakeInfo userInfo = GetUserStakeInfo(from);

                userInfo.lastStakeHeight = Ledger.CurrentIndex;
                userInfo.stakeAmount = userInfo.stakeAmount + amount;
                userInfo.stakeLevel = GetStakeLevelByAmount(userInfo.stakeAmount);

                SetUserStakeInfo(from, userInfo);
            }
        }

        public static UInt160 GetOwner() => (UInt160)Storage.Get(Storage.CurrentContext, superAdminKey);

        public static BigInteger GetWithdrawFee()
        {
            ByteString rawWithdrawFee = Storage.Get(Storage.CurrentContext, withdrawFeeKey);
            return rawWithdrawFee is null ? DefaultWithdrawFee : (BigInteger)rawWithdrawFee;
        }

        public static bool SetWithdrawFee(BigInteger amount)
        {
            ExecutionEngine.Assert(amount >= 0, "bad amount");
            ExecutionEngine.Assert(IsOwner(), "Not Owner");
            Storage.Put(Storage.CurrentContext, withdrawFeeKey, amount);
            return true;
        }

        public static void Update(ByteString nefFile, string manifest)
        {
            ExecutionEngine.Assert(IsOwner(), "Not Owner");
            ContractManagement.Update(nefFile, manifest);
        }

        public static bool TransferOwnership(UInt160 newOwner)
        {
            ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "The new owner address is invalid.");
            ExecutionEngine.Assert(IsOwner(), "Not Owner");
            Storage.Put(Storage.CurrentContext, superAdminKey, newOwner);
            return true;
        }

        #endregion

        #region WhiteList

        public static UInt160 GetStakeAssetHash()
        {
            ByteString rawStakeAssetHash = Storage.Get(Storage.CurrentContext, stakeAssetHashKey);
            return rawStakeAssetHash is null ? (UInt160)defaultStakeAssetHash : (UInt160)rawStakeAssetHash;
        }

        public static bool SetStakeAssetHash(UInt160 assetHash)
        {
            ExecutionEngine.Assert(assetHash.IsValid && !assetHash.IsZero, "bad assetHash");
            ExecutionEngine.Assert(IsOwner(), "Not Owner");
            Storage.Put(Storage.CurrentContext, stakeAssetHashKey, assetHash);
            return true;
        }

        public static UInt160 GetSpendAssetHash()
        {
            ByteString rawSpendAssetHash = Storage.Get(Storage.CurrentContext, spendAssetHashKey);
            return rawSpendAssetHash is null ? (UInt160)defaultSpendAssetHash : (UInt160)rawSpendAssetHash;
        }

        public static bool SetSpendAssetHash(UInt160 assetHash)
        {
            ExecutionEngine.Assert(assetHash.IsValid && !assetHash.IsZero, "bad assetHash");
            ExecutionEngine.Assert(IsOwner(), "Not Owner");
            Storage.Put(Storage.CurrentContext, spendAssetHashKey, assetHash);
            return true;
        }

        #endregion

        #region userStake

        public static bool Unstake(UInt160 userAddress, BigInteger unstakeAmount)
        {
            ExecutionEngine.Assert(userAddress.IsValid && !userAddress.IsZero, "bad userAddress");
            UInt160 assetHash = GetStakeAssetHash();
            BigInteger amountBefore = GetBalanceOfToken(assetHash, Runtime.ExecutingScriptHash);
            ExecutionEngine.Assert(Runtime.CheckWitness(userAddress), "check user witness fail");
            UserStakeInfo userInfo = GetUserStakeInfo(userAddress);
            ExecutionEngine.Assert(userInfo.stakeAmount > 0 && userInfo.stakeAmount >= unstakeAmount, "bad amount");

            userInfo.stakeAmount = userInfo.stakeAmount - unstakeAmount;
            userInfo.stakeLevel = GetStakeLevelByAmount(userInfo.stakeAmount);
            SetUserStakeInfo(userAddress, userInfo);

            if ((BigInteger)(Ledger.CurrentIndex - userInfo.lastStakeHeight) >= GetUnstakeTimeSpan())
            {
                SafeTransfer(assetHash, Runtime.ExecutingScriptHash, userAddress, unstakeAmount);
            }
            else
            {
                BigInteger amountWithFee = unstakeAmount * GetWithdrawFee() / WithdrawFeeDenominator;
                SafeTransfer(assetHash, Runtime.ExecutingScriptHash, userAddress, amountWithFee);
            }

            BigInteger amountAfter = GetBalanceOfToken(assetHash, Runtime.ExecutingScriptHash);
            ExecutionEngine.Assert(amountBefore - unstakeAmount <= amountAfter, "amount not correct");
            return true;
        }

        private static byte[] GetUserStakeKey(UInt160 userAddress)
        {
            return userStakePrefix.Concat(userAddress);
        }

        [Safe]
        public static UserStakeInfo GetUserStakeInfo(UInt160 userAddress)
        {
            ByteString rawUserStakeInfo = Storage.Get(Storage.CurrentContext, GetUserStakeKey(userAddress));
            if (rawUserStakeInfo is null)
            {
                return new UserStakeInfo
                {
                    lastStakeHeight = 0,
                    stakeAmount = 0,
                    stakeLevel = 0
                };
            }
            else
            {
                return (UserStakeInfo)StdLib.Deserialize(rawUserStakeInfo);
            }
        }

        private static void SetUserStakeInfo(UInt160 userAddress, UserStakeInfo stakeInfo)
        {
            Storage.Put(Storage.CurrentContext, GetUserStakeKey(userAddress), StdLib.Serialize(stakeInfo));
        }

        #endregion

        #region project management

        public static bool RegisterProject(BigInteger tokenOfferingAmount, BigInteger tokenOfferingPrice, UInt160 idoPairContract, byte allowedLevel, UInt160 tokenHash)
        {
            ExecutionEngine.Assert(tokenHash.IsValid && !tokenHash.IsZero, "bad tokenHash");
            ExecutionEngine.Assert(allowedLevel >= 1 && allowedLevel <= 6, "bad allowedLevel");
            UInt160 sender = ((Transaction)Runtime.ScriptContainer).Sender;
            ExecutionEngine.Assert(tokenOfferingAmount > 0 && tokenOfferingPrice > 0, "bad initial args");
            CallRegister(idoPairContract);
            SafeTransfer(tokenHash, sender, idoPairContract, tokenOfferingAmount);
            ExecutionEngine.Assert(ContractManagement.GetContract(tokenHash) is not null && ContractManagement.GetContract(idoPairContract) is not null, "contract is empty");
            ExecutionEngine.Assert(GetRegisteredProject(idoPairContract).isNewProject, "project has registered");
            SetRegisteredProject(new RegisteredProject
            {
                tokenOfferingAmount = tokenOfferingAmount,
                tokenOfferingPrice = tokenOfferingPrice,
                totalWeight = 0,
                tokenHash = tokenHash,
                isNewProject = false,
                isReviewed = false,
                isEnd = false,
                allowedLevel = allowedLevel
            },
                idoPairContract);
            return true;
        }

        public static bool ReviewProject(UInt160 idoPairContract)
        {
            ExecutionEngine.Assert(IsOwner(), "Not Owner");
            RegisteredProject project = GetRegisteredProject(idoPairContract);
            ExecutionEngine.Assert(project.isNewProject is false, "empty project");
            ExecutionEngine.Assert(project.isReviewed is false, "project is reviewed");
            project.reviewedHeight = Ledger.CurrentIndex;
            project.isReviewed = true;
            SetRegisteredProject(project, idoPairContract);
            return true;
        }

        private static void SetRegisteredProject(RegisteredProject project, UInt160 idoPairContract) => Storage.Put(Storage.CurrentContext, registeredProjectPrefix.Concat(idoPairContract), StdLib.Serialize(project));

        public static RegisteredProject GetRegisteredProject(UInt160 idoPairContract)
        {
            ExecutionEngine.Assert(idoPairContract.IsValid && !idoPairContract.IsZero, "bad idoPairContract");
            ByteString rawRegisteredProject = Storage.Get(Storage.CurrentContext, registeredProjectPrefix.Concat(idoPairContract));
            if (rawRegisteredProject is null)
            {
                return new RegisteredProject
                {
                    isNewProject = true
                };
            }
            else
            {
                return (RegisteredProject)StdLib.Deserialize(rawRegisteredProject);
            }
        }

        public static ByteString[] GetAllRegisteredProjects()
        {
            StorageMap projectsMap = new StorageMap(Storage.CurrentContext, registeredProjectPrefix);

            var allContracts = new List<ByteString>();

            foreach (ByteString[] project in projectsMap.Find(FindOptions.RemovePrefix))
            {
                allContracts.Add(project[0]);
            }

            return allContracts;
        }

        public static BigInteger GetProjectsCount()
        {
            StorageMap projectsMap = new StorageMap(Storage.CurrentContext, registeredProjectPrefix);

            var allContracts = new List<ByteString>();

            foreach (ByteString[] project in projectsMap.Find(FindOptions.RemovePrefix))
            {
                allContracts.Add(project[0]);
            }

            return allContracts.Count;
        }

        private static void SetProjectUserInfo(UInt160 idoPairContractHash, UInt160 user, ProjectUserInfo projectUserInfo)
        {
            ExecutionEngine.Assert(user.IsValid && !user.IsZero, "bad user");
            ExecutionEngine.Assert(idoPairContractHash.IsValid && !idoPairContractHash.IsZero, "bad idoPairContract");
            byte[] key = projectUserPrefix.Concat(idoPairContractHash).Concat(user);
            Storage.Put(Storage.CurrentContext, key, StdLib.Serialize(projectUserInfo));
        }

        public static ProjectUserInfo GetProjectUserInfo(UInt160 idoPairContractHash, UInt160 user)
        {
            byte[] key = projectUserPrefix.Concat(idoPairContractHash).Concat(user);
            ByteString rawData = Storage.Get(Storage.CurrentContext, key);
            if (rawData is null)
            {
                return new ProjectUserInfo
                {
                    weight = 0,
                    swapAmoutMax = 0,
                    swappedAmount = 0,
                    claimAmount = 0
                };
            }
            else
            {
                ProjectUserInfo projectUserInfo = (ProjectUserInfo)StdLib.Deserialize(rawData);

                RegisteredProject project = GetRegisteredProject(idoPairContractHash);
                if (Ledger.CurrentIndex - project.reviewedHeight >= GetVoteTimeSpan())
                {
                    projectUserInfo.swapAmoutMax = projectUserInfo.weight * project.tokenOfferingAmount / project.totalWeight;
                }

                return projectUserInfo;
            }
        }

        public static bool VoteForProject(UInt160 user, UInt160 idoPairContractHash)
        {
            ExecutionEngine.Assert(user.IsValid && !user.IsZero, "bad user");
            ExecutionEngine.Assert(Runtime.CheckWitness(user), "witness check fail");
            RegisteredProject project = GetRegisteredProject(idoPairContractHash);
            ExecutionEngine.Assert(project.isNewProject is false && project.isReviewed is true && project.isEnd is false, "bad project status");
            ExecutionEngine.Assert(Ledger.CurrentIndex - project.reviewedHeight < GetVoteTimeSpan(), "project time out");

            UserStakeInfo userInfo = GetUserStakeInfo(user);
            ExecutionEngine.Assert(userInfo.stakeLevel >= project.allowedLevel, "bad user level");

            ProjectUserInfo projectUserInfo = GetProjectUserInfo(idoPairContractHash, user);
            ExecutionEngine.Assert(projectUserInfo.weight == 0, "user has voted for project");

            uint weight = GetStakeWeightByLevel(userInfo.stakeLevel);
            ExecutionEngine.Assert(weight >= 0, "bad weight amount");
            project.totalWeight = project.totalWeight + weight;
            projectUserInfo.weight = weight;
            SetRegisteredProject(project, idoPairContractHash);
            SetProjectUserInfo(idoPairContractHash, user, projectUserInfo);
            return true;
        }

        public static bool EndProject(UInt160 idoPairContractHash)
        {
            ExecutionEngine.Assert(IsOwner(), "Not Owner");
            RegisteredProject project = GetRegisteredProject(idoPairContractHash);
            ExecutionEngine.Assert(project.isNewProject is false, "empty project");
            project.isEnd = true;
            SetRegisteredProject(project, idoPairContractHash);
            return true;
        }

        public static bool SwapToken(UInt160 user, UInt160 idoPairContractHash, BigInteger amount)
        {
            ExecutionEngine.Assert(Runtime.CheckWitness(user), " witness check fail");
            RegisteredProject project = GetRegisteredProject(idoPairContractHash);
            ExecutionEngine.Assert(project.isNewProject is false, "empty project");
            ExecutionEngine.Assert(project.isEnd is false, "bad project status");
            ExecutionEngine.Assert(project.isReviewed && Ledger.CurrentIndex - project.reviewedHeight > GetVoteTimeSpan(), "project review not end");
            ExecutionEngine.Assert(Ledger.CurrentIndex - project.reviewedHeight - GetVoteTimeSpan() < GetSwapTimeSpan(), "round 1 is end");

            ProjectUserInfo projectUserInfo = GetProjectUserInfo(idoPairContractHash, user);

            BigInteger canSwapAmount = projectUserInfo.swapAmoutMax - projectUserInfo.swappedAmount;
            ExecutionEngine.Assert(canSwapAmount >= amount && amount > 0, "bad swap amount");
            //transfer asset part
            BigInteger balanceBefore = GetBalanceOfToken(project.tokenHash, Runtime.ExecutingScriptHash);
            BigInteger spendAssetAmount = project.tokenOfferingPrice * amount / PriceDenominator;
            SwapAsset(user, amount);
            CallSwap(idoPairContractHash);
            SafeTransfer(GetSpendAssetHash(), user, Runtime.ExecutingScriptHash, spendAssetAmount);
            SafeTransfer(GetSpendAssetHash(), Runtime.ExecutingScriptHash, idoPairContractHash, spendAssetAmount);
            BigInteger balanceAfter = GetBalanceOfToken(project.tokenHash, Runtime.ExecutingScriptHash);

            BigInteger recAmount = balanceAfter - balanceBefore;
            ExecutionEngine.Assert(recAmount > 0, "amount not correct");

            projectUserInfo.claimAmount = projectUserInfo.claimAmount + recAmount;
            ExecutionEngine.Assert(projectUserInfo.claimAmount >= 0, "bad claim amount");

            projectUserInfo.swappedAmount = projectUserInfo.swappedAmount + amount;
            ExecutionEngine.Assert(projectUserInfo.swappedAmount >= 0, "bad swapped amount");

            SetProjectUserInfo(idoPairContractHash, user, projectUserInfo);

            return true;
        }

        public static bool SwapTokenSecondRound(UInt160 user, UInt160 idoPairContractHash, BigInteger amount)
        {
            ExecutionEngine.Assert(user.IsValid && !user.IsZero, "bad user");
            ExecutionEngine.Assert(idoPairContractHash.IsValid && !idoPairContractHash.IsZero, "bad idoPairContractHash");
            ExecutionEngine.Assert(Runtime.CheckWitness(user), "witness check fail");
            RegisteredProject project = GetRegisteredProject(idoPairContractHash);
            ExecutionEngine.Assert(project.isNewProject is false, "empty project");
            ExecutionEngine.Assert(project.isEnd is false, "bad project status");
            ExecutionEngine.Assert(project.isReviewed && Ledger.CurrentIndex - project.reviewedHeight >= GetVoteTimeSpan(), "project review not end");
            ExecutionEngine.Assert(Ledger.CurrentIndex - project.reviewedHeight - GetVoteTimeSpan() >= GetSwapTimeSpan(), "round 2 not start");
            ExecutionEngine.Assert(Ledger.CurrentIndex - project.reviewedHeight - GetVoteTimeSpan() < 2 * GetSwapTimeSpan(), "round 2 is end");
            ExecutionEngine.Assert(amount > 0, "bad swap amount");
            //transfer asset part
            BigInteger balanceBefore = GetBalanceOfToken(project.tokenHash, idoPairContractHash);
            BigInteger spendAssetAmount = project.tokenOfferingPrice * amount / PriceDenominator;
            SwapAsset(user, amount);
            CallSwap(idoPairContractHash);
            SafeTransfer(GetSpendAssetHash(), user, idoPairContractHash, spendAssetAmount);
            BigInteger balanceAfter = GetBalanceOfToken(project.tokenHash, idoPairContractHash);
            //ExecutionEngine.Assert(balanceAfter - balanceBefore == amount, "amount not correct");
            return true;
        }

        public static bool ClaimToken(UInt160 user, UInt160 idoPairContractHash)
        {
            ExecutionEngine.Assert(user.IsValid && !user.IsZero, "bad user");
            RegisteredProject project = GetRegisteredProject(idoPairContractHash);
            ExecutionEngine.Assert(project.isNewProject is false, "empty project");
            ExecutionEngine.Assert(project.isReviewed && Ledger.CurrentIndex - project.reviewedHeight - GetVoteTimeSpan() >= GetSwapTimeSpan(), "claim not start");

            ProjectUserInfo projectUserInfo = GetProjectUserInfo(idoPairContractHash, user);
            ExecutionEngine.Assert(projectUserInfo.claimAmount > 0, "no unclaimed token");

            BigInteger amount = projectUserInfo.claimAmount;
            projectUserInfo.claimAmount = 0;
            SetProjectUserInfo(idoPairContractHash, user, projectUserInfo);

            SafeTransfer(project.tokenHash, Runtime.ExecutingScriptHash, user, amount);
            return true;
        }

        #endregion

        #region calculation

        public static BigInteger GetVoteTimeSpan()
        {
            ByteString rawVoteTimeSpan = Storage.Get(Storage.CurrentContext, voteTimeSpanKey);
            return rawVoteTimeSpan is null ? DefaultVoteTimeSpan : (BigInteger)rawVoteTimeSpan;
        }

        public static BigInteger GetUnstakeTimeSpan()
        {
            ByteString rawUnstakeTimeSpan = Storage.Get(Storage.CurrentContext, unstakeTimeSpanKey);
            return rawUnstakeTimeSpan is null ? DefaultUnstakeTimeSpan : (BigInteger)rawUnstakeTimeSpan;
        }

        public static BigInteger GetSwapTimeSpan()
        {
            ByteString rawSwapTimeSpan = Storage.Get(Storage.CurrentContext, swapTimeSpanKey);
            return rawSwapTimeSpan is null ? DefaultSwapTimeSpan : (BigInteger)rawSwapTimeSpan;
        }

        public static StakeLevelAmount GetStakeLevelAmount()
        {
            ByteString rawAmount = Storage.Get(Storage.CurrentContext, levelAmountKey);
            if (!(rawAmount is null))
            {
                return (StakeLevelAmount)StdLib.Deserialize(rawAmount);
            }
            ExecutionEngine.Assert(false, "bad level amount");
            throw new InvalidOperationException();
        }

        public static byte GetStakeLevelByAmount(BigInteger amount)
        {
            StakeLevelAmount levelAmount = GetStakeLevelAmount();
            if (amount >= levelAmount.kryptoniteAmount) return 6;
            if (amount >= levelAmount.diamondAmount) return 5;
            if (amount >= levelAmount.platinumAmount) return 4;
            if (amount >= levelAmount.goldAmount) return 3;
            if (amount >= levelAmount.silverAmount) return 2;
            if (amount >= levelAmount.bronzeAmount) return 1;
            return 0;
        }

        public static uint GetStakeWeightByLevel(byte level)
        {
            return level switch
            {
                6 => 1450,
                5 => 695,
                4 => 300,
                3 => 95,
                2 => 30,
                1 => 10,
                _ => 0,
            };
        }

        #endregion

        #region adminManagement

        public static bool SetStakeLevelAmount(BigInteger bronze, BigInteger silver, BigInteger gold, BigInteger platinum, BigInteger diamond, BigInteger kryptonite)
        {
            ExecutionEngine.Assert(bronze >= 0 && silver >= bronze && gold >= silver && platinum >= gold && diamond >= platinum && kryptonite >= diamond, "bad amount");
            ExecutionEngine.Assert(IsOwner(), "witness check fail");
            ByteString rawStakeLevelAmount = StdLib.Serialize(new StakeLevelAmount
            {
                bronzeAmount = bronze,
                silverAmount = silver,
                goldAmount = gold,
                platinumAmount = platinum,
                diamondAmount = diamond,
                kryptoniteAmount = kryptonite
            });
            Storage.Put(Storage.CurrentContext, levelAmountKey, rawStakeLevelAmount);
            return true;
        }

        public static bool SetVoteTimeSpan(BigInteger timeSpan)
        {
            ExecutionEngine.Assert(IsOwner(), "witness check fail");
            ExecutionEngine.Assert(timeSpan > 0, "bad vote timeSpan");
            Storage.Put(Storage.CurrentContext, voteTimeSpanKey, timeSpan);
            return true;
        }

        public static bool SetUnstakeTimeSpan(BigInteger timeSpan)
        {
            ExecutionEngine.Assert(IsOwner(), "witness check fail");
            ExecutionEngine.Assert(timeSpan > 0, "bad unstake timeSpam");
            Storage.Put(Storage.CurrentContext, unstakeTimeSpanKey, timeSpan);
            return true;
        }

        public static bool SetSwapTimeSpan(BigInteger timeSpan)
        {
            ExecutionEngine.Assert(IsOwner(), "witness check fail");
            ExecutionEngine.Assert(timeSpan > 0, "bad swap timeSpan");
            Storage.Put(Storage.CurrentContext, swapTimeSpanKey, timeSpan);
            return true;
        }

        #endregion

        #region NEP17Helper

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

        private static BigInteger GetBalanceOfToken(UInt160 assetHash, UInt160 address)
        {
            try
            {
                var result = Contract.Call(assetHash, "balanceOf", CallFlags.ReadOnly, new object[] { address });
                return (BigInteger)result;
            }
            catch (Exception ex)
            {
                ExecutionEngine.Assert(false, ex.Message);
            }
            return 0;
        }

        #endregion

        #region stateControl
        private static void CallRegister(UInt160 idoPairContract)
        {
            try
            {
                Contract.Call(idoPairContract, "setReceiveOnProjectRegister", CallFlags.All, new object[] { });
            }
            catch (Exception ex)
            {
                ExecutionEngine.Assert(false, ex.Message);
            }
        }

        private static void CallSwap(UInt160 idoPairContract)
        {
            try
            {
                Contract.Call(idoPairContract, "setReceiveOnSwap", CallFlags.All, new object[] { });
            }
            catch (Exception ex)
            {
                ExecutionEngine.Assert(false, ex.Message);
            }
        }
        #endregion

        #region struct

        public struct UserStakeInfo
        {
            public uint lastStakeHeight;
            public BigInteger stakeAmount;
            public byte stakeLevel;
        }

        public struct ProjectUserInfo
        {
            public BigInteger weight;
            public BigInteger swapAmoutMax;
            public BigInteger swappedAmount;
            public BigInteger claimAmount;
        }

        public struct RegisteredProject
        {
            public uint reviewedHeight;
            public BigInteger tokenOfferingAmount;
            public BigInteger tokenOfferingPrice;
            public BigInteger totalWeight;
            public UInt160 tokenHash;
            public bool isNewProject;
            public bool isReviewed;
            public bool isEnd;
            public byte allowedLevel;
        }

        public struct StakeLevelAmount
        {
            public BigInteger bronzeAmount; //index: 1
            public BigInteger silverAmount; //index: 2
            public BigInteger goldAmount; //index: 3
            public BigInteger platinumAmount; //index: 4
            public BigInteger diamondAmount; //index: 5
            public BigInteger kryptoniteAmount; //index: 6
        }

        #endregion
    }
}
