// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Linq.Expressions;
using bnncmd.Exchanges;
using Bnncmd.Strategy;
using Org.BouncyCastle.Asn1.X509;

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

        public static readonly string USAT = "USAT";

        public static readonly string None = "-";

        public static bool Is(string coin)
        {
            if (coin.Equals(FDUSD, StringComparison.OrdinalIgnoreCase)) return true;
            if (coin.Equals(USDC, StringComparison.OrdinalIgnoreCase)) return true;
            if (coin.Equals(USDT, StringComparison.OrdinalIgnoreCase)) return true;
            if (coin.Equals(USAT, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    internal abstract class AbstractExchange
    {
        #region Variables

        // Exchange Info
        public abstract string Name { get; }
        public abstract int Code { get; }

        // public abstract decimal SpotTakerFee { get; }
        // public abstract decimal SpotMakerFee { get; }
        public abstract decimal FuturesTakerFee { get; }
        public abstract decimal FuturesMakerFee { get; }
        public bool IsTest { get; set; }
        public double FundingRateDepth { get; set; } = 90;

        protected readonly Dictionary<decimal, DateTime> BookState = [];

        protected string FuturesStableCoin { get; set; } = string.Empty;

        protected decimal _priceStep;

        protected CryptoExchange.Net.Objects.Sockets.UpdateSubscription? _spotOrderBookSubscription = null;

        protected CryptoExchange.Net.Objects.Sockets.UpdateSubscription? _futuresOrderBookSubscription = null;

        protected CryptoExchange.Net.Objects.Sockets.UpdateSubscription? _userFuturesDataSubscription = null;

        protected CryptoExchange.Net.Objects.Sockets.UpdateSubscription? _userSpotDataSubscription = null;

        protected readonly object Locker = new(); // static

        protected bool _isLock = false;
        protected bool IsSell { get; set; } = true;
        protected bool IsSpot { get; set; } = true;

        public const string EmptyString = "";

        private readonly Dictionary<string, HedgeInfo[]> _ratesStorage = [];

        #endregion

        #region Utils

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

        #endregion

        #region Balances

        /// <summary>
        /// Now get last funding rate, later planned to make kind of EMA FR
        /// </summary>
        /// <param name="coin"></param>
        /// <returns></returns>
        public abstract void GetEarnProducts(List<EarnProduct> products, decimal minApr);
        public abstract void GetFundingRates(List<FundingRate> rates, decimal minRate);
        public abstract decimal GetSpotBalance(string? coin = null);
        public abstract decimal FindFunds(string stableCoin, bool forSpot = true, decimal amount = 0);
        public abstract decimal GetFuturesBalance(string? coin = null);

        #endregion

        #region  Exchange Info

        public HedgeInfo[] GetDayFundingRateFromStorage(string coin)
        {
            if (_ratesStorage.TryGetValue(coin, out var rates)) return rates;
            var newRates = GetDayFundingRate(coin);
            _ratesStorage.Add(coin, newRates);
            return newRates;
        }
        public abstract HedgeInfo[] GetDayFundingRate(string coin);
        public abstract decimal GetSpotPrice(string coin, string stablecoin = EmptyString);
        public abstract decimal GetMaxLimit(string coin, bool isSpot, string stablecoin = EmptyString);
        public abstract decimal GetMinLimit(string coin, bool isSpot, string stablecoin = EmptyString);
        public abstract decimal GetOrderBookTicker(string coin, bool isSpot, bool isAsk);

        #endregion

        #region Order Routines

        public event Action<AbstractExchange>? ShortEntered;
        protected Order? _futuresOrder = null;
        private decimal _currAmount = 0;
        protected bool _showRealtimeData = true;
        protected void FireShortEntered() => ShortEntered?.Invoke(this);
        public abstract void EnterShort(string coin, decimal amount, string stableCoin = EmptyString);
        public abstract void ExitShort(string coin, decimal amount);
        protected abstract Order PlaceFuturesOrder(string symbol, decimal amount, decimal price);
        protected abstract Order CancelFuturesOrder(Order order);
        protected abstract void SubscribeFuturesOrderBook(string symbol);
        protected abstract void UnsubscribeFuturesOrderBook();

        protected Order? _spotOrder = null;
        protected abstract Order PlaceSpotOrder(string symbol, decimal amount, decimal price);
        protected abstract Order CancelSpotOrder(Order order);

        public event Action<AbstractExchange>? SpotSold;
        protected void FireSpotSold() => SpotSold?.Invoke(this);

        protected void ExecOrder(bool isSpot)
        {
            if ((isSpot && (_spotOrder == null)) || (!isSpot && (_futuresOrder == null))) return; // if event fired from different places
            _showRealtimeData = false;
            _isLock = true;
            if (isSpot)
            {
                _spotOrder = null;
                UnsubscribeSpotOrderBook();
                FireSpotSold();
            }
            else
            {
                _futuresOrder = null;
                // UnsubscribeFuturesOrderBook();
                Task.Run(() => UnsubscribeFuturesOrderBook()); // to prevent "Recursive write lock acquisitions not allowed in this mode"
                FireShortEntered(); // in real environment fired via subsription ?
            }
        }

        private Order PlaceAnOrder(string symbol, decimal amount, decimal price)
        {
            _showRealtimeData = false;
            Console.WriteLine($"New {(IsSpot ? "spot" : "futures")} {(IsSell ? "sell" : "buy")} order: {symbol}, {price} x {amount}...");
            // Console.WriteLine($"Placing {(IsSpot ? "spot" : "futures")} {(IsSell ? "sell" : "buy")} order: {symbol}, {price} x {amount}...");
            try
            {
                if (IsSpot)
                {
                    _spotOrder = PlaceSpotOrder(symbol, amount, price);
                    if (IsTest) Console.WriteLine($"Test order placed: {_spotOrder.Id}");
                    _showRealtimeData = true;
                    return _spotOrder;
                }
                else
                {
                    _futuresOrder = PlaceFuturesOrder(symbol, amount, price);
                    if (IsTest) Console.WriteLine($"Test order placed: {_futuresOrder.Id}");
                    _showRealtimeData = true;
                    return _futuresOrder;
                }
            }
            catch (Exception ex)
            {
                Console.Beep();
                Console.WriteLine(ex.Message); // "Error while order placing: " + 
                Environment.Exit(0);
                return CreateTestOrder(symbol, amount, price); // to provent Not all code return a value
            }
        }

        protected void ProcessOrderBook(string symbol, decimal[][] asks, decimal[][] bids) // acquiring
        {
            symbol = symbol.ToUpper();
            if (IsSell) ProcessOrderBookForSale(symbol, asks, bids);
            else ProcessOrderBookForAcquiring(symbol, asks, bids);
        }

        /// <summary>
        /// Get the first price
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="asks">array of [price, quontity]</param>
        /// <param name="bids"></param>
        protected void ProcessOrderBookForAcquiring(string symbol, decimal[][] asks, decimal[][] bids)
        {
            var order = IsSpot ? _spotOrder : _futuresOrder;
            var bestBid = bids[0][0];
            if (bestBid + _priceStep < asks[0][0]) bestBid += _priceStep;

            // Console.WriteLine($"{asks[0][0]} | {bids[0][0]} {bids[1][0]} {bids[2][0]} => {bestBid} [ {_priceStep} ]", false);
            // return;

            if (_showRealtimeData)
            {
                BnnUtils.ClearCurrentConsoleLine();
                Console.Write($"{asks[0][0]} | {bids[0][0]} {bids[1][0]} {bids[2][0]} => {bestBid} [ {_priceStep} ]", false);
            }
            if ((IsSpot && _spotOrderBookSubscription == null) || (!IsSpot && (_futuresOrderBookSubscription == null))) return;

            if (_isLock) return; // Recursive read lock acquisitions not allowed in this mode.
            lock (Locker)
            {
                if (_isLock) return;
                _isLock = true;
            }

            if (order == null)
            {
                BnnUtils.ClearCurrentConsoleLine();
                PlaceAnOrder(symbol, _currAmount, bestBid);
            }
            else
            {
                if (bestBid < order.Price)
                {
                    BnnUtils.ClearCurrentConsoleLine();
                    Console.WriteLine($"Price dropped ({bestBid}), it seems the order is filled ({order})\r\n");
                    ExecOrder(IsSpot);
                    return;
                }
            }

            _isLock = false;
        }

        /// <summary>
        /// Get constant price
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="asks">array of [price, quontity]</param>
        /// <param name="bids"></param>
        protected void ProcessOrderBookForSale(string symbol, decimal[][] asks, decimal[][] bids) // acquiring
        {
            // var contractSize = 1; //  _contractInfo == null ? 1M : _contractInfo.ContractSize; // 0.0001M; // btc
            var order = IsSpot ? _spotOrder : _futuresOrder;
            var bestAsk = asks[0][0];
            var bestRealAsk = GetTrueBestAsk([.. asks.Select(a => a[0])]);
            if ((bestRealAsk > 0) && (bestRealAsk - _priceStep > bids.First()[0])) bestRealAsk -= _priceStep;
            if (_showRealtimeData)
            {
                BnnUtils.ClearCurrentConsoleLine();
                Console.Write($"{asks[2][0]} {asks[1][0]} {bestAsk} | {bids[0][0]} => {(bestRealAsk == -1 ? "searching..." : bestRealAsk)} [ {_priceStep} ]", false); // / {contractSize * bestAsk * asks[0][1]:0.###}
                // Console.WriteLine($"{tempCounter}: {asks[2][0]}/{asks[2][1]} {asks[1][0]}/{asks[1][1]} {asks[0][0]}/{asks[0][1]} | {bids[0][0]}/{bids[0][1]} {bids[1][0]}/{bids[1][1]} {bids[2][0]}/{bids[2][1]} => {(bestRealAsk == -1 ? "searching..." : bestRealAsk)} [ {_priceStep} ]", false); // / {contractSize * bestAsk * asks[0][1]:0.###}
            }
            if ((bestRealAsk <= 0) || (IsSpot && (_spotOrderBookSubscription == null)) || (!IsSpot && (_futuresOrderBookSubscription == null))) return;

            lock (Locker)
            {
                if (_isLock) return;
                _isLock = true;
            }

            if (order == null)
            {
                BnnUtils.ClearCurrentConsoleLine();
                PlaceAnOrder(symbol, _currAmount, bestRealAsk);
            }
            else
            {
                if (bestAsk > order.Price)
                {
                    BnnUtils.ClearCurrentConsoleLine();
                    Console.WriteLine($"Price raised ({bestAsk}), it seems the order is filled ({order})");
                    ExecOrder(IsSpot);
                    return;
                }

                if (bestRealAsk < order.Price)
                {
                    if (IsSpot) CancelSpotOrder(order);
                    else CancelFuturesOrder(order);

                    BnnUtils.ClearCurrentConsoleLine();
                    Console.WriteLine($"The best ask dropped ({bestAsk}), the order cancelled: {order.Id}");
                    PlaceAnOrder(symbol, _currAmount, bestRealAsk);
                }
            }

            _isLock = false;
        }

        protected decimal GetTrueBestAsk(decimal[] asks)
        {
            // add the best new price
            foreach (var a in asks)
            {
                if (!BookState.ContainsKey(a)) BookState.Add(a, DateTime.Now);
            }

            // remove all prices that are out of order book ( blinked prices )
            var keysToRemove = BookState.Keys.Except(asks); // .Select(a => a[0]))
            foreach (var k in keysToRemove)
            {
                BookState.Remove(k);
            }

            // get orders older than 10 seconds
            var niceAsks = BookState
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
        public void ScanOrderBook(string symbol, decimal amount, bool isSpot, bool isSell)
        {
            _currAmount = amount;
            IsSpot = isSpot;
            IsSell = isSell;
            BookState.Clear();
            _isLock = false;
            _showRealtimeData = true;
            if (isSpot)
            {
                _spotOrder = null;
                SubscribeSpotOrderBook(symbol);
            }
            else
            {
                _futuresOrder = null;
                SubscribeFuturesOrderBook(symbol);
            }
        }

        protected Order CreateTestOrder(string symbol, decimal amount, decimal price)
        {
            return new Order()
            {
                Id = $"test_order_{Name.ToLower()}",
                Price = price,
                Amount = amount,
                Symbol = symbol,
                IsBuyer = !IsSell
            };
        }

        #endregion

        #region Spot Routines

        protected abstract void SubscribeSpotOrderBook(string symbol);
        protected abstract void UnsubscribeSpotOrderBook();
        public abstract void BuySpot(string coin, decimal amount, string stableCoin = EmptyString);
        public abstract void SellSpot(string coin, decimal amount, string stableCoin = EmptyString);

        #endregion
    }
}