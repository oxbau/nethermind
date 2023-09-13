// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using Ethereum.Test.Base;
using NUnit.Framework;

namespace Ethereum.Blockchain.Test;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class EIP1153transientStorageTests : GeneralStateTestBase
{
    // Uncomment when Eip1153 tests are merged

    // [TestCaseSource(nameof(LoadTests))]
    // public void Test(GeneralStateTest test)
    // {
    //     Assert.True(RunTest(test).Pass);
    // }

    public static IEnumerable<GeneralStateTest> LoadTests()
    {
        var loader = new TestsSourceLoader(new LoadEipTestsStrategy(), "stEIP1153-transientStorage");
        return (IEnumerable<GeneralStateTest>)loader.LoadTests();
    }
}
