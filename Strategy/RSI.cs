// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Binance.Spot;
using Binance.Spot.Models;
using DbSpace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bnncmd.Strategy
{
    internal class RSI : EMA
    {
        #region VariablesAndConstructor
        private readonly int _buyLevel = 70;
        private decimal _longAim = 0;

        public RSI(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}", false); //  {SymbolName} {_emaLength}
            // _dealParams = new SafetyTradeParams(_emaLength, _emaLengthHour, _shiftDownOffset, _shiftUpOffset, _shiftDownCoef, _shiftUpCoef);
            _isLimit = true;
        }

        public override string GetName() { return $"RSI {_rsiLength} - {SymbolName}"; }
        #endregion

        #region BackTest
        private void InitBacktest(List<Kline> klines)
        {
            CurrencyAmount = 0;
            _manager.CheckBalance();
            CalcRSIFromArchive(klines);
            _backtestInited = true;
        }


        private void ProcessBacktestKline(List<Kline> klines)
        {
            var lastKline = klines[^1];
            BacktestTime = lastKline.OpenTime;
            CurrPrice = lastKline.ClosePrice;

            if (!IsDealEntered && (_longAim > 0) && (CurrPrice >= _longAim)) EnterLong(lastKline);

            if ((lastKline.OpenTime / 1000 / 60) % _minutesInTimeframe == 0)
            {
                if (_backtestInited)
                {
                    var firstTimeFrameKline = klines[^(_minutesInTimeframe + 1)];
                    var priceChage = klines[^2].ClosePrice - firstTimeFrameKline.OpenPrice;

                    var timeFrameKline = new Kline() { OpenTime = firstTimeFrameKline.OpenTime, OpenPrice = firstTimeFrameKline.OpenPrice, ClosePrice = klines[^2].ClosePrice, HighPrice = -1, LowPrice = -1 };
                    CalcLongAim(priceChage, timeFrameKline);
                    if (AccountManager.LogLevel > 9) Log($"{timeFrameKline}; rsi: {_rsi: 0.###}; lp: {_longAim: 0.###}");
                }
                else InitBacktest(klines);
            }
        }

        private void CalcLongAim(decimal priceChage, Kline lastKline)
        {
            CalcRSI(priceChage); // , lastKline

            var priceToLong = ((_rsiLength - 1) * _negativeAvg * _buyLevel) / (100 - _buyLevel) - (_rsiLength - 1) * _positiveAvg;
            _longAim = (lastKline.ClosePrice + priceToLong);
            if (IsDealEntered && (_rsi < _buyLevel - 1)) ExitLong(lastKline, CurrPrice);
        }


        private void EnterLong(Kline kline)
        {
            Log($"enter long * {kline}; rsi {_rsi:0.###}");
            IsDealEntered = true;
            _longPrice = CurrPrice;
            // _status = SafetyTradeState.Waiting;
        }


        protected override void ExitLong(Kline kline, decimal limitPrice)
        {
            Log($"enter short {limitPrice}; rsi {_rsi:0.###} *");
            base.ExitLong(kline, limitPrice);
        }


        public override string GetCurrentInfo()
        {
            return $"GetCurrentInfo";
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
            // return 0;
        }


        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            ProcessBacktestKline(klines);
            return (double)_shortPrice; //  1000000.0;
            //return double.MaxValue;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            /* var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, Param5, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var dp = (SafetyTradeParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {dp.EmaLength}, {dp.ShiftDownOffset}, {dp.ShiftUpOffset}, {dp.ShiftDownCoef}, {dp.ShiftUpCoef}, 6, {dp.EmaHourLength}, {dp.TotalProfit}, 's'), ";
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null); */
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            /* int[] emaIntervals = [200, 220, 240];
            double[] shiftUps = [0.01, 0.012, 0.015];
            double[] shiftDowns = [0.019, 0.021, 0.025];*/

            /* int[] emaIntervals = [150, 200, 250, 300, 450];
            double[] shiftUps = [0.8, 1, 1.1, 1.4, 1.7];
            double[] shiftDowns = [0.11, 0.35, 4.1, 4.5, 5.5];*/

            /* int[] emaIntervals = [3, 5, 9, 15, 30];
            double[] shiftUps = [0.9, 1.2, 1.5, 2.3, 3.5];
            double[] shiftDowns = [0.05, 0.15, 0.3, 0.5, 0.7];*/

            /* int[] emaIntervals = [7, 250, 1350];
            int[] emaHourIntervals = [7, 72, 288];
            double[] downOffsets = [0.5, 0.767, 1];
            double[] upOffsets = [0.3, 0.572, 0.8];
            double[] downCoefs = [-0.5, -0.247, -0.1];
            double[] upCoefs = [0.05, 0.088, 0.12];

            int[] emaIntervals = [900, 1300, 1700];
            int[] emaHourIntervals = [100, 300, 500];
            double[] downOffsets = [0.9, 1.1, 1.3];
            double[] upOffsets = [0.2, 0.5, 0.9];
            double[] downCoefs = [-0.3, -0.15, 0];
            double[] upCoefs = [0.03, 0.09, 0.15];*/

            int[] emaIntervals = [1500, 1800, 2300];
            int[] emaHourIntervals = [90, 350, 700];
            double[] downOffsets = [1.0, 1.4, 1.7];
            double[] upOffsets = [0.1, 0.6, 1.1];
            double[] downCoefs = [-0.45, -0.25, -0.1];
            double[] upCoefs = [0.01, 0.05, 0.13];

            var deals = new List<BaseDealParams>();
            /* long paramsSetCount = emaIntervals.Length * emaHourIntervals.Length * upOffsets.Length * downOffsets.Length * upCoefs.Length * downCoefs.Length;
            long counter = 0;
            foreach (var el in emaIntervals)
            {
                _emaLength = el;
                _alpha = 2.0 / (_emaLength + 1);
                foreach (var ehl in emaHourIntervals)
                {
                    _emaLengthHour = ehl;
                    _alphaHour = 2.0 / (_emaLengthHour + 1);
                    foreach (var uo in upOffsets)
                    {
                        _shiftUpOffset = uo;
                        foreach (var uc in upCoefs)
                        {
                            _shiftUpCoef = uc;
                            foreach (var dno in downOffsets)
                            {
                                _shiftDownOffset = dno;
                                foreach (var dc in downCoefs)
                                {
                                    _shiftDownCoef = dc;
                                    var dp = new SafetyTradeParams(el, ehl, dno, uo, dc, uc);
                                    _dealParams = dp;
                                    _order = null;
                                    _backtestInited = false;
                                    _manager.BackTest(); // klines
                                    deals.Add(dp);

                                    counter++;
                                    BnnUtils.ClearCurrentConsoleLine();
                                    Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%");
                                }
                            }
                        }
                    }
                }
            }*/
            return deals;
        }
        #endregion

        #region RealTimeRoutines

        #endregion
    }
}