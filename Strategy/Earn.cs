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
        public Earn(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}");
            _dealParams = new EarnParams();

            // FindBestProduct();
        }

        private AbstractExchange _spotExchange = Exchange.Binance;

        private AbstractExchange _futuresExchange = Exchange.Mexc;

        public bool ControlDeal(string coin, decimal amount, string spotStablecoin = "", string futuresStablecoin = "")
        {
            var spotPrice = _spotExchange.GetSpotPrice(coin, spotStablecoin);
            if (spotPrice == 0) throw new Exception($"Cannot detect spot price on {_spotExchange.Name}");

            var requiredUsdAmount = amount * spotPrice * 1.03M;
            Console.WriteLine($"{_spotExchange.Name} {coin} spot price: {spotPrice} => usd required amount: {requiredUsdAmount:0.###}");
            var checksAreOk = true;

            // spot exchange rests 
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
            checksAreOk = checksAreOk && maxSpotOrderLimit >= amount;
            Console.WriteLine($"{_spotExchange.Name} spot max limit: {maxSpotOrderLimit:0.###} {coin} => {(maxSpotOrderLimit >= requiredUsdAmount ? "ok" : "too large :(")}");

            var maxFuturesOrderLimit = _futuresExchange.GetMaxLimit(coin, false, futuresStablecoin);
            checksAreOk = checksAreOk && maxFuturesOrderLimit >= amount;
            Console.WriteLine($"{_futuresExchange.Name} futures max limit: {maxFuturesOrderLimit:0.###} {coin} => {(maxFuturesOrderLimit >= requiredUsdAmount ? "ok" : "too large :(")}");

            // ... min limits
            var minSpotOrderLimit = _spotExchange.GetMinLimit(coin, true);
            checksAreOk = checksAreOk && minSpotOrderLimit <= amount;
            Console.WriteLine($"{_spotExchange.Name} spot min limit: {minSpotOrderLimit:0.###} {coin} => {(minSpotOrderLimit <= requiredUsdAmount ? "ok" : "too little :(")}");

            var minFuturesOrderLimit = _futuresExchange.GetMinLimit(coin, false, futuresStablecoin);
            checksAreOk = checksAreOk && minFuturesOrderLimit <= amount;
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
            SetTestMode();

            spotStablecoin = spotStablecoin.ToUpper();
            futuresStablecoin = spotStablecoin.ToUpper();

            /* Console.WriteLine($"Requested {coin} amount: {amount}\n\r");

            if (!ControlDeal(coin, amount, spotStablecoin, futuresStablecoin)) return;

            // buy futures than spot
            Console.WriteLine("Do you want to start enter with futures order?");
            var command = Console.ReadLine();
            if ((command == null) || (command.ToLower()[0] != 'y')) return;

            Console.WriteLine();
            Console.WriteLine($"{futuresExchange.Name} futures short position opening..."); //  [ {Environment.CurrentManagedThreadId} ]*/
            futuresExchange.ShortEntered += e =>
            {
                Console.Beep();
                Console.WriteLine($"\n\r{spotExchange.Name} spot buy order placing...");
                spotExchange.BuySpot(coin, amount);
            };
            futuresExchange.EnterShort(coin, amount, futuresStablecoin);
        }

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
