// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Data;
using System.Data.Common;
using System.Numerics;
using System.Reflection;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using Binance.Spot;
using Binance.Spot.Models;
using DbSpace;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Bnncmd
{
    internal class OnePercent : TradeRangeBase
    {
        public OnePercent(string symbolName, AccountManager manager) : base(symbolName, manager) { }

        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            if (_isAdaptive)
            {
                var priceChange = CalcPriceChange(klines, klines.Count - 1);
                SetupParams(priceChange);
            }
            var range = GetTradingRange(klines);

            var lastKlineDateTime = $"{BnnUtils.FormatUnixTime(klines[klines.Count - 1].OpenTime)}";
            if ((CurrPrice > 0) && (previousValue == -1) && ((double)CurrPrice <= range.Item1))
            {
                if (AccountManager.OutLog) BnnUtils.Log($"{lastKlineDateTime} this price is too low to start deal: {CurrPrice}");
                return -1; // !!! DealParams.PriceThreshold = -1; //
            }

            var threshold = (range.Item2 - range.Item1) * _dealParams.PriceThreshold;
            var result = (decimal)(range.Item1 + threshold);

            if (result <= previousValue) return previousValue; // !!!

            _confirmExtrVal = (decimal)_dealParams.ConfirmExtrPart * result; //  extrs.Item1;

            if (AccountManager.OutLog && (result != previousValue)) BnnUtils.Log($"{lastKlineDateTime} min {range.Item1} max {range.Item2} th {_dealParams.PriceThreshold} ( {((range.Item2 - range.Item1) / range.Item1):0.####}% ) => {result:0.##} - {_confirmExtrVal:0.##}");
            return result;
        }

        protected override void SaveStatistics(List<BaseDealParams> tradeResults) // async Task<long>
        {
            var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var dp = (DealParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {dp.DealProfit}, {dp.PriceThreshold}, {dp.ConfirmExtrPart}, {dp.StopLossPerc}, 0, 0, {dp.TotalProfit}, 'o'), "; //  _isBullParams --- {_isBearParams}
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);
        }

        public override string GetName() { return "One Percent"; }

        protected override void SetupParams(double priceChange, double previousValue = -1)
        {
            // bull
            if (priceChange > 0.1)
            {
                _dealParams.PriceThreshold = 0.75;
                _dealParams.DealProfit = 0.029;
                _dealParams.StopLossPerc = 0.07;
                _dealParams.ConfirmExtrPart = 0.00001;
            }

            // bear
            else if (priceChange < -0.15)
            {
                _dealParams.PriceThreshold = -0.21;
                _dealParams.DealProfit = 0.029;
                _dealParams.StopLossPerc = 0.07;
                _dealParams.ConfirmExtrPart = 0.00001;
            }

            // flat
            else
            {
                _dealParams.PriceThreshold = InitialParams.PriceThreshold;
                _dealParams.DealProfit = InitialParams.DealProfit;
                _dealParams.StopLossPerc = InitialParams.StopLossPerc;
                _dealParams.ConfirmExtrPart = InitialParams.ConfirmExtrPart;
            }
        }
    }
}