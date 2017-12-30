using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace QLick_ICO
{
    public class Contract : SmartContract
    {
        //Token Settings
        public static string Name() => "Qlink Token";
        public static string Symbol() => "QLC";
        public static readonly byte[] token_sales = {  };
        public static readonly byte[] other = {  };
        public static byte Decimals() => 8;
        private const ulong factor = 100000000; //decided by Decimals()
        private const ulong neo_decimals = 100000000;

        //ICO Settings
        private static readonly byte[] neo_asset_id = { 155, 124, 255, 218, 166, 116, 190, 174, 15, 147, 14, 190, 96, 133, 175, 144, 147, 229, 254, 86, 179, 74, 92, 34, 12, 205, 207, 110, 252, 51, 111, 197 };
        private const ulong total_amount = 600000000 * factor; // total token amount
        private const ulong pre_ico_cap = 594800000 * factor; // pre ico token amount
        private const ulong basic_rate = 520 * factor;
        private const ulong limit_neo = 15 * neo_decimals;
        private const string contribute_prefix = "prefix";

        //Time
        private const int limit_time = 21600;
        private const int ico_start_time = 1513947600;
        private const int ico_end_time = 1516626000;
        private const int ico_duration = ico_end_time - ico_start_time;

        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;

        [DisplayName("refund")]
        public static event Action<byte[], BigInteger> Refund;

        public static BigInteger TotalToken() => total_amount - pre_ico_cap;

        public static Object Main(string operation, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {             
                    return Runtime.CheckWitness(token_sales);              
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                if (operation == "init") return Init();
                if (operation == "mintTokens") return MintTokens();
                if (operation == "totalSupply") return TotalSupply();
                if (operation == "name") return Name();
                if (operation == "symbol") return Symbol();
                if (operation == "totalToken") return TotalToken();
                if (operation == "icoToken") return IcoToken();
                if (operation == "icoNeo") return IcoNeo();
                if (operation == "endIco") return EndIco();
                if (operation == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];
                    return Transfer(from, to, value);
                }
                if (operation == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return BalanceOf(account);
                }
                if (operation == "decimals") return Decimals();
            }
            //you can choice refund or not refund
            byte[] sender = GetSender();
            ulong contribute_value = getOutputValue();
            if (contribute_value > 0 && sender.Length != 0)
            {
                Refund(sender, contribute_value);
            }
            return false;
        }

        // initialization parameters, only once
        // 初始化参数
        public static bool Init()
        {
            byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
            if (total_supply.Length != 0) return false;
            Storage.Put(Storage.CurrentContext, other, pre_ico_cap);
            Storage.Put(Storage.CurrentContext, "totalSupply", pre_ico_cap);
            Transferred(null, token_sales, pre_ico_cap);
            return true;
        }

        public static BigInteger IcoToken()
        {
            BigInteger total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            return total_supply - pre_ico_cap;
        }

        public static BigInteger IcoNeo()
        {
            BigInteger total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            return (total_supply - pre_ico_cap) / basic_rate;
        }

        public static bool EndIco()
        {
            if (!Runtime.CheckWitness(token_sales)) return false;
            BigInteger total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            BigInteger remain_token = total_amount - total_supply;
            if (remain_token <= 0)
            {
                return false;
            }
            Storage.Put(Storage.CurrentContext, "totalSupply", total_amount);
            Storage.Put(Storage.CurrentContext, token_sales, remain_token);
            Transferred(null, token_sales, remain_token);
            return true;
        }


        // The function MintTokens is only usable by the chosen wallet
        // contract to mint a number of tokens proportional to the
        // amount of neo sent to the wallet contract. The function
        // can only be called during the tokenswap period
        // 将众筹的neo转化为等价的ico代币
        public static bool MintTokens()
        {
            byte[] sender = GetSender();
            // contribute asset is not neo
            if (sender.Length == 0)
            {
                return false;
            }
            uint now = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            int time = (int)now - ico_start_time;
            ulong contribute_value = GetContributeValue(time, sender);
            // the current exchange rate between ico tokens and neo during the token swap period
            // 获取众筹期间ico token和neo间的转化率
            ulong swap_rate = CurrentSwapRate(time);
            // crowdfunding failure
            // 众筹失败
            if (swap_rate == 0)
            {
                Refund(sender, contribute_value);
                return false;
            }
            // you can get current swap token amount
            ulong token = CurrentSwapToken(sender, contribute_value, swap_rate);
            if (token == 0)
            {
                return false;
            }
            // crowdfunding success
            // 众筹成功
            BigInteger balance = Storage.Get(Storage.CurrentContext, sender).AsBigInteger();
            Storage.Put(Storage.CurrentContext, sender, token + balance);
            BigInteger totalSupply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            Storage.Put(Storage.CurrentContext, "totalSupply", token + totalSupply);
            Transferred(null, sender, token);
            return true;
        }

        // get the total token supply
        // 获取已发行token总量
        public static BigInteger TotalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }

        // function that is always called when someone wants to transfer tokens.
        // 流转token调用
        public static bool Transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;
            if (!Runtime.CheckWitness(from)) return false;
            if (from == to) return true;
            BigInteger from_value = Storage.Get(Storage.CurrentContext, from).AsBigInteger();
            if (from_value < value) return false;
            if (from_value == value)
                Storage.Delete(Storage.CurrentContext, from);
            else
                Storage.Put(Storage.CurrentContext, from, from_value - value);
            BigInteger to_value = Storage.Get(Storage.CurrentContext, to).AsBigInteger();
            Storage.Put(Storage.CurrentContext, to, to_value + value);
            Transferred(from, to, value);
            return true;
        }

        // get the account balance of another account with address
        // 根据地址获取token的余额
        public static BigInteger BalanceOf(byte[] address)
        {
            return Storage.Get(Storage.CurrentContext, address).AsBigInteger();
        }

        // The function CurrentSwapRate() returns the current exchange rate
        // between ico tokens and neo during the token swap period
        private static ulong CurrentSwapRate(int time)
        {
            if (time < 0)
            {
                return 0;
            }
            else if (time <= ico_duration)
            {
                return basic_rate;
            }
            else
            {
                return 0;
            }
        }

        //whether over contribute capacity, you can get the token amount
        private static ulong CurrentSwapToken(byte[] sender, ulong value, ulong swap_rate)
        {
            ulong token = value / neo_decimals * swap_rate;
            BigInteger total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            BigInteger balance_token = total_amount - total_supply;
            if (balance_token <= 0)
            {
                Refund(sender, value);
                return 0;
            }
            else if (balance_token < token)
            {
                Refund(sender, (token - balance_token) / swap_rate * neo_decimals);
                token = (ulong)balance_token;
            }
            return token;
        }

        // check whether asset is neo and get sender script hash
        private static byte[] GetSender()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput[] reference = tx.GetReferences();
            // you can choice refund or not refund
            foreach (TransactionOutput output in reference)
            {
                if (output.AssetId == neo_asset_id) return output.ScriptHash;
            }
            return new byte[0];
        }

        // get smart contract script hash
        private static byte[] getReceiver()
        {
            return ExecutionEngine.ExecutingScriptHash;
        }

        // Get the sender sent to the contract number of NEO in address
        // 获取发送方发送到合约地址中的NEO数量
        private static ulong getOutputValue()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput[] outputs = tx.GetOutputs();
            ulong value = 0;
            foreach (TransactionOutput output in outputs)
            {
                if (output.ScriptHash == getReceiver() && output.AssetId == neo_asset_id)
                {
                    value += (ulong)output.Value;
                }
            }
            return value;
        }

        // get all you contribute neo amount
        private static ulong GetContributeValue(int time, byte[] sender)
        {
            ulong value = getOutputValue();
            if (time > 0 && time <= limit_time)
            {
                byte[] key = contribute_prefix.AsByteArray().Concat(sender);
                BigInteger total_neo = Storage.Get(Storage.CurrentContext, key).AsBigInteger();
                ulong balance_neo = limit_neo - (ulong)total_neo;
                if (balance_neo <= 0)
                {
                    Refund(sender, value);
                    return 0;
                }
                else if (balance_neo < value)
                {
                    Storage.Put(Storage.CurrentContext, key, balance_neo + total_neo);
                    Refund(sender, value - balance_neo);
                    return balance_neo;
                }
                Storage.Put(Storage.CurrentContext, key, value + total_neo);
            }
            return value;
        }
    }
}