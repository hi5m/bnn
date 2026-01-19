// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Numerics;
using Bnncmd;
using Bnncmd.Strategy;
using DbSpace;

namespace Bnncmd.Strategy
{
    public enum RsiDumpState
    {
        WaitForRsiDump,
        FindLowTfLowestMax,
        WaitForLong,
        FindHighTfMin,
        WaitForLevel05,
        Stopping,
        ExtremeStopping
    }

    internal class RsiDumpParams : DealParams
    {
        public int RsiLength { get; set; }
        public int RsiLevel { get; set; }
        public decimal SellFibLevel { get; set; }
        public decimal StopFibLevel { get; set; }
        public int MinMaxKlines { get; set; }

        public RsiDumpParams(int rsiLength, int rsiLevel, decimal sellFibLevel, decimal stopFibLevel, int minMaxKlines)
        {
            RsiLength = rsiLength;
            RsiLevel = rsiLevel;
            SellFibLevel = sellFibLevel;
            StopFibLevel = stopFibLevel;
            MinMaxKlines = minMaxKlines;

            DealProfit = 100;
            PriceThreshold = -100;
            StopLossPerc = 100;
            ConfirmExtrPart = 0.00000;
        }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{DealCount}\t{StopLossCount}"; // {MaxDealInterval}\t
            var conditions = $"rln\t{RsiLength}\trlv\t{RsiLevel}\tshl\t{SellFibLevel:0.###}\tstl\t{StopFibLevel:0.###}\tmmk\t{MinMaxKlines:0.###}";
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"rln {RsiLength}   rlv {RsiLevel}   shl {SellFibLevel:0.##}   stl {StopFibLevel}   mmk {MinMaxKlines}";
        }
    }


    internal class RsiDump : EMA
    {
        #region VariablesAndConstructor
        private RsiDumpState _state = RsiDumpState.WaitForRsiDump;

        //private readonly int _highTimeframe = 15;
        //private readonly int _lowTimeframe = 1;
        //private readonly int _highTimeframe = 60;
        //private readonly int _lowTimeframe = 5;
        private readonly int _highTimeframe = 24 * 60;
        private readonly int _lowTimeframe = 60;

        private readonly int _rsiDumpLength = 14;
        private int _buyRsiLevel = 40;
        private decimal _sellFibLevel = 0.618M;
        private decimal _stopFibLevel = -0.27M; // 0.01M; // 
        private int _minMaxFrames = 2;

        /* private readonly int _rsiDumpLength = 5;
        private int _buyRsiLevel = 45;
        private decimal _sellFibLevel = 0.55M;
        private decimal _stopFibLevel = 0.01M; // -0.27M;
        private int _minMaxFrames = 3;*/


        // private readonly decimal _lowLongFibLevel = 0.236M; // = 1; // 
        private readonly decimal _lowLongFibLevel = 0M; // = 1; // 

        private decimal _highTimeframeMax;
        private decimal _highTimeframeMin;
        private decimal _lowTimeframeMax;
        private decimal _lowTimeframeMin;
        private decimal _longAim;
        private decimal _shortAim;
        private decimal _stopAim;
        private DateTime _longTime;
        private readonly decimal _originalSlippage;

        private readonly bool _extremeStop = false;
        private readonly bool _backLong = false;
        private bool _isLowBull;

        public RsiDump(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}", false);
            _isLimit = true;
            _minutesInTimeframe = _highTimeframe;
            _rsiLength = _rsiDumpLength;
            _dealParams = new RsiDumpParams(_rsiLength, _buyRsiLevel, _sellFibLevel, _stopFibLevel, _minMaxFrames);
            _originalSlippage = Slippage; //  (decimal)Math.Pow((double)Slippage, 0.5);
            // Slippage = 0;
            // Log($"_stopSlippage: {_stopSlippage}");
        }

        public override string GetName() { return $"RSI{_rsiLength} Dump - {SymbolName} - TF{_highTimeframe}"; }
        #endregion

        #region BackTest
        private void InitBacktest(List<Kline> klines)
        {
            CurrencyAmount = 0;
            _manager.CheckBalance();
            CalcRSIFromArchive(klines);
            _leftKlines = _minMaxFrames;
            _rightKlines = _minMaxFrames;
            _backtestInited = true;
            _state = RsiDumpState.WaitForRsiDump;
        }

        public override void InitBacktestLong(int klineIndex)
        {
            base.InitBacktestLong(klineIndex);
            _state = RsiDumpState.WaitForRsiDump;
        }

        private void ProcessBacktestKline(List<Kline> klines)
        {
            var lastKline = klines[^1];
            BacktestTime = lastKline.OpenTime;
            CurrPrice = lastKline.HighPrice;

            if ((lastKline.OpenTime / 1000 / 60) % _highTimeframe == 0)
            {
                ProcessHighTimeframe(klines);
                if (_state == RsiDumpState.FindHighTfMin) FindHighTfMin(klines);
            }

            if (_manager.IsDealEntered && !IsDealEntered)
            {
                _state = RsiDumpState.WaitForRsiDump;
                return; // just calc rsi if other strategy in long position
            }

            if (((lastKline.OpenTime / 1000 / 60) % _lowTimeframe == 0) && (new[] { RsiDumpState.FindLowTfLowestMax, RsiDumpState.Stopping }.Contains(_state))) ProcessLowTimeframe(klines);

            if (_backLong)
            {
                if ((_state == RsiDumpState.FindLowTfLowestMax) && (lastKline.HighPrice > _lowTimeframeMax)) //  && _isLowBull
                {
                    _state = RsiDumpState.WaitForLong;
                    _highTimeframeMax = 0;
                    FindHighTfMin(klines);
                    Log($"last {_lowTimeframe}-mins min/max: {_lowTimeframeMin}/{_lowTimeframeMax} exceeded; wait for {_lowLongFibLevel}-fib long: {_longAim:0.###}; {_sellFibLevel}-take: {_shortAim:0.###}");
                    return;
                }

                if (_state == RsiDumpState.WaitForLong)
                {
                    if (lastKline.HighPrice > _shortAim)
                    {
                        _state = RsiDumpState.WaitForRsiDump;
                        Log($"long failed\n");
                        return;
                    }

                    if (lastKline.LowPrice < _longAim)
                    {
                        CurrPrice = lastKline.LowPrice;
                        EnterLong(lastKline);
                        FindHighTfMin(klines);
                        Log($"EL: {_longAim:0.###}, last {_highTimeframe}-mins min/max: {_highTimeframeMin}/{_highTimeframeMax}; {_sellFibLevel}-take: {_shortAim:0.###}; {_stopFibLevel}-stop: {_stopAim:0.###}");
                    }
                }
            }
            else
            {
                if ((_state == RsiDumpState.FindLowTfLowestMax) && (lastKline.HighPrice > _lowTimeframeMax)) //  && _isLowBull
                {
                    // _longAim = lastKline.OpenPrice > _lowTimeframeMax ? lastKline.OpenPrice : _lowTimeframeMax;
                    _state = RsiDumpState.FindHighTfMin;
                    _highTimeframeMax = 0; //  decimal.MaxValue;
                    FindHighTfMin(klines);
                    if (_shortAim / _lowTimeframeMax * (1 - Slippage) * (1 - Slippage) < 1)
                    {
                        Log($"take ({_shortAim}) and long price ({_lowTimeframeMax}) are too close => skip deal\n");
                        _state = RsiDumpState.WaitForRsiDump;
                        return;
                    }

                    EnterLong(lastKline);
                    Log($"EL: {_lowTimeframeMax}, last {_highTimeframe}-mins min/max: {_highTimeframeMin}/{_highTimeframeMax}; {_sellFibLevel}-take: {_shortAim}; {_stopFibLevel}-stop: {_stopAim}");
                }

            }

            // if ((new[] { RsiDumpState.WaitForLevel05, RsiDumpState.FindHighTfMin }.Contains(_state)) && ((lastKline.LowPrice < _stopAim) || (lastKline.HighPrice > _shortAim))) EnterShort(lastKline);

            if ((new[] { RsiDumpState.WaitForLevel05, RsiDumpState.FindHighTfMin, RsiDumpState.Stopping, RsiDumpState.ExtremeStopping }.Contains(_state)) && (lastKline.HighPrice > _shortAim)) ExitLong(lastKline);

            // proceess stop-loss
            if ((new[] { RsiDumpState.WaitForLevel05, RsiDumpState.FindHighTfMin }.Contains(_state)) && (lastKline.LowPrice < _stopAim))
            {
                CurrPrice = lastKline.LowPrice;
                Log($"stopping...");
                _state = RsiDumpState.Stopping;

                if (_extremeStop) ProcessLowTimeframe(klines);
                else ExitLong(lastKline);

                return;
            }

            if (_state == RsiDumpState.Stopping)
            {
                if (lastKline.HighPrice > _stopAim) ExitLong(lastKline);

                if (_lowTimeframeMax < _stopAim)
                {
                    CurrPrice = lastKline.LowPrice;
                    Log($"extreme stopping... last low max < stop: {_lowTimeframeMax:0.###} < {_stopAim:0.###}");
                    _state = RsiDumpState.ExtremeStopping;
                    ExitLong(lastKline);
                }
            }
        }


        private void ProcessLowTimeframe(List<Kline> klines)
        {
            if (!new[] { RsiDumpState.FindLowTfLowestMax, RsiDumpState.Stopping }.Contains(_state)) return;
            var lowKlines = GroupKlines(klines, _lowTimeframe * 60);
            var extremums = GetExtremums(lowKlines);
            var lastMaximum = extremums.Item4;
            var lastMinimum = extremums.Item3;
            _isLowBull = (extremums.Item1.Value < lastMinimum.Value); //  && (extremums.Item2.Value < lastMaximum.Value)
            if (lastMaximum.Value < _lowTimeframeMax)
            {
                _lowTimeframeMax = lastMaximum.Value;
                _lowTimeframeMin = lastMinimum.Value;
                if (_state == RsiDumpState.FindLowTfLowestMax)
                {
                    _longAim = _lowTimeframeMax - _lowLongFibLevel * (_lowTimeframeMax - _lowTimeframeMin);
                    Log($"rsi-{_highTimeframe:0.###}: {_rsi:0.###} < {_buyRsiLevel}; last {_lowTimeframe}-min max: {_lowTimeframeMax:0.###} (min {_lowTimeframeMin:0.###})");
                }
            }
        }


        private void FindHighTfMin(List<Kline> klines)
        {
            if (!(new[] { RsiDumpState.WaitForLong, RsiDumpState.FindHighTfMin }.Contains(_state))) return;
            // if (_state != RsiDumpState.FindHighTfMin) return;
            var highKlines = GroupKlines(klines, _highTimeframe * 60);

            var extremums = GetExtremums(highKlines);
            var lastMinimum = extremums.Item3;
            var lastMaximum = extremums.Item4;

            if (_highTimeframeMax == 0)
            {
                _highTimeframeMax = lastMaximum.Value;
                // _highTimeframeMin = Math.Min(lastMinimum.Value, highKlines[^1].LowPrice);
                _highTimeframeMin = Math.Min(highKlines[^2].LowPrice, highKlines[^1].LowPrice);
                // if (lastMinimum.Time > lastMaximum.Time) _highTimeframeMin = Math.Min(_highTimeframeMin, lastMinimum.Value); - it seems worse
                // Log($"!!!  {_highTimeframe}-mins max: {_highTimeframeMax:0.##}");

                _shortAim = (_highTimeframeMax - _highTimeframeMin) * _sellFibLevel + _highTimeframeMin;

                // stop variants
                _stopAim = (_highTimeframeMax - _highTimeframeMin) * _stopFibLevel + _highTimeframeMin;
                // _stopAim = _lowTimeframeMin; //  2024 - BTC - 5.5X?
            }

            if ((lastMaximum.Value != _highTimeframeMax) && (BnnUtils.UnitTimeToDateTime(lastMaximum.Time) < _longTime))
            {
                _highTimeframeMax = lastMaximum.Value;
                // _highTimeframeMin = Math.Min(lastMinimum.Value, _highTimeframeMin);
                // _stopAim = (_highTimeframeMax - _highTimeframeMin) * _stopFibLevel + _highTimeframeMin;
                _shortAim = (_highTimeframeMax - _highTimeframeMin) * _sellFibLevel + _highTimeframeMin;
                if (_state != RsiDumpState.WaitForLong) _state = RsiDumpState.WaitForLevel05;
                Log($"new {_highTimeframe}-mins max: {_highTimeframeMax:0.##} => short aim: {_shortAim:0.##}");
            }


            /* if ((lastMaximum.Value < _highTimeframeMax)) //  && (BnnUtils.UnitTimeToDateTime(extremums.Item4.Time) > _longTime)
            {
                _state = RsiDumpState.WaitForLevel05;
                _highTimeframeMin = Math.Min(lastMinimum.Value, _highTimeframeMin);
                _shortAim = (_highTimeframeMax - _highTimeframeMin) * _sellFibLevel + _highTimeframeMin;
                _stopAim = (_highTimeframeMax - _highTimeframeMin) * _stopFibLevel + _highTimeframeMin;
                Log($"new {_highTimeframe}-mins max: {_highTimeframeMax:0.##} => short limit: {_shortAim:0.##}; stoploss: {_stopAim:0.##}");
                return;
            }


            if (highKlines[^2].LowPrice < _highTimeframeMin)
            {
                _highTimeframeMin = highKlines[^2].LowPrice;
                _shortAim = (_highTimeframeMax - _highTimeframeMin) * _sellFibLevel + _highTimeframeMin;
                Log($"new {_highTimeframe}-mins min: {_highTimeframeMin:0.##} => short aim: {_shortAim:0.##}");
            }*/
        }


        private void ProcessHighTimeframe(List<Kline> klines)
        {
            if (!_backtestInited)
            {
                InitBacktest(klines);
                return;
            }

            var firstTimeFrameKline = klines[^(_minutesInTimeframe + 1)];
            var priceChage = klines[^2].ClosePrice - firstTimeFrameKline.OpenPrice;

            var timeFrameKline = new Kline() { OpenTime = firstTimeFrameKline.OpenTime, OpenPrice = firstTimeFrameKline.OpenPrice, ClosePrice = klines[^2].ClosePrice, HighPrice = -1, LowPrice = -1 };
            CalcRSI(priceChage); // , klines[^1]
            if (AccountManager.LogLevel > 9) Log($"{timeFrameKline}; rsi: {_rsi: 0.###}");

            if ((_state == RsiDumpState.WaitForRsiDump) && (_rsi < _buyRsiLevel))
            {
                _longAim = decimal.MaxValue;
                _lowTimeframeMax = decimal.MaxValue;
                _state = RsiDumpState.FindLowTfLowestMax;
                ProcessLowTimeframe(klines);
            }
        }


        private void EnterLong(Kline kline)
        {
            IsDealEntered = true;
            _longPrice = _longAim;
            _longTime = BnnUtils.UnitTimeToDateTime(kline.OpenTime);
            _state = RsiDumpState.FindHighTfMin;
            // _highTimeframeMax = 0; //  decimal.MaxValue;
        }


        protected override void ExitLong(Kline kline, decimal limitPrice = 0)
        {
            var isShort = kline.HighPrice > _shortAim;
            var slippage = _state == RsiDumpState.ExtremeStopping ? _originalSlippage * _stopAim : 0;
            if (_state == RsiDumpState.ExtremeStopping) limitPrice = kline.ClosePrice - slippage;
            else limitPrice = isShort ? _shortAim : _stopAim;

            Log($"{(isShort ? "ES" : "stoploss")}: {limitPrice}; {100 * (limitPrice - _longPrice) / _longPrice:0.##} %; rsi {_rsi:0.###}; slippage: {slippage:0.###}");
            base.ExitLong(kline, limitPrice);
            _state = RsiDumpState.WaitForRsiDump;
            if (!isShort)
            {
                _dealParams.StopLossCount++;
                _manager.Statistics.StopLossCount = _dealParams.StopLossCount;
            }
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
            var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, Param5, Param6, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var dp = (RsiDumpParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {dp.RsiLength}, {dp.RsiLevel}, {dp.SellFibLevel}, {dp.StopFibLevel}, {dp.MinMaxKlines}, {dp.DealCount}, {_highTimeframe}, 3, {dp.TotalProfit}, 'u'), ";
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            int[] rsiLengths = [5, 9, 14];
            // int[] rsiLengths = [5];
            int[] buyRsiLevels = [40, 30, 20];
            decimal[] sellFibLevel = [0.618M, 0.5M, 0.382M];
            decimal[] stopFibLevel = [0.01M, -0.27M, -0.5M];
            //decimal[] stopFibLevel = [0.0M, 0.382M, 0.5M];
            int[] minMaxFrames = [1, 2, 3];

            /* int[] rsiLengths = [5, 8, 11];
            int[] buyRsiLevels = [45, 39, 35];
            decimal[] sellFibLevel = [0.55M, 0.49M, 0.45M];
            decimal[] stopFibLevel = [0.03M, 0M, -0.1M];
            int[] minMaxFrames = [1, 2, 3];*/

            var deals = new List<BaseDealParams>();
            long paramsSetCount = rsiLengths.Length * buyRsiLevels.Length * sellFibLevel.Length * stopFibLevel.Length * minMaxFrames.Length;
            long counter = 0;
            foreach (var rl in rsiLengths)
            {
                _rsiLength = rl;
                foreach (var bl in buyRsiLevels)
                {
                    _buyRsiLevel = bl;
                    foreach (var shl in sellFibLevel)
                    {
                        _sellFibLevel = shl;
                        foreach (var stl in stopFibLevel)
                        {
                            _stopFibLevel = stl;
                            //_lowLongFibLevel = stl;
                            foreach (var mmf in minMaxFrames)
                            {
                                _minMaxFrames = mmf;
                                var dp = new RsiDumpParams(_rsiLength, _buyRsiLevel, _sellFibLevel, _stopFibLevel, _minMaxFrames);
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
            return deals;
        }
        #endregion

        #region RealTimeRoutines
        #endregion
    }
}