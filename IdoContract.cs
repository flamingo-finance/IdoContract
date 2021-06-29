using System;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace HelloContract
{
    [DisplayName("HelloContract")]
    [ManifestExtra("Author", "NEO")]
    [ManifestExtra("Email", "developer@neo.org")]
    [ManifestExtra("Description", "This is a HelloContract")]
    public class IdoContract : SmartContract
    {
        private static readonly byte[] userStakePrefix = new byte[] { 0x01, 0x01 };

        private static readonly byte[] timeSpanKey = new byte[] { 0x02, 0x01 };
        private static readonly ulong defaultTimeSpan = 100000000;
        private static readonly byte[] levelAmountKey = { 0x03, 0x01 };

        public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
        {
            if (CheckWhiteListAsset(Runtime.CallingScriptHash)) 
            {
                SaveUserStaking(from, amount);
            }
            else 
            {
                throw new Exception("bad asset");
            }
        }

        #region WhiteList
        public static bool CheckWhiteListAsset(UInt160 assetHash) 
        {
            var rawWhiteListResult = Storage.Get(Storage.CurrentContext, assetHash);
            if (rawWhiteListResult is null) return false;
            return true;
        }
        #endregion

        #region userStake
        public static byte[] GetUserStakeKey(UInt160 userAddress) 
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
                    lastStakeTime = Runtime.Time,
                    lastStakeAmount = amount,
                    userStakeLevel = 0,
                    isNewUser = false
                });               
            }
            else
            {
                if (GetIfTimeEnough(userInfo.lastStakeTime, Runtime.Time))
                {
                    int newUserLevel = GetStakeLevelByAmount(userInfo.lastStakeAmount);
                    SetUserStakeInfo(userAddress, new UserStakeInfo
                    {
                        lastStakeTime = Runtime.Time,
                        lastStakeAmount = amount + userInfo.lastStakeAmount,
                        userStakeLevel = newUserLevel,
                        isNewUser = false
                    });
                }
                else 
                {
                    SetUserStakeInfo(userAddress, new UserStakeInfo
                    {
                        lastStakeTime = Runtime.Time,
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
                    lastStakeTime = 0,
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
        private static void SetUserStakeInfo(UInt160 userAddress ,UserStakeInfo stakeInfo) 
        {
            Storage.Put(Storage.CurrentContext, GetUserStakeKey(userAddress), StdLib.Serialize(stakeInfo));
        }
        public static int GetUserStakingLevel(UInt160 userAddress) 
        {
            UserStakeInfo userInfo = GetUserStakeInfo(userAddress);
            if (userInfo.isNewUser == true)
            {
                return 0;
            }
            else 
            {
                if (GetIfTimeEnough(userInfo.lastStakeTime, Runtime.Time))
                {
                    return GetStakeLevelByAmount(userInfo.lastStakeAmount);
                }
                else 
                {
                    return GetUserStakeInfo(userAddress).userStakeLevel;
                }                
            }            
        }
        #endregion

        #region calculation
        private static bool GetIfTimeEnough(ulong timeStart, ulong timeEnd) 
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

        public static BigInteger GetTimeSpan() 
        {
            ByteString rawTimeSpan = Storage.Get(Storage.CurrentContext, timeSpanKey);
            return rawTimeSpan is null ? defaultTimeSpan : (BigInteger)rawTimeSpan;
        }

        public static bool SetTimeSpan(BigInteger timeSpan) 
        {
            if (timeSpan > 0)
            {
                Storage.Put(Storage.CurrentContext, timeSpanKey, timeSpan);
                return true;
            }
            else 
            {
                return false;
            }
            
        }

        public static int GetStakeLevelByAmount(BigInteger amount) 
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

        public static StakeLevelAmount GetStakeLevelAmount() 
        {
            ByteString rawAmount = Storage.Get(Storage.CurrentContext, levelAmountKey);
            if(!(rawAmount is null))
            {
                return (StakeLevelAmount)StdLib.Deserialize(rawAmount);
            }
            throw new Exception("bad level amount");
        }

        public static bool SetStakeLevelAmount(
            BigInteger bronze, 
            BigInteger silver, 
            BigInteger gold, 
            BigInteger platinum, 
            BigInteger diamond, 
            BigInteger kryptonite) 
        {
            //TODO: admin check
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

        #endregion

        public struct UserStakeInfo 
        {
            public ulong lastStakeTime;
            public BigInteger lastStakeAmount;
            public int userStakeLevel;
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
    }
}
