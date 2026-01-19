// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bnncmd;
using Bnncmd.Strategy;

namespace Bnncmd.Strategy
{
    internal class SnP500 : EMA
    {
        #region VariablesAndConstructor
        public SnP500(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}", false);
            _isLimit = true;
            // IsLong = false;
            // _dealParams = new StableCoinsParams(_stableInterval);
        }

        public override string GetName() { return $"S&P 500 - {SymbolName} - TF{AccountManager.Timeframe}"; }
        #endregion

        #region BackTest
        private void InitBacktest(List<Kline> klines)
        {
            CurrencyAmount = 0;
            _manager.CheckBalance();
            _backtestInited = true;

            /*var stableInterval = 0;
            for (var i = klines.Count - 1; i > 0; i--)
            {
                // Log($"find stable: {klines[i]} / {_lastStablePrice} / {stableInterval}");
                if ((_lastStablePrice == klines[i].HighPrice) && (klines[i].HighPrice == klines[i].LowPrice + _priceStep))
                {
                    stableInterval++;
                    if (stableInterval >= _stableInterval)
                    {
                        var buyPrice = klines[i].LowPrice;
                        if (klines[^1].LowPrice < buyPrice) _longPrice = klines[^1].LowPrice;
                        Log($"last stable high price ({_stableInterval} mins): {_lastStablePrice}; order price: {buyPrice}");
                        _order = CreateLimitOrder(buyPrice, true, false);
                        break;
                    }
                }
                else
                {
                    _lastStablePrice = klines[i].HighPrice;
                    stableInterval = 0;
                }
            }*/
        }


        public override void InitBacktestLong(int klineIndex)
        {
            base.InitBacktestLong(klineIndex);
        }


        private void ProcessBacktestKline(List<Kline> klines)
        {
            var lastKline = klines[^1];
            var penultKline = klines[^2];
            BacktestTime = lastKline.OpenTime;
            CurrPrice = lastKline.ClosePrice;
            var winDT = BnnUtils.UnitTimeToDateTime(lastKline.OpenTime);
            if (winDT.Hour * 60 + winDT.Minute < 16 * 60 + 30) return;

            if ((lastKline.ClosePrice - lastKline.OpenPrice) / lastKline.OpenPrice > 0.005M)
            {
                Log($"{lastKline}");
            }

            // Log($"ProcessBacktestKline: {BacktestTime} / {lastKline}");
            //if (lastKline.OpenTime / 1000 / 60 % 60 == 0) Log($"                      ProcessBacktestKline: {BacktestTime} / {lastKline}");

            if (!_backtestInited)
            {
                InitBacktest(klines);
                return;
            };

            if ((AccountManager.LogLevel > 9) && (lastKline.OpenTime / 1000 / 60 % 30 == 0)) Log($"{lastKline}");
            // if (_order == null) return;
        }


        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            stopLossValue = 0;
            return double.MaxValue;
        }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            ProcessBacktestKline(klines);
            return _longPrice; //  double.MaxValue;
        }


        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            ProcessBacktestKline(klines);
            return (double)_shortPrice; //  1000000.0;
            //return double.MaxValue;
        }
        #endregion
    }
}
