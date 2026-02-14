// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using Bnncmd.Strategy;

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
        public string StableCoin { get; set; } = string.Empty;
        public decimal SpotFee { get; set; } = 0;
        public string Comment { get; set; } = string.Empty;
        public int Term { get; set; } = 1;
        public decimal LimitMax { get; set; } = 0;
        public HedgeInfo? HedgeInfo { get; set; }

        // public AbstractExchange? FuturesExchange { get; set; }
        /// <summary>
        /// For binance USDT / USDC
        /// </summary>
        // public string FuturesPair { get; set; } = string.Empty;
        // public decimal DayFundingRate { get; set; }
        public decimal RealApr { get; set; }

        public override string ToString()
        {
            var apr = $"{Apr:0.##}%";
            return $"{ProductName,-19} | {apr,-7} | {Exchange.Name,-11} | {StableCoin,-5} | {Term} | {Comment}";
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


    static class StableCoin
    {
        public static readonly string FDUSD = "FDUSD";

        public static readonly string USDC = "USDC";

        public static readonly string USDT = "USDT";

        public static readonly string None = "-";
    }


    internal abstract class AbstractExchange
    {
        // Exchange Info
        public abstract string Name { get; }
        public abstract int Code { get; }

        // public abstract decimal SpotTakerFee { get; }
        // public abstract decimal SpotMakerFee { get; }
        public abstract decimal FuturesTakerFee { get; }
        public abstract decimal FuturesMakerFee { get; }
        public bool IsTest { get; set; }
        public double FundingRateDepth { get; set; } = 3;

        protected readonly Dictionary<decimal, DateTime> _bookState = [];

        protected decimal _priceStep;

        protected CryptoExchange.Net.Objects.Sockets.UpdateSubscription? _orderBookSubscription = null;

        protected static readonly object Locker = new();

        protected bool _isLock = false;

        public const string EmptyString = "";

        // Utils
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
        }

        /// <summary>
        /// Now get last funding rate, later planned to make kind of EMA FR
        /// </summary>
        /// <param name="coin"></param>
        /// <returns></returns>
        public abstract HedgeInfo[] GetDayFundingRate(string coin);
        public abstract void GetEarnProducts(List<EarnProduct> products, decimal minApr);
        public abstract void GetFundingRates(List<FundingRate> rates, decimal minRate);
        public abstract decimal GetSpotBalance(string? coin = null);
        public abstract decimal GetFuturesBalance(string? coin = null);

        // Exchange Info
        public abstract decimal GetSpotPrice(string coin, string stablecoin = EmptyString);
        public abstract decimal FindFunds(string stableCoin, bool forSpot = true, decimal amount = 0);
        public abstract decimal GetMaxLimit(string coin, bool isSpot, string stablecoin = EmptyString);
        public abstract decimal GetMinLimit(string coin, bool isSpot, string stablecoin = EmptyString);
        public abstract decimal GetOrderBookTicker(string coin, bool isSpot, bool isAsk);

        // Order routines
        public event Action<AbstractExchange>? ShortEntered;
        protected void FireShortEntered() => ShortEntered?.Invoke(this);
        public abstract void EnterShort(string coin, decimal amount, string stableCoin = EmptyString);
        public abstract void BuySpot(string coin, decimal amount);
        protected abstract Order PlaceFuturesOrder(string symbol, decimal amount, decimal price);
        protected abstract Order CancelFuturesOrder(Order order);

        protected Order? _futuresOrder = null;
        protected abstract void SubscribeOrderBookData(string symbol);
        protected abstract void UnsubscribeOrderBookData();

        private decimal _currAmount = 0;

        private bool _showRealtimeData = true;

        /// <summary>
        /// Get constant price
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="amount"></param>
        /// <param name="asks">array of [price, quontity]</param>
        /// <param name="bids"></param>
        protected void ProcessFuturesOrderBook(string symbol, decimal[][] asks, decimal[][] bids)
        {
            // var contractSize = 1; //  _contractInfo == null ? 1M : _contractInfo.ContractSize; // 0.0001M; // btc
            var bestAsk = asks[0][0];
            var bestRealAsk = GetTrueBestAsk([.. asks.Select(a => a[0])]);
            if ((bestRealAsk > 0) && (bestRealAsk - _priceStep > bids.First()[0])) bestRealAsk -= _priceStep;
            if (_showRealtimeData)
            {
                BnnUtils.ClearCurrentConsoleLine();
                // Console.Write($"{bestAsk} | {asks[0][1]} => {(bestRealAsk == -1 ? "searching" : bestRealAsk)} [ {_priceStep} ]", false); // / {contractSize * bestAsk * asks[0][1]:0.###}
                Console.Write($"{asks[2][0]} {asks[1][0]} {bestAsk} | {bids[0][0]} => {(bestRealAsk == -1 ? "searching..." : bestRealAsk)} [ {_priceStep} ]", false); // / {contractSize * bestAsk * asks[0][1]:0.###}
            }

            if ((bestRealAsk > 0) && (_orderBookSubscription != null))
            {
                lock (Locker)
                {
                    if (_isLock) return;
                    _isLock = true;
                }

                // bestRealAsk = 0.07M;
                if (_futuresOrder == null)
                {
                    _showRealtimeData = false;
                    BnnUtils.ClearCurrentConsoleLine();
                    Console.WriteLine($"Placing short order: {symbol}, {bestRealAsk} x {_currAmount}...");
                    _futuresOrder = PlaceFuturesOrder(symbol, _currAmount, bestRealAsk);
                    if (IsTest) Console.WriteLine($"Test order placed: {_futuresOrder.Id}");
                    _showRealtimeData = true;
                }
                else
                {
                    if (bestAsk > _futuresOrder.Price)
                    {
                        UnsubscribeOrderBookData();
                        BnnUtils.ClearCurrentConsoleLine();
                        Console.WriteLine($"Price raised ({bestAsk}), it seems the order is filled ({_futuresOrder})");
                        Console.WriteLine();
                        _futuresOrder = null;
                        FireShortEntered(); // in real environment fired via subsription
                        return;
                    }

                    if (bestRealAsk < _futuresOrder.Price) // (_futuresOrder != null) && 
                    {
                        UnsubscribeOrderBookData();
                        BnnUtils.ClearCurrentConsoleLine();
                        Console.WriteLine($"The best ask dropped ({bestAsk}), the order cancelling...");
                        CancelFuturesOrder(_futuresOrder);
                        Console.WriteLine($"The order cancelled");
                        // Console.Beep();
                        _futuresOrder = null;
                        return;

                        // var cancelationResult = _apiClient.UsdFuturesApi.Trading.CancelOrderAsync(symbol).Result;
                        // if (!cancelationResult.Success) throw new Exception($"Error while order cancelation: {cancelationResult.Error}");
                        // Console.WriteLine($"Cancelation result: {cancelationResult.Data}, quantity: {cancelationResult.Data.CumulativeQuantity}");
                    }
                }

                _isLock = false;
            }
        }

        protected decimal GetTrueBestAsk(decimal[] asks)
        {
            // add the best new price
            foreach (var a in asks)
            {
                if (!_bookState.ContainsKey(a)) _bookState.Add(a, DateTime.Now);
            }

            // remove all prices that are out of order book ( blinked prices )
            var keysToRemove = _bookState.Keys.Except(asks); // .Select(a => a[0]))
            foreach (var k in keysToRemove)
            {
                _bookState.Remove(k);
            }

            // get orders older than 10 seconds
            var niceAsks = _bookState
                .Where(a => a.Value < DateTime.Now.AddSeconds(-10))
                .OrderBy(a => a.Key);

            // best price
            var bestRealAsk = niceAsks.Any() ? niceAsks.First().Key : -1;
            return bestRealAsk;
        }

        /// <summary>
        ///  Find best persistant ask for 
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="amount"></param>
        public void ScanFutures(string symbol, decimal amount)
        {
            _currAmount = amount;
            _bookState.Clear();
            _isLock = false;
            _futuresOrder = null;
            _showRealtimeData = true;
            SubscribeOrderBookData(symbol);
        }
    }
}