// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bnncmd;

namespace bnncmd.Strategy
{
    internal class FundingHedgeParams : DealParams
    {
        public override string ToString() => "EarnParams / ToString";

        public override string GetParamsDesciption() => string.Empty;
    }


    internal class FundingHedge : BaseStrategy
    {
        public FundingHedge(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}");
            _dealParams = new FundingHedgeParams();

            FindBestRates();
        }

        /*public static void BuyPair(string coin, AbstractExchange spotExchange, AbstractExchange futuresExchange, decimal amount)
        {
            Console.WriteLine($"requested {coin} amount: {amount}");

            var spotPrice = spotExchange.GetSpotPrice(coin);
            if (spotPrice == 0) throw new Exception("Cannot detect spot price");

            var requiredUsdAmount = amount * spotPrice * 1.01M;
            Console.WriteLine($"{spotExchange.Name} {coin} spot price: {spotPrice} => usd required amount: {requiredUsdAmount:0.###}");

            var spotRest = spotExchange.CheckSpotBalance();
            Console.WriteLine($"{spotExchange.Name} spot stable coin rest: {spotRest:0.###} => {(spotRest >= requiredUsdAmount ? "ok" : "not enaugh :(")}");
            if (spotRest < requiredUsdAmount) spotExchange.FindFunds(string.Empty);

            var futuresRest = futuresExchange.CheckFuturesBalance(AbstractExchange.UsdtName);
            Console.WriteLine($"{futuresExchange.Name} futures {AbstractExchange.UsdtName} rest: {futuresRest:0.###} => {(futuresRest >= requiredUsdAmount ? "ok" : "not enaugh :(")}");

            var maxOrderLimit = spotExchange.GetMaxLimit(coin, true);
            Console.WriteLine($"{spotExchange.Name} spot limit: {maxOrderLimit:0.###} => {(maxOrderLimit >= amount ? "ok" : "too large :(")}");

            maxOrderLimit = futuresExchange.GetMaxLimit(coin, false);
            Console.WriteLine($"{futuresExchange.Name} futures limit: {maxOrderLimit:0.###} => {(maxOrderLimit >= amount ? "ok" : "too large :(")}");
        }*/

        public static void FindBestRates()
        {
            Console.WriteLine();
            List<FundingRate> rates = [];

            var exchanges = new List<AbstractExchange> { Exchange.Binance, Exchange.Bybit }; // , Exchange.Mexc
            // var exchanges = new List<AbstractExchange> { Exchange.Mexc };
            foreach (var e in exchanges)
            {
                Console.WriteLine($"{e.Name}...");
                e.GetFundingRates(rates, 0.02M);
            }

            Console.WriteLine("\r\n===========================\r\n");
            var sortedByRate = rates.OrderByDescending(p => p.CurrRate);

            // exchanges = [Exchange.Binance];
            exchanges = [Exchange.Binance, Exchange.Bybit, Exchange.Mexc];
            foreach (var fr in sortedByRate)
            {
                var coin = fr.Symbol[..^4].Trim('_');
                var bestFuturesAsk = fr.Exchange.GetOrderBookTicker(coin, false, true);
                Console.WriteLine($"{fr} => futures best ask : {bestFuturesAsk}");

                foreach (var e in exchanges)
                {
                    // var bestBidPrice = e.GetSpotPrice(coin);
                    var bestBidPrice = e.GetOrderBookTicker(coin, true, false);
                    if (bestBidPrice == 0) continue;
                    var spread = (bestFuturesAsk / bestBidPrice - 1) * 100;
                    if (spread > 10) continue; // ARCUSDT on MEXC and Binance are different
                    // Console.WriteLine($"   {e.Name} best spot bid: {bestBidPrice}");
                    Console.WriteLine($"   {e.Name} spread: {spread:0.###}% [ {bestFuturesAsk} / {bestBidPrice}]");
                    var realProfit = fr.CurrRate / 2 - fr.Exchange.FuturesMakerFee - e.SpotMakerFee;
                    // var dayProfit = ((p.Apr / 365 + fr) * p.Term - p.Exchange.SpotMakerFee - e.FuturesMakerFee) / p.Term / 2;
                    Console.WriteLine($"   {e.Name} real one interval profit: {realProfit:0.###}       [ = {fr.CurrRate:0.###} / 2 - {fr.Exchange.FuturesMakerFee} - {e.SpotMakerFee} ]"); //  + {spread:0.###}
                    if ((fr.SpotExchange == null) || (fr.RealSingleRate < realProfit))
                    {
                        fr.SpotExchange = e;
                        fr.RealSingleRate = realProfit;
                        fr.RealRateWithSpread = realProfit + spread;
                    }
                }
            }

            Console.WriteLine("\r\n===========================\r\n");
            var sortedByRealApr = sortedByRate.OrderByDescending(p => p.RealSingleRate);
            Console.WriteLine($"Coin                | 8-h pro   | pr w/sprd | Fut Exc | Spot Exc ");
            Console.WriteLine($"-----------------------------------------------------------------");
            foreach (var p in sortedByRealApr)
            {
                var aprStr = $"{p.RealSingleRate:0.###}";
                var aprSpreadStr = $"{p.RealRateWithSpread:0.###}";
                var spotExchange = $"{(p.SpotExchange == null ? string.Empty : p.SpotExchange.Name)}";
                // var term = $"{p.Term:0}";
                Console.WriteLine($"{p.Symbol,-19} | {aprStr,-9} | {aprSpreadStr,-9} | {p.Exchange.Name,-7} | {spotExchange,-9}"); //  | {term} | {p.LimitMax}
            }
        }

        #region Parent Methods
        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            stopLossValue = 0;
            return double.MaxValue;
        }

        public override string GetName() => "Funding Hedge";

        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1) => 0;

        protected override double GetShortValue(List<Kline> klines, double previousValue = -1) => double.MaxValue;

        protected override void SaveStatistics(List<BaseDealParams> tradeResults) { }  // override abstract method
        #endregion

        public override void Start() => BnnUtils.Log($"We are beginning...");

        public override string GetCurrentInfo() => string.Empty;
    }
}
