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


    internal class Earn : BaseStrategy
    {
        public Earn(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}");
            _dealParams = new EarnParams();

            // FindBestProduct();
        }

        public static void BuyPair(string coin, AbstractExchange spotExchange, AbstractExchange futuresExchange, decimal amount)
        {
            Console.WriteLine($"Requested {coin} amount: {amount}");
            Console.WriteLine();

            var spotPrice = spotExchange.GetSpotPrice(coin);
            if (spotPrice == 0) throw new Exception("Cannot detect spot price");

            var requiredUsdAmount = amount * spotPrice * 1.03M;
            Console.WriteLine($"{spotExchange.Name} {coin} spot price: {spotPrice} => usd required amount: {requiredUsdAmount:0.###}");
            var checksAreOk = true;

            // spot exchange rests 
            var spotRest = spotExchange.CheckSpotBalance();
            checksAreOk = checksAreOk && spotRest >= requiredUsdAmount;
            Console.WriteLine($"{spotExchange.Name} spot stable coin rest: {spotRest:0.###} => {(spotRest >= requiredUsdAmount ? "ok" : "not enaugh :(")}");
            decimal spotReserve = 0;
            if (spotRest < requiredUsdAmount) spotReserve = spotExchange.FindFunds(string.Empty, true);

            // futures exchange rests
            var futuresRest = futuresExchange.CheckFuturesBalance(AbstractExchange.UsdtName);
            checksAreOk = checksAreOk && futuresRest >= requiredUsdAmount;
            decimal futuresReserve = 0;
            Console.WriteLine($"{futuresExchange.Name} futures {AbstractExchange.UsdtName} rest: {futuresRest:0.###} => {(futuresRest >= requiredUsdAmount ? "ok" : "not enaugh :(")}");
            if (futuresRest < requiredUsdAmount) futuresReserve = futuresExchange.FindFunds(string.Empty, false);

            // ... max limits
            var maxSpotOrderLimit = spotExchange.GetMaxLimit(coin, true);
            checksAreOk = checksAreOk && maxSpotOrderLimit >= amount;
            Console.WriteLine($"{spotExchange.Name} spot max limit: {maxSpotOrderLimit:0.###} => {(maxSpotOrderLimit >= requiredUsdAmount ? "ok" : "too large :(")}");

            var maxFuturesOrderLimit = futuresExchange.GetMaxLimit(coin, false);
            checksAreOk = checksAreOk && maxFuturesOrderLimit >= amount;
            Console.WriteLine($"{futuresExchange.Name} futures max limit: {maxFuturesOrderLimit:0.###} => {(maxFuturesOrderLimit >= requiredUsdAmount ? "ok" : "too large :(")}");

            // ... min limits
            var minSpotOrderLimit = spotExchange.GetMinLimit(coin, true);
            checksAreOk = checksAreOk && minSpotOrderLimit <= amount;
            Console.WriteLine($"{spotExchange.Name} spot min limit: {minSpotOrderLimit:0.###} => {(minSpotOrderLimit <= requiredUsdAmount ? "ok" : "too little :(")}");

            var minFuturesOrderLimit = futuresExchange.GetMinLimit(coin, false);
            checksAreOk = checksAreOk && minFuturesOrderLimit <= amount;
            Console.WriteLine($"{futuresExchange.Name} futures min limit: {minFuturesOrderLimit:0.###} => {(minFuturesOrderLimit <= requiredUsdAmount ? "ok" : "too little :(")}");

            // transfers
            Console.WriteLine();
            if ((spotReserve >= requiredUsdAmount) && (spotRest < requiredUsdAmount))
            {
                Console.WriteLine("Do you want to transfer assets to spot wallet?");
                var commandTrans = Console.ReadLine();
                if ((commandTrans != null) && (commandTrans.ToLower()[0] == 'y'))
                {
                    // ranser
                }
                else return;
            }

            if ((futuresReserve >= requiredUsdAmount) && (futuresRest < requiredUsdAmount))
            {
                Console.WriteLine("Do you want to transfer assets to futures wallet?");
                var commandFutures = Console.ReadLine();
                Console.WriteLine();
                if ((commandFutures != null) && (commandFutures.ToLower()[0] == 'y')) futuresRest = futuresExchange.FindFunds(string.Empty, false, 1.015M * requiredUsdAmount - futuresRest);
                else return;
            }

            if (!checksAreOk) return;

            // buy futures
            Console.WriteLine("Do you want to make futures order?");
            var command = Console.ReadLine();
            if ((command == null) || (command.ToLower()[0] != 'y')) return;
            
            Console.WriteLine();
            Console.WriteLine($"{futuresExchange.Name} futures short position opening...");
            futuresExchange.EnterShort(coin, amount);

        }

        public static void FindBestProduct()
        {
            var fundingRateDepth = 1;
            List<EarnProduct> products = [];
            var minApr = 30;

            var exchanges = new List<AbstractExchange> { Exchange.Binance, Exchange.Bybit, Exchange.Mexc };
            // var exchanges = new List<AbstractExchange> { Exchange.Binance };
            foreach (var e in exchanges)
            {
                e.GetEarnProducts(products, minApr);
            }

            Console.WriteLine("\r\n===========================\r\n");
            // exchanges = [Exchange.Binance, Exchange.Bybit, Exchange.Mexc];
            // exchanges = [Exchange.Mexc];
            foreach (var p in products)
            {
                Console.WriteLine($"{p}");
                foreach (var e in exchanges)
                {
                    e.FundingRateDepth = fundingRateDepth;
                    var fr = e.GetDayFundingRate(p.ProductName);
                    if (fr == decimal.MinValue) continue;
                    var dayProfit = ((p.Apr / 365 + fr) * p.Term - p.Exchange.SpotMakerFee - e.FuturesMakerFee) / p.Term / 2;
                    var realApr = 365 * dayProfit;
                    Console.WriteLine($"   {e.Name.ToLower()} funding rate: {fr:0.###} => {realApr:0.###}       [ (({p.Apr:0.###} / 365 + {fr:0.###}) * {p.Term} - {p.Exchange.SpotMakerFee} - {e.FuturesMakerFee}) / {p.Term} = {dayProfit:0.###} ]");
                    if ((p.FuturesExchange == null) || (p.RealApr < realApr))
                    {
                        p.FuturesExchange = e;
                        p.DayFundingRate = fr;
                        p.RealApr = realApr;
                    }
                }
            }

            Console.WriteLine("\r\n===========================\r\n");
            var sortedByRealApr = products.OrderByDescending(p => p.RealApr);
            foreach (var p in sortedByRealApr)
            {
                var arpStr = $"{p.RealApr:0.###}";
                var futuresExchange = $"{(p.FuturesExchange == null ? string.Empty : p.FuturesExchange.Name)}";
                var term = $"{p.Term:0}";
                Console.WriteLine($"{p.ProductName,-23} | {arpStr,-9} | {p.Exchange.Name,-9} | {futuresExchange,-9} | {term,-3} | {p.LimitMax}");
            }
            // Environment.Exit(0);
        }

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
        #endregion

        public override void Start() => BnnUtils.Log($"We are beginning...");

        public override string GetCurrentInfo() => string.Empty;
    }
}
