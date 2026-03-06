// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Binance.Net.Clients;
using Bnncmd;
using Bnncmd.Strategy;
using CryptoExchange.Net.Objects;
using Newtonsoft.Json;
using System.Net;
using System.Net.WebSockets;
using Bybit.Net.Enums;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Bnncmd.Strategy
{
    internal class EarnParams : DealParams
    {
        public override string ToString() => "EarnParams / ToString";

        public override string GetParamsDesciption() => string.Empty;
    }

    internal class HedgeInfo(AbstractExchange exchanger)
    {
        public AbstractExchange Exchanger { get; set; } = exchanger;

        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// Ema FR * Day Intervals Count
        /// </summary>
        public decimal EmaFundingRate { get; set; }

        public decimal EmaApr { get; set; }

        public decimal ThreeMonthsApr { get; set; }

        public decimal CurrentFundingRate { get; set; }

        public decimal Fee { get; set; }
    }

    /*public enum EarnState
    {
        // Updating,
        Waiting,
        CollectFuturesInformation,
        WaitingForNarrowSpread,
        SpotEnterOrderCreated,
        FuturesEnterOrderCreated,
        WaitingForWideSpread,
        SpotExitOrderCreated,
        FuturesExitOrderCreated
    }*/

    internal class Earn : BaseStrategy
    {
        #region Constructor and Variables

        public Earn(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}");
            _dealParams = new EarnParams();

            // FindBestProduct();
        }

        private AbstractExchange _spotExchange = Exchange.Binance;

        private AbstractExchange _futuresExchange = Exchange.Mexc;

        private decimal _amount = 0;

        #endregion

        #region Best Easy Earn Products

        public static void FindBestProduct()
        {
            var fundingRateDepth = 90;
            List<EarnProduct> products = [];
            var minApr = 20;

            // Get earn products from all exchanges
            var exchanges = new List<AbstractExchange> { Exchange.Binance, Exchange.Bybit, Exchange.Mexc };
            // var exchanges = new List<AbstractExchange> { Exchange.Mexc };
            foreach (var e in exchanges)
            {
                e.GetEarnProducts(products, minApr);
            }

            // Get funding rates for hedging from available futures
            Console.WriteLine("\r\n===========================\r\n");
            var shortExchanges = new List<AbstractExchange> { Exchange.Binance, Exchange.Bybit };
            // var shortExchanges = new List<AbstractExchange> { Exchange.Binance };
            foreach (var p in products)
            {
                // if (p.ProductName != "LA") continue;
                Console.WriteLine($"{p}");

                // stablecoin
                if (StableCoin.Is(p.ProductName))
                {
                    p.RealApr = p.Apr;
                    continue;
                }

                // hedges
                foreach (var e in shortExchanges)
                {
                    e.FundingRateDepth = fundingRateDepth;
                    // var his = e.GetDayFundingRate(p.ProductName);
                    var his = e.GetDayFundingRateFromStorage(p.ProductName);
                    foreach (var hi in his)
                    {
                        var actualFundingRate = p.Term == 1 ? hi.EmaFundingRate : hi.ThreeMonthsApr / 365;
                        var dayProfit = ((p.Apr / 365 + actualFundingRate) * p.Term - p.SpotFee - hi.Fee) / p.Term / 2;
                        var realApr = 365 * dayProfit;
                        Console.WriteLine($"   {e.Name} {hi.Symbol} {(p.Term == 1 ? "ema" : "3 month")} day funding rate: {actualFundingRate:0.###} => {realApr:0.###}  [ (({p.Apr:0.###} / 365 + {actualFundingRate:0.###}) * {p.Term} - {p.SpotFee} - {hi.Fee}) / {p.Term} / 2 = {dayProfit:0.###} ]");
                        Console.WriteLine($"   ema apr: {hi.EmaApr:0.###}, 3 months apr: {hi.ThreeMonthsApr:0.###}, current rate: {hi.CurrentFundingRate:0.####}");
                        if ((p.HedgeInfo == null) || (p.RealApr < realApr))
                        {
                            p.HedgeInfo = hi;
                            p.RealApr = realApr;
                        }
                    }
                }
            }

            // Out results
            Console.WriteLine("\r\n===========================\r\n");
            var lastApr = 100M;
            var sortedByRealApr = products.OrderByDescending(p => p.RealApr);
            foreach (var p in sortedByRealApr)
            {
                var arpStr = $"{p.RealApr:0.###}";
                var futuresExchange = $"{(p.HedgeInfo == null ? string.Empty : p.HedgeInfo.Exchanger.Name)}";
                var term = $"{p.Term:0}";
                var futuresCoin = $"{(p.HedgeInfo == null ? string.Empty : p.HedgeInfo.Symbol[p.ProductName.Length..])}";
                var currFR = $"{(p.HedgeInfo == null ? 0 : p.HedgeInfo.CurrentFundingRate):0.###}";
                if ((lastApr >= 10) && (p.RealApr < 10)) Console.WriteLine();
                lastApr = p.RealApr;
                Console.WriteLine($"{p.ProductName,-23} | {arpStr,-9} | {p.Exchange.Name,-7} | {p.StableCoin,-5} | {futuresExchange,-7} | {futuresCoin,-5} | {term,-3} | {currFR,-6} | {p.LimitMax,-9}");
            }
        }

        #endregion

        #region Buy-Sell Coins

        public bool ControlDeal(string coin, string spotStablecoin = "", string futuresStablecoin = "")
        {
            // Console.WriteLine($"{spotStablecoin} / {futuresStablecoin}");
            // return false;

            var spotPrice = _spotExchange.GetSpotPrice(coin, spotStablecoin);
            if (spotPrice == 0) throw new Exception($"Cannot detect spot price on {_spotExchange.Name}");

            var requiredUsdAmount = _amount * spotPrice * 1.03M;
            Console.WriteLine($"{_spotExchange.Name} {coin} spot price: {spotPrice} => usd required amount: {requiredUsdAmount:0.###}");
            var checksAreOk = true;

            // spot exchange rests futuresStablecoin
            var spotRest = _spotExchange.GetSpotBalance(spotStablecoin);
            checksAreOk = checksAreOk && spotRest >= requiredUsdAmount;
            Console.WriteLine($"{_spotExchange.Name} spot stable coin rest: {spotRest:0.###} => {(spotRest >= requiredUsdAmount ? "ok" : "not enaugh :(")}");
            decimal spotReserve = 0;
            if (spotRest < requiredUsdAmount) spotReserve = _spotExchange.FindFunds(spotStablecoin, true);

            // futures exchange rests
            var futuresRest = _futuresExchange.GetFuturesBalance(futuresStablecoin == string.Empty ? StableCoin.USDT : futuresStablecoin);
            checksAreOk = checksAreOk && futuresRest >= requiredUsdAmount;
            decimal futuresReserve = 0;
            var futureCoinName = futuresStablecoin == string.Empty ? string.Empty : futuresStablecoin + ' ';
            Console.WriteLine($"{_futuresExchange.Name} futures {futureCoinName}rest: {futuresRest:0.###} => {(futuresRest >= requiredUsdAmount ? "ok" : "not enaugh :(")}");
            if (futuresRest < requiredUsdAmount) futuresReserve = _futuresExchange.FindFunds(futuresStablecoin, false);

            // ... max limits
            var maxSpotOrderLimit = _spotExchange.GetMaxLimit(coin, true);
            checksAreOk = checksAreOk && maxSpotOrderLimit >= _amount;
            Console.WriteLine($"{_spotExchange.Name} spot max limit: {maxSpotOrderLimit:0.###} {coin} => {(maxSpotOrderLimit >= requiredUsdAmount ? "ok" : "too large :(")}");

            var maxFuturesOrderLimit = _futuresExchange.GetMaxLimit(coin, false, futuresStablecoin);
            checksAreOk = checksAreOk && maxFuturesOrderLimit >= _amount;
            Console.WriteLine($"{_futuresExchange.Name} futures max limit: {maxFuturesOrderLimit:0.###} {coin} => {(maxFuturesOrderLimit >= requiredUsdAmount ? "ok" : "too large :(")}");

            // ... min limits
            var minSpotOrderLimit = _spotExchange.GetMinLimit(coin, true);
            checksAreOk = checksAreOk && minSpotOrderLimit <= _amount;
            Console.WriteLine($"{_spotExchange.Name} spot min limit: {minSpotOrderLimit:0.###} {coin} => {(minSpotOrderLimit <= requiredUsdAmount ? "ok" : "too little :(")}");

            var minFuturesOrderLimit = _futuresExchange.GetMinLimit(coin, false, futuresStablecoin);
            checksAreOk = checksAreOk && minFuturesOrderLimit <= _amount;
            Console.WriteLine($"{_futuresExchange.Name} futures min limit: {minFuturesOrderLimit:0.###} {coin} => {(minFuturesOrderLimit <= requiredUsdAmount ? "ok" : "too little :(")}");

            // transfers
            Console.WriteLine();
            if ((spotReserve >= requiredUsdAmount) && (spotRest < requiredUsdAmount))
            {
                Console.WriteLine($"Do you want to transfer some {(spotStablecoin == "" ? "assets" : spotStablecoin.ToUpper())} to {_spotExchange.Name} spot wallet?");
                var commandTrans = Console.ReadLine();
                if ((commandTrans != null) && (commandTrans.ToLower()[0] == 'y')) _spotExchange.FindFunds(spotStablecoin, true, 1.015M * requiredUsdAmount - spotRest);
                else return checksAreOk;
            }

            if ((futuresReserve + futuresRest >= requiredUsdAmount) && (futuresRest < requiredUsdAmount))
            {
                Console.WriteLine($"Do you want to transfer assets to {_futuresExchange.Name} futures wallet?");
                var commandFutures = Console.ReadLine();
                Console.WriteLine();
                if ((commandFutures != null) && (commandFutures.ToLower()[0] == 'y')) _futuresExchange.FindFunds(string.Empty, false, 1.015M * requiredUsdAmount - futuresRest); // futuresRest = 
                else return checksAreOk;
            }
            return checksAreOk;
        }

        private void SetTestMode()
        {
            _spotExchange.IsTest = true;
            _futuresExchange.IsTest = _spotExchange.IsTest;
            if (_spotExchange.IsTest) Console.WriteLine($"THE PROGRAM WORKS IN TEST MODE!\n\r");
        }

        public void SellPair(string coin, AbstractExchange spotExchange, AbstractExchange futuresExchange)
        {
            _spotExchange = spotExchange;
            _futuresExchange = futuresExchange;
            SetTestMode();

            // spot exchange rest
            var spotRest = _spotExchange.GetSpotBalance(coin); // 0;// 
            Console.WriteLine($"{_spotExchange.Name} spot {coin} rest: {spotRest:0.###}");

            // futures exchange rest
            var futuresRest = _futuresExchange.GetFuturesBalance(coin); //  0;// 
            Console.WriteLine($"{_futuresExchange.Name} futures {coin} rest: {futuresRest:0.###}");

            Console.WriteLine("\n\rDo you want to start exit with spot order?");
            var command = Console.ReadLine();
            if ((command == null) || (command.ToLower()[0] != 'y')) return;

            Console.WriteLine();
            Console.WriteLine($"{spotExchange.Name} spot sell order placing...");

            spotExchange.SpotSold += e =>
            {
                Console.Beep();
                Console.WriteLine($"\n\r{futuresExchange.Name} futures short position closing...");
                futuresExchange.ExitShort(coin, futuresRest);
            };
            spotExchange.SellSpot(coin, spotRest); // spotRest
        }

        public void BuyPair(string coin, AbstractExchange spotExchange, AbstractExchange futuresExchange, decimal amount, string spotStablecoin = "", string futuresStablecoin = "")
        {
            _spotExchange = spotExchange;
            _futuresExchange = futuresExchange;
            _amount = amount;
            SetTestMode();

            spotStablecoin = spotStablecoin.ToUpper();
            futuresStablecoin = futuresStablecoin.ToUpper();

            Console.WriteLine($"Requested {coin} amount: {amount}\n\r");

            // if (!ControlDeal(coin, spotStablecoin, futuresStablecoin)) return;

            if (coin.Equals("ETH")) BuyHighLiquidCoin(coin, spotStablecoin, futuresStablecoin);
            else BuyLowLiquidCoin(coin, amount, spotStablecoin, futuresStablecoin);
        }

        private void BuyLowLiquidCoin(string coin, decimal amount, string spotStablecoin, string futuresStablecoin)
        {
            // buy futures than spot
            Console.WriteLine("Do you want to start enter with futures order using low-liquid strategy?");
            var command = Console.ReadLine();
            if ((command == null) || (command.ToLower()[0] != 'y')) return;

            Console.WriteLine();
            Console.WriteLine($"{_futuresExchange.Name} futures short position opening..."); //  [ {Environment.CurrentManagedThreadId} ]
            _futuresExchange.ShortEntered += e =>
            {
                Console.Beep();
                Console.WriteLine($"\n\r{_spotExchange.Name} spot buy order placing...");
                _spotExchange.BuySpot(coin, amount, spotStablecoin);
            };
            _futuresExchange.EnterShort(coin, amount, futuresStablecoin);
        }

        #endregion

        #region High Liquid Routines

        private decimal _bestFuturesAskPrice = decimal.MaxValue;
        private decimal _bestSpotBidPrice = decimal.MaxValue;
        private decimal _currDelta = 0;
        private decimal _minDelta = decimal.MinValue;
        private decimal _maxDelta = decimal.MaxValue;
        private decimal _medianDelta = 0;
        private decimal _enterDelta = 0;
        private decimal _exitDelta = 0;
        private readonly decimal _enterThreshold = 0.01M;
        private readonly StringBuilder _deltaFile = new();
        private readonly object _locker = new(); // static
        private readonly bool _writeLog = false;
        private DateTime _lastLogUpdate = DateTime.Now;
        private decimal _balance = 0;
        // private DateTime _lastBeepTime = DateTime.Now;

        private System.Threading.Timer? _printTimer = null; // declare as GC disposed timer

        private Dictionary<DateTime, decimal> _deltas = [];

        private readonly int _spanToAnalyze = 7;

        private static DateTime _lastAnalyseTime = DateTime.Now;

        private SpreadState _state = SpreadState.CollectInformation;

        private void SaveStatistics()
        {
            _deltaFile.AppendLine($"{BnnUtils.GetUnixNow()};{_bestFuturesAskPrice};{_bestSpotBidPrice};{_currDelta};");
            if ((DateTime.Now.Minute != _lastLogUpdate.Minute) && (DateTime.Now.Minute % 10 == 0))
            {
                var csvFileName = $"f-s-deltas-{DateTime.Now.ToString().Replace('.', '-').Replace('/', '-').Replace(':', '-').Replace(' ', '-')}.csv";
                File.WriteAllText(csvFileName, _deltaFile.ToString());
                _deltaFile.Clear();
            };
            _lastLogUpdate = DateTime.Now;
        }

        private void CalcDelta(string coin, string spotStablecoin, string futuresStablecoin)
        {
            if ((_bestFuturesAskPrice == decimal.MaxValue) || (_bestSpotBidPrice == decimal.MaxValue)) return;
            _currDelta = (_bestFuturesAskPrice - _bestSpotBidPrice) / _bestSpotBidPrice * 100;
            _deltas.Add(DateTime.Now, _currDelta);


            if ((_state == SpreadState.WaitingForEnter) && (_currDelta > _enterDelta)) // && (_futuresExchange.FuturesOrder == null)
            {
                _state = SpreadState.Updating;
                OutCurrentState();
                Console.WriteLine();
                _futuresExchange.IsSpot = false;
                _futuresExchange.IsSell = true;
                // BnnUtils.Log();
                _futuresExchange.PlaceAnOrder(coin, futuresStablecoin, _amount, _bestFuturesAskPrice);
                _state = SpreadState.FuturesEnterOrderCreated;
                // Console.Beep();
            }

            if (_writeLog) SaveStatistics();
        }

        private void ProcessFuturesOrder(string coin, string spotStablecoin)
        {
            if (_futuresExchange.FuturesOrder == null) return;

            if (_bestFuturesAskPrice > _futuresExchange.FuturesOrder.Price)
            {
                _state = SpreadState.Updating;
                BnnUtils.Log($"Price raised ({_bestFuturesAskPrice}), it seems the futures order is filled ({_futuresExchange.FuturesOrder})");
                _spotExchange.IsSpot = true;
                _spotExchange.IsSell = false;
                _spotExchange.PlaceAnOrder(coin, spotStablecoin, _amount, _bestSpotBidPrice);
                // _futuresExchange.FuturesOrder = null;
                _state = SpreadState.SpotEnterOrderCreated;
                return;
            }

            if ((_bestFuturesAskPrice < _futuresExchange.FuturesOrder.Price) || (_currDelta < _enterDelta))
            {
                if (_bestFuturesAskPrice < _futuresExchange.FuturesOrder.Price) BnnUtils.Log($"The best ask dropped ({_bestFuturesAskPrice}), the futures order cancelled: {_futuresExchange.FuturesOrder.Id}"); // ; CD: {_currDelta}
                if (_currDelta < _enterDelta) BnnUtils.Log($"Current delta dropped ({_currDelta:0.###}), the order cancelled: {_futuresExchange.FuturesOrder.Id}");
                // _futuresExchange.FuturesOrder = null;
                _state = SpreadState.WaitingForEnter;
            }
        }

        private void ProcessSpotOrder(string coin, string spotStablecoin)
        {
            if (_spotExchange.SpotOrder == null) return;

            if (_bestSpotBidPrice < _spotExchange.SpotOrder.Price)
            {
                _state = SpreadState.Updating;
                BnnUtils.Log($"Price droped ({_bestSpotBidPrice}), it seems the order is filled ({_spotExchange.SpotOrder})");

                // _state = SpreadState.WaitingForExit;
                if (_futuresExchange.FuturesOrder != null)
                {
                    var dealResult = (_futuresExchange.FuturesOrder.Price / _spotExchange.SpotOrder.Price - 1) * 100;
                    _balance = _balance + dealResult - _medianDelta;
                    BnnUtils.Log($"Deal delta: {dealResult:0.###} => {_balance:0.###}", false);
                    BnnUtils.Log(string.Empty, false);
                    BnnUtils.Log(string.Empty, false);
                    _state = SpreadState.WaitingForEnter;

                }
            }

            if (_bestSpotBidPrice > _spotExchange.SpotOrder.Price)
            {
                _state = SpreadState.Updating;
                BnnUtils.Log($"The best ask raised ({_bestSpotBidPrice}), the order cancelled: {_spotExchange.SpotOrder.Id}");
                _spotExchange.PlaceAnOrder(coin, spotStablecoin, _amount, _bestSpotBidPrice);
                _state = SpreadState.SpotEnterOrderCreated;
            }
        }

        private void BuyHighLiquidCoin(string coin, string spotStablecoin, string futuresStablecoin)
        {
            // Console.WriteLine("Do you want to start analyze spread using high-liquid strategy?");
            _futuresExchange.SubscribeBookTickerFutures(coin + futuresStablecoin, (bestAskPrice, bestAskQuantity, bestBidPrice, bestBidQuantity) =>
            {
                if (_bestFuturesAskPrice == bestAskPrice) return;
                _bestFuturesAskPrice = bestAskPrice;

                if (_state == SpreadState.FuturesEnterOrderCreated) ProcessFuturesOrder(coin, spotStablecoin);

                CalcDelta(coin, spotStablecoin, futuresStablecoin);
            });

            _spotExchange.SubscribeBookTickerSpot(coin + spotStablecoin, (bestAskPrice, bestAskQuantity, bestBidPrice, bestBidQuantity) =>
            {
                if (_bestSpotBidPrice == bestBidPrice) return;
                _bestSpotBidPrice = bestBidPrice;

                if (_state == SpreadState.SpotEnterOrderCreated) ProcessSpotOrder(coin, spotStablecoin);

                CalcDelta(coin, spotStablecoin, futuresStablecoin);
            });

            _printTimer = new System.Threading.Timer(e => Task.Run(() => ProcessData()), null, 0, 150);
        }

        private void OutCurrentState()
        {
            BnnUtils.ClearCurrentConsoleLine();
            Console.Write($"{_futuresExchange.Name} futures {_bestFuturesAskPrice} | {_bestSpotBidPrice} {_spotExchange.Name} spot => {_currDelta:0.###}% [{_minDelta:0.###}..{_exitDelta:0.###}..{_medianDelta:0.###}..{_enterDelta:0.###}..{_maxDelta:0.###}] {_state}...");
        }

        private void ProcessData()
        {
            if ((_bestFuturesAskPrice == decimal.MaxValue) || (_bestSpotBidPrice == decimal.MaxValue) || (_state == SpreadState.Updating)) return;
            lock (_locker)
            {
                OutCurrentState();
                // Console.Write($"{_futuresExchange.Name} futures {_bestFuturesAskPrice} | {_bestSpotBidPrice} {_spotExchange.Name} spot => {_currDelta:0.#####}% [{_minDelta:0.###}..{_maxDelta:0.###}]");
            }

            // calc statistics
            if (_lastAnalyseTime.Minute != DateTime.Now.Minute)
            {
                var sortedDeltas = _deltas.Values.Order().ToArray();
                _medianDelta = sortedDeltas[sortedDeltas.Length / 2];
                _minDelta = sortedDeltas[0];
                _maxDelta = sortedDeltas[^1];
                _enterDelta = sortedDeltas[(int)(sortedDeltas.Length * (1 - _enterThreshold))];
                _exitDelta = sortedDeltas[(int)(sortedDeltas.Length * _enterThreshold)];

                // delete the first minute data !!!
                if (((DateTime.Now - _deltas.First().Key).TotalMinutes > _spanToAnalyze) && (_state == SpreadState.CollectInformation)) _state = SpreadState.WaitingForEnter;
            }
            _lastAnalyseTime = DateTime.Now;

            /* if (_currDelta > 0) // && ((DateTime.Now - _lastBeepTime).TotalSeconds > 50))
            {
                // BnnUtils.ClearCurrentConsoleLine();
                Console.WriteLine($"   ( {DateTime.Now:HH:mm:ss.fff} )"); // : {delta:0.####}
                if ((DateTime.Now - _lastBeepTime).TotalSeconds > 50) Console.Beep();
                _lastBeepTime = DateTime.Now;
            }*/
        }

        #endregion

        #region Monitor Routines

        private static void OutCalendar()
        {
            Console.WriteLine($"\n\r *** Calendar *** \n\r");

            var snpDay = new DateTime(2026, 03, 01);
            Console.WriteLine($"S&P500: {snpDay:dd.MM.yyyy}{(snpDay <= DateTime.Now ? "!!!" : string.Empty)}");
            if (snpDay > DateTime.Now) Console.Beep();

            var bondsDay = new DateTime(2026, 05, 01);
            Console.WriteLine($"Bonds: {bondsDay:dd.MM.yyyy}{(bondsDay <= DateTime.Now ? "!!!" : string.Empty)}");
            if (bondsDay > DateTime.Now) Console.Beep();
        }

        private static void OutAssetsPrices()
        {
            Console.WriteLine($"\n\r *** Assets *** \n\r");

            var goldPrice = Exchange.Bybit.GetSpotPrice("XAUT");
        }

        private static void OutMonitorInfo(object? state)
        {
            Console.Clear();
            OutCalendar();
            OutAssetsPrices();

            // check atives lows 

            // curr active earns state

            // new best earn

            // total crypto balance
        }

        public static void Monitor()
        {
            _ = new System.Threading.Timer(OutMonitorInfo, null, 10 * 60 * 1000, Timeout.Infinite);
            OutMonitorInfo(null);
        }

        #endregion

        #region Parent Methods
        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            stopLossValue = 0;
            return double.MaxValue;
        }

        public override string GetName() { return "Earn"; }

        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            return 0;
        }

        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            return double.MaxValue;
        }

        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            // override abstract method
        }

        public override void Start() => BnnUtils.Log($"We are beginning...");

        public override string GetCurrentInfo() => string.Empty;

        #endregion
    }
}
