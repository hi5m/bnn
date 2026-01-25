// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using Org.BouncyCastle.Asn1.Mozilla;

namespace Bnncmd
{
    internal class FundingRate(AbstractExchange exchange, string symbol, decimal currRate)
    {
        public AbstractExchange Exchange { get; set; } = exchange;
        public string Symbol { get; set; } = symbol;
        public decimal CurrRate { get; set; } = currRate;
        public int Interval { get; set; } = 0;
        public AbstractExchange? SpotExchange { get; set; }
        public decimal RealSingleRate { get; set; } = 0;
        public decimal RealRateWithSpread { get; set; }

        public override string ToString()
        {
            var rate = $"{CurrRate:0.###}%";
            return $"{Symbol,-19} | {rate,-6} | {Exchange.Name,-7} | {Interval} | ";
        }
    }

    internal class EarnProduct(AbstractExchange exchange, string productName, decimal apr)
    {
        public EarnProduct(AbstractExchange exchange, string productName, decimal apr, string comment) : this(exchange, productName, apr)
        {
            Comment = comment;
        }
        public AbstractExchange Exchange { get; set; } = exchange;
        public string ProductName { get; set; } = productName;
        public decimal Apr { get; set; } = apr;
        public string Comment { get; set; } = string.Empty;
        public int Term { get; set; } = 1;
        public decimal LimitMax { get; set; } = 0;

        public AbstractExchange? FuturesExchange { get; set; }
        /// <summary>
        /// For binance USDT / USDC
        /// </summary>
        public string FuturesPair { get; set; } = string.Empty;
        public decimal DayFundingRate { get; set; }
        public decimal RealApr { get; set; }

        public override string ToString()
        {
            var apr = $"{Apr:0.##}%";
            return $"{ProductName,-19} | {apr,-7} | {Exchange.Name,-11} | {Term} | {Comment}";
        }
    }


    internal class Exchange
    {
        private static readonly BinanceExchange s_binance = new();
        public static BinanceExchange Binance { get { return s_binance; } }

        private static readonly BybitExchange s_bybit = new();
        public static BybitExchange Bybit { get { return s_bybit; } }

        private static readonly MexcExchange s_mexc = new();
        public static MexcExchange Mexc { get { return s_mexc; } }

        public static AbstractExchange GetExchangeByName(string exchName)
        {
            if (exchName.Equals(Binance.Name, StringComparison.InvariantCultureIgnoreCase)) return Binance;
            if (exchName.Equals(Bybit.Name, StringComparison.InvariantCultureIgnoreCase)) return Bybit;
            if (exchName.Equals(Mexc.Name, StringComparison.InvariantCultureIgnoreCase)) return Mexc;
            throw new Exception($"Unknown exchange: {exchName}");
        }
    }


    internal abstract class AbstractExchange
    {
        public abstract string Name { get; }
        public abstract int Code { get; }

        public static readonly string UsdtName = "USDT";
        public abstract decimal SpotTakerFee { get; }
        public abstract decimal SpotMakerFee { get; }
        public abstract decimal FuturesTakerFee { get; }
        public abstract decimal FuturesMakerFee { get; }
        public double FundingRateDepth { get; set; } = 3;

        protected readonly Dictionary<decimal, DateTime> _bookState = [];

        protected decimal _priceStep;

        /// <summary>
        /// Get something like a EMA for FR. Last FR by time is the first item in array
        /// </summary>
        /// <param name="rates"></param>
        /// <returns></returns>
        protected static decimal GetEmaFundingRate(decimal[] rates)
        {
            var itemK = 0.3M;
            var ema = rates.Last();
            for (int i = rates.Length - 2; i >= 0; i--)
            {
                ema = itemK * rates[i] + (1 - itemK) * ema;
            }
            return ema;
        }

        protected static string DownloadWithCurl(string batchFile)
        {
            var batchDir = AppContext.BaseDirectory + "\\cmd\\";

            var psi = new ProcessStartInfo()
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchDir + batchFile}\"",
                WorkingDirectory = batchDir,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi) ?? throw new Exception("not lounched");
            process.WaitForExit();
            return File.ReadAllText(Path.ChangeExtension(batchDir + batchFile, "json"));
            // return process.StandardOutput.ReadToEnd();
        }

        /// <summary>
        /// Now get last funding rate, later planned to make kind of EMA FR
        /// </summary>
        /// <param name="coin"></param>
        /// <returns></returns>
        public abstract decimal GetDayFundingRate(string coin);
        public abstract void GetEarnProducts(List<EarnProduct> products, decimal minApr);
        public abstract void GetFundingRates(List<FundingRate> rates, decimal minRate);
        public abstract decimal CheckSpotBalance(string? coin = null);
        public abstract decimal CheckFuturesBalance(string? coin = null);
        public abstract decimal GetSpotPrice(string coin);
        public abstract decimal FindFunds(string coin, bool forSpot = true, decimal amount = 0);
        public abstract void EnterShort(string coin, decimal amount);
        public abstract decimal GetMaxLimit(string coin, bool isSpot);
        public abstract decimal GetMinLimit(string coin, bool isSpot);
        public abstract decimal GetOrderBookTicker(string coin, bool isSpot, bool isAsk);
    }
}