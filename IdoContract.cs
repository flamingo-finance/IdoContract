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
    [DisplayName("idoContract")]
    [ManifestExtra("Author", "NEO")]
    [ManifestExtra("Email", "developer@neo.org")]
    [ManifestExtra("Description", "This is a initial dex offering platform contract")]
    [ContractPermission("*")]
    public class IdoContract : SmartContract
    {
        #region prefix
        private static readonly byte[] userStakePrefix = new byte[] { 0x01, 0x01 };
        private static readonly byte[] stakeAssetHashKey = new byte[] { 0x01, 0x02 };
        private static readonly byte[] spendAssetHashKey = new byte[] { 0x01, 0x03 };
        private static readonly byte[] userVotePrefix = new byte[] { 0x01, 0x04 };
        private static readonly byte[] userSwapPrefix = new byte[] { 0x01, 0x05 };
        private static readonly byte[] userClaimPrefix = new byte[] { 0x01, 0x06 };
        [InitialValue("0x1415ab3b409a95555b77bc4ab6a7d9d7be0eddbd", ContractParameterType.Hash160)]// big endian
        private static readonly byte[] defaultStakeAssetHash = default; //FLM

        [InitialValue("0x83c442b5dc4ee0ed0e5249352fa7c75f65d6bfd6", ContractParameterType.Hash160)]// big endian
        private static readonly byte[] defaultSpendAssetHash = default; //fUSDT

        [InitialValue("NVGUQ1qyL4SdSm7sVmGVkXetjEsvw2L3NT", ContractParameterType.Hash160)]
        private static readonly UInt160 originOwner = default;

        private static readonly byte[] timeSpanKey = new byte[] { 0x02, 0x01 };
        private static readonly ulong defaultTimeSpan = 100000000;
        private static readonly byte[] unstakeTimeSpanKey = new byte[] { 0x02, 0x02 };
        private static readonly uint defaultUnstakeTimeSpan = 6172;
        private static readonly byte[] voteTimeSpanKey = new byte[] { 0x02, 0x03 };
        private static readonly uint defaultVoteTimeSpan = 21602;
        private static readonly byte[] levelAmountKey = { 0x03, 0x01 };

        private const ulong priceDenominator = 1000_000_000_000_000_000;
        private const int withdrawFeeDenominator = 10000;
        private const int defaultWithdrawFee = 8000;
        private static readonly byte[] withdrawFeeKey = { 0x04, 0x01 };

        private static readonly byte[] superAdminKey = { 0x05, 0x01 };

        private static readonly byte[] registedProjectPrefix = new byte[] { 0x06, 0x01 };
        private static readonly byte[] registedProjectTotalWeightPrefix = new byte[] { 0x06, 0x02 };
        #endregion

        #region event
        public static event Action<byte[], UInt160> OnDeploy;
        public static event Action<UInt160, BigInteger> SwapAsset;
        #endregion

        #region admin setting
        private static bool IsOwner() => Runtime.CheckWitness(GetOwner());
        public static void _deploy(object data, bool update)
        {           
            Storage.Put(Storage.CurrentContext, superAdminKey, originOwner);
            OnDeploy(superAdminKey, originOwner);
        }
        public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
        {
            if (GetStakeAssetHash() == Runtime.CallingScriptHash)
            {
                SaveUserStaking(from, amount);
            }
        }
        public static UInt160 GetOwner() => (UInt160)Storage.Get(Storage.CurrentContext, superAdminKey);
        public static BigInteger GetWithdrawFee()
        {
            ByteString rawWithdrawFee = Storage.Get(Storage.CurrentContext, withdrawFeeKey);
            return rawWithdrawFee is null ? defaultWithdrawFee : (BigInteger)rawWithdrawFee;
        }
        public static bool SetWithdrawFee(BigInteger amount)
        {
            if (!IsOwner()) throw new Exception("Not owner");
            Storage.Put(Storage.CurrentContext, withdrawFeeKey, amount);
            return true;
        }

        public static void Update(ByteString nefFile, string manifest, object data = null)
        {
            if (!IsOwner()) throw new Exception("No authorization.");

            ContractManagement.Update(nefFile, manifest, data);
        }

        public static bool TransferOwnership(UInt160 newOwner)
        {
            if (!newOwner.IsValid) throw new Exception("The new owner address is invalid.");
            if (!IsOwner()) throw new Exception("No authorization.");

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
            if (!IsOwner()) throw new Exception("Not owner");
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
            if (!IsOwner()) throw new Exception("Not owner");
            Storage.Put(Storage.CurrentContext, spendAssetHashKey, assetHash);
            return true;
        }
        #endregion

        #region userStake
        public static bool Unstake(UInt160 userAddress, BigInteger unstakeAmount)
        {
            UInt160 assetHash = GetStakeAssetHash();
            BigInteger amountBefore = GetBalanceOfToken(assetHash, Runtime.ExecutingScriptHash);
            if (!Runtime.CheckWitness(userAddress)) throw new Exception("CUWF");//check user witness fail
            UserStakeInfo stakeInfo = GetUserStakeInfo(userAddress);
            if (stakeInfo.isNewUser) throw new Exception("bad address");
            if (stakeInfo.lastStakeAmount < unstakeAmount) throw new Exception("bad amount");
            byte stakeLevel = GetUserStakingLevel(userAddress);
            if (GetEnoughTimeForUnstake(stakeInfo.lastStakeHeight, Ledger.CurrentIndex, stakeLevel >= 4))
            {
                SafeTransfer(assetHash, Runtime.ExecutingScriptHash, userAddress, unstakeAmount);
                SaveUserStaking(userAddress, -unstakeAmount);
            }
            else
            {
                BigInteger amountWithFee = unstakeAmount * GetWithdrawFee() / withdrawFeeDenominator;
                SafeTransfer(assetHash, Runtime.ExecutingScriptHash, userAddress, amountWithFee);
                SaveUserStaking(userAddress, -unstakeAmount);
            }
            BigInteger amountAfter = GetBalanceOfToken(assetHash, Runtime.ExecutingScriptHash);
            if (amountBefore - unstakeAmount > amountAfter) throw new Exception("ANC");//amount is not correct after unstake;
            return true;
        }
        private static byte[] GetUserStakeKey(UInt160 userAddress)
        {
            return userStakePrefix.Concat(userAddress);
        }
        private static bool SaveUserStaking(UInt160 userAddress, BigInteger amount)
        {
            UserStakeInfo userInfo = GetUserStakeInfo(userAddress);
            if (userInfo.isNewUser)
            {
                SetUserStakeInfo(userAddress, new UserStakeInfo
                {
                    lastStakeHeight = Ledger.CurrentIndex,
                    lastStakeAmount = amount,
                    userStakeLevel = 0,
                    isNewUser = false
                });
            }
            else
            {
                if (GetIfTimeEnough(userInfo.lastStakeHeight, Ledger.CurrentIndex))
                {
                    byte newUserLevel = GetStakeLevelByAmount(userInfo.lastStakeAmount);
                    SetUserStakeInfo(userAddress, new UserStakeInfo
                    {
                        lastStakeHeight = Ledger.CurrentIndex,
                        lastStakeAmount = amount + userInfo.lastStakeAmount,
                        userStakeLevel = newUserLevel,
                        isNewUser = false
                    });
                }
                else
                {
                    SetUserStakeInfo(userAddress, new UserStakeInfo
                    {
                        lastStakeHeight = Ledger.CurrentIndex,
                        lastStakeAmount = amount + userInfo.lastStakeAmount,
                        userStakeLevel = userInfo.userStakeLevel,
                        isNewUser = false
                    });
                }
            }
            return true;
        }
        public static UserStakeInfo GetUserStakeInfo(UInt160 userAddress)
        {
            ByteString rawUserStakeInfo = Storage.Get(Storage.CurrentContext, GetUserStakeKey(userAddress));
            if (rawUserStakeInfo is null)
            {
                return new UserStakeInfo
                {
                    lastStakeHeight = 0,
                    lastStakeAmount = 0,
                    userStakeLevel = 0,
                    isNewUser = true
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
        public static byte GetUserStakingLevel(UInt160 userAddress)
        {
            UserStakeInfo userInfo = GetUserStakeInfo(userAddress);
            if (userInfo.isNewUser == true)
            {
                return 0;
            }
            else
            {
                if (GetIfTimeEnough(userInfo.lastStakeHeight, Ledger.CurrentIndex))
                {
                    return GetStakeLevelByAmount(userInfo.lastStakeAmount);
                }
                else
                {
                    return GetUserStakeInfo(userAddress).userStakeLevel;
                }
            }
        }
        public static BigInteger GetSwapAmoutMax(UInt160 user, UInt160 idoPairContractHash)
        {
            BigInteger userWeight = GetRegistedProjectUserWeight(idoPairContractHash, user);
            BigInteger totalWeight = GetRegistedProjectTotalWeight(idoPairContractHash);
            BigInteger offeringAmount = GetRegistedProject(idoPairContractHash).tokenOfferingAmount;
            return GetSwapAmountMaxImplementation(userWeight, totalWeight, offeringAmount);

        }
        private static BigInteger GetSwapAmountMaxImplementation(BigInteger userWeight, BigInteger totalWeight, BigInteger offeringAmount)
        {
            return userWeight * offeringAmount / totalWeight;
        }
        public static BigInteger GetUserCanSwapAmount(UInt160 idoPairContract, UInt160 user)
        {
            ByteString rawAmount = Storage.Get(Storage.CurrentContext, userSwapPrefix.Concat(idoPairContract).Concat(user));
            BigInteger MaxAmount = GetSwapAmoutMax(user, idoPairContract);
            return rawAmount is null ? MaxAmount : (MaxAmount - (BigInteger)rawAmount);
        }
        private static void AddUserSwapAmount(UInt160 idoPairContract, UInt160 user, BigInteger amount)
        {
            byte[] key = userSwapPrefix.Concat(idoPairContract).Concat(user);
            ByteString rawOriginAmount = Storage.Get(Storage.CurrentContext, key);
            if (rawOriginAmount is null)
            {
                Storage.Put(Storage.CurrentContext, key, amount);
            }
            else
            {
                Storage.Put(Storage.CurrentContext, key, amount + (BigInteger)rawOriginAmount);
            }
        }
        private static byte[] GetUserClaimAmountKey(UInt160 idoPairContract, UInt160 user)
        {
            return userClaimPrefix.Concat(idoPairContract).Concat(user);
        }
        #endregion

        #region project management
        public static bool RegisterProject(BigInteger tokenOfferingAmount, BigInteger tokenOfferingPrice, UInt160 idoPairContract, byte allowedLevel, UInt160 tokenHash)
        {
            UInt160 sender = ((Transaction)Runtime.ScriptContainer).Sender;
            if (tokenOfferingAmount <= 0 || tokenOfferingPrice <= 0) throw new Exception("BIA");//bad initial args
            SwapAsset(sender, tokenOfferingAmount);
            SafeTransfer(tokenHash, sender, idoPairContract, tokenOfferingAmount);
            if (ContractManagement.GetContract(tokenHash) is null || ContractManagement.GetContract(idoPairContract) is null) throw new Exception("BCH");//bad contract Hash       
            if (!(GetRegistedProject(idoPairContract).isNewProject)) throw new Exception("PHBR");//project has been registed
            SetRegistedProject(new RegistedProject
            {
                tokenOfferingAmount = tokenOfferingAmount,
                tokenOfferingPrice = tokenOfferingPrice,
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
            if (!IsOwner()) throw new Exception("WCF");//witness check fail
            RegistedProject project = GetRegistedProject(idoPairContract);
            project.reviewedHeight = Ledger.CurrentIndex;
            project.isReviewed = true;
            SetRegistedProject(project, idoPairContract);
            return true;
        }
        private static void SetRegistedProject(RegistedProject project, UInt160 idoPairContract) => Storage.Put(Storage.CurrentContext, registedProjectPrefix.Concat(idoPairContract), StdLib.Serialize(project));
        public static RegistedProject GetRegistedProject(UInt160 idoPairContract)
        {
            ByteString rawRegistedProject = Storage.Get(Storage.CurrentContext, registedProjectPrefix.Concat(idoPairContract));
            if (rawRegistedProject is null)
            {
                return new RegistedProject
                {
                    isNewProject = true
                };
            }
            else
            {
                return (RegistedProject)StdLib.Deserialize(rawRegistedProject);
            }
        }
        public static bool VoteForProject(UInt160 user, UInt160 idoPairContractHash)
        {
            if (!Runtime.CheckWitness(user)) throw new Exception("WCF");//witness check fail
            RegistedProject project = GetRegistedProject(idoPairContractHash);
            if (project.isNewProject == true || project.isReviewed != true || project.isEnd == true) throw new Exception("BPS");// bad project status
            byte level = GetUserStakingLevel(user);
            if (level < project.allowedLevel) throw new Exception("BUL");// bad user level
            if (Ledger.CurrentIndex - project.reviewedHeight >= 21602) throw new Exception("PTO");//project time out
            BigInteger userWeight = GetRegistedProjectUserWeight(idoPairContractHash, user);
            if (userWeight != 0) throw new Exception("UHV");// user has voted for project
            uint weight = GetStakeWeightByLevel(level);
            AddProjectWeight(idoPairContractHash, weight);
            Storage.Put(Storage.CurrentContext, userVotePrefix.Concat(idoPairContractHash).Concat(user), weight);
            return true;
        }
        public static bool EndProject(UInt160 idoPairContractHash)
        {
            if (!IsOwner()) throw new Exception("WCF");//witness check fail
            RegistedProject project = GetRegistedProject(idoPairContractHash);
            project.isEnd = true;
            SetRegistedProject(project, idoPairContractHash);
            return true;
        }
        private static void AddProjectWeight(UInt160 idoPairContractHash, BigInteger amount)
        {
            byte[] registedProjectTotalWeightKey = registedProjectTotalWeightPrefix.Concat(idoPairContractHash);
            ByteString rawWeight = Storage.Get(Storage.CurrentContext, registedProjectTotalWeightKey);
            if (rawWeight is null)
            {
                if (amount < 0) throw new Exception("BWA");//bad weight amount
                Storage.Put(Storage.CurrentContext, registedProjectTotalWeightKey, amount);
            }
            else
            {
                if ((BigInteger)rawWeight + amount < 0) throw new Exception("BWA");//bad weight amount
                Storage.Put(Storage.CurrentContext, registedProjectTotalWeightKey, (BigInteger)rawWeight + amount);
            }
        }
        public static bool SwapToken(UInt160 user, UInt160 idoPairContractHash, BigInteger amount)
        {
            if (!Runtime.CheckWitness(user)) throw new Exception("WCF");// witness check fail
            RegistedProject project = GetRegistedProject(idoPairContractHash);
            if (project.isEnd == true) throw new Exception("BPS");// bad project status
            if (!project.isReviewed || Ledger.CurrentIndex - project.reviewedHeight < GetVoteTimeSpan()) throw new Exception("RNE");// project reviewed not end yet
            BigInteger canSwapAmount = GetUserCanSwapAmount(idoPairContractHash, user);
            if (canSwapAmount < amount || amount <= 0) throw new Exception("BSA");//bad swap amount;
            //transfer asset part
            BigInteger balanceBefore = GetBalanceOfToken(project.tokenHash, Runtime.ExecutingScriptHash);
            BigInteger spendAssetAmount = project.tokenOfferingPrice * amount / priceDenominator;
            SwapAsset(user, amount);
            SafeTransfer(GetSpendAssetHash(), user, Runtime.ExecutingScriptHash, spendAssetAmount);
            SafeTransfer(GetSpendAssetHash(), Runtime.ExecutingScriptHash, idoPairContractHash, spendAssetAmount);
            BigInteger balanceAfter = GetBalanceOfToken(project.tokenHash, Runtime.ExecutingScriptHash);
            if (balanceAfter - balanceBefore != amount) throw new Exception("AMC");// amount not correct
            AddUserClaimAmount(idoPairContractHash, user, amount);
            AddUserSwapAmount(idoPairContractHash, user, amount);
            return true;
        }
        private static void AddUserClaimAmount(UInt160 idoPairContractHash, UInt160 user, BigInteger amount)
        {
            byte[] key = GetUserClaimAmountKey(idoPairContractHash, user);
            BigInteger oldAmount = GetUserClaimAmountImple(key);
            amount = amount + oldAmount;
            if (amount < 0) throw new Exception("BAM");// bad amount
            Storage.Put(Storage.CurrentContext, key, amount);
        }
        public static BigInteger GetUserClaimAmount(UInt160 idoPairContractHash, UInt160 user)
        {
            byte[] key = GetUserClaimAmountKey(idoPairContractHash, user);
            return GetUserClaimAmountImple(key);
        }
        private static BigInteger GetUserClaimAmountImple(byte[] key)
        {
            ByteString rawAmount = Storage.Get(Storage.CurrentContext, key);
            return rawAmount is null ? 0 : (BigInteger)rawAmount;
        }
        public static bool ClaimToken(UInt160 user, UInt160 idoPairContractHash)
        {
            RegistedProject project = GetRegistedProject(idoPairContractHash);
            if (!project.isReviewed || Ledger.CurrentIndex - project.reviewedHeight < 2 * GetVoteTimeSpan()) throw new Exception("RNE");// project reviewed not end yet
            byte[] key = GetUserClaimAmountKey(idoPairContractHash, user);
            BigInteger oldAmount = GetUserClaimAmountImple(key);
            if (oldAmount <= 0) throw new Exception("NCT");// no unclaimed token            
            SafeTransfer(project.tokenHash, Runtime.ExecutingScriptHash, user, oldAmount);
            AddUserClaimAmount(idoPairContractHash, user, -oldAmount);
            return true;
        }
        public static bool SwapTokenSecondRound(UInt160 user, UInt160 idoPairContractHash, BigInteger amount)
        {
            if (!Runtime.CheckWitness(user)) throw new Exception("WCF");// witness check fail
            RegistedProject project = GetRegistedProject(idoPairContractHash);
            if (project.isEnd == true) throw new Exception("BPS");// bad project status
            if (!project.isReviewed || Ledger.CurrentIndex - project.reviewedHeight < 2 * GetVoteTimeSpan()) throw new Exception("RNE");// project reviewed not end yet
            if (amount <= 0) throw new Exception("BSA");//bad swap amount;
            //transfer asset part
            BigInteger balanceBefore = GetBalanceOfToken(project.tokenHash, user);
            BigInteger spendAssetAmount = project.tokenOfferingPrice * amount / priceDenominator;
            SwapAsset(user, amount);
            SafeTransfer(GetSpendAssetHash(), user, idoPairContractHash, spendAssetAmount);
            BigInteger balanceAfter = GetBalanceOfToken(project.tokenHash, user);
            if (balanceBefore - balanceAfter != amount) throw new Exception("AMC");// amount not correct
            return true;
        }
        public static BigInteger GetRegistedProjectTotalWeight(UInt160 idoPairContractHash)
        {
            byte[] registedProjectTotalWeightKey = registedProjectTotalWeightPrefix.Concat(idoPairContractHash);
            ByteString rawWeight = Storage.Get(Storage.CurrentContext, registedProjectTotalWeightKey);
            return rawWeight is null ? 0 : (BigInteger)rawWeight;
        }
        public static BigInteger GetRegistedProjectUserWeight(UInt160 idoPairContractHash, UInt160 user)
        {
            byte[] userVoteKey = userVotePrefix.Concat(idoPairContractHash).Concat(user);
            ByteString rawVoted = Storage.Get(Storage.CurrentContext, userVoteKey);
            return rawVoted is null ? 0 : (BigInteger)rawVoted;
        }
        #endregion

        #region calculation
        public static bool GetIfTimeEnough(ulong timeStart, ulong timeEnd)
        {
            if ((BigInteger)(timeEnd - timeStart) >= GetTimeSpan())
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        public static bool GetEnoughTimeForUnstake(uint heightStart, uint heightEnd, bool ifHighLevel)
        {
            if (ifHighLevel)
            {
                if ((BigInteger)(heightEnd - heightStart) * 2 >= GetUnstakeTimeSpan())
                {
                    return true;
                }
            }
            if ((BigInteger)(heightEnd - heightStart) >= GetUnstakeTimeSpan())
            {
                return true;
            }
            return false;
        }
        public static BigInteger GetVoteTimeSpan()
        {
            ByteString rawVoteTimeSpan = Storage.Get(Storage.CurrentContext, voteTimeSpanKey);
            return rawVoteTimeSpan is null ? defaultVoteTimeSpan : (BigInteger)rawVoteTimeSpan;
        }
        public static BigInteger GetTimeSpan()
        {
            ByteString rawTimeSpan = Storage.Get(Storage.CurrentContext, timeSpanKey);
            return rawTimeSpan is null ? defaultTimeSpan : (BigInteger)rawTimeSpan;
        }
        public static BigInteger GetUnstakeTimeSpan()
        {
            ByteString rawUnstakeTimeSpan = Storage.Get(Storage.CurrentContext, unstakeTimeSpanKey);
            return rawUnstakeTimeSpan is null ? defaultUnstakeTimeSpan : (BigInteger)rawUnstakeTimeSpan;
        }
        public static StakeLevelAmount GetStakeLevelAmount()
        {
            ByteString rawAmount = Storage.Get(Storage.CurrentContext, levelAmountKey);
            if (!(rawAmount is null))
            {
                return (StakeLevelAmount)StdLib.Deserialize(rawAmount);
            }
            throw new Exception("bad level amount");
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
                6 => 900,
                5 => 400,
                4 => 150,
                3 => 65,
                2 => 30,
                1 => 10,
                _ => 0,
            };
        }
        #endregion

        #region adminManagement
        public static bool SetStakeLevelAmount(BigInteger bronze, BigInteger silver, BigInteger gold, BigInteger platinum, BigInteger diamond, BigInteger kryptonite)
        {
            if (!IsOwner()) throw new Exception("WCF");//witness check fail
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
            if (!IsOwner()) throw new Exception("WCF");//witness check fail
            if (timeSpan > 0)
            {
                Storage.Put(Storage.CurrentContext, voteTimeSpanKey, timeSpan);
                return true;
            }
            else
            {
                throw new Exception("BA");// bad args
            }
        }
        public static bool SetTimeSpan(BigInteger timeSpan)
        {
            if (!IsOwner()) throw new Exception("WCF");//witness check fail
            if (timeSpan > 0)
            {
                Storage.Put(Storage.CurrentContext, timeSpanKey, timeSpan);
                return true;
            }
            else
            {
                throw new Exception("BA");// bad args
            }
        }
        public static bool SetUnstakeTimeSpan(BigInteger timeSpan)
        {
            if (!IsOwner()) throw new Exception("WCF");//witness check fail
            if (timeSpan > 0)
            {
                Storage.Put(Storage.CurrentContext, unstakeTimeSpanKey, timeSpan);
                return true;
            }
            else
            {
                throw new Exception("BA");// bad args
            }
        }
        #endregion

        #region NEP17Helper
        private static void SafeTransfer(UInt160 token, UInt160 from, UInt160 to, BigInteger amount)
        {
            var result = (bool)Contract.Call(token, "transfer", CallFlags.All, new object[] { from, to, amount, null });
            if (!result) throw new Exception("tf");//transfer fail;
        }

        private static BigInteger GetBalanceOfToken(UInt160 assetHash, UInt160 address)
        {
            var result = Contract.Call(assetHash, "balanceOf", CallFlags.All, new object[] { address });
            return (BigInteger)result;
        }
        #endregion

        #region struct
        public struct UserStakeInfo
        {
            public uint lastStakeHeight;
            public BigInteger lastStakeAmount;
            public byte userStakeLevel;
            public bool isNewUser;
        }
        public struct StakeLevelAmount
        {
            public BigInteger bronzeAmount;//index: 1
            public BigInteger silverAmount;//index: 2
            public BigInteger goldAmount;//index: 3
            public BigInteger platinumAmount;//index: 4
            public BigInteger diamondAmount;//index: 5
            public BigInteger kryptoniteAmount;//index: 6
        }
        public struct RegistedProject
        {
            public uint reviewedHeight;
            public BigInteger tokenOfferingAmount;
            public BigInteger tokenOfferingPrice;
            public UInt160 tokenHash;
            public bool isNewProject;
            public bool isReviewed;
            public bool isEnd;
            public byte allowedLevel;
        }
        #endregion
    }
}
