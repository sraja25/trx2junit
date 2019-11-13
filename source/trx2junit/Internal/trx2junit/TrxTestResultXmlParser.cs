﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using trx2junit.Models;

namespace trx2junit
{
    public class TrxTestResultXmlParser : TrxBase, ITestResultXmlParser<TrxTest>
    {
        private readonly XElement _trx;
        private readonly TrxTest  _test = new TrxTest();
        //---------------------------------------------------------------------
        public TrxTestResultXmlParser(XElement? trx) => _trx = trx ?? throw new ArgumentNullException(nameof(trx));
        //---------------------------------------------------------------------
        public TrxTest Result => _test;
        //---------------------------------------------------------------------
        public void Parse()
        {
            this.ReadTimes();
            this.ReadResultSummary();
            this.ReadTestDefinitions();
            this.ReadUnitTestResults();
        }
        //---------------------------------------------------------------------
        private void ReadTimes()
        {
            XElement xTimes = _trx.Element(s_XN + "Times");

            _test.Times = new TrxTimes
            {
                Creation = xTimes.ReadDateTime("creation"),
                Queuing  = xTimes.ReadDateTime("queuing"),
                Start    = xTimes.ReadDateTime("start"),
                Finish   = xTimes.ReadDateTime("finish")
            };
        }
        //---------------------------------------------------------------------
        private void ReadResultSummary()
        {
            XElement xResultSummary = _trx.Element(s_XN + "ResultSummary");
            XElement xCounters      = xResultSummary.Element(s_XN + "Counters");

            _test.ResultSummary = new TrxResultSummary
            {
                Outcome  = Enum.Parse<TrxOutcome>(xResultSummary.Attribute("outcome").Value),
                Errors   = xCounters.ReadInt("error"),
                Executed = xCounters.ReadInt("executed"),
                Failed   = xCounters.ReadInt("failed"),
                Passed   = xCounters.ReadInt("passed"),
                Total    = xCounters.ReadInt("total")
            };

            // MsTest doesn't have this element, so be defensive
            _test.ResultSummary.StdOut = xResultSummary
                .Elements(s_XN + "Output")
                .FirstOrDefault()
                ?.Elements(s_XN + "StdOut")
                ?.FirstOrDefault()
                ?.Value;
        }
        //---------------------------------------------------------------------
        private void ReadTestDefinitions()
        {
            XElement xTestDefinitions = _trx.Element(s_XN + "TestDefinitions");

            if (xTestDefinitions == null) return;

            foreach (XElement xUnitTest in xTestDefinitions.Elements(s_XN + "UnitTest"))
            {
                var testDefinition = new TrxTestDefinition
                {
                    Id          = xUnitTest.ReadGuid("id"),
                    ExecutionId = xUnitTest.Element(s_XN + "Execution") .ReadGuid("id"),
                    TestClass   = xUnitTest.Element(s_XN + "TestMethod").Attribute("className").Value,
                    TestMethod  = xUnitTest.Element(s_XN + "TestMethod").Attribute("name").Value
                };

                _test.TestDefinitions.Add(testDefinition);
            }
        }
        //---------------------------------------------------------------------
        private void ReadUnitTestResults()
        {
            XElement xResults = _trx.Element(s_XN + "Results");

            if (xResults == null) return;

            foreach (XElement xResult in xResults.Elements(s_XN + "UnitTestResult"))
            {
                XElement xInnerResults = xResult.Element(s_XN + "InnerResults");

                if (xInnerResults == null)
                {
                    TrxUnitTestResult unitTestResult = ParseUnitTestResults(xResult);
                    _test.UnitTestResults.Add(unitTestResult);
                }
                else
                {
                    bool hasFailedTest = false;

                    foreach (XElement xInnerResult in xInnerResults.Elements(s_XN + "UnitTestResult"))
                    {
                        TrxUnitTestResult unitTestResult = ParseUnitTestResults(xInnerResult);
                        _test.UnitTestResults.Add(unitTestResult);

                        if (unitTestResult.Outcome == TrxOutcome.Failed)
                            hasFailedTest = true;
                    }

                    // MsTest counts the wrapper test, but we won't count it
                    // https://github.com/gfoidl/trx2junit/pull/40#issuecomment-484682771
                    Debug.Assert(_test?.ResultSummary != null);
                    _test!.ResultSummary!.Total--;

                    if (hasFailedTest)
                        _test.ResultSummary.Failed--;
                }
            }
        }
        //---------------------------------------------------------------------
        private static TrxUnitTestResult ParseUnitTestResults(XElement xResult)
        {
            var unitTestResult = new TrxUnitTestResult
            {
                Duration     = xResult.ReadTimeSpan("duration"),
                EndTime      = xResult.ReadDateTime("endTime"),
                ExecutionId  = xResult.ReadGuid("executionId"),
                Outcome      = Enum.Parse<TrxOutcome>(xResult.Attribute("outcome").Value),
                StartTime    = xResult.ReadDateTime("startTime"),
                TestId       = xResult.ReadGuid("testId"),
                TestName     = xResult.Attribute("testName").Value,
                ComputerName = xResult.Attribute("computerName").Value
            };

            XElement xOutput = xResult.Element(s_XN + "Output");
            if (xOutput != null)
            {
                XElement xErrorInfo = xOutput.Element(s_XN + "ErrorInfo");
                if (xErrorInfo != null)
                {
                    XElement xMessage = xErrorInfo.Element(s_XN + "Message");
                    if (xMessage != null)
                        unitTestResult.Message = xMessage.Value;

                    XElement xStackTrace = xErrorInfo.Element(s_XN + "StackTrace");
                    if (xStackTrace != null)
                        unitTestResult.StackTrace = xStackTrace.Value;
                }
            }

            // MsTest doesn't report a duration for ignored tests, but 'time' is requited by junit.xsd
            if (!unitTestResult.Duration.HasValue)
                unitTestResult.Duration = (unitTestResult.EndTime - unitTestResult.StartTime) ?? TimeSpan.Zero;

            return unitTestResult;
        }
    }
}