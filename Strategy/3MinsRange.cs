// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Bnncmd;
using Bnncmd.Strategy;
using DbSpace;
using Org.BouncyCastle.Bcpg;

namespace Bnncmd.Strategy
{
    internal class ThreeMinsRangeParams : EmaDealParams
    {
        public int Timeframe { get; set; }

        public bool IsRecalcTake { get; set; }

        public decimal MaxMinKlines { get; set; }

        public ThreeMinsRangeParams(int emaLength, double stopLossKoeff, bool isRecalcTake, decimal dummy2, int timeframe) : base(0, 1, 0, 0)
        {
            EmaLength = emaLength;
            StopLossPerc = stopLossKoeff;
            IsRecalcTake = isRecalcTake;
            MaxMinKlines = dummy2;
            Timeframe = timeframe;
        }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{DealCount}\t\t{StopLossCount}\t\t{MaxDealInterval}";
            var conditions = $"ema\t{EmaLength}\tsl\t{StopLossPerc:0.###}\ttf\t{Timeframe}\tir\t{IsRecalcTake}"; // \tip\t{ImpulsePeriod}\tmmk\t{MaxMinKlines}
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"ema\t{EmaLength}\tsl\t{StopLossPerc:0.###}\ttf\t{Timeframe}"; // \tip\t{ImpulsePeriod}min\tmmk\t{MaxMinKlines}
        }
    }

    internal class ThreeMinsRange : EMA
    {
        #region VariablesAndConstructor
        private bool _isWaitAfterStop = true;
        private decimal _klineLow = decimal.MaxValue;
        private decimal _klineHigh = 0;
        private readonly decimal _defaultSlippage;
        // private readonly int _minMaxKlines = 3;

        private readonly int _emaLength = 5;
        private bool _recalcTakeProfit = true;
        private double _stopLossKoeff = 3;
        private int _timeframe = 60 * 4;
        //private double _stopLossKoeff = 1.5;
        //private int _timeframe = 4 * 60;

        protected List<Kline>? _currKlines;

        static List<ThreeMinsRange> CoinInDepth { get; set; } = new();

        public ThreeMinsRange(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}", false);
            _isLimit = true;
            IsDealEntered = false;
            _byClosePrice = false;
            // _leftKlines = _minMaxKlines;
            // _rightKlines = _minMaxKlines;

            EmaLength = _emaLength;
            _defaultSlippage = Slippage;
            _dealParams = new ThreeMinsRangeParams(EmaLength, _stopLossKoeff, _recalcTakeProfit, 0, _timeframe);
            _isFutures = false;
        }

        public override string GetName() { return $"3 Mins Range - {SymbolName}, TF{_timeframe}, EMA{_emaLength}, SL{_stopLossKoeff}, {(_recalcTakeProfit ? "recalc" : "no recalc")}"; }
        #endregion

        #region BackTest
        private void InitBacktest() // List<Kline> klines
        {
            CurrencyAmount = 0;
            _manager.CheckBalance();
            _backtestInited = true;
            IsDealEntered = false;
            _isWaitAfterStop = true;
            _klineLow = decimal.MaxValue;
            _klineHigh = 0;

            if (_currKlines == null) return;
            _rangeKlines = GroupKlines(_currKlines, _timeframe * 60);
            _currentHighEMA = (decimal)CalcEMA(EmaLength, _rangeKlines.Count - 1, false);
            _currentLowEMA = (decimal)CalcEMA(EmaLength, _rangeKlines.Count - 1, true);
            if (AccountManager.LogLevel > 9) Log($"initial {_timeframe}-high EMA{EmaLength}: {_currentHighEMA:0.###}; initial {_timeframe}-low EMA{EmaLength}: {_currentLowEMA:0.###}");
        }


        private void ProcessBacktestKline(List<Kline> klines)
        {
            _currKlines = klines;
            var lastKline = klines[^1];
            BacktestTime = lastKline.OpenTime;
            CurrPrice = lastKline.ClosePrice;

            if (_backtestInited)
            {
                if (((lastKline.OpenTime / 1000 / 60) % _timeframe == 0) && (_klineHigh != 0))
                {
                    // CalcMaxMin();
                    _currentHighEMA = _klineHigh * _alpha + (1 - _alpha) * _currentHighEMA;
                    _currentLowEMA = _klineLow * _alpha + (1 - _alpha) * _currentLowEMA;
                    if (AccountManager.LogLevel > 9) Log($"HighPrice: {_klineHigh}; current {_timeframe}-high EMA{EmaLength}: {_currentHighEMA:0.###}; current {_timeframe}-low EMA{EmaLength}: {_currentLowEMA:0.###}");
                    _klineLow = decimal.MaxValue;
                    _klineHigh = 0;
                }
                if (lastKline.HighPrice > _klineHigh) _klineHigh = lastKline.HighPrice;
                if (lastKline.LowPrice < _klineLow) _klineLow = lastKline.LowPrice;
            }
            else InitBacktest();

            if (IsDealEntered) CheckExit();
            else CheckEnter();
        }


        public override void InitBacktestLong(int klineIndex)
        {
            // base.InitBacktestLong(klineIndex);
            IsDealEntered = false;
        }


        /*private void CalcMaxMin()
        {
            if (_currKlines == null) return;
            var groupedlines = GroupKlines(_currKlines, _timeframe * 60);
            var extremums = GetExtremums(groupedlines);
            _isBull = (extremums.Item4.Value > extremums.Item2.Value); //  (extremums.Item3.Value > extremums.Item1.Value) && 
        }*/


        private void CheckExit()
        {
            if (_currKlines == null) return;
            var lastKline = _currKlines[^1];

            if (_isFutures)
            {
                if (_recalcTakeProfit) _stopLossPrice = _currentHighEMA;
                if ((lastKline.LowPrice < _longPrice) || (lastKline.HighPrice > _stopLossPrice)) ExitShort(lastKline, lastKline.HighPrice > _stopLossPrice);
            }
            else
            {
                if (_recalcTakeProfit) _shortPrice = _currentHighEMA;
                if ((lastKline.HighPrice > _shortPrice) || (lastKline.LowPrice < _stopLossPrice)) ExitLong(lastKline, lastKline.LowPrice < _stopLossPrice);
            }
        }


        private void CheckEnter()
        {
            if (_currKlines == null) return;
            //if (_manager.IsDealEntered) return;
            var lastKline = _currKlines[^1];

            if (lastKline.HighPrice > _currentHighEMA) _isWaitAfterStop = false;
            if (_isWaitAfterStop) return;
            if ((lastKline.HighPrice > _currentLowEMA) && (lastKline.LowPrice < _currentLowEMA))
            {
                if (_isFutures) EnterShort(lastKline);
                else EnterLong(lastKline);
            }
        }


        private void EnterLong(Kline kline)
        {
            if (CoinInDepth.Contains(this)) return;
            CoinInDepth.Add(this);
            if (CoinInDepth.Count != 5)
            {
                Log($"long level * ( {_timeframe}-mins low EMA{EmaLength}: {_currentLowEMA:0.###} ) [{CoinInDepth.Count}]");
                return;
            }

            IsDealEntered = true;
            _longPrice = _currentLowEMA;
            _shortPrice = _currentHighEMA;
            _stopLossPrice = _currentLowEMA - (decimal)_stopLossKoeff * (_currentHighEMA - _currentLowEMA);
            Log($"enter long * LP: {_longPrice:0.###}; TP: {_shortPrice:0.###}; SL: {_stopLossPrice:0.###} ( {_timeframe}-mins low EMA{EmaLength}: {_currentLowEMA:0.###} ) [{CoinInDepth.Count}]");
        }


        protected void ExitLong(Kline kline, bool isStop)
        {
            Log($"{(isStop ? "stoploss :( " : "exit long :)")} {kline}");
            IsDealEntered = false;
            CoinInDepth.Clear();

            if (isStop)
            {
                _isWaitAfterStop = true;
                Slippage = _defaultSlippage / 2;
                base.ExitLong(kline, _stopLossPrice);
            }
            else
            {
                Slippage = 0;
                base.ExitLong(kline, _shortPrice);
            }
        }


        private void EnterShort(Kline kline)
        {
            IsDealEntered = true;
            _shortPrice = _currentLowEMA;
            _stopLossPrice = _currentHighEMA;
            _longPrice = _currentLowEMA - (decimal)_stopLossKoeff * (_currentHighEMA - _currentLowEMA);
            Log($"enter short * SP: {_shortPrice:0.###}; TP: {_longPrice:0.###}; SL: {_stopLossPrice:0.###} ( {_timeframe}-mins low EMA{EmaLength}: {_currentLowEMA:0.###} )");
        }


        private void ExitShort(Kline kline, bool isStop)
        {
            Log($"{(isStop ? "stoploss :( " : "exit short :)")} {kline}");
            // IsDealEntered = false;
            _isWaitAfterStop = true;

            if (isStop)
            {
                // Slippage = _defaultSlippage / 2;
                base.ExitLong(kline, _stopLossPrice);
            }
            else
            {
                // Slippage = 0;
                base.ExitLong(kline, _longPrice);
            }
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            /* int[] timeframes = [60 * 4];
            bool[] isRecalcTakeProfit = [true, false];
            double[] stopLossKoeffs = [0.5, 1, 1.5];
            int[] emas = [21]; // ema / takeprofit */

            int[] timeframes = [60 * 4, 60, 15];
            bool[] isRecalcTakeProfit = [true, false];
            double[] stopLossKoeffs = [0.5, 1, 1.5, 3, 5];
            int[] emas = [21, 9, 5]; // ema / takeprofit 

            /*int[] timeframes = [60 * 24, 60 * 4, 60, 15, 5];
            bool[] isRecalcTakeProfit = [true, false];
            double[] stopLossKoeffs = [0.5, 1, 1.5, 3, 5];
            int[] emas = [21, 15, 9, 5, 3]; // ema / takeprofit */

            /*int[] impulsePeriods = [1, 5, 10, 30, 60];
            // int[] maxMinKlines = [2, 3, 4, 5, 7];
            decimal[] maxMinKlines = [0.005M, 0.01M, 0.015M, 0.03M, 0.05M]; // ema / takeprofit */
            var deals = new List<BaseDealParams>();
            long paramsSetCount = timeframes.Length * isRecalcTakeProfit.Length * stopLossKoeffs.Length * emas.Length; //   * impulsePeriods.Length * maxMinKlines.Length
            long counter = 0;

            foreach (var tf in timeframes)
            {
                _timeframe = tf;
                foreach (var ema in emas)
                {
                    EmaLength = ema;
                    foreach (var slk in stopLossKoeffs)
                    {
                        _stopLossKoeff = slk;
                        foreach (var irp in isRecalcTakeProfit)
                        {
                            _recalcTakeProfit = irp;
                            var ipr = new ThreeMinsRangeParams(EmaLength, _stopLossKoeff, _recalcTakeProfit, 0, _timeframe);
                            _dealParams = ipr;
                            _order = null;
                            _backtestInited = false;
                            if (AccountManager.LogLevel > 0) Log($" *** {GetName()} ***\n\r");
                            _manager.BackTest(); // klines
                            deals.Add(ipr);

                            counter++;
                            BnnUtils.ClearCurrentConsoleLine();
                            Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%");
                        }
                    }
                }
            }

            return deals;
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
            return (double)_shortPrice;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, Param5, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var dp = (ThreeMinsRangeParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {dp.EmaLength}, {dp.StopLossPerc}, {dp.IsRecalcTake}, {dp.Timeframe}, 0, 0, 1, {dp.TotalProfit}, '3'), ";
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);
        }
        #endregion
    }
}
