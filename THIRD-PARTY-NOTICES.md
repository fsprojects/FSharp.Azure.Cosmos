# Third-party notices

This repository and the packages built from it include material from the third-party projects listed below. The
original copyright notices and licences are reproduced here.

## Azure Cosmos DB .NET SDK v3

* Source: <https://github.com/Azure/azure-cosmos-dotnet-v3>, tag `3.62.0` (commit `fe16fe00617e962dbab246244813d605871c086d`)
* Licence: MIT
* Used in `FSharp.Azure.Cosmos.Sql` (`src/Cosmos.Sql`) and its tests (`tests/Cosmos.Sql.Tests`):
  * the node inventory of the syntax tree, after the classes in `Microsoft.Azure.Cosmos/src/SqlObjects` and the
    grammar `Microsoft.Azure.Cosmos/src/Query/Core/Parser/sql.g4`;
  * the printing rules, after `Microsoft.Azure.Cosmos/src/SqlObjects/Visitors/SqlObjectTextSerializer.cs`;
  * the names of the built-in functions in the catalog, from `SqlFunctionCallScalarExpression.Names` in
    `Microsoft.Azure.Cosmos/src/SqlObjects/SqlFunctionCallScalarExpression.cs`;
  * expected query texts in the tests, from the baselines in
    `Microsoft.Azure.Cosmos/tests/Microsoft.Azure.Cosmos.Tests/BaselineTest/TestBaseline` and
    `Microsoft.Azure.Cosmos/tests/Microsoft.Azure.Cosmos.EmulatorTests/BaselineTest/TestBaseline`.

```text
MIT License

Copyright (c) Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE
```
