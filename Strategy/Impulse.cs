// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Bnncmd;
using Bnncmd.Strategy;
using DbSpace;

namespace Bnncmd.Strategy
{
    public enum ShortType
    {
        Limit,
        MinMax,
        EMA
    }

    internal class ImpulseParams : EmaDealParams
    {
        public decimal ImpulseValue { get; set; }

        public int Timeframe { get; set; }

        public decimal ImpulsePeriod { get; set; }

        public decimal MaxMinKlines { get; set; }

        public ImpulseParams(decimal impulseValue, double stopLossValue, int immpulsePeriod, decimal maxMinKlines, int timeframe) : base(0, 1, 0, 0)
        {
            ImpulseValue = impulseValue;
            StopLossPerc = stopLossValue;
            ImpulsePeriod = immpulsePeriod;
            MaxMinKlines = maxMinKlines;
            Timeframe = timeframe;
        }


        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{DealCount}\t\t{StopLossCount}\t\t{MaxDealInterval}";
            var conditions = $"iv\t{ImpulseValue}\tsl\t{StopLossPerc:0.###}\tip\t{ImpulsePeriod}\tmmk\t{MaxMinKlines}\ttf\t{Timeframe}";
            return results + "\t" + conditions;
        }


        public override string GetParamsDesciption()
        {
            return $"iv\t{ImpulseValue * 100}%\tsl\t{StopLossPerc * 100:0.###}%\tip\t{ImpulsePeriod}min\tmmk\t{MaxMinKlines}\ttf\t{Timeframe}";
        }
    }


    internal class Impulse : EMA
    {
        #region VariablesAndConstructor
        private decimal _impulseValue = 0.015M;
        private double _stopLossValue = 0.015;
        private decimal _takeProfitValue = 0.05M;
        private int _impulsePeriod = 1;
        private int _lowTimeframe = 5;
        private readonly int _minMaxKlines = 15;

        /*private decimal _impulseValue = 0.015M;
        private decimal _takeProfitValue = 0.01M;
        private int _impulsePeriod = 30;
        private readonly int _lowTimeframe = 5;*/

        private readonly ShortType _shortType = ShortType.Limit;
        protected List<Kline>? _currKlines;

        public Impulse(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}", false);
            _isLimit = true;
            IsDealEntered = false;
            _leftKlines = _minMaxKlines;
            _rightKlines = _minMaxKlines;
            EmaLength = 19;
            _dealParams = new ImpulseParams(_impulseValue, _stopLossValue, _impulsePeriod, _leftKlines, _lowTimeframe);
        }

        public override string GetName() { return $"Impulse - {SymbolName} - TF{_lowTimeframe} - {_shortType}"; }
        #endregion

        #region BackTest
        private void InitBacktest() // List<Kline> klines
        {
            CurrencyAmount = 0;
            _manager.CheckBalance();
            _backtestInited = true;
            IsDealEntered = false;

            if (_currKlines == null) return;
            _rangeKlines = GroupKlines(_currKlines, _lowTimeframe * 60);
            _currentCloseEMA = (decimal)CalcEMA(EmaLength, _rangeKlines.Count - 1);
            if (AccountManager.LogLevel > 9) Log($"initial EMA{_lowTimeframe}: {_currentCloseEMA}");
        }


        public override void InitBacktestLong(int klineIndex)
        {
            base.InitBacktestLong(klineIndex);
        }


        private void CalcMaxMin()
        {
            if (_currKlines == null) return;
            var lowKlines = GroupKlines(_currKlines, _lowTimeframe * 60);
            var extremums = GetExtremums(lowKlines);
            if ((CurrPrice > extremums.Item3.Value) && (extremums.Item3.Value > _stopLossPrice)) _shortPrice = extremums.Item3.Value; // last minimum
            else _shortPrice = _stopLossPrice;
            // Log($"min-max: {extremums.Item1} => {extremums.Item2} => {extremums.Item3} => {extremums.Item4}");
        }


        private void CheckShort()
        {
            if (_currKlines == null) return;
            var lastKline = _currKlines[^1];
            switch (_shortType)
            {
                case ShortType.Limit:
                    if ((lastKline.HighPrice > _shortPrice) || (lastKline.LowPrice < _stopLossPrice)) EnterShort(lastKline, lastKline.LowPrice < _stopLossPrice);
                    break;

                case ShortType.MinMax:
                    if ((lastKline.LowPrice < _shortPrice) || (lastKline.LowPrice < _stopLossPrice)) EnterShort(lastKline, lastKline.LowPrice < _stopLossPrice);
                    if ((lastKline.OpenTime / 1000 / 60) % _lowTimeframe == 0)
                    {
                        var oldShort = _shortPrice;
                        CalcMaxMin();
                        if (_shortPrice != oldShort) Log($"new short price: {_shortPrice}");
                    }
                    break;

                case ShortType.EMA:
                    if (_currentCloseEMA < _previousCloseEMA)
                    {
                        Log($"ema{EmaLength} turned: {_previousCloseEMA:0.###} => {_currentCloseEMA:0.###}");
                        _shortPrice = lastKline.ClosePrice;
                        EnterShort(lastKline, false); // lastKline.LowPrice < _stopLossPrice
                    }

                    if (lastKline.LowPrice < _stopLossPrice) EnterShort(lastKline, true);
                    break;
            }
        }


        private void CheckLong()
        {
            if (_currKlines == null) return;
            var lastKline = _currKlines[^1];
            if ((lastKline.ClosePrice - _currKlines[^_impulsePeriod].OpenPrice) / _currKlines[^_impulsePeriod].OpenPrice > _impulseValue) EnterLong(lastKline);
            /* if ((lastKline.ClosePrice - _currKlines[^60].OpenPrice) / _currKlines[^60].OpenPrice > _impulseValue) EnterLong(lastKline);
            if ((lastKline.ClosePrice - _currKlines[^5].OpenPrice) / _currKlines[^5].OpenPrice > 0.01M) EnterLong(lastKline);
            if ((lastKline.ClosePrice - lastKline.OpenPrice) / lastKline.OpenPrice > 0.005M) EnterLong(lastKline);*/
        }


        private void ProcessBacktestKline(List<Kline> klines)
        {
            _currKlines = klines;
            var lastKline = klines[^1];
            BacktestTime = lastKline.OpenTime;
            CurrPrice = lastKline.ClosePrice;

            if (_backtestInited)
            {
                if (((lastKline.OpenTime / 1000 / 60) % _lowTimeframe == 0) && (_shortType == ShortType.EMA))
                {
                    _previousCloseEMA = _currentCloseEMA;
                    _currentCloseEMA = klines[^2].ClosePrice * _alpha + (1 - _alpha) * _currentCloseEMA;
                    if (AccountManager.LogLevel > 9) Log($"current EMA{_lowTimeframe}: {_currentCloseEMA}");
                }
            }
            else InitBacktest();

            if (IsDealEntered) CheckShort();
            else CheckLong();

            if (AccountManager.LogLevel > 9) Log($"{lastKline}");
        }


        private void EnterLong(Kline kline)
        {
            IsDealEntered = true;
            _longPrice = kline.ClosePrice;
            _stopLossPrice = _longPrice * (1 - (decimal)_stopLossValue);
            if (_shortType == ShortType.MinMax) CalcMaxMin();
            else _shortPrice = _longPrice * (1 + _takeProfitValue);
            Log($"enter long * LP: {_longPrice}; TP: {_shortPrice}; SL: {_stopLossPrice}");
        }


        protected void EnterShort(Kline kline, bool isStop)
        {
            Log($"{(isStop ? "stoploss :( " : "enter short :)")} {kline}");
            IsDealEntered = false;

            if (isStop) base.ExitLong(kline, _stopLossPrice);
            else base.ExitLong(kline, _shortPrice);
            // Log(string.Empty);
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            // int[] timeframes = [1, 5, 15];
            int[] timeframes = [1];
            decimal[] impulseValues = [0.005M, 0.01M, 0.015M, 0.02M, 0.03M];
            double[] stopLossValues = [0.005, 0.01, 0.015, 0.03, 0.05];
            int[] impulsePeriods = [1, 5, 10, 30, 60];
            // int[] maxMinKlines = [2, 3, 4, 5, 7];
            //decimal[] maxMinKlines = [5, 9, 15, 19, 25]; // ema / takeprofit
            decimal[] maxMinKlines = [0.005M, 0.01M, 0.015M, 0.03M, 0.05M]; // ema / takeprofit

            var deals = new List<BaseDealParams>();
            long paramsSetCount = impulseValues.Length * stopLossValues.Length * impulsePeriods.Length * maxMinKlines.Length * timeframes.Length; //  * shortAngles.Length
            long counter = 0;


            foreach (var tf in timeframes)
            {
                _lowTimeframe = tf;
                foreach (var mmk in maxMinKlines)
                {
                    _leftKlines = (int)mmk;
                    _rightKlines = (int)mmk;
                    EmaLength = (int)mmk;
                    _takeProfitValue = mmk;
                    foreach (var iv in impulseValues)
                    {
                        _impulseValue = iv;
                        foreach (var tp in stopLossValues)
                        {
                            _stopLossValue = tp;
                            foreach (var ip in impulsePeriods)
                            {
                                _impulsePeriod = ip;
                                var ipr = new ImpulseParams(_impulseValue, _stopLossValue, _impulsePeriod, mmk, _lowTimeframe);
                                _dealParams = ipr;
                                _order = null;
                                // InitBacktest();
                                _backtestInited = false;
                                _manager.BackTest(); // klines
                                deals.Add(ipr);

                                counter++;
                                BnnUtils.ClearCurrentConsoleLine();
                                Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%");
                            }
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
            return (double)_shortPrice; //  1000000.0;
            //return double.MaxValue;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, Param5, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var dp = (ImpulseParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {dp.ImpulseValue}, {dp.StopLossPerc}, {dp.ImpulsePeriod}, {dp.DealCount}, {dp.MaxMinKlines}, {dp.Timeframe}, 4, {dp.TotalProfit}, 'i'), ";
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);
        }
        #endregion
    }
}
